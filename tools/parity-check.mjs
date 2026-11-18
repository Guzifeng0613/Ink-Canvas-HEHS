// 互认契约检查器：manifest 完整性、@parity 锚点（每个 core 文件 + 目标归属）、tokens/glyphs 漂移、schema 非空。
// 用法：node tools/parity-check.mjs [--final] [--phase A|B|C|D|E|F|G] [--scope core] [--self-test]
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { resolve, dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { extractTokens, XAML_PATH } from './extract-tokens.mjs';
import { extractGlyphs, SRC_PATH } from './extract-glyphs.mjs';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const STATUS = new Set(['planned', 'ported', 'adapted', 'not-ported']);
const PHASES = new Set(['A', 'B', 'C', 'D', 'E', 'F', 'G']);
const SCHEMAS = ['settings', 'icink', 'board-pack', 'ppt-manifest'];

export function validateManifest(manifest, root, opts, errors) {
  const scopePhase = opts.phase || null;
  const final = !!opts.final;
  if (!manifest || !Array.isArray(manifest.features)) {
    errors.push('manifest.features 不是数组');
    return;
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
    if ((f.arkts || []).length > 0) {
      const csharpSet = new Set((f.csharp || []).map(normalize));
      for (const rel of f.arkts || []) {
        const abs = resolve(root, rel);
        if (!existsSync(abs)) continue;
        const anchors = readAnchors(abs).map(normalize);
        if (anchors.length === 0) {
          errors.push(where + ': arkts 文件缺少 @parity 锚点 ' + rel);
          continue;
        }
        if (!anchors.some((a) => csharpSet.has(a))) {
          errors.push(where + ': 锚点目标不属于该 feature 的 csharp 列表 ' + rel + ' → ' + anchors.join(','));
        }
      }
    }
    const inScope = !scopePhase || f.phase === scopePhase;
    if (final && inScope) {
      if (f.status === 'planned') errors.push(where + ': --final 不允许 planned');
      if (f.status !== 'not-ported' && (f.arkts || []).length === 0) errors.push(where + ': --final 要求至少一个 arkts 文件');
    }
  }
}

function normalize(p) {
  return p.replace(/\\/g, '/');
}

export function readAnchors(file) {
  const text = readFileSync(file, 'utf8');
  const result = [];
  const re = /@parity\s+([^\s（(]+)/g;
  let m;
  while ((m = re.exec(text)) !== null) result.push(m[1]);
  return result;
}

function walk(dir, out) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      if (e.name === 'build' || e.name === 'oh_modules' || e.name === '.hvigor') continue;
      walk(p, out);
    } else if (e.name.endsWith('.ts') || e.name.endsWith('.ets')) {
      out.push(p);
    }
  }
}

export function scanAnchors(root, errors) {
  const files = [];
  const harmonyDir = resolve(root, 'harmony');
  if (existsSync(harmonyDir)) walk(harmonyDir, files);
  const coreDir = resolve(root, 'harmony/core/src');
  let anchors = 0;
  const pairs = [];
  for (const file of files) {
    const rel = normalize(relative(root, file));
    const targets = readAnchors(file);
    for (const target of targets) {
      anchors++;
      pairs.push({ arkts: rel, csharp: normalize(target) });
      if (!existsSync(resolve(root, target))) {
        errors.push('anchor 目标不存在: ' + target + ' (in ' + rel + ')');
      }
    }
    if (rel.startsWith('harmony/core/src') && targets.length === 0) {
      errors.push('core 文件缺少 @parity 锚点: ' + rel);
    }
  }
  return { anchors, pairs };
}

export function checkAnchorsRegistry(root, scannedPairs, errors) {
  const path = resolve(root, 'parity/anchors.json');
  if (!existsSync(path)) {
    errors.push('缺少 parity/anchors.json');
    return;
  }
  const registry = JSON.parse(readFileSync(path, 'utf8'));
  const reg = new Set((registry.pairs || []).map((p) => normalize(p.arkts) + '|' + normalize(p.csharp)));
  const scan = new Set(scannedPairs.map((p) => p.arkts + '|' + p.csharp));
  for (const key of scan) {
    if (!reg.has(key)) errors.push('anchors.json 缺少登记: ' + key);
  }
  for (const key of reg) {
    if (!scan.has(key)) errors.push('anchors.json 登记了不存在的锚点: ' + key);
  }
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
  for (const name of SCHEMAS) {
    const path = resolve(root, 'parity/schemas/' + name + '.schema.json');
    if (!existsSync(path)) {
      errors.push('缺少 schema ' + name);
      continue;
    }
    const schema = JSON.parse(readFileSync(path, 'utf8'));
    if (!schema.properties || Object.keys(schema.properties).length === 0) {
      errors.push('schema 未定义字段: ' + name);
    }
  }
}

