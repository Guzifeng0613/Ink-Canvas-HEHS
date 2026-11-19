// @parity src/InkCanvas.Features/Board/BoardSelectionElement.cs（+ BoardService.SetImagePresent/SetMediaPresent）
// G2 元素选区桥的画板实现：把页面图片/视频包装为可选元素。
// SetPresent 离场记住 z 序索引、在场按原序插回；transform 走 BoardElementMath.appendTransform
// （行向量 existing * delta，与图片拖拽同一管线）；duplicate 共享字节、新 Id、在场。
import type { ISelectionElement, ElementSnapshot } from '@inkcanvas/ink';
import { Rect, SelectionFrameState, Matrix3x2, matrixToArray } from '@inkcanvas/core';
import type { BoardAsset } from './BoardDocument';
import { BoardDocument, cloneBoardAsset } from './BoardDocument';
import { readTransform, appendTransform } from './BoardElementMath';
import { snapshotGeometry, applyGeometry } from './BoardElementHistory';

export class BoardSelectionElement implements ISelectionElement {
  private readonly board: BoardDocument;
  private readonly asset: BoardAsset;
  private readonly isMedia: boolean;
  private absentIndex: number = -1;

  constructor(board: BoardDocument, asset: BoardAsset, isMedia: boolean) {
    this.board = board;
    this.asset = asset;
    this.isMedia = isMedia;
  }

  id(): string {
    return this.asset.Id;
  }

  private pageList(): BoardAsset[] {
    return this.isMedia ? this.board.Current.Media : this.board.Current.Images;
  }

  isPresent(): boolean {
    return this.pageList().indexOf(this.asset) >= 0;
  }

  getFrame(): SelectionFrameState {
    return {
      Bounds: new Rect(
        this.asset.Left, this.asset.Top,
        this.asset.Left + this.asset.Width, this.asset.Top + this.asset.Height),
      Transform: readTransform(this.asset.Transform)
    };
  }

  setPresent(present: boolean): void {
    const list = this.pageList();
    const index = list.indexOf(this.asset);
    if (present) {
      if (index >= 0) {
        return;
      }
      const at = Math.min(this.absentIndex < 0 ? list.length : this.absentIndex, list.length);
      list.splice(at, 0, this.asset);
      this.absentIndex = -1;
    } else {
      if (index < 0) {
        return;
      }
      list.splice(index, 1);
      this.absentIndex = index;
    }
  }

  transform(delta: Matrix3x2): void {
    this.asset.Transform = appendTransform(this.asset.Transform, matrixToArray(delta));
  }

  duplicate(): ISelectionElement {
    const list = this.pageList();
    const copy = cloneBoardAsset(this.asset, this.asset.Id + '#' + list.length);
    list.push(copy);
    return new BoardSelectionElement(this.board, copy, this.isMedia);
  }

  capture(): ElementSnapshot {
    return snapshotGeometry(this.asset);
  }

  restore(snapshot: ElementSnapshot): void {
    applyGeometry(this.asset, snapshot);
  }
}

