import { InkCanvasSurface, CanvasInkRenderer, pointerKind } from '@inkcanvas/platform';
import { FloatingBar, PenPalette, ShapePicker } from '@inkcanvas/controls';
import {
  InkController,
  PointerSample,
  InkTool,
  InkEraserMode,
  buildScene,
  Invalidation
} from '@inkcanvas/ink';
import type { SceneStrokeInput, SceneSelectionInput } from '@inkcanvas/ink';
import { InkStroke, InkColor } from '@inkcanvas/core';
import type { ShapeKind, StrokePointInput } from '@inkcanvas/core';

@Entry
@Component
struct Index {
  @State dark: boolean = false;
  @State paletteOpen: boolean = false;
  @State paletteHighlighter: boolean = false;
  @State shapeOpen: boolean = false;
  @State selectedId: string = 'pen';
  @State widthMenu: number = 5;
  @State alpha: number = 140;
  @State taperIndex: number = 0;
  @State currentPacked: number = 0xFF000000;
  @State selectedShape: number = -1;
  @State collapsed: boolean = false;
  @State pptActive: boolean = false;
  @State windowWidth: number = 0;
  @State windowHeight: number = 0;
  private renderer: CanvasInkRenderer | null = null;
  private controller: InkController = new InkController();

  aboutToAppear(): void {
    this.controller.Invalidated = () => {
      this.refreshScene();
    };
    this.controller.CurrentAttributes.BaseWidth = 2.5;
  }

