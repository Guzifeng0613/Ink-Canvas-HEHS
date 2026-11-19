// @parity src/InkCanvas.Features/Board/BoardService.cs（分页/元素/保存语义）
import {
  InkDocument,
  InkStroke,
  InkPoint,
  InkColor,
  StrokeAttributes,
  PenNib,
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
import { BoardElementHistory } from './BoardElementHistory';

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
  // 页内元素变换撤销历史（镜像 C# Current.History）；与墨迹撤销协调见 entry 撤销接线。
  History: BoardElementHistory = new BoardElementHistory();
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
    Nib: stroke.Attributes.Nib,
    NibRotation: stroke.Attributes.NibRotation,
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
  attributes.Nib = summary.Nib === 1 ? PenNib.Ellipse45 : PenNib.Round;
  attributes.NibRotation = summary.NibRotation;
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

  // 原子清空当前页（C# ClearCurrentPage）：墨迹+图片+视频一并清空，一个页内撤销单元整体恢复。
  // 注：单控制器架构下墨迹撤销栈在翻页时清空（BindDocument），此页内单元走 BoardPage.History。
  clearCurrentPage(): boolean {
    const page = this.Current;
    const strokes = page.Document.Strokes.slice();
    const images = page.Images.slice();
    const media = page.Media.slice();
    if (strokes.length + images.length + media.length === 0) {
      return false;
    }
    const apply = (): void => {
      page.Document.clear();
      page.Images.length = 0;
      page.Media.length = 0;
    };
    const revert = (): void => {
      page.Document.addRange(strokes);
      page.Images.length = 0;
      for (let i = 0; i < images.length; i++) {
        page.Images.push(images[i]);
      }
      page.Media.length = 0;
      for (let i = 0; i < media.length; i++) {
        page.Media.push(media[i]);
      }
    };
    apply();
    page.History.push({ name: 'clear board page', redo: apply, undo: revert });
    return true;
  }

  // 衍至新页（C# CloneSelectionToNewPage）：选中墨迹/图片/视频克隆到新页（共享字节、新 Id），
  // 单撤销单元记在新页 History；翻页携带绘制状态由单控制器天然成立（属性跨页保持）。
  cloneSelectionToNewPage(strokes: InkStroke[], imageIds: string[], mediaIds: string[], idPrefix: string): boolean {
    if (this.Pages.length >= BoardDocument.MAX_PAGES) {
      return false;
    }
    if (strokes.length + imageIds.length + mediaIds.length === 0) {
      return false;
    }
    const clones: InkStroke[] = [];
    for (let i = 0; i < strokes.length; i++) {
      clones.push(strokes[i].clone());
    }
    const source = this.Current;
    const images: BoardAsset[] = [];
    for (let i = 0; i < imageIds.length; i++) {
      for (let j = 0; j < source.Images.length; j++) {
        if (source.Images[j].Id === imageIds[i]) {
          images.push(this.cloneAsset(source.Images[j], idPrefix + '-img-' + i));
          break;
        }
      }
    }
    const media: BoardAsset[] = [];
    for (let i = 0; i < mediaIds.length; i++) {
      for (let j = 0; j < source.Media.length; j++) {
        if (source.Media[j].Id === mediaIds[i]) {
          media.push(this.cloneAsset(source.Media[j], idPrefix + '-media-' + i));
          break;
        }
      }
    }
    if (clones.length + images.length + media.length === 0) {
      return false;
    }
    if (!this.addPage()) {
      return false;
    }
    const page = this.Current;
    const apply = (): void => {
      page.Document.addRange(clones);
      for (let i = 0; i < images.length; i++) {
        page.Images.push(images[i]);
      }
      for (let i = 0; i < media.length; i++) {
        page.Media.push(media[i]);
      }
    };
    const revert = (): void => {
      page.Document.removeAll(clones);
      page.Images = page.Images.filter((asset: BoardAsset): boolean => images.indexOf(asset) < 0);
      page.Media = page.Media.filter((asset: BoardAsset): boolean => media.indexOf(asset) < 0);
    };
    apply();
    page.History.push({ name: 'clone selection to page', redo: apply, undo: revert });
    return true;
  }

  private cloneAsset(asset: BoardAsset, newId: string): BoardAsset {
    return {
      Id: newId,
      Name: asset.Name,
      Data: asset.Data,
      ContentType: asset.ContentType,
      Left: asset.Left,
      Top: asset.Top,
      Width: asset.Width,
      Height: asset.Height,
      Transform: asset.Transform === null ? null : asset.Transform.slice()
    };
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

export function cloneBoardAsset(asset: BoardAsset, newId: string): BoardAsset {
  return {
    Id: newId,
    Name: asset.Name,
    Data: asset.Data,
    ContentType: asset.ContentType,
    Left: asset.Left,
    Top: asset.Top,
    Width: asset.Width,
    Height: asset.Height,
    Transform: asset.Transform === null ? null : asset.Transform.slice()
  };
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

export interface BoardPageCounts {
  pages: number;
  current: number;
  strokes: number;
  images: number;
  media: number;
}

// 确定性向量入口：loadJson 后按序执行 ops，返回每步结果。
// op：'counts' 当前页计数；'clear' 原子清空；'undo'/'redo' 当前页 History；
// 'clone:<笔画索引表>|<图片Id表>|<视频Id表>' 衍至新页（Id 前缀 'v'）。
export function boardPageOpsProbe(text: string, ops: string[]): Object[] {
  const board = new BoardDocument();
  board.loadJson(text);
  const results: Object[] = [];
  for (let i = 0; i < ops.length; i++) {
    const op = ops[i];
    if (op === 'counts') {
      results.push({
        pages: board.Pages.length,
        current: board.CurrentIndex,
        strokes: board.Current.Document.Count,
        images: board.Current.Images.length,
        media: board.Current.Media.length
      } as BoardPageCounts);
    } else if (op === 'clear') {
      results.push(board.clearCurrentPage());
    } else if (op === 'undo') {
      results.push(board.Current.History.undo());
    } else if (op === 'redo') {
      results.push(board.Current.History.redo());
    } else if (op.startsWith('clone:')) {
      const parts = op.substring(6).split('|');
      const indexText = parts.length > 0 ? parts[0] : '';
      const imageText = parts.length > 1 ? parts[1] : '';
      const mediaText = parts.length > 2 ? parts[2] : '';
      const indexes: number[] = [];
      const indexParts = indexText.length > 0 ? indexText.split(',') : [];
      for (let j = 0; j < indexParts.length; j++) {
        indexes.push(Number(indexParts[j]));
      }
      const strokes: InkStroke[] = [];
      for (let j = 0; j < indexes.length; j++) {
        const stroke = board.Current.Document.Strokes[indexes[j]];
        if (stroke !== undefined) {
          strokes.push(stroke);
        }
      }
      const imageIds = imageText.length > 0 ? imageText.split(',') : [];
      const mediaIds = mediaText.length > 0 ? mediaText.split(',') : [];
      results.push(board.cloneSelectionToNewPage(strokes, imageIds, mediaIds, 'v'));
    }
  }
  return results;
}
