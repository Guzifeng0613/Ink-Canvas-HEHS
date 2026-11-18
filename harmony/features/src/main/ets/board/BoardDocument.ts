// @parity src/InkCanvas.Features/Board/BoardService.cs（分页/元素/保存语义）
import {
  InkDocument,
  InkStroke,
  InkPoint,
  InkColor,
  StrokeAttributes,
  parseBoardPack,
  stringifyBoardPack,
  parseIcink,
  stringifyIcink
} from '@inkcanvas/core';
import type {
  BoardPackSummary,
  BoardPageSummary,
  BoardAssetSummary,
  BoardStrokeSummary
} from '@inkcanvas/core';

export enum BoardBackground {
  Black = 0,
  White = 1,
  Grid = 2,
  Lined = 3
}

export interface BoardAsset {
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

export class BoardPage {
  Document: InkDocument = new InkDocument();
  Images: BoardAsset[] = [];
  Media: BoardAsset[] = [];
}

function assetSummary(asset: BoardAsset): BoardAssetSummary {
  return {
    Id: asset.Id,
    Name: asset.Name,
    Data: asset.Data,
    ContentType: asset.ContentType,
    Left: asset.Left,
    Top: asset.Top,
    Width: asset.Width,
    Height: asset.Height,
    Transform: asset.Transform
  };
}

function assetFromSummary(summary: BoardAssetSummary): BoardAsset {
  return {
    Id: summary.Id,
    Name: summary.Name,
    Data: summary.Data,
    ContentType: summary.ContentType,
    Left: summary.Left,
    Top: summary.Top,
    Width: summary.Width,
    Height: summary.Height,
    Transform: summary.Transform
  };
}

function strokeSummary(stroke: InkStroke): BoardStrokeSummary {
  const points: number[][] = [];
  for (let i = 0; i < stroke.Count; i++) {
    const p = stroke.Points[i];
    points.push([p.X, p.Y, p.Pressure, p.Width, p.TimeMs]);
  }
  return {
    Color: stroke.Attributes.Color.packedArgb(),
    Width: stroke.Attributes.BaseWidth,
    Highlighter: stroke.Attributes.IsHighlighter,
    HighlighterAlpha: stroke.Attributes.HighlighterAlpha,
    Taper: stroke.Attributes.TaperKind,
    Shape: stroke.IsRecognizedShape,
    ObjectFrame: stroke.exportObjectFrame(),
    Points: points
  };
}

function strokeFromSummary(summary: BoardStrokeSummary): InkStroke {
  const attributes = new StrokeAttributes();
  attributes.Color = InkColor.fromPackedArgb(summary.Color);
  attributes.BaseWidth = summary.Width;
  attributes.IsHighlighter = summary.Highlighter;
  attributes.HighlighterAlpha = summary.HighlighterAlpha;
  attributes.TaperKind = summary.Taper;
  const stroke = new InkStroke(attributes);
  stroke.IsRecognizedShape = summary.Shape;
  const points: InkPoint[] = [];
  for (let i = 0; i < summary.Points.length; i++) {
    const row = summary.Points[i];
    if (row.length >= 4) {
      points.push(new InkPoint(row[0], row[1], row[2], row[3], row.length > 4 ? row[4] : 0));
    }
  }
  stroke.restorePoints(points);
  stroke.complete();
  stroke.importObjectFrame(summary.ObjectFrame);
  return stroke;
}

export class BoardDocument {
  static readonly MAX_PAGES: number = 99;
  Pages: BoardPage[] = [new BoardPage()];
  CurrentIndex: number = 0;
  Background: BoardBackground = BoardBackground.Black;
  GridSpacing: number = 40;

  get Current(): BoardPage {
    return this.Pages[this.CurrentIndex];
  }

  get PageCount(): number {
    return this.Pages.length;
  }

  addPage(): boolean {
    if (this.Pages.length >= BoardDocument.MAX_PAGES) {
      return false;
    }
    this.Pages.splice(this.CurrentIndex + 1, 0, new BoardPage());
    this.CurrentIndex += 1;
    return true;
  }

  removePage(index: number): boolean {
    if (this.Pages.length <= 1 || index < 0 || index >= this.Pages.length) {
      return false;
    }
    this.Pages.splice(index, 1);
    if (this.CurrentIndex >= this.Pages.length) {
      this.CurrentIndex = this.Pages.length - 1;
    }
    return true;
  }

  navigate(delta: number): void {
    this.CurrentIndex = Math.min(Math.max(this.CurrentIndex + delta, 0), this.Pages.length - 1);
  }