  build() {
    Stack({ alignContent: Alignment.TopStart }) {
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor(this.dark ? '#FF1B1B1B' : '#FFFAFAFA')

      InkCanvasSurface({
        onRendererReady: (renderer: CanvasInkRenderer) => {
          this.renderer = renderer;
          this.renderer.resize(this.windowWidth, this.windowHeight);
          this.refreshScene();
        }
      })

      if (this.paletteOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          PenPalette({
            highlighter: this.paletteHighlighter,
            dark: this.dark,
            lightColorScheme: !this.dark,
            widthMenu: this.widthMenu,
            alpha: this.alpha,
            taperIndex: this.taperIndex,
            currentPacked: this.currentPacked,
            onColor: (packed: number) => {
              this.currentPacked = packed;
              this.controller.CurrentAttributes.Color = InkColor.fromPackedArgb(packed);
              this.paletteOpen = false;
            },
            onWidth: (stored: number) => {
              this.widthMenu = stored * 2;
              this.controller.CurrentAttributes.BaseWidth = stored;
            },
            onAlpha: (value: number) => {
              this.alpha = value;
              this.controller.CurrentAttributes.HighlighterAlpha = value;
            },
            onTaper: (kind: number) => {
              this.taperIndex = kind === 1 ? 0 : (kind === 2 ? 1 : 2);
              this.controller.CurrentAttributes.TaperKind = kind;
            },
            onToggleScheme: () => {
              this.dark = !this.dark;
            },
            onClose: () => {
              this.paletteOpen = false;
            }
          }).margin({ bottom: 170 })
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.shapeOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          ShapePicker({
            dark: this.dark,
            selectedKind: this.selectedShape,
            onSelect: (kind: number, longPress: boolean) => {
              this.selectedShape = kind;
              this.controller.SelectShape(kind as ShapeKind, longPress);
              if (!longPress) {
                this.shapeOpen = false;
                this.selectedId = 'shapes';
              }
            }
          }).margin({ bottom: 170 })
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      Stack({ alignContent: Alignment.Bottom }) {
        FloatingBar({
          dark: this.dark,
          showLabels: false,
          selectedId: this.selectedId,
          folded: this.collapsed,
          pptActive: this.pptActive,
          onAction: (id: string) => {
            this.handleAction(id);
          }
        }).margin({ bottom: 100 })
      }
      .width('100%')
      .height('100%')
      .hitTestBehavior(HitTestMode.Transparent)
    }
    .width('100%')
    .height('100%')
    .onAreaChange((oldValue: Area, newValue: Area) => {
      this.windowWidth = Number(newValue.width);
      this.windowHeight = Number(newValue.height);
      if (this.renderer !== null) {
        this.renderer.resize(this.windowWidth, this.windowHeight);
      }
    })
    .onTouch((event: TouchEvent) => {
      this.handleTouch(event);
    })
  }

  private handleTouch(event: TouchEvent): void {
    const timeMs = event.timestamp / 1000000;
    const touches = event.changedTouches;
    for (let i = 0; i < touches.length; i++) {
      const touch = touches[i];
      const pressure = event.pressure > 0 ? event.pressure : 0.5;
      const width = touch.width === undefined ? 0 : touch.width;
      const height = touch.height === undefined ? 0 : touch.height;
      const sample = new PointerSample(
        touch.id,
        touch.x,
        touch.y,
        pressure,
        width,
        height,
        pointerKind(event.source, event.sourceTool),
        timeMs
      );
      if (event.type === TouchType.Down) {
        this.controller.onPointerDown(sample);
      } else if (event.type === TouchType.Move) {
        this.controller.onPointerMove(sample);
      } else if (event.type === TouchType.Up) {
        this.controller.onPointerUp(sample);
      } else if (event.type === TouchType.Cancel) {
        this.controller.onPointerCanceled(sample);
      }
    }
  }

  private toSceneStroke(stroke: InkStroke): SceneStrokeInput {
    const points: StrokePointInput[] = [];
    for (let i = 0; i < stroke.Count; i++) {
      const point = stroke.Points[i];
      points.push({ X: point.X, Y: point.Y, Width: point.Width });
    }
    return {
      BaseWidth: stroke.Attributes.BaseWidth,
      ColorPacked: stroke.Attributes.Color.packedArgb(),
      IsHighlighter: stroke.Attributes.IsHighlighter,
      HighlighterAlpha: stroke.Attributes.HighlighterAlpha,
      TaperKind: stroke.Attributes.TaperKind,
      Points: points
    };
  }

  private refreshScene(): void {
    if (this.renderer === null) {
      return;
    }
    const committed: SceneStrokeInput[] = [];
    const strokes = this.controller.Document.Strokes;
    for (let i = 0; i < strokes.length; i++) {
      committed.push(this.toSceneStroke(strokes[i]));
    }
    const activeStroke = this.controller.ActiveStroke;
    const active = activeStroke === null ? null : this.toSceneStroke(activeStroke);
    let selection: SceneSelectionInput | null = null;
    if (this.controller.Selection.length > 0) {
      const frame = this.controller.selectionFrameSummary();
      const b = frame.Bounds;
      const m = frame.Transform;
      selection = {
        Left: b[0], Top: b[1], Right: b[2], Bottom: b[3],
        RotationRadians: Math.atan2(m[1], m[0])
      };
    }
    this.renderer.render(buildScene(committed, active, selection), Invalidation.full());
  }

  private handleAction(id: string): void {
    if (id === 'pen') {
      this.controller.Tool = InkTool.Pen;
      this.controller.CurrentAttributes.IsHighlighter = false;
      this.paletteHighlighter = false;
      this.paletteOpen = true;
      this.selectedId = 'pen';
    } else if (id === 'highlighter') {
      this.controller.Tool = InkTool.Highlighter;
      this.controller.CurrentAttributes.IsHighlighter = true;
      this.paletteHighlighter = true;
      this.paletteOpen = true;
      this.selectedId = 'highlighter';
    } else if (id === 'shapes') {
      this.shapeOpen = !this.shapeOpen;
    } else if (id === 'eraserCircle') {
      this.controller.Tool = InkTool.Eraser;
      this.controller.EraserMode = InkEraserMode.Area;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'eraserStroke') {
      this.controller.Tool = InkTool.Eraser;
      this.controller.EraserMode = InkEraserMode.WholeStroke;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'select') {
      this.controller.Tool = InkTool.Select;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'mouse') {
      this.controller.Tool = InkTool.Pan;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'clear' || id === 'clearAndMouse') {
      this.controller.ClearSelection();
      this.controller.Clear();
      if (id === 'clearAndMouse') {
        this.controller.Tool = InkTool.Pan;
        this.selectedId = 'mouse';
      }
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'undo') {
      this.controller.Undo();
    } else if (id === 'redo') {
      this.controller.Redo();
    } else if (id === 'toggleCollapse') {
      this.collapsed = !this.collapsed;
    } else if (id === 'fold') {
      this.collapsed = true;
    } else if (id === 'gesture') {
      this.selectedId = 'gesture';
    } else if (id === 'tools') {
      this.paletteOpen = false;
      this.shapeOpen = false;
      this.selectedId = 'tools';
    } else if (id === 'board') {
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    }
  }
}
