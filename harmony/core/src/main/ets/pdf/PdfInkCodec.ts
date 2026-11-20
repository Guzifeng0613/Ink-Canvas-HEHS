// @parity src/InkCanvas.Pdf/PdfInkCodec.cs
// D8 纯逻辑（二）：PDF 注释内的无损二进制墨迹状态（I CK2 魔数/zlib 外壳之下的字节格式）。
// 压缩外壳在两端解耦：Node 向量用 node:zlib，真机用 @ohos.zlib（宿主负责）。
// 字节格式与 C# 逐字段一致：小端；float=IEEE754 单精度；int32；bool=1 字节。
// 注意：JS 无 float32 视图写入口——用 Float32Array 编解码保证单精度往返（与 C# ReadSingle/Write 一致）。
import { InkStroke, Rect, SelectionFrameState } from '../ink/InkStroke';
import type { ShapeSourceData } from '../ink/InkStroke';
import { InkPoint } from '../ink/InkPoint';
import { InkColor } from '../ink/InkColor';
import { StrokeAttributes, TaperKind, PenNib } from '../ink/StrokeAttributes';

const MAGIC = 0x324b4349; // "ICK2" 小端读入值（C# int Magic = 0x324B4349）
const LEGACY_MAGIC = 0x314b4349;
export const PDF_INK_MAXIMUM_POINTS = 2000000;

class Writer {
  private buffer: ArrayBuffer = new ArrayBuffer(1024);
  private view: DataView = new DataView(this.buffer);
  private length: number = 0;
  private scratch: Float32Array = new Float32Array(1);

  private ensure(extra: number): void {
    if (this.length + extra <= this.buffer.byteLength) {
      return;
    }
    let capacity = this.buffer.byteLength * 2;
    while (capacity < this.length + extra) {
      capacity *= 2;
    }
    const next = new ArrayBuffer(capacity);
    new Uint8Array(next).set(new Uint8Array(this.buffer, 0, this.length));
    this.buffer = next;
    this.view = new DataView(this.buffer);
  }

  byte(value: number): void {
    this.ensure(1);
    this.view.setUint8(this.length, value);
    this.length += 1;
  }

  int32(value: number): void {
    this.ensure(4);
    this.view.setInt32(this.length, value, true);
    this.length += 4;
  }

  int64(value: number): void {
    this.ensure(8);
    this.view.setBigInt64(this.length, BigInt(Math.trunc(value)), true);
    this.length += 8;
  }

  float(value: number): void {
    this.ensure(4);
    this.scratch[0] = value;
    this.view.setFloat32(this.length, this.scratch[0], true);
    this.length += 4;
  }

  bytes(values: number[] | Uint8Array): void {
    this.ensure(values.length);
    for (let i = 0; i < values.length; i++) {
      this.view.setUint8(this.length + i, values[i]);
    }
    this.length += values.length;
  }

  text(value: string, limit: number, total: { value: number }): void {
    const encoded = utf8Encode(value === null ? '' : value);
    if (encoded.length > limit || (total.value += encoded.length) > 16 * 1024 * 1024) {
      throw new RangeError('PDF shape definition is too large.');
    }
    this.int32(encoded.length);
    this.bytes(encoded);
  }

  toBytes(): Uint8Array {
    return new Uint8Array(this.buffer, 0, this.length).slice();
  }
}

class Reader {
  private view: DataView;
  private data: Uint8Array;
  private position: number = 0;
  private scratch: Float32Array = new Float32Array(1);

  constructor(data: Uint8Array) {
    this.data = data;
    this.view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  }

  private ensure(extra: number): void {
    if (this.position + extra > this.data.length) {
      throw new RangeError('End of stream.');
    }
  }

  atEnd(): boolean {
    return this.position >= this.bytes.length;
  }

  byte(): number {
    this.ensure(1);
    const value = this.view.getUint8(this.position);
    this.position += 1;
    return value;
  }

  boolean(): boolean {
    return this.byte() !== 0;
  }

  int32(): number {
    this.ensure(4);
    const value = this.view.getInt32(this.position, true);
    this.position += 4;
    return value;
  }

  uint32(): number {
    this.ensure(4);
    const value = this.view.getUint32(this.position, true);
    this.position += 4;
    return value;
  }

