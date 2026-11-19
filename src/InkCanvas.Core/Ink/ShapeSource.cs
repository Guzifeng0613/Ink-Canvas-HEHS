namespace InkCanvas.Core.Ink;

/// <summary>Provider-owned formula data shared by every contour of a selectable shape.</summary>
public sealed record ShapeSource(Guid GroupId, string Provider, string Definition);
