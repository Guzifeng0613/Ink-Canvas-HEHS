// @parity src/InkCanvas.Pdf/PdfPageGeometry.cs + PdfPageRange.cs + PdfContinuousLayout.cs
// D8 纯逻辑（一）：PDF 页几何（CropBox/UserUnit/旋转 ↔ 96DIP）、页码范围解析、连续文档布局。
import { Rect } from '../ink/InkStroke';
import { Matrix3x2, multiplyMatrix, createTranslationMatrix } from '../math/Matrix3x2';

export interface PdfPoint {
  X: number;
  Y: number;
}

/** 显示侧（已旋转 CropBox，96DIP）↔ PDF 用户空间 映射。 */
export class PdfPageGeometry {
  readonly Left: number;
  readonly Bottom: number;
  readonly Right: number;
  readonly Top: number;
  readonly Rotation: number;
  readonly UserUnit: number;

  constructor(left: number, bottom: number, right: number, top: number, rotation: number, userUnit: number = 1) {
    this.Left = left;
    this.Bottom = bottom;
    this.Right = right;
    this.Top = top;
    this.Rotation = rotation;
    this.UserUnit = userUnit;
  }

  get NormalizedRotation(): number {
    return ((this.Rotation % 360) + 360) % 360;
  }

  get Width(): number {
    return this.Right - this.Left;
  }

  get Height(): number {
    return this.Top - this.Bottom;
  }

  get DipWidth(): number {
    return (this.NormalizedRotation === 90 || this.NormalizedRotation === 270 ? this.Height : this.Width)
      * this.UserUnit * 96 / 72;
  }

  get DipHeight(): number {
    return (this.NormalizedRotation === 90 || this.NormalizedRotation === 270 ? this.Width : this.Height)
      * this.UserUnit * 96 / 72;
  }

  ToPdf(point: PdfPoint): PdfPoint {
    const factor = 72 / (96 * this.UserUnit);
    const x = point.X * factor;
    const y = point.Y * factor;
    if (this.NormalizedRotation === 90) {
      return { X: this.Left + y, Y: this.Bottom + x };
    }
    if (this.NormalizedRotation === 180) {
      return { X: this.Right - x, Y: this.Bottom + y };
    }
    if (this.NormalizedRotation === 270) {
      return { X: this.Right - y, Y: this.Top - x };
    }
    return { X: this.Left + x, Y: this.Top - y };
  }

  FromPdf(point: PdfPoint): PdfPoint {
    const factor = 96 * this.UserUnit / 72;
    if (this.NormalizedRotation === 90) {
      return { X: (point.Y - this.Bottom) * factor, Y: (point.X - this.Left) * factor };
    }
    if (this.NormalizedRotation === 180) {
      return { X: (this.Right - point.X) * factor, Y: (point.Y - this.Bottom) * factor };
    }
    if (this.NormalizedRotation === 270) {
      return { X: (this.Top - point.Y) * factor, Y: (this.Right - point.X) * factor };
    }
    return { X: (point.X - this.Left) * factor, Y: (this.Top - point.Y) * factor };
  }
}

/** 解析用户输入的 1 基页码范围（"1-5,8"，中文逗号兼容），返回 0 基去重升序；容量有界。 */
export function parsePdfPageRange(value: string, pageCount: number, maximum: number): number[] {
  const pages = new Set<number>();
  const entries = (value === null ? '' : value).replace('，', ',')
    .split(',').map((entry: string): string => entry.trim()).filter((entry: string): boolean => entry.length > 0);
  for (let i = 0; i < entries.length; i++) {
    const parts = entries[i].split('-').map((part: string): string => part.trim());
    if (parts.length > 2) {
      throw new RangeError('页码范围无效，请输入 1-5,8 这样的范围。');
    }
    const start = Number(parts[0]);
    const end = Number(parts[parts.length - 1]);
    if (!Number.isInteger(start) || !Number.isInteger(end) || String(start) !== parts[0] || String(end) !== parts[parts.length - 1]
      || start < 1 || end < start || end > pageCount) {
      throw new RangeError('页码范围无效，请输入 1-5,8 这样的范围。');
    }
    if (end - start + 1 > maximum) {
      throw new RangeError('所选页数超过画板剩余容量。');
    }
    for (let p = start; p <= end; p++) {
      pages.add(p - 1);
      if (pages.size > maximum) {
        throw new RangeError('所选页数超过画板剩余容量。');
      }
    }
  }
  if (pages.size === 0) {
    throw new RangeError('请至少选择一页。');
  }
  const result = Array.from(pages);
  result.sort((a: number, b: number): number => a - b);
  return result;
}

