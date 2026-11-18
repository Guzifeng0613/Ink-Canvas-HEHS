// @parity src/InkCanvas.Controls/Controls/FloatingBar.cs
// 四卡布局：拖动把手 / 主栏 / 手势 / PPT 退出牌。平板与 2in1 使用同一组件。
import { FLOATING_BAR_BUTTONS, FloatingBarButton } from './FloatingBarCatalog';
import { ThemeTokens, themeTokens } from './ThemeTokens';
import { IconGlyph, symbolForButton } from './IconGlyph';
import { barCollapseTransition } from './Motion';

@Component
export struct FloatingBar {
  @Prop dark: boolean = false;
  @Prop showLabels: boolean = false;
  @Prop uiScale: number = 1;
  @Prop selectedId: string = 'pen';
  @Prop folded: boolean = false;
  @Prop pptActive: boolean = false;
  @Prop textIconFallback: boolean = true;
  // 能力门控由 entry 探测后传入（controls 不直接依赖 platform）。
  @Prop materialBlur: boolean = false;
  @Prop symbolIcons: boolean = true;
  @Prop reduceMotion: boolean = false;
  // appearance 设置接线（显隐/彩色/透明按钮）。
  @Prop showEraserButton: boolean = true;
  @Prop showHideControlButton: boolean = false;
  @Prop showLRSwitchButton: boolean = false;
  @Prop showExitButton: boolean = false;
  @Prop showFingerToggle: boolean = false;
  @Prop colorfulBackground: boolean = false;
  @Prop transparentButtons: boolean = true;
  onAction: (id: string) => void = (id: string) => {
    void id;
  };
  onDrag: (dx: number, dy: number) => void = (dx: number, dy: number) => {
    void dx;
    void dy;
  };
  onDragFinished: () => void = () => {
  };

  private tokens(): ThemeTokens {
    return themeTokens(this.dark);
  }

  private iconText(button: FloatingBarButton): string {
    return this.textIconFallback ? button.Label.substring(0, 1) : button.Glyph;
  }

  private buttonVisible(button: FloatingBarButton): boolean {
    if (button.Id === 'eraserCircle' || button.Id === 'eraserStroke') {
      return this.showEraserButton;
    }
    if (button.Id === 'hideControl') {
      return this.showHideControlButton;
    }
    if (button.Id === 'lrSwitch') {
      return this.showLRSwitchButton;
    }
    return button.DefaultVisible;
  }

  // 彩色浮条：C# PopupBrushes.FloatBarColorful 渐变 #9580B0FF → #95C0FFC0，(0,0)→(1,1)。
  // 非彩色时用同色双 stop 渐变等效纯色，保持单一渲染路径。
  private barGradient(base: string): LinearGradientOptions {
    if (this.colorfulBackground) {
      return { angle: 135, colors: [['#9580B0FF', 0.0], ['#95C0FFC0', 1.0]] };
    }
    return { angle: 135, colors: [[base, 0.0], [base, 1.0]] };
  }

  private buttonBase(): ResourceColor {
    if (this.transparentButtons) {
      return Color.Transparent;
    }
    return this.dark ? '#14FFFFFF' : '#14000000';
  }

