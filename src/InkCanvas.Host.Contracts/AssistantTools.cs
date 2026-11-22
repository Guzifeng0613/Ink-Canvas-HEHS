using System.Text.Json;

namespace InkCanvas.Host.Contracts;

public sealed record AssistantToolDefinition(string Name, string Description, string Parameters);
public sealed record AssistantToolResult(string Text, byte[] Image = null);

/// <summary>Native capabilities available to the assistant. Plugins can contribute tools through this registry.</summary>
public interface IAssistantToolRegistry
{
    IReadOnlyList<AssistantToolDefinition> Definitions { get; }
    IDisposable Register(AssistantToolDefinition definition, Func<JsonElement, CancellationToken, Task<AssistantToolResult>> execute);
    Task<AssistantToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken);
}
