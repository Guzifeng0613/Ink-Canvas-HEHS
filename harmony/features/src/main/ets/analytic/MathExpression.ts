// @parity src/InkCanvas.AnalyticGeometry/MathExpression.cs
// D7 解析几何内核（一）：有界算术文法——一次编译、固定数值函数集。
// ArkTS 无表达式树编译，Parser 直接产出求值闭包（语义与 C# Expression 树一致）。
export type Evaluator = (x: number, y: number, t: number, a: number, b: number, c: number) => number;

export class FormatError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'FormatError';
  }
}

const enum_ = {
  End: 0, Number: 1, Name: 2, Plus: 3, Minus: 4, Multiply: 5, Divide: 6,
  Power: 7, Open: 8, Close: 9, Comma: 10
} as const;
type TokenKind = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10;

class Token {
  Kind: TokenKind;
  Text: string;
  Number: number;
  constructor(kind: TokenKind, text: string, number: number = 0) {
    this.Kind = kind;
    this.Text = text;
    this.Number = number;
  }
}

interface NativeFunction {
  Arity: number;
  One?: (value: number) => number;
  Two?: (left: number, right: number) => number;
}

const FUNCTIONS: Record<string, NativeFunction> = {
  'sin': { Arity: 1, One: Math.sin },
  'cos': { Arity: 1, One: Math.cos },
  'tan': { Arity: 1, One: Math.tan },
  'asin': { Arity: 1, One: Math.asin },
  'acos': { Arity: 1, One: Math.acos },
  'atan': { Arity: 1, One: Math.atan },
  'sinh': { Arity: 1, One: Math.sinh },
  'cosh': { Arity: 1, One: Math.cosh },
  'tanh': { Arity: 1, One: Math.tanh },
  'sqrt': { Arity: 1, One: Math.sqrt },
  'abs': { Arity: 1, One: Math.abs },
  'exp': { Arity: 1, One: Math.exp },
  'ln': { Arity: 1, One: Math.log },
  'log': { Arity: 1, One: Math.log10 },
  'log10': { Arity: 1, One: Math.log10 },
  'floor': { Arity: 1, One: Math.floor },
  'ceil': { Arity: 1, One: Math.ceil },
  'round': { Arity: 1, One: Math.round },
  'min': { Arity: 2, Two: Math.min },
  'max': { Arity: 2, Two: Math.max },
  'pow': { Arity: 2, Two: Math.pow },
  'atan2': { Arity: 2, Two: Math.atan2 }
};

const PARAMETER_NAMES: string[] = ['x', 'y', 't', 'a', 'b', 'c'];

/** 归一：上标 → 幂、NFKC、小写、数学符号 → ASCII、π→pi、θ→t。 */
export function normalizeExpression(value: string): string {
  let text = (value === null ? '' : value)
    .replace('²', '^2').replace('³', '^3').replace('⁴', '^4')
    .normalize('NFKC').toLowerCase()
    .replace('−', '-').replace('–', '-').replace('×', '*').replace('·', '*').replace('÷', '/')
    .replace('π', 'pi').replace('θ', 't').replace('：', ':');
  return text;
}

export interface CompiledExpression {
  Evaluate: Evaluator;
  Variables: string[];
}

export function compileExpression(text: string): CompiledExpression {
  if (text === null || text.trim().length === 0 || text.length > 4096) {
    throw new FormatError('公式为空或过长。');
  }
  const parser = new Parser(normalizeExpression(text));
  const body = parser.Parse();
  const variables = parser.Variables.slice();
  return { Evaluate: body, Variables: variables };
}

class Parser {
  private readonly text: string;
  private position: number = 0;
  private nodes: number = 0;
  private depth: number = 0;
  private token: Token = new Token(enum_.End, '');
  readonly Variables: string[] = [];

  constructor(text: string) {
    this.text = text;
    this.Next();
  }

  Parse(): Evaluator {
    const result = this.Sum();
    if (this.token.Kind !== enum_.End) {
      throw this.Error('此处有多余的字符');
    }
    return result;
  }

  private Error(message: string): FormatError {
    return new FormatError(message + '，位置 ' + Math.max(1, this.position) + '。');
  }

