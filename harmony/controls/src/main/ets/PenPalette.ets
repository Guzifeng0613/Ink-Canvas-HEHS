// @parity src/InkCanvas.Controls/Controls/PenPalette.cs
import {
  HIGHLIGHTER_ALPHA_DEFAULT,
  HIGHLIGHTER_ALPHA_MAX,
  HIGHLIGHTER_ALPHA_MIN,
  HIGHLIGHTER_ALPHA_STEP,
  HIGHLIGHTER_COLORS,
  PEN_COLORS_DARK,
  PEN_COLORS_LIGHT,
  PEN_WIDTH_MAX,
  PEN_WIDTH_MIN,
  PEN_WIDTH_STEP,
  packedSwatch,
  snap,
  taperKindForIndex,
  toStoredPenWidth
} from './PenPaletteCatalog';
import type { Swatch } from './PenPaletteCatalog';
import { ThemeTokens, themeTokens } from './ThemeTokens';

@Component
export struct PenPalette {
  @Prop highlighter: boolean = false;
  @Prop dark: boolean = true;
  @Prop lightColorScheme: boolean = false;
  @Prop widthMenu: number = 5;
  @Prop alpha: number = HIGHLIGHTER_ALPHA_DEFAULT;
  @Prop taperIndex: number = 0;
  @Prop currentPacked: number = 0xFF000000;
  @Prop scale: number = 1;
  onColor: (packed: number) => void = (packed: number) => {
    void packed;
  };
  onWidth: (storedWidth: number) => void = (storedWidth: number) => {
    void storedWidth;
  };
  onAlpha: (alpha: number) => void = (alpha: number) => {
    void alpha;
  };
  onTaper: (kind: number) => void = (kind: number) => {
    void kind;
  };
  onToggleScheme: () => void = () => {
  };
  onClose: () => void = () => {
  };

  private tokens(): ThemeTokens {
    return themeTokens(this.dark);
  }

  private swatches(): Swatch[] {
    if (this.highlighter) {
      return HIGHLIGHTER_COLORS;
    }
    return this.lightColorScheme ? PEN_COLORS_LIGHT : PEN_COLORS_DARK;
  }

  private isSelected(swatch: Swatch): boolean {
    const packed = packedSwatch(swatch);
    return ((packed >>> 8) & 0xFFFFFF) === ((this.currentPacked >>> 8) & 0xFFFFFF);
  }

  build() {
    Column() {
      this.TitleBand()
      Column({ space: 10 * this.scale }) {
        if (!this.highlighter) {
          this.TaperRow()
          this.WidthRow()
          this.ColorGrid()
        } else {
          this.AlphaRow()
          this.ColorGrid()
          this.WidthRow()
          Text('原菜单范围 20–240，步长 20。')
            .fontSize(12 * this.scale)
            .fontColor(this.tokens().textSecondary)
            .width('100%')
        }
      }
      .padding(12 * this.scale)
    }
    .width((this.highlighter ? 208 : 248) * this.scale)
    .backgroundColor(this.tokens().floatBarBackground)
    .border({ width: 1, color: this.tokens().floatBarBorder })
    .borderRadius(5 * this.scale)
  }

  @Builder
  TitleBand() {
    Row() {
      Text(this.highlighter ? '荧光笔' : '墨迹选项')
        .fontSize(12 * this.scale)
        .fontWeight(FontWeight.Bold)
        .fontColor(this.tokens().textPrimary)
        .layoutWeight(1)
      Text('×')
        .fontSize(14 * this.scale)
        .fontColor(this.tokens().textSecondary)
        .onClick(() => {
          this.onClose();
        })
    }
    .width('100%')
    .padding({ left: 15 * this.scale, right: 15 * this.scale, top: 5 * this.scale, bottom: 5 * this.scale })
    .backgroundColor(this.tokens().surfaceAlt)
    .borderRadius({ topLeft: 5 * this.scale, topRight: 5 * this.scale })
  }

