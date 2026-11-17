// @parity src/InkCanvas.Core/Ink/StrokeAttributes.cs
import { InkColor } from './InkColor';

export enum TaperKind {
  None = 0,
  Tail = 1,
  Velocity = 2
}

export class StrokeAttributes {
  Color: InkColor = InkColor.black();
  BaseWidth: number = 3;
  IsHighlighter: boolean = false;
  HighlighterAlpha: number = 96;
  TaperKind: TaperKind = TaperKind.Tail;

  effectiveTaper(): TaperKind {
    return this.IsHighlighter ? TaperKind.None : this.TaperKind;
  }

  effectiveColor(): InkColor {
    return this.IsHighlighter ? this.Color.withAlpha(this.HighlighterAlpha) : this.Color;
  }

  clone(): StrokeAttributes {
    const copy = new StrokeAttributes();
    copy.Color = new InkColor(this.Color.A, this.Color.R, this.Color.G, this.Color.B);
    copy.BaseWidth = this.BaseWidth;
    copy.IsHighlighter = this.IsHighlighter;
    copy.HighlighterAlpha = this.HighlighterAlpha;
    copy.TaperKind = this.TaperKind;
    return copy;
  }
}

export interface StrokeAttributesInput {
  ColorPacked: number;
  BaseWidth: number;
  IsHighlighter: boolean;
  HighlighterAlpha: number;
  TaperKind: number;
}

export interface StrokeAttributesEffective {
  effectiveTaper: number;
  effectiveArgb: number;
  baseWidth: number;
}

// 向量入口：由纯数据计算有效笔锋与有效颜色（不依赖类实例的构造细节）。
export function strokeAttributesEffective(input: StrokeAttributesInput): StrokeAttributesEffective {
  const attributes = new StrokeAttributes();
  attributes.Color = InkColor.fromPackedArgb(input.ColorPacked);
  attributes.BaseWidth = input.BaseWidth;
  attributes.IsHighlighter = input.IsHighlighter === true;
  attributes.HighlighterAlpha = input.HighlighterAlpha;
  attributes.TaperKind = input.TaperKind as TaperKind;
  return {
    effectiveTaper: attributes.effectiveTaper(),
    effectiveArgb: attributes.effectiveColor().packedArgb(),
    baseWidth: attributes.BaseWidth
  };
}