  private Sum(): Evaluator {
    this.Enter();
    try {
      let result = this.Product();
      while (this.token.Kind === enum_.Plus || this.token.Kind === enum_.Minus) {
        const operation = this.token.Kind;
        this.Next();
        const right = this.Product();
        this.Count();
        const left = result;
        result = operation === enum_.Plus
          ? (x, y, t, a, b, c): number => left(x, y, t, a, b, c) + right(x, y, t, a, b, c)
          : (x, y, t, a, b, c): number => left(x, y, t, a, b, c) - right(x, y, t, a, b, c);
      }
      return result;
    } finally {
      this.depth -= 1;
    }
  }

  private Product(): Evaluator {
    let result = this.Unary();
    while (this.token.Kind === enum_.Multiply || this.token.Kind === enum_.Divide
      || this.token.Kind === enum_.Open || this.token.Kind === enum_.Name || this.token.Kind === enum_.Number) {
      const operation = this.token.Kind;
      if (operation === enum_.Multiply || operation === enum_.Divide) {
        this.Next();
      }
      const right = this.Unary();
      this.Count();
      const left = result;
      result = operation === enum_.Divide
        ? (x, y, t, a, b, c): number => left(x, y, t, a, b, c) / right(x, y, t, a, b, c)
        : (x, y, t, a, b, c): number => left(x, y, t, a, b, c) * right(x, y, t, a, b, c);
    }
    return result;
  }

  private Unary(): Evaluator {
    this.Enter();
    try {
      if (this.token.Kind === enum_.Plus || this.token.Kind === enum_.Minus) {
        const negative = this.token.Kind === enum_.Minus;
        this.Next();
        const operand = this.Unary();
        this.Count();
        return negative
          ? (x, y, t, a, b, c): number => -operand(x, y, t, a, b, c)
          : operand;
      }
      const value = this.Primary();
      if (this.token.Kind === enum_.Power) {
        this.Next();
        const exponent = this.Unary();
        this.Count();
        return (x, y, t, a, b, c): number => Math.pow(value(x, y, t, a, b, c), exponent(x, y, t, a, b, c));
      }
      return value;
    } finally {
      this.depth -= 1;
    }
  }

  private Primary(): Evaluator {
    this.Count();
    if (this.token.Kind === enum_.Number) {
      const number = this.token.Number;
      this.Next();
      return (): number => number;
    }
    if (this.token.Kind === enum_.Open) {
      this.Next();
      const value = this.Sum();
      this.Expect(enum_.Close, '缺少右括号');
      return value;
    }
    if (this.token.Kind !== enum_.Name) {
      throw this.Error('需要数值、变量或左括号');
    }
    const name = this.token.Text;
    this.Next();
    if (name === 'pi') {
      return (): number => Math.PI;
    }
    if (name === 'e') {
      return (): number => Math.E;
    }
    const parameter = PARAMETER_NAMES.indexOf(name);
    if (parameter >= 0) {
      if (this.Variables.indexOf(name) < 0) {
        this.Variables.push(name);
      }
      return (x, y, t, a, b, c): number => [x, y, t, a, b, c][parameter];
    }
    if (name.length > 1 && this.onlyParameterLetters(name)) {
      const letters = name.split('');
      for (let i = 0; i < letters.length; i++) {
        if (this.Variables.indexOf(letters[i]) < 0) {
          this.Variables.push(letters[i]);
        }
      }
      return (x, y, t, a, b, c): number => {
        let value = 1;
        for (let i = 0; i < letters.length; i++) {
          value *= [x, y, t, a, b, c][PARAMETER_NAMES.indexOf(letters[i])];
        }
        return value;
      };
    }
    const fn = FUNCTIONS[name];
    if (fn === undefined) {
      throw this.Error('未知名称 ' + name);
    }
    this.Expect(enum_.Open, '函数 ' + name + ' 后需要左括号');
    const arguments_: Evaluator[] = [];
    for (let i = 0; i < fn.Arity; i++) {
      if (i > 0) {
        this.Expect(enum_.Comma, '函数参数之间需要逗号');
      }
      arguments_.push(this.Sum());
    }
    this.Expect(enum_.Close, '函数参数后需要右括号');
    const one = fn.One;
    const two = fn.Two;
    if (fn.Arity === 1 && one !== undefined) {
      const arg = arguments_[0];
      return (x, y, t, a, b, c): number => one(arg(x, y, t, a, b, c));
    }
    const left = arguments_[0];
    const right = arguments_[1];
    return (x, y, t, a, b, c): number => (two as (l: number, r: number) => number)(left(x, y, t, a, b, c), right(x, y, t, a, b, c));
  }

