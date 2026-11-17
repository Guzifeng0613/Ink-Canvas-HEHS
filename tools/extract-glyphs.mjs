// 从 WinUI3 Glyphs.cs 提取图标码点清单，生成 parity/glyphs.json。
// target 由 Phase D 填充（Symbol 名称或自绘 id）；未填充时 checker 在 --final 拒绝。
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const SRC_PATH = resolve(ROOT, 'src/InkCanvas.Controls/Controls/Glyphs.cs');
export const OUT_PATH = resolve(ROOT, 'parity/glyphs.json');

export function extractGlyphs(text) {
  const out = [];
  const re = /public const string (\w+) = "(?:\\u([0-9A-Fa-f]{4})|([^"\\]))";/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    const name = m[1];
    if (name.endsWith('Font')) {
      continue;
    }
    const cp = m[2] !== undefined ? parseInt(m[2], 16) : m[3].codePointAt(0);
    out.push({
      name,
      codepoint: 'U+' + cp.toString(16).toUpperCase().padStart(4, '0'),
      char: String.fromCodePoint(cp),
      target: null,
    });
  }
  return out;
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const glyphs = extractGlyphs(readFileSync(SRC_PATH, 'utf8'));
  writeFileSync(OUT_PATH, JSON.stringify({ note: 'target: Symbol 名或 custom:<id>，Phase D 填充', glyphs }, null, 2) + '\n', 'utf8');
  console.log('glyphs written:', glyphs.length);
}
