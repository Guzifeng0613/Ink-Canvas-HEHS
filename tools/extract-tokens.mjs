// 从 WinUI3 Tokens.xaml 提取主题 token 与度量，生成 parity/tokens.json（单一来源快照）。
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const XAML_PATH = resolve(ROOT, 'src/InkCanvas.Controls/Themes/Tokens.xaml');
export const OUT_PATH = resolve(ROOT, 'parity/tokens.json');

export function extractTokens(text) {
  const tokens = { light: {}, dark: {}, metrics: {} };
  const dictRe = /<ResourceDictionary x:Key="(Light|Dark)">([\s\S]*?)<\/ResourceDictionary>/g;
  let m;
  while ((m = dictRe.exec(text)) !== null) {
    const theme = m[1] === 'Light' ? 'light' : 'dark';
    const body = m[2];
    const colorRe = /<Color x:Key="([^"]+)">(#[0-9A-Fa-f]{6,8})<\/Color>/g;
    let c;
    while ((c = colorRe.exec(body)) !== null) {
      tokens[theme][c[1]] = c[2].toUpperCase();
    }
  }
  const metricRes = [
    [/<x:Double x:Key="([^"]+)">([0-9.]+)<\/x:Double>/g, (v) => Number(v)],
    [/<CornerRadius x:Key="([^"]+)">([0-9.]+)<\/CornerRadius>/g, (v) => Number(v)],
    [/<Thickness x:Key="([^"]+)">([0-9.,]+)<\/Thickness>/g, (v) => v.split(',').map(Number)],
  ];
  for (const [re, conv] of metricRes) {
    let d;
    while ((d = re.exec(text)) !== null) {
      tokens.metrics[d[1]] = conv(d[2]);
    }
  }
  return tokens;
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const tokens = extractTokens(readFileSync(XAML_PATH, 'utf8'));
  writeFileSync(OUT_PATH, JSON.stringify(tokens, null, 2) + '\n', 'utf8');
  console.log('tokens written:', Object.keys(tokens.light).length, 'light colors,',
    Object.keys(tokens.metrics).length, 'metrics');
}
