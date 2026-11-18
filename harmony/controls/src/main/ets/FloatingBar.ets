// @parity src/InkCanvas.Controls/Controls/FloatingBar.cs
// 四卡布局：拖动把手 / 主栏 / 手势 / PPT 退出牌。平板与 2in1 使用同一组件。
import { FLOATING_BAR_BUTTONS, FloatingBarButton } from './FloatingBarCatalog';
import { ThemeTokens, themeTokens } from './ThemeTokens';

@Component
export struct FloatingBar {
  @Prop dark: boolean = false;
  @Prop showLabels: boolean = false;
  @Prop scale: number = 1;
  @Prop selectedId: string = 'pen';
  @Prop folded: boolean = false;
  @Prop pptActive: boolean = false;
  @Prop textIconFallback: boolean = true;
  onAction: (id: string) => void = (id: string) => {
    void id;
  };
  onDrag: (dx: number, dy: number) => void = (dx: number, dy: number) => {
    void dx;
    void dy;
  };
  onDragEnd: () => void = () => {
  };

  private tokens(): ThemeTokens {
    return themeTokens(this.dark);
  }

  private iconText(button: FloatingBarButton): string {
    return this.textIconFallback ? button.Label.substring(0, 1) : button.Glyph;
  }

  build() {
    Row({ space: 2.778 * this.scale }) {
      this.DragHandleCard()
      if (!this.folded) {
        this.PrimaryCard()
        this.GestureCard()
      }
      if (this.pptActive) {
        this.PptExitCard()
      }
    }
    .padding({
      left: this.tokens().barPadding[0] * this.scale,
      top: this.tokens().barPadding[1] * this.scale,
      right: this.tokens().barPadding[2] * this.scale,
      bottom: this.tokens().barPadding[3] * this.scale
    })
    .backgroundColor(this.tokens().floatBarBackground)
    .borderRadius(this.tokens().barCorner)
    .border({ width: 1, color: this.tokens().floatBarBorder })
    .gesture(
      PanGesture()
        .onActionUpdate((event: GestureEvent) => {
          this.onDrag(event.offsetX, event.offsetY);
        })
        .onActionEnd(() => {
          this.onDragEnd();
        })
    )
  }

  @Builder
  DragHandleCard() {
    Column() {
      Text('☺')
        .fontSize(Math.max(16, 27.778 * this.scale))
        .fontColor(this.tokens().textPrimary)
    }
    .width(50 * this.scale)
    .height(50 * this.scale)
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.tokens().buttonRadius)
    .backgroundColor(this.tokens().surfaceAlt)
    .onClick(() => {
      this.onAction('toggleCollapse');
    })
  }

  @Builder
  PrimaryCard() {
    Row({ space: 0 }) {
      ForEach(FLOATING_BAR_BUTTONS, (button: FloatingBarButton) => {
        if (button.DefaultVisible) {
          this.ButtonCell(button)
        }
      }, (button: FloatingBarButton) => button.Id)
    }
    .padding({ left: 3, right: 3, top: 0, bottom: 0 })
    .backgroundColor(this.tokens().surface)
    .borderRadius(this.tokens().buttonRadius)
  }

  @Builder
  ButtonCell(button: FloatingBarButton) {
    Column() {
      Text(this.iconText(button))
        .fontSize(button.Id === 'gesture' ? 20 * this.scale : 16 * this.scale)
        .fontColor(this.tokens().textPrimary)
      if (this.showLabels) {
        Text(button.Label)
          .fontSize(this.tokens().labelSize * this.scale)
          .fontColor(this.tokens().textPrimary)
          .margin({ top: -1.389 * this.scale })
      }
    }
    .width(Math.max(44, this.tokens().buttonWidth * this.scale))
    .height(Math.max(44, this.tokens().buttonHeight * this.scale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.selectedId === button.Id ? this.tokens().selectedRadius : this.tokens().buttonRadius)
    .backgroundColor(this.selectedId === button.Id ? this.tokens().floatBarSelected : Color.Transparent)
    .opacity(button.DefaultEnabled ? 1 : 0.4)
    .onClick(() => {
      if (button.DefaultEnabled) {
        this.onAction(button.Id);
      }
    })
  }

  @Builder
  GestureCard() {
    Column() {
      Text(this.textIconFallback ? '手' : '')
        .fontSize(20 * this.scale)
        .fontColor(this.tokens().textPrimary)
      if (this.showLabels) {
        Text('手势')
          .fontSize(this.tokens().labelSize * this.scale)
          .fontColor(this.tokens().textPrimary)
      }
    }
    .width(Math.max(44, 47.222 * this.scale))
    .height(Math.max(44, 47.222 * this.scale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.tokens().buttonRadius)
    .backgroundColor(this.tokens().surfaceAlt)
    .onClick(() => {
      this.onAction('gesture');
    })
  }

  @Builder
  PptExitCard() {
    Column() {
      Text('退出')
        .fontSize(11 * this.scale)
        .fontColor(this.tokens().textPrimary)
    }
    .width(Math.max(44, 41.667 * this.scale))
    .height(Math.max(44, 27.778 * this.scale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.tokens().buttonRadius)
    .backgroundColor(this.tokens().badgeBackground)
    .onClick(() => {
      this.onAction('pptExit');
    })
  }
}
