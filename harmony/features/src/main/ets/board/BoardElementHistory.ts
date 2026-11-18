// @parity src/InkCanvas.Features/Board/BoardService.cs（RecordImageTransform/RecordImageChange 页内撤销）
// 画板元素（图片/视频）变换的页内撤销历史：一次完整拖拽/缩放/旋转记为单撤销单元。
// 镜像 C# Current.History.Push(DelegateAction)：before==after 不入栈；新变更清空 redo。
import { sameTransform } from './BoardElementMath';

export interface ElementGeometry {
  Left: number;
  Top: number;
  Width: number;
  Height: number;
  Transform: number[] | null;
}

export interface ElementAssetLike {
  Left: number;
  Top: number;
  Width: number;
  Height: number;
  Transform: number[] | null;
}

export function snapshotGeometry(asset: ElementAssetLike): ElementGeometry {
  return {
    Left: asset.Left,
    Top: asset.Top,
    Width: asset.Width,
    Height: asset.Height,
    Transform: asset.Transform === null ? null : asset.Transform.slice()
  };
}

export function sameGeometry(a: ElementGeometry, b: ElementGeometry): boolean {
  return a.Left === b.Left && a.Top === b.Top && a.Width === b.Width && a.Height === b.Height
    && sameTransform(a.Transform, b.Transform);
}

export function applyGeometry(target: ElementAssetLike, geometry: ElementGeometry): void {
  target.Left = geometry.Left;
  target.Top = geometry.Top;
  target.Width = geometry.Width;
  target.Height = geometry.Height;
  target.Transform = geometry.Transform === null ? null : geometry.Transform.slice();
}

export interface ElementUndoEntry {
  name: string;
  redo: () => void;
  undo: () => void;
}

export class BoardElementHistory {
  private undoStack: ElementUndoEntry[] = [];
  private redoStack: ElementUndoEntry[] = [];

  get CanUndo(): boolean {
    return this.undoStack.length > 0;
  }

  get CanRedo(): boolean {
    return this.redoStack.length > 0;
  }

  push(entry: ElementUndoEntry): void {
    this.undoStack.push(entry);
    this.redoStack = [];
  }

  undo(): boolean {
    const entry = this.undoStack.pop();
    if (entry === undefined) {
      return false;
    }
    entry.undo();
    this.redoStack.push(entry);
    return true;
  }

  redo(): boolean {
    const entry = this.redoStack.pop();
    if (entry === undefined) {
      return false;
    }
    entry.redo();
    this.undoStack.push(entry);
    return true;
  }

  clear(): void {
    this.undoStack = [];
    this.redoStack = [];
  }
}

// 记录一次完成的元素变换；before==after 时不入栈（返回 false）。
export function recordTransform(
  history: BoardElementHistory,
  asset: ElementAssetLike,
  before: ElementGeometry,
  after: ElementGeometry,
  name: string
): boolean {
  if (sameGeometry(before, after)) {
    return false;
  }
  const beforeCopy = snapshotGeometry(before);
  const afterCopy = snapshotGeometry(after);
  history.push({
    name,
    redo: (): void => {
      applyGeometry(asset, afterCopy);
    },
    undo: (): void => {
      applyGeometry(asset, beforeCopy);
    }
  });
  return true;
}

// 确定性向量入口：变更已施加（asset 起始为 after），记录后按 ops 撤销/重做，返回每步几何。
export function elementTransformProbe(
  before: ElementGeometry,
  after: ElementGeometry,
  ops: string[]
): ElementGeometry[] {
  const asset: ElementAssetLike = snapshotGeometry(after);
  const history = new BoardElementHistory();
  recordTransform(history, asset, before, after, 'transform');
  const results: ElementGeometry[] = [];
  for (let i = 0; i < ops.length; i++) {
    if (ops[i] === 'undo') {
      history.undo();
    } else if (ops[i] === 'redo') {
      history.redo();
    }
    results.push(snapshotGeometry(asset));
  }
  return results;
}