  int64(): number {
    this.ensure(8);
    const value = this.view.getBigInt64(this.position, true);
    this.position += 8;
    return Number(value);
  }

  float(): number {
    this.ensure(4);
    this.scratch[0] = this.view.getFloat32(this.position, true);
    this.position += 4;
    return this.scratch[0];
  }

  bytes(count: number): Uint8Array {
    this.ensure(count);
    const value = this.data.slice(this.position, this.position + count);
    this.position += count;
    return value;
  }

  text(limit: number, total: { value: number }): string {
    const length = this.int32();
    if (length < 0 || length > limit || (total.value += length) > 16 * 1024 * 1024) {
      throw new RangeError('PDF shape definition is too large.');
    }
    return utf8Decode(this.bytes(length));
  }
}

import { util } from '@kit.ArkTS';

const utf8Encoder: util.TextEncoder = util.TextEncoder.create('utf-8');
const utf8Decoder: util.TextDecoder = util.TextDecoder.create('utf-8');

function utf8Encode(value: string): Uint8Array {
  return utf8Encoder.encodeInto(value);
}

function utf8Decode(values: Uint8Array): string {
  return utf8Decoder.decodeToString(values);
}

// GUID：C# 写 Guid.ToByteArray()（16 字节，.NET 混合序）；两侧按 16 不透明字节往返，
// ArkTS 侧用 'g' + hex 表示（跨端语义为不透明 id）。
function guidBytes(id: string): number[] {
  const hex = (id.length >= 34 ? id.substring(2) : id).replace(/-/g, '');
  const out: number[] = [];
  for (let i = 0; i < 16; i++) {
    out.push(parseInt(hex.length >= 32 ? hex.substring(i * 2, i * 2 + 2) : '00', 16));
  }
  return out;
}

function guidFromBytes(values: Uint8Array): string {
  let hex = '';
  for (let i = 0; i < values.length; i++) {
    hex += values[i].toString(16).padStart(2, '0');
  }
  return 'g' + hex;
}

function encodeId(writer: Writer, id: string): void {
  const bytes = guidBytes(id);
  if (bytes.some((value: number): boolean => Number.isNaN(value))) {
    // 非 32 位 hex 的 id（如 ArkTS 'stroke-3'）：写确定性哈希 16 字节（往返保留原 id 不保证——
    // C# 侧 GUID 域；此处文档化：跨端往返以字节为准）。
    let hash = 0;
    for (let i = 0; i < id.length; i++) {
      hash = (hash * 31 + id.charCodeAt(i)) | 0;
    }
    for (let i = 0; i < 16; i++) {
      bytes[i] = (hash >>> ((i % 4) * 8)) & 0xFF;
    }
  }
  writer.bytes(bytes);
}

export interface PdfInkSummary {
  Id: string;
  LayerOrder: number | null;
  IsRecognizedShape: boolean;
  Color: number;
  BaseWidth: number;
  IsHighlighter: boolean;
  HighlighterAlpha: number;
  TaperKind: number;
  Nib: number;
  NibRotation: number;
  ObjectFrame: number[] | null;
  ShapeSourceGroup: string | null;
  ShapeSourceProvider: string | null;
  ShapeSourceDefinition: string | null;
  Points: number[][];
}