  toPack(indices: number[]): BoardPackSummary {
    const pages: BoardPageSummary[] = [];
    for (let i = 0; i < indices.length; i++) {
      const page = this.Pages[indices[i]];
      const strokes: BoardStrokeSummary[] = [];
      for (let s = 0; s < page.Document.Strokes.length; s++) {
        strokes.push(strokeSummary(page.Document.Strokes[s]));
      }
      const images: BoardAssetSummary[] = [];
      for (let a = 0; a < page.Images.length; a++) {
        images.push(assetSummary(page.Images[a]));
      }
      const media: BoardAssetSummary[] = [];
      for (let a = 0; a < page.Media.length; a++) {
        media.push(assetSummary(page.Media[a]));
      }
      pages.push({ Strokes: strokes, Images: images, Media: media });
    }
    return {
      Format: 'inkcanvas-board',
      Version: 1,
      Background: backgroundName(this.Background),
      GridSpacing: this.GridSpacing,
      Pages: pages
    };
  }

  saveBoardJson(): string {
    const indices: number[] = [];
    for (let i = 0; i < this.Pages.length; i++) {
      indices.push(i);
    }
    return stringifyBoardPack(this.toPack(indices));
  }

  // C# SavePage：无图片时只保存 .icink（视频会丢失——基线缺陷复刻）。
  savePageJson(index: number): string {
    const page = this.Pages[index];
    if (page.Images.length === 0) {
      return stringifyIcink(page.Document.Strokes);
    }
    return stringifyBoardPack(this.toPack([index]));
  }

  loadJson(text: string): void {
    let pack: BoardPackSummary;
    try {
      pack = parseBoardPack(text);
    } catch (e) {
      // 仅当确实不是画板包（缺少 Format 字段）时才回退到单表面 .icink；
      // 画板包版本/页数/损坏错误向上抛出，避免静默清空当前画板。
      let hasFormat = false;
      try {
        const probe = JSON.parse(text) as Record<string, Object>;
        hasFormat = probe !== null && typeof probe === 'object' && probe['Format'] !== undefined;
      } catch (probeError) {
        hasFormat = false;
      }
      if (hasFormat) {
        throw e;
      }
      const strokes = parseIcink(text);
      const page = new BoardPage();
      page.Document.addRange(strokes);
      this.Pages = [page];
      this.CurrentIndex = 0;
      return;
    }
    const pages: BoardPage[] = [];
    for (let i = 0; i < pack.Pages.length; i++) {
      const source = pack.Pages[i];
      const page = new BoardPage();
      const strokes: InkStroke[] = [];
      for (let s = 0; s < source.Strokes.length; s++) {
        strokes.push(strokeFromSummary(source.Strokes[s]));
      }
      page.Document.addRange(strokes);
      for (let a = 0; a < source.Images.length; a++) {
        page.Images.push(assetFromSummary(source.Images[a]));
      }
      for (let a = 0; a < source.Media.length; a++) {
        page.Media.push(assetFromSummary(source.Media[a]));
      }
      pages.push(page);
    }
    if (pages.length === 0) {
      pages.push(new BoardPage());
    }
    this.Pages = pages;
    this.CurrentIndex = 0;
    if (pack.Background === 'White') {
      this.Background = BoardBackground.White;
    } else if (pack.Background === 'Grid') {
      this.Background = BoardBackground.Grid;
    } else if (pack.Background === 'Lined') {
      this.Background = BoardBackground.Lined;
    }
    if (pack.GridSpacing > 0) {
      this.GridSpacing = Math.min(Math.max(pack.GridSpacing, 12), 160);
    }
  }
}

export interface BoardDocumentSummary {
  Pages: number;
  Current: number;
  Background: string;
  GridSpacing: number;
  CurrentStrokes: number;
  CurrentImages: number;
  CurrentMedia: number;
}

function backgroundName(value: BoardBackground): string {
  if (value === BoardBackground.White) {
    return 'White';
  }
  if (value === BoardBackground.Grid) {
    return 'Grid';
  }
  if (value === BoardBackground.Lined) {
    return 'Lined';
  }
  return 'Black';
}

export function boardRoundtrip(text: string): BoardDocumentSummary {
  const board = new BoardDocument();
  board.loadJson(text);
  const saved = board.saveBoardJson();
  const reloaded = new BoardDocument();
  reloaded.loadJson(saved);
  return boardDocumentSummary(reloaded);
}

export interface BoardSavePageProbe {
  IsBoardPack: boolean;
  MediaCount: number;
}

export function boardSavePageProbe(text: string, index: number): BoardSavePageProbe {
  const board = new BoardDocument();
  board.loadJson(text);
  const saved = board.savePageJson(index);
  try {
    const pack = parseBoardPack(saved);
    return { IsBoardPack: true, MediaCount: pack.Pages[0].Media.length };
  } catch (e) {
    return { IsBoardPack: false, MediaCount: 0 };
  }
}

export function boardDocumentSummary(board: BoardDocument): BoardDocumentSummary {
  return {
    Pages: board.Pages.length,
    Current: board.CurrentIndex,
    Background: BoardBackground[board.Background],
    GridSpacing: board.GridSpacing,
    CurrentStrokes: board.Current.Document.Count,
    CurrentImages: board.Current.Images.length,
    CurrentMedia: board.Current.Media.length
  };
}
