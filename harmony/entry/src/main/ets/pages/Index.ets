import { InkCanvasSurface, CanvasInkRenderer } from '@inkcanvas/platform';
import { FloatingBar, PenPalette, ShapePicker } from '@inkcanvas/controls';

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
              this.paletteOpen = false;
            },
            onWidth: (stored: number) => {
              this.widthMenu = stored * 2;
            },
            onAlpha: (value: number) => {
              this.alpha = value;
            },
            onTaper: (kind: number) => {
              this.taperIndex = kind === 1 ? 0 : (kind === 2 ? 1 : 2);
            },
            onToggleScheme: () => {
              this.dark = !this.dark;
            },
            onClose: () => {
              this.paletteOpen = false;
            }
          })
            .margin({ bottom: 170 })
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
              if (!longPress) {
                this.shapeOpen = false;
                this.selectedId = 'shapes';
              }
            }
          })
            .margin({ bottom: 170 })
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
        })
          .margin({ bottom: 100 })
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
  }

  private handleAction(id: string): void {
    if (id === 'pen') {
      this.paletteHighlighter = false;
      this.paletteOpen = true;
      this.selectedId = 'pen';
    } else if (id === 'highlighter') {
      this.paletteHighlighter = true;
      this.paletteOpen = true;
      this.selectedId = 'highlighter';
    } else if (id === 'shapes') {
      this.shapeOpen = !this.shapeOpen;
    } else if (id === 'tools') {
      this.paletteOpen = false;
      this.shapeOpen = false;
      this.selectedId = 'tools';
    } else if (id === 'toggleCollapse') {
      this.collapsed = !this.collapsed;
    } else if (id === 'fold') {
      this.collapsed = true;
    } else if (id === 'gesture') {
      this.selectedId = 'gesture';
    } else if (id === 'clear') {
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'eraserCircle' || id === 'eraserStroke' || id === 'select') {
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'board' || id === 'mouse' || id === 'clearAndMouse') {
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    }
  }
}
