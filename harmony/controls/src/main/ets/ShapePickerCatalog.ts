// @parity src/InkCanvas.Controls/Controls/ShapePreview.cs（ShapeMenuCatalog 24 项顺序与度量）
export interface ShapeMenuEntry {
  Kind: number;
  Label: string;
  Id: string;
  Row: number;
  Margin: number[];
  SlotWidth: number;
  Rotation: number;
  LongPress: boolean;
}

export const SHAPE_MENU_ROWS: ShapeMenuEntry[][] = [
  [
    { Kind: 0, Label: '直线', Id: 'Line', Row: 0, Margin: [16, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: true },
    { Kind: 1, Label: '虚线', Id: 'DashedLine', Row: 0, Margin: [0, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: true },
    { Kind: 2, Label: '点线', Id: 'DotLine', Row: 0, Margin: [0, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: true },
    { Kind: 3, Label: '箭头', Id: 'ArrowLine', Row: 0, Margin: [0, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: true },
    { Kind: 4, Label: '平行线', Id: 'ParallelLine', Row: 0, Margin: [0, 11.5, 16, 11.5], SlotWidth: 0, Rotation: 0, LongPress: true }
  ],
  [
    { Kind: 5, Label: '坐标系（双向）', Id: 'Coordinate1', Row: 1, Margin: [16, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 6, Label: '坐标系（x 单向）', Id: 'Coordinate2', Row: 1, Margin: [0, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 7, Label: '坐标系（y 单向）', Id: 'Coordinate3', Row: 1, Margin: [0, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 8, Label: '坐标系（第一象限）', Id: 'Coordinate4', Row: 1, Margin: [0, 12, 0, 12], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 9, Label: '空间坐标系', Id: 'Coordinate5', Row: 1, Margin: [-1, 11.5, -1, 11.5], SlotWidth: 0, Rotation: 0, LongPress: false }
  ],
  [
    { Kind: 11, Label: '中心矩形', Id: 'RectangleCenter', Row: 2, Margin: [16, 11, 2, 11], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 13, Label: '圆', Id: 'Circle', Row: 2, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 16, Label: '虚线圆', Id: 'DashedCircle', Row: 2, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 14, Label: '中心椭圆', Id: 'EllipseCenter', Row: 2, Margin: [0, 10, 0, 10], SlotWidth: 30, Rotation: 0, LongPress: false },
    { Kind: 15, Label: '椭圆（含焦点）', Id: 'EllipseCenterWithFocalPoint', Row: 2, Margin: [0, 10, 0, 10], SlotWidth: 30, Rotation: 0, LongPress: false }
  ],
  [
    { Kind: 17, Label: '双曲线', Id: 'Hyperbola', Row: 3, Margin: [16, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 18, Label: '双曲线（含焦点）', Id: 'HyperbolaWithFocalPoint', Row: 3, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 21, Label: '抛物线（上开口）', Id: 'ParabolaUp', Row: 3, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 20, Label: '抛物线（含焦点）', Id: 'ParabolaWithFocalPoint', Row: 3, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 90, LongPress: false },
    { Kind: 19, Label: '抛物线（右开口）', Id: 'ParabolaRight', Row: 3, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 90, LongPress: false }
  ],
  [
    { Kind: 10, Label: '矩形', Id: 'Rectangle', Row: 4, Margin: [16, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 22, Label: '圆柱', Id: 'Cylinder', Row: 4, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 23, Label: '圆锥', Id: 'Cone', Row: 4, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false },
    { Kind: 24, Label: '长方体', Id: 'Cuboid', Row: 4, Margin: [0, 10, 0, 10], SlotWidth: 0, Rotation: 0, LongPress: false }
  ]
];

export interface ShapeCatalogSummary {
  Count: number;
  Kinds: number[];
  RowCounts: number[];
}

export function shapeCatalogSummary(): ShapeCatalogSummary {
  const kinds: number[] = [];
  const rowCounts: number[] = [];
  for (let r = 0; r < SHAPE_MENU_ROWS.length; r++) {
    rowCounts.push(SHAPE_MENU_ROWS[r].length);
    for (let i = 0; i < SHAPE_MENU_ROWS[r].length; i++) {
      kinds.push(SHAPE_MENU_ROWS[r][i].Kind);
    }
  }
  return { Count: kinds.length, Kinds: kinds, RowCounts: rowCounts };
}