  private onlyParameterLetters(name: string): boolean {
    for (let i = 0; i < name.length; i++) {
      if ('xytabc'.indexOf(name.charAt(i)) < 0) {
        return false;
      }
    }
    return true;
  }

  private Expect(kind: TokenKind, message: string): void {
    if (this.token.Kind !== kind) {
      throw this.Error(message);
    }
    this.Next();
  }

  private Enter(): void {
    this.depth += 1;
    if (this.depth > 48) {
      throw this.Error('公式嵌套过深');
    }
  }

  private Count(): void {
    this.nodes += 1;
    if (this.nodes > 512) {
      throw this.Error('公式过于复杂');
    }
  }

  private Next(): void {
    while (this.position < this.text.length && this.isWhitespace(this.text.charAt(this.position))) {
      this.position += 1;
    }
    if (this.position === this.text.length) {
      this.token = new Token(enum_.End, '');
      return;
    }
    const start = this.position;
    const first = this.text.charAt(this.position);
    this.position += 1;
    if (this.isDigit(first) || first === '.') {
      while (this.position < this.text.length
        && (this.isDigit(this.text.charAt(this.position)) || this.text.charAt(this.position) === '.')) {
        this.position += 1;
      }
      if (this.position < this.text.length && this.text.charAt(this.position) === 'e') {
        let exponent = this.position + 1;
        if (exponent < this.text.length && (this.text.charAt(exponent) === '+' || this.text.charAt(exponent) === '-')) {
          exponent += 1;
        }
        if (exponent < this.text.length && this.isDigit(this.text.charAt(exponent))) {
          this.position = exponent + 1;
          while (this.position < this.text.length && this.isDigit(this.text.charAt(this.position))) {
            this.position += 1;
          }
        }
      }
      const number = this.text.substring(start, this.position);
      const value = Number(number);
      if (!Number.isFinite(value)) {
        throw this.Error('数值无效');
      }
      this.token = new Token(enum_.Number, number, value);
      return;
    }
    if (this.isLetter(first)) {
      while (this.position < this.text.length && this.isLetterOrDigit(this.text.charAt(this.position))) {
        this.position += 1;
      }
      this.token = new Token(enum_.Name, this.text.substring(start, this.position));
      return;
    }
    switch (first) {
      case '+': this.token = new Token(enum_.Plus, first); return;
      case '-': this.token = new Token(enum_.Minus, first); return;
      case '*': this.token = new Token(enum_.Multiply, first); return;
      case '/': this.token = new Token(enum_.Divide, first); return;
      case '^': this.token = new Token(enum_.Power, first); return;
      case '(': this.token = new Token(enum_.Open, first); return;
      case ')': this.token = new Token(enum_.Close, first); return;
      case ',': this.token = new Token(enum_.Comma, first); return;
      default: throw this.Error('不支持的字符 ' + first);
    }
  }

  private isWhitespace(ch: string): boolean {
    return ch === ' ' || ch === '\t' || ch === '\r' || ch === '\n' || ch === '\f' || ch === '\v'
      || ch === ' ' || ch === '﻿';
  }

  private isDigit(ch: string): boolean {
    return ch >= '0' && ch <= '9';
  }

  private isLetter(ch: string): boolean {
    return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
  }

  private isLetterOrDigit(ch: string): boolean {
    return this.isLetter(ch) || this.isDigit(ch);
  }
}
// ---- 确定性向量入口 ----------------------------------------------------------

export function expressionNormalizeProbe(value: string): string {
  return normalizeExpression(value);
}

export function expressionEvaluateProbe(
  text: string,
  x: number,
  y: number,
  t: number,
  a: number,
  b: number,
  c: number
): Object {
  try {
    const compiled = compileExpression(text);
    return compiled.Evaluate(x, y, t, a, b, c);
  } catch (e) {
    if (e instanceof FormatError) {
      return 'ERROR:' + (e.message as string).split('，位置')[0];
    }
    return 'ERROR:' + String(e);
  }
}
