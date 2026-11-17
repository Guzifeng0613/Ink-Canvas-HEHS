// 黄金向量运行器：零依赖执行 ArkTS/TS 纯逻辑模块。
// 用法：node --experimental-transform-types parity/run-vectors.mjs [--only <vector-id>]...
// 向量文件结构：{ id, entry, cases: [{ id, fn, input: any[], expected, tolerance? }] }
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const VEC_DIR = resolve(ROOT, 'parity/vectors');

function near(a, b, tol) {
  if (typeof a === 'number' && typeof b === 'number') {
    if (Number.isNaN(a) && Number.isNaN(b)) return true;
    return Math.abs(a - b) <= tol;
  }
  if (Array.isArray(a) || Array.isArray(b)) {
    if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length) return false;
    for (let i = 0; i < a.length; i++) {
      if (!near(a[i], b[i], tol)) return false;
    }
    return true;
  }
  if (a && b && typeof a === 'object' && typeof b === 'object') {
    const ka = Object.keys(a).sort();
    const kb = Object.keys(b).sort();
    if (ka.length !== kb.length || ka.some((k, i) => k !== kb[i])) return false;
    for (const k of ka) {
      if (!near(a[k], b[k], tol)) return false;
    }
    return true;
  }
  return a === b;
}

async function main() {
  const args = process.argv.slice(2);
  const only = [];
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--only') only.push(args[++i]);
  }
  if (!existsSync(VEC_DIR)) {
    console.log('0 vectors (parity/vectors 不存在)');
    return;
  }
  let files = readdirSync(VEC_DIR).filter((f) => f.endsWith('.json')).sort();
  if (only.length > 0) {
    files = files.filter((f) => only.includes(f.replace(/\.json$/, '')) || only.includes(f));
  }
  let passed = 0;
  let failed = 0;
  const failures = [];
  for (const file of files) {
    const doc = JSON.parse(readFileSync(join(VEC_DIR, file), 'utf8'));
    if (!doc.entry) {
      failures.push(file + ': 缺少 entry');
      failed++;
      continue;
    }
    const entryAbs = resolve(ROOT, doc.entry);
    if (!existsSync(entryAbs)) {
      failures.push(file + ': entry 不存在 ' + doc.entry);
      failed++;
      continue;
    }
    let mod;
    try {
      mod = await import(pathToFileURL(entryAbs).href);
    } catch (e) {
      failures.push(file + ': import 失败 ' + String(e && e.message ? e.message : e));
      failed++;
      continue;
    }
    for (const c of doc.cases || []) {
      const fn = mod[c.fn];
      if (typeof fn !== 'function') {
        failures.push(file + '#' + c.id + ': 导出函数缺失 ' + c.fn);
        failed++;
        continue;
      }
      let actual;
      try {
        actual = fn(...(c.input || []));
      } catch (e) {
        failures.push(file + '#' + c.id + ': 抛异常 ' + String(e && e.message ? e.message : e));
        failed++;
        continue;
      }
      const tol = typeof c.tolerance === 'number' ? c.tolerance : 0;
      if (near(actual, c.expected, tol)) {
        passed++;
      } else {
        failed++;
        failures.push(file + '#' + c.id + ': 期望 ' + JSON.stringify(c.expected) + ' 实际 ' + JSON.stringify(actual));
      }
    }
  }
  for (const f of failures) console.error('FAIL ' + f);
  console.log('vectors: ' + passed + ' passed, ' + failed + ' failed (' + files.length + ' files)');
  if (failed > 0) process.exit(1);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
