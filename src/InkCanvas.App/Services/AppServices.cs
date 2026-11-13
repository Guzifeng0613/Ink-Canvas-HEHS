using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
using InkCanvas.Features.Board;
using InkCanvas.Features.Persistence;
using InkCanvas.Features.Ppt;
using InkCanvas.Features.Tools;
using InkCanvas.Ink;

namespace InkCanvas.App.Services;

/// <summary>
/// Composition root. Owns the long-lived services and is the only place that translates
/// persisted settings into live behaviour.
/// </summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Store = new SettingsStore();
        Settings = Store.Load();
        Board = new BoardService();
        Ppt = new PptInteropService();
        AutoSave = new AutoSaveService(Settings.Automation);
        Lottery = new LotteryService();
        ApplySettings();
    }

    public SettingsStore Store { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>Ink on the transparent screen overlay.</summary>
    public InkController OverlayInk { get; } = new();

    public BoardService Board { get; }

    public PptInteropService Ppt { get; }

    public AutoSaveService AutoSave { get; }

    public LotteryService Lottery { get; }

    /// <summary>Push the current settings into every controller.</summary>
    public void ApplySettings()
    {
        var taper = Settings.Canvas.InkStyle switch
        {
            1 => TaperKind.Velocity,
            2 => TaperKind.None,
            _ => TaperKind.Tail,
        };

        foreach (var controller in AllControllers())
        {
            controller.CurrentAttributes.BaseWidth = (float)Math.Clamp(Settings.Canvas.InkWidth, 0.5, 60);
            controller.CurrentAttributes.TaperKind = taper;
            controller.EraserRadius = 8f + Math.Clamp(Settings.Canvas.EraserSize, 0, 8) * 8f;
            controller.RecognizeShapes = Settings.InkToShape.IsInkToShapeEnabled;
            controller.MultiTouchEnabled = Settings.Gesture.IsEnableMultiTouchMode;
            controller.ConfigureGestures(
                Settings.Gesture.IsEnableTwoFingerZoom,
                Settings.Gesture.IsEnableTwoFingerTranslate,
                Settings.Gesture.IsEnableTwoFingerRotation);
            controller.Palm.PalmContactThreshold = Math.Max(8, Settings.Advanced.FingerModeBoundsWidth);
            controller.GesturesTargetSelection = Settings.Gesture.IsEnableTwoFingerRotationOnSelection;
        }

        Ppt.AllowWps = Settings.PowerPoint.IsSupportWPS;
        Lottery.PeopleCount = Settings.Rand.PeopleCount;
        Lottery.NoRepeat = Settings.Rand.IsNotRepeatName;
    }

    public IEnumerable<InkController> AllControllers()
    {
        yield return OverlayInk;
        foreach (var page in Board.Pages) yield return page;
    }

    public void SaveSettings() => Store.Save(Settings);

    /// <summary>Re-read settings from disk (used after the settings window edits them).</summary>
    public void Reload()
    {
        Settings = Store.Load();
        ApplySettings();
    }

    public void Dispose()
    {
        OverlayInk.Dispose();
        foreach (var page in Board.Pages) page.Dispose();
    }
}