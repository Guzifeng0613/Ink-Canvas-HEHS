// 互认契约检查器：manifest 完整性、@parity 锚点、tokens/glyphs 漂移。
// 用法：node tools/parity-check.mjs [--final] [--phase A] [--self-test]
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { extractTokens, XAML_PATH } from './extract-tokens.mjs';
import { extractGlyphs, SRC_PATH } from './extract-glyphs.mjs';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const STATUS = new Set(['planned', 'ported', 'adapted', 'not-ported']);
const PHASES = new Set(['A', 'B', 'C', 'D', 'E', 'F', 'G']);

export function validateManifest(manifest, root, opts) {
  const errors = [];
  const scopePhase = opts.phase || null;
  const final = !!opts.final;
  if (!manifest || !Array.isArray(manifest.features)) {
    return ['manifest.features 不是数组'];
  }
  const ids = new Set();
  for (const f of manifest.features) {
    const where = 'feature ' + (f.id || '<missing id>');
    if (!f.id) errors.push(where + ': 缺少 id');
    if (ids.has(f.id)) errors.push(where + ': id 重复');
    ids.add(f.id);
    if (!STATUS.has(f.status)) errors.push(where + ': 非法 status ' + f.status);
    if (!PHASES.has(f.phase)) errors.push(where + ': 非法 phase ' + f.phase);
    if (typeof f.notes !== 'string' || f.notes.length === 0) errors.push(where + ': notes 必填');
    for (const rel of f.csharp || []) {
      if (!existsSync(resolve(root, rel))) errors.push(where + ': csharp 文件不存在 ' + rel);
    }
    for (const rel of f.arkts || []) {
      if (!existsSync(resolve(root, rel))) errors.push(where + ': arkts 文件不存在 ' + rel);
    }
    for (const rel of f.vectors || []) {
      if (!existsSync(resolve(root, rel))) errors.push(where + ': vector 文件不存在 ' + rel);
    }
    const inScope = !scopePhase || f.phase === scopePhase;
    if (final && inScope) {
      if (f.status === 'planned') errors.push(where + ': --final 不允许 planned');
      if ((f.arkts || []).length === 0) errors.push(where + ': --final 要求至少一个 arkts 文件');
    }
  }
  return errors;
}

export function scanAnchors(root, errors) {
  const files = [];
  const walk = (dir) => {
    for (const e of readdirSync(dir, { withFileTypes: true })) {
      const p = join(dir, e.name);
      if (e.isDirectory()) {
        if (e.name === 'build' || e.name === 'oh_modules' || e.name === '.hvigor') continue;
        walk(p);
      } else if (e.name.endsWith('.ts') || e.name.endsWith('.ets')) {
        files.push(p);
      }
    }
  };
  const harmonyDir = resolve(root, 'harmony');
  if (existsSync(harmonyDir)) walk(harmonyDir);
  let anchors = 0;
  for (const file of files) {
    const text = readFileSync(file, 'utf8');
    const m = text.match(/@parity\s+(\S+)/);
    if (!m) continue;
    anchors++;
    const target = resolve(root, m[1]);
    if (!existsSync(target)) errors.push('anchor 目标不存在: ' + m[1] + ' (in ' + file.slice(root.length + 1) + ')');
  }
  return anchors;
}

export function checkGeneratedSnapshots(root, errors) {
  const tokenPath = resolve(root, 'parity/tokens.json');
  const glyphPath = resolve(root, 'parity/glyphs.json');
  if (!existsSync(tokenPath)) {
    errors.push('缺少 parity/tokens.json（运行 node tools/extract-tokens.mjs）');
  } else {
    const fresh = extractTokens(readFileSync(XAML_PATH, 'utf8'));
    const committed = JSON.parse(readFileSync(tokenPath, 'utf8'));
    if (JSON.stringify(fresh) !== JSON.stringify(committed)) {
      errors.push('tokens.json 与 Tokens.xaml 漂移（重新运行 extract-tokens.mjs）');
    }
  }
  if (!existsSync(glyphPath)) {
    errors.push('缺少 parity/glyphs.json（运行 node tools/extract-glyphs.mjs）');
  } else {
    const fresh = extractGlyphs(readFileSync(SRC_PATH, 'utf8'));
    const committed = JSON.parse(readFileSync(glyphPath, 'utf8')).glyphs;
    if (fresh.length !== committed.length) {
      errors.push('glyphs.json 条目数漂移（重新运行 extract-glyphs.mjs）');
    }
    const names = new Set(committed.map((g) => g.name));
    for (const g of fresh) {
      if (!names.has(g.name)) errors.push('glyphs.json 缺少 ' + g.name);
    }
  }
}

function selfTest() {
  const good = {
    features: [{ id: 'x', name: 'x', phase: 'A', status: 'ported', csharp: [], arkts: ['harmony/entry/src/main/ets/pages/Index.ets'], vectors: [], tests: [], notes: 'n' }],
  };
  const bad = {
    features: [
      { id: 'x', name: 'x', phase: 'Z', status: 'wat', notes: '' },
      { id: 'x', name: 'x', phase: 'A', status: 'planned', notes: 'n' },
    ],
  };
  const e1 = validateManifest(good, ROOT, { final: true, phase: 'A' });
  const e2 = validateManifest(bad, ROOT, { final: true, phase: 'A' });
  if (e1.length !== 0) throw new Error('self-test: good manifest rejected: ' + e1.join('; '));
  if (e2.length < 4) throw new Error('self-test: bad manifest not rejected enough: ' + e2.length);
  console.log('self-test OK');
}

function main() {
  const args = process.argv.slice(2);
  if (args.includes('--self-test')) {
    selfTest();
    return;
  }
  const final = args.includes('--final');
  const pi = args.indexOf('--phase');
  const phase = pi >= 0 ? args[pi + 1] : null;
  const errors = [];
  const manifestPath = resolve(ROOT, 'parity/manifest.json');
  if (!existsSync(manifestPath)) {
    errors.push('缺少 parity/manifest.json');
  } else {
    const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
    errors.push(...validateManifest(manifest, ROOT, { final, phase }));
  }
  const anchors = scanAnchors(ROOT, errors);
  checkGeneratedSnapshots(ROOT, errors);
  if (errors.length > 0) {
    console.error('parity-check FAILED (' + errors.length + '):');
    for (const e of errors) console.error(' - ' + e);
    process.exit(1);
  }
  console.log('parity-check OK (anchors: ' + anchors + ', final: ' + final + (phase ? ', phase: ' + phase : '') + ')');
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
