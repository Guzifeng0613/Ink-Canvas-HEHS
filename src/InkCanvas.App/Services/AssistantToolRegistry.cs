using System.Text.Json;
using InkCanvas.Host.Contracts;
using Microsoft.UI.Dispatching;

namespace InkCanvas.App.Services;

public sealed class AssistantToolRegistry : IAssistantToolRegistry
{
    private readonly Dictionary<string, (AssistantToolDefinition Definition, Func<JsonElement, CancellationToken, Task<AssistantToolResult>> Execute)> _tools = new(StringComparer.Ordinal);
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    public IReadOnlyList<AssistantToolDefinition> Definitions => _tools.Values.Select(tool => tool.Definition).ToArray();

    public IDisposable Register(AssistantToolDefinition definition, Func<JsonElement, CancellationToken, Task<AssistantToolResult>> execute)
    {
        if (!_tools.TryAdd(definition.Name, (definition, execute))) throw new InvalidOperationException("工具名称已注册。" + definition.Name);
        return new Registration(() => _tools.Remove(definition.Name));
    }

    public Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<AssistantToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_tools.TryGetValue(name, out var tool)) throw new InvalidOperationException("工具当前不可用。" + name);
                result.TrySetResult(await tool.Execute(arguments, cancellationToken));
            }
            catch (OperationCanceledException) { result.TrySetCanceled(cancellationToken); }
            catch (Exception exception) { result.TrySetException(exception); }
        })) result.TrySetException(new ObjectDisposedException(nameof(AssistantToolRegistry)));
        return result.Task;
    }

    private sealed class Registration(Action release) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
