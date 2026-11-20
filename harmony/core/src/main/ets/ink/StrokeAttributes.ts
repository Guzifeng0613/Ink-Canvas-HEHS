// @parity src/InkCanvas.Core/Ink/StrokeAttributes.cs
import { InkColor } from './InkColor';
import { Matrix3x2, identityMatrix, multiplyMatrix, createScaleMatrix, createRotationMatrix } from '../math/Matrix3x2';

export enum PenNib {
  Round = 0,
  Ellipse45 = 1
}

export enum TaperKind {
  None = 0,
  Tail = 1,
  Velocity = 2,
  SmoothPen = 3,
  SoftPen = 4,
  Natural = 5,
  Calligraphy = 6
}

export class StrokeAttributes {
  Color: InkColor = InkColor.black();
  BaseWidth: number = 3;
  IsHighlighter: boolean = false;
  HighlighterAlpha: number = 96;
  TaperKind: TaperKind = TaperKind.Tail;
  Nib: PenNib = PenNib.Round;
  NibRotation: number = 0;

  nibTransform(): Matrix3x2 {
    if (this.Nib === PenNib.Ellipse45) {
      return multiplyMatrix(createScaleMatrix(1, 0.5), createRotationMatrix(-Math.PI / 4 + this.NibRotation));
    }
    return identityMatrix();
  }

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
    copy.Nib = this.Nib;
    copy.NibRotation = this.NibRotation;
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
