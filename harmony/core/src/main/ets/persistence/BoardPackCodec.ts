// @parity src/InkCanvas.Features/Board/BoardService.cs (BoardFile/BoardPageFile 格式)
// 画板包：Format/Version 严格校验；Background 大小写不敏感；GridSpacing 夹 12..160；
// 页笔画**无 HighlighterAlpha**（v1 缺陷复刻）；资源 Data 保持 base64 字符串。
export interface BoardStrokeSummary {
  Color: number;
  Width: number;
  Highlighter: boolean;
  HighlighterAlpha: number;
  Taper: number;
  Shape: boolean;
  ObjectFrame: number[] | null;
  Points: number[][];
}

export interface BoardAssetSummary {
  Id: string;
  Name: string;
  Data: string;
  ContentType: string;
  Left: number;
  Top: number;
  Width: number;
  Height: number;
  Transform: number[] | null;
}

export interface BoardPageSummary {
  Strokes: BoardStrokeSummary[];
  Images: BoardAssetSummary[];
  Media: BoardAssetSummary[];
}

export interface BoardPackSummary {
  Format: string;
  Version: number;
  Background: string;
  GridSpacing: number;
  Pages: BoardPageSummary[];
}

function asDict(value: object): Record<string, Object> {
  return value as Record<string, Object>;
}

function num(value: Object | undefined, fallback: number): number {
  return typeof value === 'number' && isFinite(value as number) ? value as number : fallback;
}

function str(value: Object | undefined, fallback: string): string {
  return typeof value === 'string' ? value as string : fallback;
}

function bool(value: Object | undefined, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value as boolean : fallback;
}

class RectShape {
  Left: number;
  Top: number;
  Right: number;
  Bottom: number;

  constructor(left: number, top: number, right: number, bottom: number) {
    this.Left = left;
    this.Top = top;
    this.Right = right;
    this.Bottom = bottom;
  }

  isEmpty(): boolean {
    return this.Right - this.Left <= 0 || this.Bottom - this.Top <= 0;
  }
}

function normalizeBackground(value: string): string {
  const lower = value.toLowerCase();
  if (lower === 'white') {
    return 'White';
  }
  if (lower === 'grid') {
    return 'Grid';
  }
  if (lower === 'lined') {
    return 'Lined';
  }
  return 'Black';
}

function parseStroke(raw: object): BoardStrokeSummary | null {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
    return null;
  }
  const dict = asDict(raw);
  let taper = num(dict['Taper'], 0);
  if (taper !== 0 && taper !== 1 && taper !== 2) {
    taper = 0;
  }
  const points: number[][] = [];
  const rawPoints = dict['Points'];
  if (Array.isArray(rawPoints)) {
    const rows = rawPoints as Object[];
    for (let i = 0; i < rows.length; i++) {
      const row = rows[i];
      if (Array.isArray(row) && (row as Object[]).length >= 4) {
        const values = row as number[];
        points.push([
          num(values[0], 0), num(values[1], 0), num(values[2], 0.5),
          num(values[3], 0), values.length > 4 ? num(values[4], 0) : 0
        ]);
      }
    }
  }
  let objectFrame: number[] | null = null;
  const rawFrame = dict['ObjectFrame'];
  if (Array.isArray(rawFrame) && (rawFrame as Object[]).length === 10) {
    const values = rawFrame as number[];
    let finite = true;
    const copy: number[] = [];
    for (let k = 0; k < 10; k++) {
      const v = num(values[k], 0);
      if (!isFinite(v)) {
        finite = false;
      }
      copy.push(v);
    }
    if (finite) {
      const bounds = new RectShape(copy[0], copy[1], copy[2], copy[3]);
      const det = copy[4] * copy[7] - copy[5] * copy[6];
      if (!bounds.isEmpty() && Math.abs(det) > 1e-6) {
        objectFrame = copy;
      }
    }
  }
  return {
    Color: num(dict['Color'], 0),
    Width: num(dict['Width'], 0),
    Highlighter: bool(dict['Highlighter'], false),
    HighlighterAlpha: num(dict['HighlighterAlpha'], 96),
    Taper: taper,
    Shape: bool(dict['Shape'], false),
    ObjectFrame: objectFrame,
    Points: points
  };
}

