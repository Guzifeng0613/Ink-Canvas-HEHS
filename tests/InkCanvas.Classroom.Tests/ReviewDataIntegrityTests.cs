using System.Numerics;
using InkCanvas.Core.Classroom;
using InkCanvas.Core.Ink;
using InkCanvas.Features.Board;
using InkCanvas.Features.Classroom;
using InkCanvas.Ink;
using Xunit;

namespace InkCanvas.Classroom.Tests;

public sealed class ReviewDataIntegrityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ink-pr5-integrity-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void SameMillisecondPageChangesRetainTheFinalPage()
    {
        using var controller = new InkController();
        using var recorder = new InkRecorder(new BoardService(controller)) { SessionDirectory = _directory };
        recorder.Start();
        recorder.Append(new TimelineEvent { TimeMs = 500, Kind = TimelineEventKind.PageChanged, PageIndex = 1 });
        recorder.Append(new TimelineEvent { TimeMs = 500, Kind = TimelineEventKind.PageChanged, PageIndex = 2 });
        Assert.Equal(2, recorder.Session.Events.Last().PageIndex);
    }

    [Fact]
    public void SameMillisecondEraseListsAreBothReplayed()
    {
        using var controller = new InkController();
        var first = Stroke();
        var second = Stroke();
        controller.Document.AddRange(new[] { first, second });
        using var recorder = new InkRecorder(new BoardService(controller)) { SessionDirectory = _directory };
        recorder.Start();
        recorder.Append(new TimelineEvent { TimeMs = 500, Kind = TimelineEventKind.StrokesRemoved, StrokeIds = new[] { first.Id.ToString() } });
        recorder.Append(new TimelineEvent { TimeMs = 500, Kind = TimelineEventKind.StrokesRemoved, StrokeIds = new[] { second.Id.ToString() } });
        Assert.Empty(new InkTimeline(recorder.Session).BuildFrame(500));
    }

    [Fact]
    public void SamePayloadPrefixDoesNotDiscardTheLaterResult()
    {
        using var controller = new InkController();
        using var recorder = new InkRecorder(new BoardService(controller)) { SessionDirectory = _directory };
        recorder.Start();
        string prefix = new('x', 64);
        recorder.Append(new TimelineEvent { TimeMs = 500, Kind = TimelineEventKind.AiStep, Payload = prefix + "first" });
        recorder.Append(new TimelineEvent { TimeMs = 500, Kind = TimelineEventKind.AiStep, Payload = prefix + "second" });
        Assert.Equal(2, recorder.Session.Events.Count(item => item.Kind == TimelineEventKind.AiStep));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordingRoundTripKeepsNibBrushAndEditableShapeMetadata(bool shape)
    {
        var original = Stroke();
        original.Attributes.Nib = PenNib.Ellipse45;
        original.Attributes.NibRotation = .3f;
        original.Attributes.TaperKind = shape ? TaperKind.InkWash : TaperKind.Calligraphy;
        original.Attributes.InkWash = new InkWashBrushOptions(.8f, -15, .6f);
        original.Attributes.InkWashSeed = 12345;
        original.LayerOrder = 27;
        original.ShapeSource = shape ? new ShapeSource(Guid.NewGuid(), "test-provider", "shape definition") : null;
        original.IsRecognizedShape = shape;
        original.ImportObjectFrame(new float[] { 0, 0, 40, 20, 1, 0, 0, 1, 5, 6 });
        var restored = StrokeJsonCodec.Deserialize(StrokeJsonCodec.Serialize(StrokeSimplifier.Simplify(original)));
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Attributes.Nib, restored.Attributes.Nib);
        Assert.Equal(original.Attributes.NibRotation, restored.Attributes.NibRotation);
        Assert.Equal(original.Attributes.InkWash, restored.Attributes.InkWash);
        Assert.Equal(original.Attributes.InkWashSeed, restored.Attributes.InkWashSeed);
        Assert.Equal(original.LayerOrder, restored.LayerOrder);
        Assert.Equal(original.ShapeSource, restored.ShapeSource);
        Assert.Equal(original.ExportObjectFrame(), restored.ExportObjectFrame());
    }

    [Fact]
    public void RecordingRoundTripKeepsPressureAndTiltSamples()
    {
        var original = new InkStroke(new StrokeAttributes { BaseWidth = 8, TaperKind = TaperKind.Calligraphy });
        original.AddPoint(0, 0, .2f, 0, hasPressure: true, tiltX: 20, tiltY: -10);
        original.AddPoint(20, 0, 1, 10, hasPressure: true, tiltX: -30, tiltY: 25);
        original.AddPoint(40, 0, .2f, 20, hasPressure: true, tiltX: 10, tiltY: 15);
        original.Complete();
        var restored = StrokeJsonCodec.Deserialize(StrokeJsonCodec.Serialize(StrokeSimplifier.Simplify(original)));
        Assert.Equal(original.Points.ToArray(), restored.Points.ToArray());
        Assert.Equal(original.Bounds, restored.Bounds);
    }

    [Fact]
    public void SimplifyingAStraightStrokeKeepsItsWidthPeak()
    {
        var original = Stroke();
        original.RestorePoints(new[] { new InkPoint(0, 0, .5f, 1, 0), new InkPoint(20, 0, .5f, 30, 10), new InkPoint(40, 0, .5f, 1, 20) });
        var simplified = StrokeSimplifier.Simplify(original);
        Assert.Equal(original.Bounds, simplified.Bounds);
    }

    private static InkStroke Stroke()
    {
        var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 4, TaperKind = TaperKind.None });
        stroke.AddPoint(0, 0);
        stroke.AddPoint(40, 0);
        stroke.Complete();
        return stroke;
    }
}
