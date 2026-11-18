// @parity src/InkCanvas.Controls/Controls/FloatingBar.cs（主栏 19 按钮顺序与默认显隐）
export interface FloatingBarButton {
  Id: string;
  Label: string;
  Glyph: string;
  Tooltip: string;
  DefaultVisible: boolean;
  DefaultEnabled: boolean;
}

// 顺序与 05-controls §4.2 一致（左→右）。Glyph 为 Segoe 码点；HarmonyOS 侧
// 图标映射在 parity/glyphs.json 中维护，未映射前使用文字标签兜底。
export const FLOATING_BAR_BUTTONS: FloatingBarButton[] = [
  { Id: 'mouse', Label: '鼠标', Glyph: '', Tooltip: '鼠标模式 — Alt+Q', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'pen', Label: '批注', Glyph: '', Tooltip: '批注 — Alt+D', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'highlighter', Label: '荧光笔', Glyph: '', Tooltip: '荧光笔', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'clear', Label: '清空', Glyph: '', Tooltip: '清空 — Ctrl+E', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'eraserCircle', Label: '圆形擦', Glyph: '', Tooltip: '圆形擦 — Alt+E', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'eraserStroke', Label: '墨迹擦', Glyph: '', Tooltip: '墨迹擦', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'select', Label: '选择', Glyph: '', Tooltip: '选择 — Alt+S', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'shapes', Label: '图形', Glyph: '', Tooltip: '图形', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'insertImage', Label: '图片', Glyph: '', Tooltip: '插入图片：暂未接入', DefaultVisible: true, DefaultEnabled: false },
  { Id: 'insertVideo', Label: '视频', Glyph: '', Tooltip: '插入视频：暂未接入', DefaultVisible: true, DefaultEnabled: false },
  { Id: 'undo', Label: '撤销', Glyph: '', Tooltip: '撤销 — Ctrl+Z', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'redo', Label: '重做', Glyph: '', Tooltip: '重做 — Ctrl+Y', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'clearAndMouse', Label: '清并鼠', Glyph: '', Tooltip: '清空并进入鼠标模式', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'hideControl', Label: '隐藏控制', Glyph: '', Tooltip: '收起/展开主工具栏', DefaultVisible: false, DefaultEnabled: true },
  { Id: 'lrSwitch', Label: '左右切换', Glyph: '', Tooltip: '左右切换：暂未接入', DefaultVisible: false, DefaultEnabled: false },
  { Id: 'board', Label: '画板', Glyph: '', Tooltip: '画板 — Alt+B', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'tools', Label: '工具', Glyph: '', Tooltip: '更多设置', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'fold', Label: '隐藏', Glyph: '', Tooltip: '收纳工具栏到屏幕侧边 — Alt+V', DefaultVisible: true, DefaultEnabled: true },
  { Id: 'gesture', Label: '手势', Glyph: '', Tooltip: '手势选项', DefaultVisible: true, DefaultEnabled: true }
];

export interface FloatingBarCatalogSummary {
  Count: number;
  DefaultVisibleCount: number;
  Ids: string[];
}

export function catalogSummary(): FloatingBarCatalogSummary {
  const ids: string[] = [];
  let visible = 0;
  for (let i = 0; i < FLOATING_BAR_BUTTONS.length; i++) {
    const button = FLOATING_BAR_BUTTONS[i];
    ids.push(button.Id);
    if (button.DefaultVisible) {
      visible++;
    }
  }
  return { Count: FLOATING_BAR_BUTTONS.length, DefaultVisibleCount: visible, Ids: ids };
}
