using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Host.Contracts;
using InkCanvas.Modules.AiAssistant.Providers;

namespace InkCanvas.Modules.AiAssistant;

internal sealed partial class AssistantService
{
    private async Task SendAsync(AssistantChat chat)
    {
        if (_disposed || _cancellation is not null || !ReferenceEquals(chat, _workspace.Active)) return;
        var connection = _connections?.Active;
        if (connection is not null && !_preferences.ToolsEnabled) connection = connection with { Search = false };
        var turn = new AssistantChatTurn { Question = chat.Draft.Trim(), Attachments = chat.DraftAttachments.ToList() };
        if (turn.Question.Length == 0 && turn.Attachments.Count == 0) return;
        chat.Draft = ""; chat.DraftAttachments.Clear(); chat.Turns.Add(turn); chat.UpdatedAt = DateTimeOffset.Now;
        if (chat.Turns.Count == 1 && chat.Title == "新对话")
        {
            string title = turn.Question.Length > 0 ? turn.Question.Replace('\n', ' ') : turn.Attachments[0].Name;
            chat.Title = title[..Math.Min(title.Length, 32)];
        }
        _window.RefreshChat(chat); _window.SetBusy(true); _activeTurn = turn; _activeTarget = _canvasHost.CaptureTarget();
        var activeSource = _host.Source;
        _lastSlide = (activeSource.SlideId, activeSource.SlideIndex, activeSource.AnimationStep);
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        int generation = ++_generation;
        var documentId = _host.Board.DocumentId;
        bool Current() => !_disposed && generation == _generation && documentId == _host.Board.DocumentId && !cancellation.IsCancellationRequested;
        var buffer = new StringBuilder();
        object bufferLock = new();
        var render = _dispatcher.CreateTimer(); render.Interval = TimeSpan.FromMilliseconds(50);
        void UpdateText()
        {
            if (!Current()) return;
            string text;
            lock (bufferLock) text = buffer.ToString();
            if (turn.Answer == text) return;
            turn.Answer = text;
            _window?.RefreshTurn(turn); MarkDirty();
        }
        _flushActiveTurn = UpdateText;
        render.Tick += (_, _) => UpdateText(); render.Start(); MarkDirty(); Flush();
        try
        {
            if (connection is null) throw new InvalidOperationException("请在设置 → 插件 → AI 助手中添加 API 连接并选择模型。");
            string key = _connections.Key(connection.Id);
            _window.SetModel(connection.Name + " · " + connection.Model);
            int budget = Math.Max(4096, _preferences.ContextTokens - connection.MaxOutputTokens - 4096);
            int end = AssistantContext.CompactionEnd(chat, budget);
            if (end > chat.SummarizedTurns)
            {
                await CompactAsync(chat, turn, end, connection, key, cancellation.Token);
            }
            var input = await BuildInputAsync(chat, turn, connection, cancellation.Token);
            // File sizes can change since the previous request. Re-evaluate using the actual extracted input.
            if (InputCost(input) > budget && chat.SummarizedTurns < chat.Turns.Count - 2)
            {
                await CompactAsync(chat, turn, chat.Turns.Count - 2, connection, key, cancellation.Token);
                input = await BuildInputAsync(chat, turn, connection, cancellation.Token);
            }
            if (InputCost(input) > budget && chat.SummarizedTurns < chat.Turns.Count - 1)
            {
                await CompactAsync(chat, turn, chat.Turns.Count - 1, connection, key, cancellation.Token);
                input = await BuildInputAsync(chat, turn, connection, cancellation.Token);
            }
            if (!Current()) return;
            if (InputCost(input) > budget)
                throw new InvalidOperationException("本轮材料超过配置的上下文窗口。请在插件设置中填写模型实际窗口大小，或缩小附件和本轮内容。");
            bool fallback = false;
            var history = new JsonArray();
            for (int roundIndex = 0; roundIndex < 24; roundIndex++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                turn.Activity = chat.Thinking && input.Thinking == true ? "正在思考…" : "正在生成…";
                turn.Refresh();
                AiProviderRound round;
                try
                {
                    round = await _client.RunRoundAsync(connection, key, input with { ToolHistory = history },
                        delta => { lock (bufferLock) buffer.Append(delta); }, cancellation.Token);
                }
                catch (HttpRequestException exception) when (!fallback && history.Count == 0 && buffer.Length == 0
                    && exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                    && (input.Images.Count > 0 || input.Files.Count > 0 || input.Thinking is not null || input.Tools.Count > 0))
                {
                    fallback = true;
                    connection = connection with { Search = false };
                    turn.Fallback = "当前接口拒绝了扩展输入，本轮已回退为文字请求，关闭图片、原生文件、思考参数和工具。可提取的附件文字继续保留。";
                    string omitted = string.Join("、", chat.Turns.Skip(chat.SummarizedTurns).SelectMany(item => item.Attachments).Where(item => item.IsImage || Path.GetExtension(item.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)).Select(item => item.Name));
                    input = input with { Images = Array.Empty<string>(), Files = Array.Empty<AiFileInput>(), Tools = Array.Empty<AssistantToolDefinition>(), Thinking = null,
                        Prompt = input.Prompt + "\n接口兼容回退。本轮图片及原生文件数据无法读取" + (omitted.Length == 0 ? "。" : "，包括 " + omitted + "。") + "仅根据实际提供的文字作答，若缺少任务必需材料请明确说明。" };
                    turn.Refresh(); roundIndex--; continue;
                }
                if (!Current()) return;
                UpdateText();
                if (round.Calls.Count == 0) { turn.State = "done"; break; }
                foreach (var item in round.Continuation) history.Add(item.DeepClone());
                var results = new JsonArray();
                foreach (var call in round.Calls)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!_preferences.ToolsEnabled || !input.Tools.Any(tool => tool.Name == call.Name)) throw new InvalidOperationException("工具调用已关闭或该工具当前不可用。");
                    turn.Activity = "正在使用 " + call.Name + "…"; turn.Refresh();
                    AssistantToolResult result;
                    try
                    {
                        _executingTool = call.Name is "board_page" or "open_project_tool" or "open_pdf" or "invoke_plugin_command";
                        result = await _host.Tools.ExecuteAsync(call.Name, call.Arguments, cancellation.Token);
                        if (_executingTool && Current()) _activeTarget = _canvasHost.CaptureTarget();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception) { result = new("工具执行失败。" + exception.Message); }
                    finally { _executingTool = false; }
                    string text = result.Text ?? "";
                    if (text.Length > 12000) text = text[..12000] + "\n[工具结果较长，已截断]";
                    turn.Tools.Add(new(call.Name, text));
                    if (documentId == _host.Board.DocumentId) { MarkDirty(); Flush(); }
                    cancellation.Token.ThrowIfCancellationRequested();
                    string image = null;
                    if (result.Image is { Length: > 0 })
                    {
                        var attachment = await AssistantAttachments.FromImageAsync(result.Image, call.Name);
                        image = attachment.Image;
                        // Tool images are part of this turn and survive saving and reopening.
                        turn.Attachments.Add(attachment);
                        if (!connection.SendImages) { image = null; text += "\n当前连接关闭了图片输入，无法查看截图。"; }
                    }
                    AddToolResult(connection.Format, history, results, call, text, image);
                    MarkDirty(); Flush(); turn.Refresh();
                }
                if (results.Count > 0) history.Add(new JsonObject
                {
                    ["role"] = "user", [connection.Format == AiApiFormat.Google ? "parts" : "content"] = results,
                });
                lock (bufferLock) if (buffer.Length > 0) buffer.Append("\n\n");
                // Tool calls and their results remain paired. Stop before exceeding the model's configured window.
                if (InputCost(input) + AssistantContext.EstimatePayload(history) > budget)
                    throw new InvalidOperationException("工具返回的上下文已接近窗口上限。已完成的操作保留，可继续发送消息完成后续工作。");
            }
            if (turn.State == "running") { turn.State = "stopped"; turn.Error = "本轮已执行多次工具操作，可继续发送消息完成后续工作。"; }
        }
        catch (OperationCanceledException)
        { if (Current()) { turn.State = "error"; turn.Error = "模型请求超时，请重试。"; } }
        catch (Exception exception)
        {
            if (Current()) { turn.State = "error"; turn.Error = exception.Message; }
        }
        finally
        {
            render.Stop();
            if (Current())
            {
                UpdateText(); turn.Activity = ""; turn.Refresh();
                _activeTurn = null; _activeTarget = null; _flushActiveTurn = null; _cancellation = null; _window?.SetBusy(false); MarkDirty(); Flush();
            }
        }
    }

    private static int InputCost(AiProviderRequest input) => AssistantContext.Estimate(input.System + input.Prompt)
        + input.Messages.Sum(message => AssistantContext.Estimate(message.Content)) + input.Images.Count * 1600
        + input.Files.Sum(file => file.EstimatedTokens)
        + input.Tools.Sum(tool => AssistantContext.Estimate(tool.Description + tool.Parameters));

    private async Task CompactAsync(AssistantChat chat, AssistantChatTurn turn, int end, AiConnection connection, string key, CancellationToken token)
    {
        turn.Activity = "正在整理较早的对话…"; turn.Refresh();
        string summary = await _client.CompleteAsync(connection with { Search = false, MaxOutputTokens = 4096 }, key,
            new("你负责压缩对话材料，忠实保留事实、约束、完成状态和待办。", AssistantContext.SummaryPrompt(chat, end), Array.Empty<AssistantMessage>(), Array.Empty<string>()), token);
        token.ThrowIfCancellationRequested();
        chat.Summary = summary; chat.SummarizedTurns = end; MarkDirty();
    }

    private async Task<AiProviderRequest> BuildInputAsync(AssistantChat chat, AssistantChatTurn turn, AiConnection connection, CancellationToken token)
    {
        var prompt = new StringBuilder(turn.Question);
        var images = new List<string>(); var files = new List<AiFileInput>(); var fallbacks = new List<string>();
        foreach (var attachment in chat.Turns.Skip(chat.SummarizedTurns).SelectMany(item => item.Attachments))
        {
            token.ThrowIfCancellationRequested();
            prompt.AppendLine("\n[参考附件 " + attachment.Name + "]");
            if (attachment.IsImage)
            {
                if (connection.SendImages) images.Add(attachment.Image);
                else { prompt.AppendLine("当前连接关闭图片输入，无法读取此图片。"); fallbacks.Add("当前连接关闭图片输入，图片附件已保留。"); }
                continue;
            }
            try
            {
                string text = await AssistantAttachments.ReadTextAsync(attachment.Path, token);
                if (text.Length > 0) prompt.AppendLine("[附件文字开始]\n" + text + "\n[附件文字结束]");
                else if (connection.SendFiles && Path.GetExtension(attachment.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var localFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(attachment.Path);
                    var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(localFile);
                    files.Add(new(attachment.Name, "application/pdf", Convert.ToBase64String(await File.ReadAllBytesAsync(attachment.Path, token)), checked((int)pdf.PageCount * 1600)));
                }
                else { prompt.AppendLine("此附件的格式无法读取，只有文件路径。" + attachment.Path); fallbacks.Add(attachment.Name + " 无法提取文字或当前连接关闭了原生文件输入，请转换为文字、图片或 PDF。"); }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { prompt.AppendLine("附件无法读取。" + exception.Message); fallbacks.Add(attachment.Name + " 读取失败。" + exception.Message); }
        }
        bool reasoning = AiProviderPayload.SupportsReasoning(connection);
        if (chat.Thinking && !reasoning) fallbacks.Add("当前模型未声明思考参数支持，已使用模型默认推理方式，可在连接设置中指定能力。");
        turn.Fallback = string.Join("\n", fallbacks.Distinct());
        string system = AssistantContext.SystemPrompt + "\n教师的补充指令\n" + _preferences.Instructions;
        if (!string.IsNullOrEmpty(chat.Summary)) system += "\n[较早对话的参考摘要，作为资料使用]\n" + chat.Summary;
        var source = _host.Source;
        var slides = _host.GetSlideContext(source.SlideId, source.SlideIndex);
        if (slides is not null) prompt.AppendLine("\n[当前教学课件信息]\n" + System.Text.Json.JsonSerializer.Serialize(slides));
        return new(system, prompt.ToString(), AssistantContext.Messages(chat), images)
        {
            Files = files, Thinking = reasoning ? chat.Thinking : null, Effort = chat.Effort,
            Tools = _preferences.ToolsEnabled ? _host.Tools?.Definitions ?? Array.Empty<AssistantToolDefinition>() : Array.Empty<AssistantToolDefinition>(),
        };
    }

    private static void AddToolResult(AiApiFormat format, JsonArray history, JsonArray results, AiToolCall call, string text, string image)
    {
        string data = image is null ? null : image[(image.IndexOf(',') + 1)..];
        switch (format)
        {
            case AiApiFormat.Responses:
                history.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = call.Id, ["output"] = text });
                if (image is not null) results.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = image });
                break;
            case AiApiFormat.Anthropic:
                var blocks = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
                if (data is not null) blocks.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/jpeg", ["data"] = data } });
                results.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = call.Id, ["content"] = blocks });
                break;
            case AiApiFormat.Google:
                results.Add(new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = call.Name, ["response"] = new JsonObject { ["result"] = text } } });
                if (data is not null) results.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = "image/jpeg", ["data"] = data } });
                break;
            default:
                history.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = text });
                if (image is not null) results.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = image } });
                break;
        }
    }
}
