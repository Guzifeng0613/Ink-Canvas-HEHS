// @parity src/InkCanvas.AnalyticGeometry/PlotDefinition.cs + PlotCompiler.cs
// D7 解析几何内核（二）：绘图定义/校验 + 编译（直角/参数/极坐标 → FunctionY/FunctionX/Implicit/Parametric/Polar）。
import { normalizeExpression, compileExpression, FormatError } from './MathExpression';
import type { CompiledExpression } from './MathExpression';

export enum PlotKind {
  Cartesian = 0,
  Parametric = 1,
  Polar = 2
}

export interface PlotDefinition {
  Kind: number;
  Formula: string;
  SecondFormula: string;
  XMin: number;
  XMax: number;
  YMin: number;
  YMax: number;
  TMin: number;
  TMax: number;
  A: number;
  B: number;
  C: number;
  IncludeAxes: boolean;
}

export function createDefaultPlotDefinition(): PlotDefinition {
  return {
    Kind: PlotKind.Cartesian,
    Formula: 'y=a*x^2+b*x+c',
    SecondFormula: 'sin(t)',
    XMin: -10,
    XMax: 10,
    YMin: -7.5,
    YMax: 7.5,
    TMin: 0,
    TMax: Math.PI * 2,
    A: 1,
    B: 0,
    C: 0,
    IncludeAxes: true
  };
}

export function validatePlotDefinition(definition: PlotDefinition): void {
  if (definition.Kind !== PlotKind.Cartesian && definition.Kind !== PlotKind.Parametric
    && definition.Kind !== PlotKind.Polar) {
    throw new FormatError('请选择有效的坐标类型。');
  }
  if (definition.Formula === null || definition.Formula.trim().length === 0
    || definition.Formula.length > 2048
    || (definition.SecondFormula !== null && definition.SecondFormula.length > 2048)) {
    throw new FormatError('请输入公式，每个公式最多 2048 个字符。');
  }
  const values: number[] = [definition.XMin, definition.XMax, definition.YMin, definition.YMax,
    definition.TMin, definition.TMax, definition.A, definition.B, definition.C];
  for (let i = 0; i < values.length; i++) {
    if (!Number.isFinite(values[i]) || Math.abs(values[i]) > 1e8) {
      throw new FormatError('坐标和参数必须是绝对值不超过 10⁸ 的有限数值。');
    }
  }
  if (definition.XMax - definition.XMin < 1e-7 || definition.YMax - definition.YMin < 1e-7
    || definition.TMax - definition.TMin < 1e-7) {
    throw new FormatError('每个范围的最大值必须大于最小值。');
  }
}

export enum CompiledPlotKind {
  FunctionY = 0,
  FunctionX = 1,
  Implicit = 2,
  Parametric = 3,
  Polar = 4
}

export interface CompiledPlot {
  Kind: number;
  First: CompiledExpression;
  Second: CompiledExpression | null;
}

export function compilePlot(definition: PlotDefinition): CompiledPlot {
  validatePlotDefinition(definition);
  const formula = normalizeExpression(definition.Formula).trim();
  if (definition.Kind === PlotKind.Parametric) {
    const first = compileExpression(removePrefix(formula, 'x'));
    const second = compileExpression(removePrefix(normalizeExpression(definition.SecondFormula), 'y'));
    if (containsVariable(first, 'x') || containsVariable(first, 'y')
      || containsVariable(second, 'x') || containsVariable(second, 'y')) {
      throw new FormatError('参数方程请使用变量 t 和参数 a、b、c。');
    }
    return { Kind: CompiledPlotKind.Parametric, First: first, Second: second };
  }
  if (definition.Kind === PlotKind.Polar) {
    const radius = compileExpression(removePrefix(formula, 'r'));
    if (containsVariable(radius, 'x') || containsVariable(radius, 'y')) {
      throw new FormatError('极坐标请使用角度 t 或 θ，以及参数 a、b、c。');
    }
    return { Kind: CompiledPlotKind.Polar, First: radius, Second: null };
  }
  const sides = formula.split('=');
  if (sides.length > 2) {
    throw new FormatError('方程需要完整的左右两侧，等号最多一个。');
  }
  for (let i = 0; i < sides.length; i++) {
    if (sides[i].trim().length === 0) {
      throw new FormatError('方程需要完整的左右两侧，等号最多一个。');
    }
  }
  if (sides.length === 1) {
    const expression = compileExpression(formula);
    checkCartesian(expression);
    return {
      Kind: containsVariable(expression, 'y') ? CompiledPlotKind.Implicit : CompiledPlotKind.FunctionY,
      First: expression,
      Second: null
    };
  }
  const right = compileExpression(sides[1]);
  checkCartesian(right);
  if (sides[0].trim() === 'y' && !containsVariable(right, 'y')) {
    return { Kind: CompiledPlotKind.FunctionY, First: right, Second: null };
  }
  if (sides[0].trim() === 'x' && !containsVariable(right, 'x')) {
    return { Kind: CompiledPlotKind.FunctionX, First: right, Second: null };
  }
  const difference = compileExpression('(' + sides[0] + ')-(' + sides[1] + ')');
  checkCartesian(difference);
  return { Kind: CompiledPlotKind.Implicit, First: difference, Second: null };
}

function containsVariable(expression: CompiledExpression, name: string): boolean {
  return expression.Variables.indexOf(name) >= 0;
}

function checkCartesian(expression: CompiledExpression): void {
  if (containsVariable(expression, 't')) {
    throw new FormatError('直角坐标请使用 x、y 和参数 a、b、c。');
  }
}

function removePrefix(text: string, variable: string): string {
  const sides = text.split('=');
  if (sides.length === 1) {
    return text;
  }
  const head = sides[0].replace(/ /g, '');
  if (sides.length === 2 && (head === variable || head === variable + '(t)')) {
    return sides[1];
  }
  throw new FormatError('此处请输入 ' + variable + ' 的表达式。');
}
// 确定性向量入口：定义 → 编译 → 类型 + 变量集（错误返回 'ERROR:' + 消息）。
export function plotCompileProbe(
  kind: number,
  formula: string,
  secondFormula: string,
  xMin: number, xMax: number, yMin: number, yMax: number,
  tMin: number, tMax: number, a: number, b: number, c: number
): Object {
  const definition: PlotDefinition = {
    Kind: kind,
    Formula: formula,
    SecondFormula: secondFormula,
    XMin: xMin, XMax: xMax, YMin: yMin, YMax: yMax,
    TMin: tMin, TMax: tMax, A: a, B: b, C: c,
    IncludeAxes: true
  };
  try {
    const compiled = compilePlot(definition);
    const result: Record<string, Object> = {
      Kind: compiled.Kind,
      Variables: compiled.First.Variables
    };
    if (compiled.Second !== null) {
      result['SecondVariables'] = compiled.Second.Variables;
    }
    return result;
  } catch (e) {
    if (e instanceof FormatError) {
      return 'ERROR:' + e.message;
    }
    return 'ERROR:' + String(e);
  }
}