// 确定性向量入口：按 id 定位元素（图片优先，找不到再查视频），执行 ops 序列。
// op：'present:<id>'/'absent:<id>'/'restore:<id>'、'frame:<id>'（LTRB+Transform）、
// 'move:<id>|<m11>,<m12>,<m21>,<m22>,<tx>,<ty>'、'dup:<id>'（新 Id + 列表长度）、
// 'list'（图片/视频 Id 顺序）、'capres:<id>|<tx>,<ty>'（快照→平移→恢复→几何）。
export function boardElementSelectionProbe(text: string, ops: string[]): Object[] {
  const board = new BoardDocument();
  board.loadJson(text);
  // 同一元素跨 op 复用同一实例（离场索引等实例状态必须在 op 间保留）。
  const cache = new Map<string, BoardSelectionElement>();
  const results: Object[] = [];
  for (let i = 0; i < ops.length; i++) {
    const op = ops[i];
    const sep = op.indexOf(':');
    const kind = sep >= 0 ? op.substring(0, sep) : op;
    const arg = sep >= 0 ? op.substring(sep + 1) : '';
    if (kind === 'list') {
      const ids: string[] = [];
      for (let j = 0; j < board.Current.Images.length; j++) {
        ids.push(board.Current.Images[j].Id);
      }
      for (let j = 0; j < board.Current.Media.length; j++) {
        ids.push('m:' + board.Current.Media[j].Id);
      }
      results.push(ids);
    } else {
      const pipe = arg.indexOf('|');
      const idPart = pipe >= 0 ? arg.substring(0, pipe) : arg;
      const rest = pipe >= 0 ? arg.substring(pipe + 1) : '';
      const element = findElement(board, idPart, cache);
      if (element === null) {
        results.push(false);
        continue;
      }
      if (kind === 'present') {
        results.push(element.isPresent());
      } else if (kind === 'absent') {
        element.setPresent(false);
        results.push(true);
      } else if (kind === 'restore') {
        element.setPresent(true);
        results.push(true);
      } else if (kind === 'frame') {
        const frame = element.getFrame();
        results.push([
          frame.Bounds.Left, frame.Bounds.Top, frame.Bounds.Right, frame.Bounds.Bottom,
          frame.Transform.M11, frame.Transform.M12, frame.Transform.M21, frame.Transform.M22,
          frame.Transform.M31, frame.Transform.M32
        ]);
      } else if (kind === 'move') {
        const values = rest.split(',');
        element.transform(new Matrix3x2(
          Number(values[0]), Number(values[1]), Number(values[2]),
          Number(values[3]), Number(values[4]), Number(values[5])));
        results.push(true);
      } else if (kind === 'dup') {
        const copy = element.duplicate();
        results.push({ Id: copy.id(), Present: copy.isPresent() });
      } else if (kind === 'capres') {
        const snapshot = element.capture();
        const values = rest.split(',');
        element.transform(new Matrix3x2(1, 0, 0, 1, Number(values[0]), Number(values[1])));
        element.restore(snapshot);
        const asset = findAsset(board, idPart);
        results.push(asset === null ? null : [asset.Left, asset.Top, asset.Width, asset.Height]);
      }
    }
  }
  return results;
}

function findElement(board: BoardDocument, id: string, cache?: Map<string, BoardSelectionElement>): BoardSelectionElement | null {
  // 先查缓存：离场元素已不在页面列表中，但仍需按实例恢复。
  if (cache !== undefined) {
    const existing = cache.get(id);
    if (existing !== undefined) {
      return existing;
    }
  }
  for (let i = 0; i < board.Current.Images.length; i++) {
    if (board.Current.Images[i].Id === id) {
      return cachedWrap(board, board.Current.Images[i], false, id, cache);
    }
  }
  for (let i = 0; i < board.Current.Media.length; i++) {
    if (board.Current.Media[i].Id === id) {
      return cachedWrap(board, board.Current.Media[i], true, id, cache);
    }
  }
  return null;
}

function cachedWrap(
  board: BoardDocument,
  asset: BoardAsset,
  isMedia: boolean,
  id: string,
  cache?: Map<string, BoardSelectionElement>
): BoardSelectionElement {
  if (cache !== undefined) {
    const existing = cache.get(id);
    if (existing !== undefined) {
      return existing;
    }
    const created = new BoardSelectionElement(board, asset, isMedia);
    cache.set(id, created);
    return created;
  }
  return new BoardSelectionElement(board, asset, isMedia);
}

function findAsset(board: BoardDocument, id: string): BoardAsset | null {
  for (let i = 0; i < board.Current.Images.length; i++) {
    if (board.Current.Images[i].Id === id) {
      return board.Current.Images[i];
    }
  }
  for (let i = 0; i < board.Current.Media.length; i++) {
    if (board.Current.Media[i].Id === id) {
      return board.Current.Media[i];
    }
  }
  return null;
}