/** 编码为未压缩字节流（zlib 外壳由宿主套上：C# ZLibStream / node:zlib / @ohos.zlib）。 */
export function encodePdfInk(strokes: InkStroke[]): Uint8Array {
  if (strokes === null || strokes.length > 100000) {
    throw new RangeError('Invalid PDF ink count.');
  }
  let totalPoints = 0;
  for (let i = 0; i < strokes.length; i++) {
    if (strokes[i] === null || strokes[i].Count === 0) {
      throw new RangeError('Invalid PDF ink count.');
    }
    totalPoints += strokes[i].Count;
  }
  if (totalPoints > PDF_INK_MAXIMUM_POINTS) {
    throw new RangeError('Invalid PDF ink count.');
  }
  const writer = new Writer();
  const sourceBytes = { value: 0 };
  writer.int32(MAGIC);
  writer.int32(strokes.length);
  for (let i = 0; i < strokes.length; i++) {
    const stroke = strokes[i];
    const a = stroke.Attributes;
    encodeId(writer, stroke.Id);
    writer.byte(stroke.LayerOrder !== null ? 1 : 0);
    if (stroke.LayerOrder !== null) {
      writer.int64(stroke.LayerOrder);
    }
    writer.byte(stroke.IsRecognizedShape ? 1 : 0);
    writer.int32(a.Color.packedArgb() | 0);
    writer.float(a.BaseWidth);
    writer.byte(a.IsHighlighter ? 1 : 0);
    writer.byte(a.HighlighterAlpha);
    writer.int32(a.TaperKind);
    writer.int32(a.Nib);
    writer.float(a.NibRotation);
    const frame = stroke.exportObjectFrame();
    writer.byte(frame !== null ? 1 : 0);
    if (frame !== null) {
      for (let j = 0; j < frame.length; j++) {
        writer.float(frame[j]);
      }
    }
    writer.byte(stroke.ShapeSource !== null ? 1 : 0);
    if (stroke.ShapeSource !== null) {
      writer.bytes(guidBytes(stroke.ShapeSource.GroupId));
      writer.text(stroke.ShapeSource.Provider, 128, sourceBytes);
      writer.text(stroke.ShapeSource.Definition, 16384, sourceBytes);
    }
    writer.int32(stroke.Count);
    for (let j = 0; j < stroke.Count; j++) {
      const p = stroke.Points[j];
      writer.float(p.X);
      writer.float(p.Y);
      writer.float(p.Pressure);
      writer.float(p.Width);
      writer.float(p.TimeMs);
    }
  }
  return writer.toBytes();
}

export function decodePdfInkSummary(data: Uint8Array): PdfInkSummary[] {
  const reader = new Reader(data);
  const magic = reader.int32();
  if (magic !== MAGIC && magic !== LEGACY_MAGIC) {
    throw new RangeError('Unsupported PDF ink metadata.');
  }
  const count = reader.int32();
  if (count < 0 || count > 100000) {
    throw new RangeError('Invalid PDF ink count.');
  }
  const result: PdfInkSummary[] = [];
  const sourceBytes = { value: 0 };
  let total = 0;
  for (let i = 0; i < count; i++) {
    const id = guidFromBytes(reader.bytes(16));
    const hasLayer = reader.boolean();
    const layer = hasLayer ? reader.int64() : null;
    const shape = reader.boolean();
    const color = reader.uint32();
    const baseWidth = reader.float();
    if (!Number.isFinite(baseWidth) || baseWidth <= 0 || baseWidth > 1000000) {
      throw new RangeError('Invalid PDF brush metadata.');
    }
    const isHighlighter = reader.boolean();
    const highlighterAlpha = reader.byte();
    const taperKind = reader.int32();
    const nib = reader.int32();
    const nibRotation = reader.float();
    if (!Number.isFinite(nibRotation) || taperKind < 0 || taperKind > 6 || nib < 0 || nib > 1) {
      throw new RangeError('Invalid PDF brush metadata.');
    }
    let frame: number[] | null = null;
    if (reader.boolean()) {
      frame = [];
      for (let j = 0; j < 10; j++) {
        const value = reader.float();
        if (!Number.isFinite(value)) {
          throw new RangeError('Invalid PDF object frame.');
        }
        frame.push(value);
      }
    }
    let group: string | null = null;
    let provider: string | null = null;
    let definition: string | null = null;
    if (magic === MAGIC && reader.boolean()) {
      group = guidFromBytes(reader.bytes(16));
      provider = reader.text(128, sourceBytes);
      definition = reader.text(16384, sourceBytes);
    }
    const pointCount = reader.int32();
    if (pointCount <= 0 || pointCount > PDF_INK_MAXIMUM_POINTS || (total += pointCount) > PDF_INK_MAXIMUM_POINTS) {
      throw new RangeError('PDF ink point limit exceeded.');
    }
    const points: number[][] = [];
    for (let j = 0; j < pointCount; j++) {
      const x = reader.float();
      const y = reader.float();
      const pressure = reader.float();
      const width = reader.float();
      const time = reader.float();
      if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(pressure)
        || !Number.isFinite(width) || !Number.isFinite(time)
        || Math.abs(x) > 10000000 || Math.abs(y) > 10000000 || width < 0 || width > 1000000) {
        throw new RangeError('Invalid PDF ink point.');
      }
      points.push([x, y, pressure, width, time]);
    }
    result.push({
      Id: id, LayerOrder: layer, IsRecognizedShape: shape,
      Color: color, BaseWidth: baseWidth, IsHighlighter: isHighlighter,
      HighlighterAlpha: highlighterAlpha, TaperKind: taperKind, Nib: nib, NibRotation: nibRotation,
      ObjectFrame: frame, ShapeSourceGroup: group, ShapeSourceProvider: provider,
      ShapeSourceDefinition: definition, Points: points
    });
  }
  if (!reader.atEnd()) {
    throw new RangeError('Unexpected PDF ink data.');
  }
  return result;
}
// ---- 确定性向量入口 ----------------------------------------------------------

