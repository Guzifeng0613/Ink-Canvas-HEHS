# Whiteboard port notes

This port replaces the reconstructed WinUI board shell in `src/InkCanvas.App/Views/BoardWindow.cs` and extends `src/InkCanvas.Features/Board/BoardService.cs`. Upstream behavior and layout references are from commit `8b2c95b` in the original WPF tree.

## Source evidence

- `Ink Canvas/MainWindow.xaml` lines 797–990: page strip at lower left, centered tool strip, left-side previous/next/add/exit affordances, page indicator; the bottom row is 55 DIP in the WPF layout.
- `Ink Canvas/MainWindow.xaml` lines 1260–1325, 1888–2015, 2288–2360: selection, pen, area eraser, stroke eraser, shape, undo and redo control groups.
- `Ink Canvas/MainWindow.xaml` lines 2790–2865: right-side add-page action.
- `Ink Canvas/MainWindow_cs/MW_BoardControls.cs`: independent page history snapshots; previous/next/add/delete page logic; maximum 99 pages; next at the last page adds one; index display and navigation limits.
- `Ink Canvas/MainWindow_cs/MW_BoardIcons.cs`: black/white switching, area/stroke eraser modes and shape/palette interactions.
- `Ink Canvas/MainWindow_cs/MW_ElementsControls.cs` and `Ink Canvas/Helpers/InkCanvasElementsHelper.cs`: embedded image insertion, center/scale on insertion, selectable/movable/resizable elements and element history. The original also supports media; the current WinUI ink controller and this port intentionally do not claim media support.
- `Ink Canvas/MainWindow_cs/MW_Save&OpenStrokes.cs`: original save/open includes strokes plus canvas children; WPF ISF archives are not the WinUI `.icink` wire format.
- `LICENSE`: GNU GPL v3. This port does not remove, change or replace the existing license.

## Implemented / adapted

- Preserves left/page, center/tools and right/actions groups in a dock at the bottom. Windowed view uses the new `WindowChrome.AddTitleBar` helper; fullscreen switches to the same board content without the title strip.
- Per-page `InkController`, including independent ink document, undo/redo and transform. Upstream page count ceiling is 99; next at the final page creates a page, while navigating back only visits existing pages. Deleting the last page clears it instead of reaching zero pages.
- Page navigation count, add/delete/previous/next affordances and disabled state.
- Selection, pen, highlighter, whole-stroke eraser, shape catalogue, undo/redo, black/white/grid/lined backgrounds, background scale and grid spacing controls. They call current controller/service functionality; shape/erase semantics are described accurately for the existing engine.
- Save/open current page and entire board via `.icink`. Existing single-page InkFileStore documents continue to read; new board packages use a versioned JSON envelope stored with the `.icink` extension. A legacy single-page `.icink` can be opened as a board.
- Raster images are embedded per page and selectable, moveable, aspect-preserving resizable, deleteable and included in page-level undo and the board package. Coordinates and dimensions are DIP-based.
- Destructive clear, page delete and replacement-open actions ask for confirmation.
- Existing `BoardWindow(AppServices, IShellActions)` binds the board's current controller into the floating toolbar; `CurrentPageChanged` fires after surface rebind. Closing the board via the board window restores screen overlay and floating-bar controller.

## Deliberate gaps / adaptation notes

- This is a functional WinUI reconstruction, not a pixel-identical WPF XAML/resource/animation recreation. Board surface style and spacing use current WinUI tokens, and actions are labelled text rather than the original vector/font icon artwork.
- The legacy pen flyout exposes colors, width presets and taper. It does not port all legacy highlighter alpha controls, palette geometry, nib-mode toggles, gesture-mode toggles, platform-specific stylus classifier options, launch-EasiCamera/Desmos shortcuts, timers, random selection, screenshot, replay, settings, or guide entrypoints. These require APIs or dependencies outside the owned files; no inert/fake feature controls are exposed.
- Eraser behavior is the current controller's whole-stroke hit erasure. It is not WPF's partial point erasure. Media-element insertion is not implemented; image insert/display/selection transform is.
- `.isf` compatibility is not added. The project explicitly documents the migration break and current `.icink` format in `src/InkCanvas.Features/Persistence/Stores.cs` and `src/InkCanvas.App/Views/SettingsWindow.cs`; old archives are not presented as supported.
- A single global board background and grid spacing are retained, like the legacy `Settings.Canvas.UsingWhiteboard`; per-page ink and images are independent.

## Verification

Per user instruction, behavioral/UI verification and hardware checks were skipped. The integrated Release solution compile passed with zero warnings/errors. No board workflow or drag/drop UI was exercised in this pass.
