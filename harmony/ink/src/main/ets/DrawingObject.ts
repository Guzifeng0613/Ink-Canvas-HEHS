// @parity src/InkCanvas.Ink/DrawingObject.cs + InkLayers.cs（纯算法部分）
// D1 层排列系统：墨迹/图片/视频统一的自后向前 z 序 + 置顶/前移/后移/置底命令。
// 纯算法独立成函数（可向量）：排序键、下一层号、重排（含 ShapeSource 组保序）。
import type { InkStroke } from '@inkcanvas/core';
import type { ISelectionElement } from './ControllerTypes';

/** 与 C# LayerAction 同值。 */
export enum LayerAction {
  BringToFront = 0,
  MoveForward = 1,
  MoveBackward = 2,
  SendToBack = 3
}

/** 共享 z 序中的一个绘制条目（Stroke 或 Element 二选一）。 */
export class DrawingObject {
  readonly Stroke: InkStroke | null;
  readonly Element: ISelectionElement | null;

  constructor(stroke: InkStroke | null, element: ISelectionElement | null) {
    this.Stroke = stroke;
    this.Element = element;
  }

  id(): string {
    return this.Stroke !== null ? this.Stroke.Id : (this.Element !== null ? this.Element.id() : '');
  }

  get LayerOrder(): number | null {
    return this.Stroke !== null ? this.Stroke.LayerOrder : (this.Element !== null ? this.Element.layerOrder() : null);
  }

  set LayerOrder(value: number | null) {
    if (this.Stroke !== null) {
      this.Stroke.LayerOrder = value;
    } else if (this.Element !== null) {
      this.Element.setLayerOrder(value);
    }
  }

  /** 图片元素排最底（long.MinValue），其它元素其上（+1），无层号墨迹最顶（long.MaxValue）。 */
  get SortOrder(): number {
    const order = this.LayerOrder;
    if (order !== null) {
      return order;
    }
    if (this.Stroke !== null) {
      return Number.MAX_SAFE_INTEGER;
    }
    return this.Element !== null && this.Element.isImageElement()
      ? -Number.MAX_SAFE_INTEGER
      : -Number.MAX_SAFE_INTEGER + 1;
  }
}

/** 稳定排序：SortOrder 升序（ES2019+ sort 稳定——同序保持原相对顺序，墨迹保文档序）。 */
export function sortDrawingObjects(objects: DrawingObject[]): DrawingObject[] {
  const result = objects.slice();
  result.sort((a: DrawingObject, b: DrawingObject): number => a.SortOrder - b.SortOrder);
  return result;
}

/**
 * C# NextLayerOrder：存在无层号对象或接近溢出时，全体重排 0..n-1 并返回 n；
 * 否则返回最大层号 + 1。orders 为当前全部对象的层号（null 表示未分层）。
 */
export function nextLayerOrder(orders: (number | null)[]): number {
  if (orders.length === 0) {
    return 0;
  }
  let max = -Number.MAX_SAFE_INTEGER;
  let hasNull = false;
  for (let i = 0; i < orders.length; i++) {
    if (orders[i] === null) {
      hasNull = true;
    } else if (orders[i] as number > max) {
      max = orders[i] as number;
    }
  }
  if (hasNull || max >= Number.MAX_SAFE_INTEGER - orders.length - 1) {
    return orders.length;
  }
  return max + 1;
}

export interface ArrangeInput {
  /** 重排前的完整顺序（对象的稳定标识）。 */
  Ids: string[];
  /** 各对象的层号（null=未分层）。 */
  Orders: (number | null)[];
  /** 选中的对象标识集合。 */
  SelectedIds: string[];
  /** 墨迹的 ShapeSource GroupId（无则 null）——同组轮廓作为整体移动。 */
  GroupIds: (string | null)[];
  Action: number;
}

export interface ArrangeOutput {
  /** 重排后的对象顺序（下标即新排名）；不可重排时为 null。 */
  Order: string[] | null;
  /** 与 Order 对应的新层号。 */
  Orders: (number | null)[];
}

/**
 * C# TryArrange + ArrangeSelection 的纯算法：重排选中对象（ShapeSource 同组绑定排名），
 * 返回新顺序与新层号；无选择/全选/顺序未变时 Order=null。
 */
export function arrangeObjects(input: ArrangeInput): ArrangeOutput {
  const n = input.Ids.length;
  if (n === 0 || input.SelectedIds.length === 0 || input.SelectedIds.length === n) {
    return { Order: null, Orders: input.Orders.slice() };
  }
  const selected = new Set<string>(input.SelectedIds);
  let order = input.Ids.slice();
  if (input.Action === LayerAction.BringToFront || input.Action === LayerAction.SendToBack) {
    const chosen: string[] = [];
    const other: string[] = [];
    for (let i = 0; i < n; i++) {
      (selected.has(order[i]) ? chosen : other).push(order[i]);
    }
    order = input.Action === LayerAction.BringToFront ? other.concat(chosen) : chosen.concat(other);
  } else if (input.Action === LayerAction.MoveForward) {
    for (let i = order.length - 2; i >= 0; i--) {
      if (selected.has(order[i]) && !selected.has(order[i + 1])) {
        const temp = order[i];
        order[i] = order[i + 1];
        order[i + 1] = temp;
      }
    }
  } else {
    for (let i = 1; i < order.length; i++) {
      if (selected.has(order[i]) && !selected.has(order[i - 1])) {
        const temp = order[i];
        order[i] = order[i - 1];
        order[i - 1] = temp;
      }
    }
  }
  let changed = false;
  for (let i = 0; i < n; i++) {
    if (order[i] !== input.Ids[i]) {
      changed = true;
      break;
    }
  }
  if (!changed) {
    return { Order: null, Orders: input.Orders.slice() };
  }
  // 新层号 = 重排后下标；ShapeSource 同组取组内最小（向前）/最大（向后）排名。
  const indexById = new Map<string, number>();
  for (let i = 0; i < n; i++) {
    indexById.set(order[i], i);
  }
  const forward = input.Action === LayerAction.BringToFront || input.Action === LayerAction.MoveForward;
  const groupRank = new Map<string, number>();
  for (let i = 0; i < n; i++) {
    const group = input.GroupIds[i];
    if (group === null) {
      continue;
    }
    const rank = indexById.get(input.Ids[i]) as number;
    const existing = groupRank.get(group);
    if (existing === undefined
      || (forward ? rank < existing : rank > existing)) {
      groupRank.set(group, rank);
    }
  }
  const orders: (number | null)[] = [];
  for (let i = 0; i < n; i++) {
    const group = input.GroupIds[i];
    orders.push(group !== null && groupRank.has(group) ? groupRank.get(group) as number : indexById.get(input.Ids[i]) as number);
  }
  return { Order: order, Orders: orders };
}