  @Builder
  TaperRow() {
    Row() {
      Text('笔锋')
        .fontSize(14 * this.scale)
        .fontColor(this.tokens().textPrimary)
        .margin({ right: 10 * this.scale })
      Row({ space: 6 * this.scale }) {
        ForEach(['点集', '速率', '关闭'], (label: string, index: number) => {
          Text(label)
            .fontSize(13 * this.scale)
            .fontColor(this.taperIndex === index ? Color.White : this.tokens().textPrimary)
            .backgroundColor(this.taperIndex === index ? this.tokens().accent : Color.Transparent)
            .borderRadius(4 * this.scale)
            .padding({ left: 8 * this.scale, right: 8 * this.scale, top: 4 * this.scale, bottom: 4 * this.scale })
            .onClick(() => {
              this.onTaper(taperKindForIndex(index));
            })
        }, (label: string) => label)
      }
      .layoutWeight(1)
      .justifyContent(FlexAlign.End)
    }
    .width('100%')
    .height(32 * this.scale)
  }

  @Builder
  WidthRow() {
    Row() {
      Text('粗细')
        .fontSize(14 * this.scale)
        .fontColor(this.tokens().textPrimary)
        .margin({ right: 10 * this.scale })
      Slider({
        value: this.widthMenu,
        min: PEN_WIDTH_MIN,
        max: PEN_WIDTH_MAX,
        step: PEN_WIDTH_STEP
      })
        .layoutWeight(1)
        .onChange((value: number) => {
          const snapped = snap(value, PEN_WIDTH_MIN, PEN_WIDTH_STEP);
          this.onWidth(toStoredPenWidth(snapped));
        })
      Text(Math.round(this.widthMenu).toString())
        .fontSize(14 * this.scale)
        .fontColor(this.tokens().textPrimary)
        .width(32 * this.scale)
        .textAlign(TextAlign.End)
    }
    .width('100%')
    .height(32 * this.scale)
  }

  @Builder
  AlphaRow() {
    Row() {
      Text('透明度')
        .fontSize(14 * this.scale)
        .fontColor(this.tokens().textPrimary)
        .margin({ right: 10 * this.scale })
      Slider({
        value: this.alpha,
        min: HIGHLIGHTER_ALPHA_MIN,
        max: HIGHLIGHTER_ALPHA_MAX,
        step: HIGHLIGHTER_ALPHA_STEP
      })
        .layoutWeight(1)
        .onChange((value: number) => {
          this.onAlpha(snap(value, HIGHLIGHTER_ALPHA_MIN, HIGHLIGHTER_ALPHA_STEP));
        })
      Text(Math.round(this.alpha).toString())
        .fontSize(14 * this.scale)
        .fontColor(this.tokens().textPrimary)
        .width(32 * this.scale)
        .textAlign(TextAlign.End)
    }
    .width('100%')
    .height(32 * this.scale)
  }

  @Builder
  ColorGrid() {
    Flex({ wrap: FlexWrap.Wrap, justifyContent: FlexAlign.Center }) {
      if (!this.highlighter) {
        Column() {
          Text(this.lightColorScheme ? '暗' : '亮')
            .fontSize(14 * this.scale)
            .fontColor(this.tokens().textPrimary)
          Text(this.lightColorScheme ? '暗系' : '亮系')
            .fontSize(9 * this.scale)
            .fontColor(this.tokens().textSecondary)
        }
        .width(40 * this.scale)
        .height(40 * this.scale)
        .margin(2 * this.scale)
        .justifyContent(FlexAlign.Center)
        .border({ width: 1.5, color: this.tokens().floatBarBorder })
        .borderRadius(20 * this.scale)
        .onClick(() => {
          this.onToggleScheme();
        })
      }
      ForEach(this.swatches(), (swatch: Swatch) => {
        Stack() {
          Column()
            .width(40 * this.scale)
            .height(40 * this.scale)
            .backgroundColor(CanvasInkRendererStyle(swatch))
            .border({ width: 1, color: this.tokens().floatBarBorder })
            .borderRadius(20 * this.scale)
          if (this.isSelected(swatch)) {
            Text('✓')
              .fontSize(16 * this.scale)
              .fontColor(swatch.Name === '白色' ? Color.Black : Color.White)
          }
        }
        .width(44 * this.scale)
        .height(44 * this.scale)
        .onClick(() => {
          this.onColor(packedSwatch(swatch));
        })
      }, (swatch: Swatch) => swatch.Name)
    }
    .width('100%')
  }
}

function CanvasInkRendererStyle(swatch: Swatch): string {
  return 'rgb(' + swatch.R + ',' + swatch.G + ',' + swatch.B + ')';
}
