using System.Numerics;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Ink;

namespace InkCanvas.Host.Contracts;

public sealed record AssistantCanvasSnapshot(string Kind, IReadOnlyList<InkStroke> Strokes,
    IReadOnlyList<Guid> Selection, Matrix3x2 ViewTransform)
{
    public IReadOnlyList<AssistantCanvasObject> Objects { get; init; } = Array.Empty<AssistantCanvasObject>();
}
public sealed record AssistantCanvasObject(Guid Id, string Kind, Rect Bounds, bool Selected);

public sealed record AssistantAgentProgress(string Message, string Tool = "");
public sealed record AssistantAgentRequest(string Question, IReadOnlyList<AssistantMessage> Messages);

/// <summary>Every method marshals to the canvas thread and checks that its captured target is still current.</summary>
public interface IAssistantAgentCanvas
{
    Task<AssistantCanvasSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task<bool> CreateShapesAsync(IReadOnlyList<InkStroke> strokes, CancellationToken cancellationToken);
    Task<bool> TransformSelectionAsync(Matrix3x2 transform, CancellationToken cancellationToken);
    Task<byte[]> CaptureAsync(CancellationToken cancellationToken);
}

/// <summary>Optional Agent implementations receive native canvas tools and report their current action.</summary>
public interface IAssistantAgentProvider
{
    string Name { get; }
    Task<string> RunAsync(AssistantAgentRequest request, IAssistantAgentCanvas canvas,
        IProgress<AssistantAgentProgress> progress, CancellationToken cancellationToken);
}

/// <summary>Exported by the AI plugin. A future Agent module attaches here for the lifetime of its registration.</summary>
public interface IAssistantAgentBridge
{
    IDisposable Register(IAssistantAgentProvider provider);
}