function selfTest() {
  const good = {
    features: [{
      id: 'x', name: 'x', phase: 'A', status: 'ported', csharp: ['harmony/entry/src/main/ets/pages/Index.ets'],
      arkts: ['harmony/entry/src/main/ets/pages/Index.ets'], vectors: [], tests: [], notes: 'n'
    }]
  };
  const bad = {
    features: [
      { id: 'x', name: 'x', phase: 'Z', status: 'wat', notes: '' },
      { id: 'x', name: 'x', phase: 'A', status: 'planned', notes: 'n' }
    ]
  };
  const e1 = [];
  validateManifest(good, ROOT, { final: true, phase: 'A' }, e1);
  const e2 = [];
  validateManifest(bad, ROOT, { final: true, phase: 'A' }, e2);
  if (e2.length < 4) throw new Error('self-test: bad manifest not rejected enough: ' + e2.length);
  if (e1.some((e) => e.includes('不允许 planned')) || e1.some((e) => e.includes('要求至少'))) {
    throw new Error('self-test: good manifest rejected: ' + e1.join('; '));
  }
  console.log('self-test OK');
}

function main() {
  const args = process.argv.slice(2);
  if (args.includes('--self-test')) {
    selfTest();
    return;
  }
  const errors = [];
  let final = false;
  let phase = null;
  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--final') {
      final = true;
    } else if (arg === '--phase') {
      phase = args[++i];
      if (!PHASES.has(phase)) {
        errors.push('非法 --phase 值: ' + phase + '（允许 A–G）');
      }
    } else if (arg === '--scope') {
      const scope = args[++i];
      if (scope === 'core') {
        phase = 'A';
      } else {
        errors.push('不支持的 --scope 值: ' + scope + '（当前仅 core）');
      }
    } else {
      errors.push('未知参数: ' + arg);
    }
  }

  const manifestPath = resolve(ROOT, 'parity/manifest.json');
  if (!existsSync(manifestPath)) {
    errors.push('缺少 parity/manifest.json');
  } else {
    const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
    validateManifest(manifest, ROOT, { final, phase }, errors);
  }
  if (existsSync(manifestPath)) {
    const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
    const counts = { ported: 0, adapted: 0, 'not-ported': 0, planned: 0 };
    for (const f of manifest.features || []) {
      counts[f.status] = (counts[f.status] || 0) + 1;
    }
    const total = (manifest.features || []).length;
    if (manifest.denominator !== undefined && manifest.denominator !== total) {
      errors.push('denominator 与 features 数量不一致: ' + manifest.denominator + ' vs ' + total);
    }
    const covered = counts.ported + counts.adapted;
    const percent = total > 0 ? Math.round(covered * 10000 / total) / 100 : 0;
    if (manifest.coverage !== undefined) {
      if (manifest.coverage.total !== total || manifest.coverage.ported !== counts.ported ||
          manifest.coverage.adapted !== counts.adapted || manifest.coverage['not-ported'] !== counts['not-ported'] ||
          manifest.coverage.percent !== percent) {
        errors.push('coverage 与 status 重算不一致（应为 ' + covered + '/' + total + '=' + percent + '%）');
      }
    }
  }
  const scanned = scanAnchors(ROOT, errors);
  checkAnchorsRegistry(ROOT, scanned.pairs, errors);
  checkGeneratedSnapshots(ROOT, errors);

  if (errors.length > 0) {
    console.error('parity-check FAILED (' + errors.length + '):');
    for (const e of errors) console.error(' - ' + e);
    process.exit(1);
  }
  console.log('parity-check OK (anchors: ' + scanned.anchors + ', final: ' + final +
    (phase ? ', phase: ' + phase : '') + ')');
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
