// @parity src/InkCanvas.Controls/Controls/Glyphs.cs
// Segoe 码点 → HarmonyOS sys.symbol 映射（B10 图标适配）。
// 码点与 parity/glyphs.json 的 codepoint 对齐；symbol 名逐一核对自
// SDK 26.0.1 Beta1 的 ets-loader/sysResource.js symbol 资源表（4164 项）。
// **需真机核对**：各 symbol 的起始 API 版本与真机渲染效果未实机验证；
// 未知码点/未映射项回退文字（不破坏现有布局）。
// $r 只接受字面量资源名，故渲染分支按 symbol 名逐一展开。

export interface GlyphSymbolEntry {
  codepoint: string;
  symbol: string;
}

// 码点 → symbol 名（尽力映射；空串表示无映射，回退文字）。
export const GLYPH_SYMBOLS: GlyphSymbolEntry[] = [
  { codepoint: 'U+E8B0', symbol: 'mouse' },                 // Cursor → 鼠标
  { codepoint: 'U+EE56', symbol: 'pencil_tip' },            // PenWpf → 笔尖
  { codepoint: 'U+E7E6', symbol: 'highlighter_tip' },       // Highlight → 荧光笔
  { codepoint: 'U+E74D', symbol: 'trash' },                 // Delete → 清空
  { codepoint: 'U+E75C', symbol: 'eraser_line' },           // Eraser → 橡皮
  { codepoint: 'U+E958', symbol: 'square_dashed' },         // SelectWpf → 虚线选框
  { codepoint: 'U+F158', symbol: 'rectangle_stack' },       // ShapesWpf → 图形叠层
  { codepoint: 'U+EE71', symbol: 'picture' },               // InsertMedia → 图片（视频按按钮覆盖）
  { codepoint: 'U+E7A7', symbol: 'undo' },                  // Undo
  { codepoint: 'U+E7A6', symbol: 'redo' },                  // Redo
  { codepoint: 'U+E7B3', symbol: 'eye_slash' },             // HideControl → 隐藏
  { codepoint: 'U+E8AB', symbol: 'arrow_left_arrow_right' },// LRSwitch → 左右
  { codepoint: 'U+E929', symbol: 'square_and_pencil' },     // BoardWpf → 画板
  { codepoint: 'U+E8A9', symbol: 'wrench_and_screwdriver' },// Tools → 工具
  { codepoint: 'U+ED1A', symbol: 'arrow_left_to_line' },    // FoldWpf → 收纳到侧边
  { codepoint: 'U+F271', symbol: 'hand_tap' },              // Gesture → 手势
  { codepoint: 'U+E70F', symbol: 'pencil_tip' },            // Pen
  { codepoint: 'U+E713', symbol: 'gearshape' },             // Settings
  { codepoint: 'U+E722', symbol: 'camera' },                // Camera
  { codepoint: 'U+E916', symbol: 'timer' },                 // Stopwatch
  { codepoint: 'U+E716', symbol: 'person_2' },              // People
  { codepoint: 'U+E76E', symbol: '' },                      // Emoji → 保留文字 ☺
  { codepoint: 'U+E899', symbol: 'move' },                  // EmojiDrag → 拖动
  { codepoint: 'U+E768', symbol: 'play' },                  // Play
  { codepoint: 'U+E769', symbol: 'pause' },                 // Pause
  { codepoint: 'U+E823', symbol: 'arrow_counterclockwise' },// Replay
  { codepoint: 'U+E897', symbol: 'questionmark_circle' },   // Help
  { codepoint: 'U+E946', symbol: 'info_circle' },           // Info
  { codepoint: 'U+E711', symbol: 'xmark' },                 // Close
  { codepoint: 'U+E74E', symbol: 'save' },                  // SaveStrokes
  { codepoint: 'U+ED25', symbol: 'folder' },                // OpenStrokes
  { codepoint: 'U+E82D', symbol: 'doc_plaintext' },         // GuideWpf
  { codepoint: 'U+E7E8', symbol: 'arrow_right_to_line' },   // ExitWpf
  { codepoint: 'U+E70E', symbol: 'chevron_up' },            // ChevronUp
  { codepoint: 'U+E70D', symbol: 'chevron_down' },          // ChevronDown
  { codepoint: 'U+E76B', symbol: 'chevron_left' },          // ChevronLeft
  { codepoint: 'U+E76C', symbol: 'chevron_right' },         // ChevronRight
  { codepoint: 'U+E72E', symbol: 'lock' },                  // Lock
  { codepoint: 'U+E785', symbol: 'lock_open' },             // Unlock（需真机核对 lock_open 名）
  { codepoint: 'U+E710', symbol: 'plus' },                  // Add
  { codepoint: 'U+E896', symbol: 'arrow_down_circle' },     // Download
  { codepoint: 'U+E72C', symbol: 'arrow_counterclockwise' },// Restart
  { codepoint: 'U+E7C2', symbol: 'move' },                  // GestureTranslate
  { codepoint: 'U+E740', symbol: 'magnifyingglass' },       // GestureZoom
  { codepoint: 'U+E7AD', symbol: 'arrow_clockwise' }        // GestureRotate
];

