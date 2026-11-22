using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace InkCanvas.Modules.AiAssistant.Providers;

public static class AiProviderPayload
{
    public static HttpRequestMessage Create(AiConnection connection, string key, AiProviderRequest input, bool stream)
    {
        if (string.IsNullOrWhiteSpace(connection.Model)) throw new InvalidOperationException("请先选择或填写模型。");
        var request = new HttpRequestMessage(HttpMethod.Post, AiEndpoint.Completion(connection, stream));
        Authenticate(request, connection, key);
        var body = new JsonObject { ["model"] = connection.Model, ["stream"] = stream };
        switch (connection.Format)
        {
            case AiApiFormat.ChatCompletions:
                body["messages"] = Messages(input, connection.SendImages, response: false);
                body[connection.Provider is "openai" or "openai-responses" ? "max_completion_tokens" : "max_tokens"] = connection.MaxOutputTokens;
                break;
            case AiApiFormat.Responses:
                body["instructions"] = input.System;
                body["input"] = Messages(input, connection.SendImages, response: true);
                body["max_output_tokens"] = connection.MaxOutputTokens;
                body["store"] = false;
                break;
            case AiApiFormat.Anthropic:
                body["system"] = input.System;
                body["max_tokens"] = connection.MaxOutputTokens;
                body["messages"] = Messages(input, connection.SendImages, anthropic: true);
                break;
            case AiApiFormat.Google:
                body.Remove("model"); body.Remove("stream");
                body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = input.System }) };
                var contents = new JsonArray();
                foreach (var message in input.Messages.Where(message => message.Role is "user" or "assistant"))
                    contents.Add(new JsonObject { ["role"] = message.Role == "assistant" ? "model" : "user",
                        ["parts"] = new JsonArray(new JsonObject { ["text"] = message.Content }) });
                var parts = new JsonArray(new JsonObject { ["text"] = input.Prompt });
                if (connection.SendImages)
                    foreach (string image in input.Images)
                    {
                        var (mime, data) = Image(image);
                        parts.Add(new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = mime, ["data"] = data } });
                    }
                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = parts });
                body["contents"] = contents;
                body["generationConfig"] = new JsonObject { ["maxOutputTokens"] = connection.MaxOutputTokens };
                break;
        }
        if (connection.Search)
        {
            if (!AiProviderCatalog.SupportsSearch(connection)) throw new InvalidOperationException("当前供应商与协议尚未配置联网搜索，请关闭此选项。");
            if (connection.Provider == "qwen")
            {
                if (connection.Format == AiApiFormat.ChatCompletions) body["enable_search"] = true;
                else body["tools"] = new JsonArray(new JsonObject { ["type"] = "web_search" });
            }
            else if (connection.Provider == "deepseek")
            {
                var tool = new JsonObject { ["type"] = "deepseek_web_search" };
                if (connection.Format == AiApiFormat.Anthropic) tool["name"] = "deepseek_web_search";
                body["tools"] = new JsonArray(tool);
            }
            else if (connection.Format == AiApiFormat.Google)
                body["tools"] = new JsonArray(new JsonObject { ["google_search"] = new JsonObject() });
            else body["tools"] = new JsonArray(new JsonObject { ["type"] = "web_search" });
        }
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
        return request;
    }

    public static void Authenticate(HttpRequestMessage request, AiConnection connection, string key)
    {
        if (connection.Provider == "opencode")
        {
            if (connection.Format == AiApiFormat.Anthropic) request.Headers.Add("anthropic-version", "2023-06-01");
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return;
        }
        if (connection.Format == AiApiFormat.Anthropic)
        {
            request.Headers.Add("anthropic-version", "2023-06-01");
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("x-api-key", key);
        }
        else if (connection.Format == AiApiFormat.Google)
        {
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("x-goog-api-key", key);
        }
        else if (connection.Provider == "mimo")
        {
            if (!string.IsNullOrWhiteSpace(key)) request.Headers.Add("api-key", key);
        }
        else if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    private static JsonArray Messages(AiProviderRequest input, bool images, bool response = false, bool anthropic = false)
    {
        var messages = new JsonArray();
        if (!response && !anthropic) messages.Add(new JsonObject { ["role"] = "system", ["content"] = input.System });
        foreach (var message in input.Messages.Where(message => message.Role is "user" or "assistant"))
            messages.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
        var content = new JsonArray(new JsonObject { ["type"] = response ? "input_text" : "text", ["text"] = input.Prompt });
        if (images)
            foreach (string image in input.Images)
            {
                var (mime, data) = Image(image);
                if (anthropic) content.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject
                    { ["type"] = "base64", ["media_type"] = mime, ["data"] = data } });
                else if (response) content.Add(new JsonObject { ["type"] = "input_image", ["image_url"] = $"data:{mime};base64,{data}" });
                else content.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{mime};base64,{data}" } });
            }
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = content });
        return messages;
    }

    private static (string Mime, string Data) Image(string image)
    {
        if (image.StartsWith("data:", StringComparison.Ordinal))
        {
            int separator = image.IndexOf(";base64,", StringComparison.Ordinal);
            if (separator < 0) throw new InvalidDataException("图片需要使用 Base64 编码。");
            return (image[5..separator], image[(separator + 8)..]);
        }
        return ("image/png", image);
    }
}