interface StrokeInput {
  points: number[][];
  color: number;
  baseWidth: number;
  isHighlighter: boolean;
  highlighterAlpha: number;
  taperKind: number;
  nib: number;
  nibRotation: number;
  objectFrame: number[] | null;
  layerOrder: number;
  groupId?: string;
  provider?: string;
  definition?: string;
}

function buildCodecStroke(input: StrokeInput): InkStroke {
  const attributes = new StrokeAttributes();
  attributes.Color = InkColor.fromPackedArgb(input.color);
  attributes.BaseWidth = input.baseWidth;
  attributes.IsHighlighter = input.isHighlighter;
  attributes.HighlighterAlpha = input.highlighterAlpha;
  attributes.TaperKind = input.taperKind as TaperKind;
  attributes.Nib = input.nib as PenNib;
  attributes.NibRotation = input.nibRotation;
  const stroke = new InkStroke(attributes);
  stroke.LayerOrder = input.layerOrder;
  if (input.objectFrame !== null) {
    stroke.importObjectFrame(input.objectFrame);
  }
  if (input.groupId !== undefined && input.provider !== undefined && input.definition !== undefined) {
    const source: ShapeSourceData = {
      GroupId: input.groupId,
      Provider: input.provider,
      Definition: input.definition
    };
    stroke.ShapeSource = source;
  }
  const points: InkPoint[] = [];
  for (let i = 0; i < input.points.length; i++) {
    const row = input.points[i];
    points.push(new InkPoint(row[0], row[1], row[2], row[3], row[4]));
  }
  stroke.restorePoints(points);
  stroke.complete();
  return stroke;
}

export function pdfInkRoundtripProbe(strokes: StrokeInput[]): Object {
  try {
    const built = strokes.map((input: StrokeInput): InkStroke => buildCodecStroke(input));
    const encoded = encodePdfInk(built);
    const decoded = decodePdfInkSummary(encoded);
    const result: Record<string, Object> = { Strokes: decoded.length };
    if (decoded.length > 0) {
      const first = decoded[0];
      result['FirstPoint'] = first.Points[0];
      result['LastPoint'] = first.Points[first.Points.length - 1];
      if (first.LayerOrder !== null) {
        result['LayerOrder'] = first.LayerOrder;
      }
      result['IsHighlighter'] = first.IsHighlighter;
      result['HighlighterAlpha'] = first.HighlighterAlpha;
      result['TaperKind'] = first.TaperKind;
      result['Nib'] = first.Nib;
      result['NibRotation'] = first.NibRotation;
      if (first.ObjectFrame !== null) {
        result['ObjectFrame'] = first.ObjectFrame;
      }
      if (first.ShapeSourceGroup !== null) {
        result['ShapeSourceGroup'] = first.ShapeSourceGroup;
        result['ShapeSourceProvider'] = first.ShapeSourceProvider;
        result['ShapeSourceDefinition'] = first.ShapeSourceDefinition;
      }
    }
    if (decoded.length > 1) {
      const second = decoded[1];
      result['SecondLayerOrder'] = second.LayerOrder;
      result['SecondColor'] = second.Color;
      result['SecondFirstPoint'] = second.Points[0];
    }
    return result;
  } catch (e) {
    return 'ERROR:' + (e as Error).message;
  }
}

export function pdfInkDecodeGarbageProbe(bytes: number[]): Object {
  try {
    decodePdfInkSummary(new Uint8Array(bytes));
    return 'ERROR:NO ERROR';
  } catch (e) {
    return 'ERROR:' + (e as Error).message;
  }
}