export function symbolForCodepoint(codepoint: string): string {
  for (let i = 0; i < GLYPH_SYMBOLS.length; i++) {
    if (GLYPH_SYMBOLS[i].codepoint === codepoint) {
      return GLYPH_SYMBOLS[i].symbol;
    }
  }
  return '';
}

function codepointOf(char: string): string {
  if (char.length === 0) {
    return '';
  }
  const cp = char.codePointAt(0);
  if (cp === undefined) {
    return '';
  }
  return 'U+' + cp.toString(16).toUpperCase().padStart(4, '0');
}

// 浮条按钮：同码点不同语义时按 Id 覆盖（圆形擦/墨迹擦、图片/视频）。
export function symbolForButton(id: string, glyph: string): string {
  if (id === 'eraserStroke') {
    return 'eraser_line_fill';
  }
  if (id === 'insertVideo') {
    return 'video';
  }
  if (id === 'insertImage') {
    return 'picture';
  }
  return symbolForCodepoint(codepointOf(glyph));
}

// 工具菜单入口 Id → symbol（无映射回退文字）。
export function symbolForTool(id: string): string {
  if (id === 'courseware') {
    return 'doc_text';
  }
  if (id === 'screenshot') {
    return 'camera';
  }
  if (id === 'desmos') {
    return 'calculator';
  }
  if (id === 'update') {
    return 'arrow_down_circle';
  }
  if (id === 'countdown') {
    return 'timer';
  }
  if (id === 'lottery') {
    return 'shuffle';
  }
  if (id === 'replay') {
    return 'play_circle';
  }
  if (id === 'settings') {
    return 'gearshape';
  }
  if (id === 'guide') {
    return 'questionmark_circle';
  }
  if (id === 'changelog') {
    return 'doc_plaintext';
  }
  if (id === 'closeTools') {
    return 'xmark';
  }
  return '';
}

export interface IconGlyphOptions {
  symbol: string;
  fallback: string;
  size: number;
  color: ResourceColor;
  enabled: boolean;
}

