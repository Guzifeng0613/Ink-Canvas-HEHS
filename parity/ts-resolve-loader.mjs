// ESM 解析钩子：把 ArkTS 风格的无扩展名相对导入解析为 .ts / .ets / index.ts。
// 用法：node --experimental-transform-types --experimental-loader ./parity/ts-resolve-loader.mjs parity/run-vectors.mjs
import { existsSync, statSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';

export async function resolve(specifier, context, nextResolve) {
  if ((specifier.startsWith('./') || specifier.startsWith('../')) && context.parentURL) {
    let target;
    try {
      target = fileURLToPath(new URL(specifier, context.parentURL));
    } catch {
      return nextResolve(specifier, context);
    }
    const candidates = [
      target,
      target + '.ts',
      target + '.ets',
      target + '.js',
      target + '/index.ts',
      target + '/index.ets',
      target + '/index.js'
    ];
    for (const candidate of candidates) {
      try {
        if (existsSync(candidate) && statSync(candidate).isFile()) {
          return { url: pathToFileURL(candidate).href, shortCircuit: true };
        }
      } catch {
        // try next candidate
      }
    }
  }
  return nextResolve(specifier, context);
}