/** 连续文档坐标（页垂直排布、水平居中、24 间距）与页内注释坐标互转。 */
export class PdfContinuousLayout {
  private readonly pages: Rect[] = [];
  readonly Width: number;
  readonly Height: number;

  constructor(geometries: PdfPageGeometry[], gap: number = 24) {
    if (geometries.length === 0) {
      throw new RangeError('A PDF must contain at least one page.');
    }
    let width = 0;
    for (let i = 0; i < geometries.length; i++) {
      width = Math.max(width, geometries[i].DipWidth);
    }
    this.Width = width;
    let y = 0;
    for (let index = 0; index < geometries.length; index++) {
      const page = geometries[index];
      const x = (width - page.DipWidth) / 2;
      this.pages.push(new Rect(x, y, x + page.DipWidth, y + page.DipHeight));
      y += page.DipHeight + gap;
    }
    this.Height = y - gap;
  }

  get Count(): number {
    return this.pages.length;
  }

  Page(index: number): Rect {
    return this.pages[index];
  }

  PageView(index: number, documentView: Matrix3x2): Matrix3x2 {
    return multiplyMatrix(createTranslationMatrix(this.pages[index].Left, this.pages[index].Top), documentView);
  }

  DocumentView(index: number, pageView: Matrix3x2): Matrix3x2 {
    return multiplyMatrix(createTranslationMatrix(-this.pages[index].Left, -this.pages[index].Top), pageView);
  }

  NearestPage(y: number): number {
    let low = 0;
    let high = this.Count - 1;
    while (low < high) {
      const middle = Math.floor((low + high) / 2);
      if (y > (this.pages[middle].Bottom + this.pages[middle + 1].Top) / 2) {
        low = middle + 1;
      } else {
        high = middle;
      }
    }
    return low;
  }

  HitTest(point: PdfPoint): number {
    const index = this.NearestPage(point.Y);
    return this.pages[index].contains(point.X, point.Y) ? index : -1;
  }

  VisiblePages(top: number, bottom: number): number[] {
    const result: number[] = [];
    for (let index = this.NearestPage(top); index < this.Count && this.pages[index].Top <= bottom; index++) {
      if (this.pages[index].Bottom >= top) {
        result.push(index);
      }
    }
    return result;
  }
}

// 确定性向量入口。
export function pdfGeometryProbe(
  left: number, bottom: number, right: number, top: number, rotation: number, userUnit: number
): Object {
  const geometry = new PdfPageGeometry(left, bottom, right, top, rotation, userUnit);
  return {
    DipWidth: geometry.DipWidth,
    DipHeight: geometry.DipHeight,
    ToPdf: [geometry.ToPdf({ X: 96, Y: 48 }).X, geometry.ToPdf({ X: 96, Y: 48 }).Y],
    FromPdf: [geometry.FromPdf({ X: left + 72, Y: top - 36 }).X, geometry.FromPdf({ X: left + 72, Y: top - 36 }).Y]
  };
}

export function pdfRangeProbe(value: string, pageCount: number, maximum: number): Object {
  try {
    return parsePdfPageRange(value, pageCount, maximum);
  } catch (e) {
    return 'ERROR:' + (e as Error).message;
  }
}

export function pdfLayoutProbe(
  sizes: number[][], gap: number, ops: string[]
): Object {
  const geometries: PdfPageGeometry[] = [];
  for (let i = 0; i < sizes.length; i++) {
    geometries.push(new PdfPageGeometry(0, 0, sizes[i][0], sizes[i][1], 0, 1));
  }
  const layout = new PdfContinuousLayout(geometries, gap);
  const result: Record<string, Object> = {
    Width: layout.Width,
    Height: layout.Height
  };
  const pages: number[][] = [];
  for (let i = 0; i < layout.Count; i++) {
    const rect = layout.Page(i);
    pages.push([rect.Left, rect.Top, rect.Right, rect.Bottom]);
  }
  result['Pages'] = pages;
  const answers: Object[] = [];
  for (let i = 0; i < ops.length; i++) {
    const parts = ops[i].split(':');
    if (parts[0] === 'nearest') {
      answers.push(layout.NearestPage(Number(parts[1])));
    } else if (parts[0] === 'hit') {
      const values = parts[1].split(',');
      answers.push(layout.HitTest({ X: Number(values[0]), Y: Number(values[1]) }));
    } else if (parts[0] === 'visible') {
      const values = parts[1].split(',');
      answers.push(layout.VisiblePages(Number(values[0]), Number(values[1])));
    }
  }
  result['Ops'] = answers;
  return result;
}