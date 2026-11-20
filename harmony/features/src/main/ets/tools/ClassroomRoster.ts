// @parity src/InkCanvas.Features/Tools/ClassroomRoster.cs
// D5 课堂名册：接受「每行一个名字」或带姓名列的 CSV/TSV 表；带引号字段转义。
// Decode（字节→文本）：C# 为 UTF-8 严格→GB18030(936) 回退；ArkTS 宿主侧用
// util.TextDecoder('utf-8')，失败回退 'gbk'（鸿蒙支持 GBK 解码；真机核对）。
export function parseRoster(text: string): string[] {
  const stripped = text.replace(/^﻿/, '');
  const lines = stripped.split(/\r\n|\n|\r/);
  const rows: string[] = [];
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].trim().length > 0) {
      rows.push(lines[i]);
    }
  }
  if (rows.length === 0) {
    return [];
  }
  const separator = rows[0].indexOf('\t') >= 0 ? '\t' : ',';
  const header = splitFields(rows[0], separator);
  let nameColumn = -1;
  for (let i = 0; i < header.length; i++) {
    const field = header[i].trim().toLowerCase();
    if (field === '姓名' || field === '名字' || field === 'name' || field === 'student name') {
      nameColumn = i;
      break;
    }
  }
  const column = Math.max(0, nameColumn);
  const result: string[] = [];
  for (let i = nameColumn >= 0 ? 1 : 0; i < rows.length; i++) {
    const fields = splitFields(rows[i], separator);
    if (fields.length > column) {
      const name = fields[column].trim();
      if (name.length > 0) {
        result.push(name);
      }
    }
  }
  return result;
}

/** CSV 字段切分：双引号包裹，"" 转义为 "，引号内分隔符不切。 */
export function splitFields(row: string, separator: string): string[] {
  const fields: string[] = [];
  let field = '';
  let quoted = false;
  for (let index = 0; index < row.length; index++) {
    const character = row.charAt(index);
    if (character === '"') {
      if (quoted && index + 1 < row.length && row.charAt(index + 1) === '"') {
        field += '"';
        index += 1;
      } else {
        quoted = !quoted;
      }
    } else if (character === separator && !quoted) {
      fields.push(field);
      field = '';
    } else {
      field += character;
    }
  }
  fields.push(field);
  return fields;
}

// 确定性向量入口：文本 → 名单 + 各行字段数（校验 CSV 引号切分）。
export function rosterProbe(text: string): { Names: string[]; FieldCounts: number[] } {
  const names = parseRoster(text);
  const stripped = text.replace(/^﻿/, '');
  const lines = stripped.split(/\r\n|\n|\r/).filter((line: string): boolean => line.trim().length > 0);
  const separator = lines.length > 0 && lines[0].indexOf('\t') >= 0 ? '\t' : ',';
  const counts: number[] = [];
  for (let i = 0; i < lines.length; i++) {
    counts.push(splitFields(lines[i], separator).length);
  }
  return { Names: names, FieldCounts: counts };
}