  build() {
    Row({ space: 2.778 * this.uiScale }) {
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
      left: this.tokens().barPadding[0] * this.uiScale,
      top: this.tokens().barPadding[1] * this.uiScale,
      right: this.tokens().barPadding[2] * this.uiScale,
      bottom: this.tokens().barPadding[3] * this.uiScale
    })
    .linearGradient(this.barGradient(this.tokens().floatBarBackground))
    .backgroundBlurStyle(this.materialBlur ? BlurStyle.Thin : BlurStyle.NONE)
    .borderRadius(this.tokens().barCorner)
    .border({ width: 1, color: this.tokens().floatBarBorder })
    .gesture(
      PanGesture()
        .onActionUpdate((event: GestureEvent) => {
          this.onDrag(event.offsetX, event.offsetY);
        })
        .onActionEnd(() => {
          this.onDragFinished();
        })
    )
  }

  @Builder
  DragHandleCard() {
    Column() {
      Text('☺')
        .fontSize(Math.max(16, 27.778 * this.uiScale))
        .fontColor(this.tokens().textPrimary)
    }
    .width(50 * this.uiScale)
    .height(50 * this.uiScale)
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.tokens().buttonRadius)
    .linearGradient(this.barGradient(this.tokens().surfaceAlt))
    .onClick(() => {
      this.onAction('toggleCollapse');
    })
  }

  @Builder
  PrimaryCard() {
    Row({ space: 0 }) {
      ForEach(FLOATING_BAR_BUTTONS, (button: FloatingBarButton) => {
        if (this.buttonVisible(button)) {
          this.ButtonCell(button)
        }
      }, (button: FloatingBarButton) => button.Id)
      if (this.showFingerToggle) {
        this.ExtraCell('fingerMode', '手指', 'hand_tap')
      }
      if (this.showExitButton) {
        this.ExtraCell('exit', '退出', 'xmark')
      }
    }
    .padding({ left: 3, right: 3, top: 0, bottom: 0 })
    .linearGradient(this.barGradient(this.tokens().surface))
    .borderRadius(this.tokens().buttonRadius)
    .transition(barCollapseTransition(this.reduceMotion))
  }

  // 目录外的条件按钮（退出/手指模式），不改 19 按钮目录（floating-bar-catalog 向量钉住）。
  @Builder
  ExtraCell(id: string, label: string, symbol: string) {
    Column() {
      IconGlyph({
        symbol: symbol,
        fallback: this.textIconFallback ? label.substring(0, 1) : '',
        size: 16 * this.uiScale,
        color: this.tokens().textPrimary,
        enabled: this.symbolIcons
      })
      if (this.showLabels) {
        Text(label)
          .fontSize(this.tokens().labelSize * this.uiScale)
          .fontColor(this.tokens().textPrimary)
          .margin({ top: -1.389 * this.uiScale })
      }
    }
    .width(Math.max(44, this.tokens().buttonWidth * this.uiScale))
    .height(Math.max(44, this.tokens().buttonHeight * this.uiScale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.selectedId === id ? this.tokens().selectedRadius : this.tokens().buttonRadius)
    .backgroundColor(this.selectedId === id ? this.tokens().floatBarSelected : this.buttonBase())
    .onClick(() => {
      this.onAction(id);
    })
  }

  @Builder
  ButtonCell(button: FloatingBarButton) {
    Column() {
      IconGlyph({
        symbol: symbolForButton(button.Id, button.Glyph),
        fallback: this.iconText(button),
        size: button.Id === 'gesture' ? 20 * this.uiScale : 16 * this.uiScale,
        color: this.tokens().textPrimary,
        enabled: this.symbolIcons
      })
      if (this.showLabels) {
        Text(button.Label)
          .fontSize(this.tokens().labelSize * this.uiScale)
          .fontColor(this.tokens().textPrimary)
          .margin({ top: -1.389 * this.uiScale })
      }
    }
    .width(Math.max(44, this.tokens().buttonWidth * this.uiScale))
    .height(Math.max(44, this.tokens().buttonHeight * this.uiScale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.selectedId === button.Id ? this.tokens().selectedRadius : this.tokens().buttonRadius)
    .backgroundColor(this.selectedId === button.Id ? this.tokens().floatBarSelected : this.buttonBase())
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
      IconGlyph({
        symbol: 'hand_tap',
        fallback: this.textIconFallback ? '手' : '',
        size: 20 * this.uiScale,
        color: this.tokens().textPrimary,
        enabled: this.symbolIcons
      })
      if (this.showLabels) {
        Text('手势')
          .fontSize(this.tokens().labelSize * this.uiScale)
          .fontColor(this.tokens().textPrimary)
      }
    }
    .width(Math.max(44, 47.222 * this.uiScale))
    .height(Math.max(44, 47.222 * this.uiScale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.tokens().buttonRadius)
    .linearGradient(this.barGradient(this.tokens().surfaceAlt))
    .transition(barCollapseTransition(this.reduceMotion))
    .onClick(() => {
      this.onAction('gesture');
    })
  }

  @Builder
  PptExitCard() {
    Column() {
      Text('退出')
        .fontSize(11 * this.uiScale)
        .fontColor(this.tokens().textPrimary)
    }
    .width(Math.max(44, 41.667 * this.uiScale))
    .height(Math.max(44, 27.778 * this.uiScale))
    .justifyContent(FlexAlign.Center)
    .borderRadius(this.tokens().buttonRadius)
    .linearGradient(this.barGradient(this.tokens().badgeBackground))
    .onClick(() => {
      this.onAction('pptExit');
    })
  }
}