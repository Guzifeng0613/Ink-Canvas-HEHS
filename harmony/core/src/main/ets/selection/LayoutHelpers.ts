// @parity src/InkCanvas.Core/Selection/FloatingToolbarLayout.cs + BoardDockLayout.cs
// 纯逻辑布局助手（G16）：选区旁工具条候选位评分放置；画板三条 55DIP 底栏自适应两行。
import { Rect } from '../ink/InkStroke';

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function overlapArea(a: Rect, b: Rect): number {
  if (b.isEmpty()) {
    return 0;
  }
  const x = Math.max(0, Math.min(a.Right, b.Right) - Math.max(a.Left, b.Left));
  const y = Math.max(0, Math.min(a.Bottom, b.Bottom) - Math.max(a.Top, b.Top));
  return x * y;
}

export interface ToolbarPlaceInput {
  Work: number[];
  Selection: number[];
  Obstacle: number[];
  Width: number;
  Height: number;
  Gap: number;
}

// 单坐标系内的常量规模候选搜索（含负显示器原点）；返回 [L,T,R,B]。
// 评分：障碍重叠×10000 + 选区重叠×100 + 中心距²×0.001 + 候选序×0.01。
export function placeToolbar(
  work: number[],
  selection: number[],
  obstacle: number[],
  sizeWidth: number,
  sizeHeight: number,
  gap: number
): number[] {
  const workRect = new Rect(work[0], work[1], work[2], work[3]);
  const selectionRect = new Rect(selection[0], selection[1], selection[2], selection[3]);
  const obstacleRect = new Rect(obstacle[0], obstacle[1], obstacle[2], obstacle[3]);
  const width = Math.min(sizeWidth, workRect.width());
  const height = Math.min(sizeHeight, workRect.height());
  const x = selectionRect.centerX() - width / 2;
  const candidates: number[][] = [
    [x, selectionRect.Bottom + gap],
    [x, selectionRect.Top - height - gap],
    [selectionRect.Right + gap, selectionRect.centerY() - height / 2],
    [selectionRect.Left - width - gap, selectionRect.centerY() - height / 2],
    [x, obstacleRect.Top - height - gap],
    [x, obstacleRect.Bottom + gap],
    [workRect.Left + gap, workRect.Top + gap],
    [workRect.Right - width - gap, workRect.Top + gap],
    [workRect.Left + gap, workRect.Bottom - height - gap],
    [workRect.Right - width - gap, workRect.Bottom - height - gap]
  ];
  const inflatedObstacle = obstacleRect.isEmpty() ? Rect.empty() : obstacleRect.inflate(gap);
  const inflatedSelection = selectionRect.inflate(gap);
  let best = Rect.empty();
  let bestScore = Number.MAX_VALUE;
  for (let i = 0; i < candidates.length; i++) {
    const left = clamp(candidates[i][0], workRect.Left, workRect.Right - width);
    const top = clamp(candidates[i][1], workRect.Top, workRect.Bottom - height);
    const rect = new Rect(left, top, left + width, top + height);
    const dx = rect.centerX() - selectionRect.centerX();
    const dy = rect.centerY() - selectionRect.centerY();
    const score = overlapArea(rect, inflatedObstacle) * 10000
      + overlapArea(rect, inflatedSelection) * 100
      + (dx * dx + dy * dy) * 0.001 + i * 0.01;
    if (score < bestScore) {
      bestScore = score;
      best = rect;
    }
  }
  return [best.Left, best.Top, best.Right, best.Bottom];
}

export interface BoardDockLayoutResult {
  Scale: number;
  TwoRows: boolean;
  Height: number;
}

// 三条 55DIP 底栏在小屏保持居中不重叠：放不下时折两行并按可用宽度收缩缩放。
export function boardDockLayout(
  viewportWidth: number,
  requestedScale: number,
  leftWidth: number,
  centerWidth: number,
  rightWidth: number
): BoardDockLayoutResult {
  const width = Math.max(1, viewportWidth - 12);
  let scale = clamp(requestedScale, 0.5, 1.5);
  const twoRows = (centerWidth + 2 * Math.max(leftWidth, rightWidth) + 16) * scale > width;
  if (twoRows) {
    scale = Math.min(scale, width / Math.max(centerWidth, leftWidth + rightWidth + 12));
  }
  return { Scale: scale, TwoRows: twoRows, Height: (twoRows ? 114 : 55) * scale };
}