function parseAsset(raw: object, isVideo: boolean): BoardAssetSummary | null {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
    return null;
  }
  const dict = asDict(raw);
  const contentType = str(dict['ContentType'], isVideo ? 'video/mp4' : 'image/png');
  if (isVideo && !contentType.toLowerCase().startsWith('video/')) {
    return null;
  }
  const transformValue = dict['Transform'];
  let transform: number[] | null = null;
  if (Array.isArray(transformValue) && (transformValue as Object[]).length === 6) {
    const values = transformValue as number[];
    transform = [num(values[0], 1), num(values[1], 0), num(values[2], 0), num(values[3], 1), num(values[4], 0), num(values[5], 0)];
  }
  return {
    Id: str(dict['Id'], ''),
    Name: str(dict['Name'], isVideo ? 'Video' : 'Image'),
    Data: str(dict['Data'], ''),
    ContentType: contentType,
    Left: num(dict['Left'], 0),
    Top: num(dict['Top'], 0),
    Width: num(dict['Width'], isVideo ? 360 : 0),
    Height: num(dict['Height'], isVideo ? 240 : 0),
    Transform: transform
  };
}

function parsePage(raw: object): BoardPageSummary {
  const dict = asDict(raw);
  const strokes: BoardStrokeSummary[] = [];
  const rawStrokes = dict['Strokes'];
  if (Array.isArray(rawStrokes)) {
    const list = rawStrokes as Object[];
    for (let i = 0; i < list.length; i++) {
      const stroke = parseStroke(list[i]);
      if (stroke !== null) {
        strokes.push(stroke);
      }
    }
  }
  const images: BoardAssetSummary[] = [];
  const rawImages = dict['Images'];
  if (Array.isArray(rawImages)) {
    const list = rawImages as Object[];
    for (let i = 0; i < list.length; i++) {
      const image = parseAsset(list[i], false);
      if (image !== null) {
        images.push(image);
      }
    }
  }
  const media: BoardAssetSummary[] = [];
  const rawMedia = dict['Media'];
  if (Array.isArray(rawMedia)) {
    const list = rawMedia as Object[];
    for (let i = 0; i < list.length; i++) {
      const item = parseAsset(list[i], true);
      if (item !== null) {
        media.push(item);
      }
    }
  }
  return { Strokes: strokes, Images: images, Media: media };
}

export function parseBoardPack(text: string): BoardPackSummary {
  const root = JSON.parse(text) as object;
  if (root === null || typeof root !== 'object' || Array.isArray(root)) {
    throw new Error('The board file is empty or invalid.');
  }
  const dict = asDict(root);
  if (str(dict['Format'], '') !== 'inkcanvas-board') {
    throw new Error('Not a board file.');
  }
  const version = num(dict['Version'], 0);
  if (version !== 1) {
    throw new Error('Unsupported board file version ' + version + '.');
  }
  const pages: BoardPageSummary[] = [];
  const rawPages = dict['Pages'];
  if (Array.isArray(rawPages)) {
    const list = rawPages as Object[];
    for (let i = 0; i < list.length; i++) {
      const page = list[i];
      if (page !== null && typeof page === 'object' && !Array.isArray(page)) {
        pages.push(parsePage(page));
      }
    }
  }
  if (pages.length > 99) {
    throw new Error('A board can contain at most 99 pages.');
  }
  if (pages.length === 0) {
    pages.push({ Strokes: [], Images: [], Media: [] });
  }
  let gridSpacing = num(dict['GridSpacing'], 40);
  if (!(gridSpacing > 0)) {
    gridSpacing = 40;
  }
  gridSpacing = Math.min(Math.max(gridSpacing, 12), 160);
  return {
    Format: 'inkcanvas-board',
    Version: 1,
    Background: normalizeBackground(str(dict['Background'], 'Black')),
    GridSpacing: gridSpacing,
    Pages: pages
  };
}

export function stringifyBoardPack(pack: BoardPackSummary): string {
  return JSON.stringify({
    Format: pack.Format,
    Version: pack.Version,
    Background: pack.Background,
    GridSpacing: pack.GridSpacing,
    Pages: pack.Pages
  });
}

export function boardRoundtrip(text: string): BoardPackSummary {
  return parseBoardPack(stringifyBoardPack(parseBoardPack(text)));
}