// 跨文件 @Builder：单对象参数按引用传递。enabled=false（能力探测不支持）
// 或 symbol 为空时回退文字，保持原有布局尺寸。
@Builder
export function IconGlyph(options: IconGlyphOptions) {
  if (!options.enabled || options.symbol === '') {
    Text(options.fallback)
      .fontSize(options.size)
      .fontColor(options.color)
  } else if (options.symbol === 'mouse') {
    SymbolGlyph($r('sys.symbol.mouse')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'pencil_tip') {
    SymbolGlyph($r('sys.symbol.pencil_tip')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'highlighter_tip') {
    SymbolGlyph($r('sys.symbol.highlighter_tip')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'trash') {
    SymbolGlyph($r('sys.symbol.trash')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'eraser_line') {
    SymbolGlyph($r('sys.symbol.eraser_line')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'eraser_line_fill') {
    SymbolGlyph($r('sys.symbol.eraser_line_fill')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'square_dashed') {
    SymbolGlyph($r('sys.symbol.square_dashed')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'rectangle_stack') {
    SymbolGlyph($r('sys.symbol.rectangle_stack')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'picture') {
    SymbolGlyph($r('sys.symbol.picture')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'video') {
    SymbolGlyph($r('sys.symbol.video')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'undo') {
    SymbolGlyph($r('sys.symbol.undo')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'redo') {
    SymbolGlyph($r('sys.symbol.redo')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'eye_slash') {
    SymbolGlyph($r('sys.symbol.eye_slash')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'arrow_left_arrow_right') {
    SymbolGlyph($r('sys.symbol.arrow_left_arrow_right')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'square_and_pencil') {
    SymbolGlyph($r('sys.symbol.square_and_pencil')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'wrench_and_screwdriver') {
    SymbolGlyph($r('sys.symbol.wrench_and_screwdriver')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'arrow_left_to_line') {
    SymbolGlyph($r('sys.symbol.arrow_left_to_line')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'hand_tap') {
    SymbolGlyph($r('sys.symbol.hand_tap')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'doc_text') {
    SymbolGlyph($r('sys.symbol.doc_text')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'camera') {
    SymbolGlyph($r('sys.symbol.camera')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'calculator') {
    SymbolGlyph($r('sys.symbol.calculator')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'arrow_down_circle') {
    SymbolGlyph($r('sys.symbol.arrow_down_circle')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'timer') {
    SymbolGlyph($r('sys.symbol.timer')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'shuffle') {
    SymbolGlyph($r('sys.symbol.shuffle')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'play_circle') {
    SymbolGlyph($r('sys.symbol.play_circle')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'gearshape') {
    SymbolGlyph($r('sys.symbol.gearshape')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'questionmark_circle') {
    SymbolGlyph($r('sys.symbol.questionmark_circle')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'doc_plaintext') {
    SymbolGlyph($r('sys.symbol.doc_plaintext')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'xmark') {
    SymbolGlyph($r('sys.symbol.xmark')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'person_2') {
    SymbolGlyph($r('sys.symbol.person_2')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'move') {
    SymbolGlyph($r('sys.symbol.move')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'play') {
    SymbolGlyph($r('sys.symbol.play')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'pause') {
    SymbolGlyph($r('sys.symbol.pause')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'arrow_counterclockwise') {
    SymbolGlyph($r('sys.symbol.arrow_counterclockwise')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'info_circle') {
    SymbolGlyph($r('sys.symbol.info_circle')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'save') {
    SymbolGlyph($r('sys.symbol.save')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'folder') {
    SymbolGlyph($r('sys.symbol.folder')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'arrow_right_to_line') {
    SymbolGlyph($r('sys.symbol.arrow_right_to_line')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'chevron_up') {
    SymbolGlyph($r('sys.symbol.chevron_up')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'chevron_down') {
    SymbolGlyph($r('sys.symbol.chevron_down')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'chevron_left') {
    SymbolGlyph($r('sys.symbol.chevron_left')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'chevron_right') {
    SymbolGlyph($r('sys.symbol.chevron_right')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'lock') {
    SymbolGlyph($r('sys.symbol.lock')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'lock_open') {
    SymbolGlyph($r('sys.symbol.lock_open')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'plus') {
    SymbolGlyph($r('sys.symbol.plus')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'magnifyingglass') {
    SymbolGlyph($r('sys.symbol.magnifyingglass')).fontSize(options.size).fontColor([options.color])
  } else if (options.symbol === 'arrow_clockwise') {
    SymbolGlyph($r('sys.symbol.arrow_clockwise')).fontSize(options.size).fontColor([options.color])
  } else {
    Text(options.fallback)
      .fontSize(options.size)
      .fontColor(options.color)
  }
}