// @parity src/InkCanvas.AnalyticGeometry/PlotSampler.cs
// D7 解析几何内核（三）：屏幕空间自适应曲线 + marching squares 隐函数 + 缝合，工作量有界。
// 640×480 屏幕格、32000 点/220000 次求值上限；自适应误差 0.55（黄金分割校验 0.38196601125）；
// 隐函数 256×192 网格、二分求根 12 次、中心消歧；段端点 1024 量化缝合。
import type { CompiledPlot, PlotDefinition } from './PlotCompiler';
import { validatePlotDefinition } from './PlotCompiler';
import { CompiledPlotKind } from './PlotCompiler';

export const PLOT_WIDTH: number = 640;
export const PLOT_HEIGHT: number = 480;
export const MAXIMUM_PLOT_POINTS: number = 32000;
export const MAXIMUM_PLOT_EVALUATIONS: number = 220000;

export interface PlotPoint {
  X: number;
  Y: number;
}

export interface PlotMesh {
  Paths: PlotPoint[][];
  Evaluations: number;
  Limited: boolean;
}

class BudgetExceededError extends Error {
}

interface Segment {
  First: PlotPoint;
  Last: PlotPoint;
}

class BudgetError extends Error {
}

export function samplePlot(compiled: CompiledPlot, definition: PlotDefinition): PlotMesh {
  validatePlotDefinition(definition);
  return new Sampler(compiled, definition).Run();
}

class Sampler {
  private readonly compiled: CompiledPlot;
  private readonly definition: PlotDefinition;
  private readonly paths: PlotPoint[][] = [];
  private readonly current: PlotPoint[] = [];
  private readonly segments: Segment[] = [];
  private evaluations: number = 0;
  private points: number = 0;
  private limited: boolean = false;

  constructor(compiled: CompiledPlot, definition: PlotDefinition) {
    this.compiled = compiled;
    this.definition = definition;
  }

  Run(): PlotMesh {
    try {
      if (this.compiled.Kind === CompiledPlotKind.Implicit) {
        this.Implicit();
      } else {
        const first = this.compiled.Kind === CompiledPlotKind.FunctionY ? this.definition.XMin
          : this.compiled.Kind === CompiledPlotKind.FunctionX ? this.definition.YMin
            : this.definition.TMin;
        const last = this.compiled.Kind === CompiledPlotKind.FunctionY ? this.definition.XMax
          : this.compiled.Kind === CompiledPlotKind.FunctionX ? this.definition.YMax
            : this.definition.TMax;
        const intervals = 128;
        let start = this.Curve(first);
        for (let i = 1; i <= intervals; i++) {
          const next = first + (last - first) * i / intervals;
          const end = this.Curve(next);
          this.Adaptive(first + (last - first) * (i - 1) / intervals, start, next, end, 0);
          start = end;
        }
        this.Flush();
      }
    } catch (e) {
      if (e instanceof BudgetError) {
        this.limited = true;
        this.Flush();
      } else {
        throw e;
      }
    }
    return { Paths: this.paths, Evaluations: this.evaluations, Limited: this.limited };
  }

  private Evaluate(expression: (x: number, y: number, t: number, a: number, b: number, c: number) => number,
    x: number, y: number, t: number): number {
    if (this.evaluations >= MAXIMUM_PLOT_EVALUATIONS) {
      throw new BudgetError();
    }
    this.evaluations += 1;
    return expression(x, y, t, this.definition.A, this.definition.B, this.definition.C);
  }

  private Screen(x: number, y: number): PlotPoint {
    return {
      X: (x - this.definition.XMin) * PLOT_WIDTH / (this.definition.XMax - this.definition.XMin),
      Y: (this.definition.YMax - y) * PLOT_HEIGHT / (this.definition.YMax - this.definition.YMin)
    };
  }

  private Field(screen: PlotPoint): number {
    return this.Evaluate(this.compiled.First.Evaluate,
      this.definition.XMin + screen.X * (this.definition.XMax - this.definition.XMin) / PLOT_WIDTH,
      this.definition.YMax - screen.Y * (this.definition.YMax - this.definition.YMin) / PLOT_HEIGHT, 0);
  }

  private Curve(parameter: number): PlotPoint {
    if (this.compiled.Kind === CompiledPlotKind.FunctionY) {
      return this.Screen(parameter, this.Evaluate(this.compiled.First.Evaluate, parameter, 0, 0));
    }
    if (this.compiled.Kind === CompiledPlotKind.FunctionX) {
      return this.Screen(this.Evaluate(this.compiled.First.Evaluate, 0, parameter, 0), parameter);
    }
    if (this.compiled.Kind === CompiledPlotKind.Parametric) {
      const second = this.compiled.Second as CompiledExpressionLike;
      return this.Screen(this.Evaluate(this.compiled.First.Evaluate, 0, 0, parameter),
        this.Evaluate(second.Evaluate, 0, 0, parameter));
    }
    return this.Polar(parameter);
  }

  private Polar(parameter: number): PlotPoint {
    const radius = this.Evaluate(this.compiled.First.Evaluate, 0, 0, parameter);
    return this.Screen(radius * Math.cos(parameter), radius * Math.sin(parameter));
  }

  private static Finite(point: PlotPoint): boolean {
    return Number.isFinite(point.X) && Number.isFinite(point.Y);
  }

  private static DistanceSquared(a: PlotPoint, b: PlotPoint): number {
    return (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
  }

  private static Error(actual: PlotPoint, first: PlotPoint, last: PlotPoint, fraction: number): number {
    return Math.sqrt(Sampler.DistanceSquared(actual, {
      X: first.X + (last.X - first.X) * fraction,
      Y: first.Y + (last.Y - first.Y) * fraction
    }));
  }

  private static OutsideSameSide(a: PlotPoint, b: PlotPoint, c: PlotPoint): boolean {
    return (a.X < -2 && b.X < -2 && c.X < -2) || (a.X > PLOT_WIDTH + 2 && b.X > PLOT_WIDTH + 2 && c.X > PLOT_WIDTH + 2)
      || (a.Y < -2 && b.Y < -2 && c.Y < -2) || (a.Y > PLOT_HEIGHT + 2 && b.Y > PLOT_HEIGHT + 2 && c.Y > PLOT_HEIGHT + 2);
  }

  private Adaptive(start: number, first: PlotPoint, end: number, last: PlotPoint, depth: number): void {
    const middle = (start + end) / 2;
    const center = this.Curve(middle);
    const finite = Sampler.Finite(first) && Sampler.Finite(center) && Sampler.Finite(last);
    if (finite && Sampler.OutsideSameSide(first, center, last)) {
      this.Flush();
      return;
    }
    let error = finite ? Sampler.Error(center, first, last, 0.5) : Number.POSITIVE_INFINITY;
    if (finite && error <= 0.55 && depth === 0) {
      const check = this.Curve(start + (end - start) * 0.38196601125);
      error = Sampler.Finite(check)
        ? Math.max(error, Sampler.Error(check, first, last, 0.38196601125))
        : Number.POSITIVE_INFINITY;
    }
    if (!finite || error > 0.55) {
      if (depth < 10) {
        this.Adaptive(start, first, middle, center, depth + 1);
        this.Adaptive(middle, center, end, last, depth + 1);
      } else {
        if (finite) {
          this.limited = true;
        }
        this.Flush();
      }
      return;
    }
    this.AddClipped(first, last);
  }

  private AddClipped(first: PlotPoint, last: PlotPoint): void {
    const dx = last.X - first.X;
    const dy = last.Y - first.Y;
    // enter/exit 作为跨四次 Clip 的累积状态（C# ref 参数语义）。
    this.clipEnter = 0;
    this.clipExit = 1;
    if (!this.Clip(-dx, first.X) || !this.Clip(dx, PLOT_WIDTH - first.X)
      || !this.Clip(-dy, first.Y) || !this.Clip(dy, PLOT_HEIGHT - first.Y)) {
      this.Flush();
      return;
    }
    const a: PlotPoint = { X: first.X + dx * this.clipEnter, Y: first.Y + dy * this.clipEnter };
    const b: PlotPoint = { X: first.X + dx * this.clipExit, Y: first.Y + dy * this.clipExit };
    if (this.current.length === 0
      || Sampler.DistanceSquared(this.current[this.current.length - 1], a) > 0.001) {
      this.Flush();
      this.Append(a);
    }
    if (this.current.length > 0
      && Sampler.DistanceSquared(this.current[this.current.length - 1], b) > 1e-10) {
      this.Append(b);
    }
    if (this.clipExit < 1) {
      this.Flush();
    }
  }

  // clipEnter/clipExit 为累积状态（ArkTS 无 ref 参数；四次调用间保持）。
  private clipEnter: number = 0;
  private clipExit: number = 1;

  private Clip(direction: number, offset: number): boolean {
    if (Math.abs(direction) < 1e-15) {
      return offset >= 0;
    }
    const ratio = offset / direction;
    if (direction < 0) {
      if (ratio > this.clipExit) {
        return false;
      }
      this.clipEnter = Math.max(this.clipEnter, ratio);
    } else {
      if (ratio < this.clipEnter) {
        return false;
      }
      this.clipExit = Math.min(this.clipExit, ratio);
    }
    return true;
  }

  private Append(point: PlotPoint): void {
    if (this.points >= MAXIMUM_PLOT_POINTS) {
      throw new BudgetError();
    }
    this.points += 1;
    this.current.push(point);
  }

  private Flush(): void {
    if (this.current.length >= 2) {
      this.paths.push(this.current.slice());
    }
    this.current.length = 0;
  }

  private Implicit(): void {
    const columns = 256;
    const rows = 192;
    let previous: number[] = [];
    let next: number[] = [];
    for (let x = 0; x <= columns; x++) {
      previous.push(this.Field({ X: x * PLOT_WIDTH / columns, Y: 0 }));
    }
    for (let row = 0; row < rows; row++) {
      const top = row * PLOT_HEIGHT / rows;
      const bottom = (row + 1) * PLOT_HEIGHT / rows;
      next = [];
      for (let x = 0; x <= columns; x++) {
        next.push(this.Field({ X: x * PLOT_WIDTH / columns, Y: bottom }));
      }
      for (let column = 0; column < columns; column++) {
        const corners: PlotPoint[] = [
          { X: column * PLOT_WIDTH / columns, Y: top },
          { X: (column + 1) * PLOT_WIDTH / columns, Y: top },
          { X: (column + 1) * PLOT_WIDTH / columns, Y: bottom },
          { X: column * PLOT_WIDTH / columns, Y: bottom }
        ];
        const values: number[] = [previous[column], previous[column + 1], next[column + 1], next[column]];
        const crossings: PlotPoint[] = [];
        for (let edge = 0; edge < 4; edge++) {
          const other = (edge + 1) % 4;
          if (!Number.isFinite(values[edge]) || !Number.isFinite(values[other])
            || (values[edge] >= 0) === (values[other] >= 0)) {
            continue;
          }
          const crossing = this.Root(corners[edge], values[edge], corners[other], values[other]);
          if (crossing !== null) {
            crossings.push(crossing);
          }
        }
        if (crossings.length === 2) {
          this.AddSegment(crossings[0], crossings[1]);
        } else if (crossings.length === 4) {
          const center = this.Field({
            X: (corners[0].X + corners[2].X) / 2,
            Y: (top + bottom) / 2
          });
          if ((center >= 0) === (values[0] >= 0)) {
            this.AddSegment(crossings[0], crossings[1]);
            this.AddSegment(crossings[2], crossings[3]);
          } else {
            this.AddSegment(crossings[0], crossings[3]);
            this.AddSegment(crossings[1], crossings[2]);
          }
        }
      }
      const swap = previous;
      previous = next;
      next = swap;
    }
    this.Stitch();
  }

  private Root(first: PlotPoint, a: number, last: PlotPoint, b: number): PlotPoint | null {
    const tolerance = Math.max(1e-10, Math.min(Math.abs(a), Math.abs(b)) * 1e-5);
    for (let attempt = 0; attempt < 12; attempt++) {
      let fraction = a / (a - b);
      fraction = attempt > 2
        ? Math.min(Math.max(fraction, 0.05), 0.95)
        : Math.min(Math.max(fraction, 0), 1);
      const point: PlotPoint = {
        X: first.X + (last.X - first.X) * fraction,
        Y: first.Y + (last.Y - first.Y) * fraction
      };
      const value = this.Field(point);
      if (!Number.isFinite(value)) {
        return null;
      }
      if (Math.abs(value) <= tolerance) {
        return point;
      }
      if ((value >= 0) === (a >= 0)) {
        first = point;
        a = value;
      } else {
        last = point;
        b = value;
      }
    }
    return null;
  }

  private AddSegment(a: PlotPoint, b: PlotPoint): void {
    if (this.segments.length >= MAXIMUM_PLOT_POINTS / 2) {
      throw new BudgetError();
    }
    if (Sampler.DistanceSquared(a, b) > 1e-12) {
      this.segments.push({ First: a, Last: b });
    }
  }

  private static Key(point: PlotPoint): string {
    return Math.round(point.X * 1024) + ',' + Math.round(point.Y * 1024);
  }

  private Stitch(): void {
    const edges = new Map<string, number[]>();
    const add = (key: string, index: number): void => {
      const values = edges.get(key);
      if (values === undefined) {
        edges.set(key, [index]);
      } else {
        values.push(index);
      }
    };
    for (let i = 0; i < this.segments.length; i++) {
      add(Sampler.Key(this.segments[i].First), i);
      add(Sampler.Key(this.segments[i].Last), i);
    }
    const used: boolean[] = new Array<boolean>(this.segments.length).fill(false);
    const trace = (index: number, start: string): void => {
      let key = start;
      while (index >= 0 && !used[index]) {
        used[index] = true;
        const segment = this.segments[index];
        const a = Sampler.Key(segment.First) === key ? segment.First : segment.Last;
        const b = Sampler.Key(segment.First) === key ? segment.Last : segment.First;
        if (this.current.length === 0) {
          this.Append(a);
        }
        this.Append(b);
        key = Sampler.Key(b);
        if (key === start) {
          break;
        }
        const candidates = edges.get(key);
        let nextIndex = -1;
        if (candidates !== undefined) {
          for (let i = 0; i < candidates.length; i++) {
            if (!used[candidates[i]]) {
              nextIndex = candidates[i];
              break;
            }
          }
        }
        index = nextIndex;
      }
      this.Flush();
    };
    edges.forEach((values: number[], key: string): void => {
      if (values.length !== 2) {
        for (let i = 0; i < values.length; i++) {
          if (!used[values[i]]) {
            trace(values[i], key);
          }
        }
      }
    });
    for (let i = 0; i < this.segments.length; i++) {
      if (!used[i]) {
        trace(i, Sampler.Key(this.segments[i].First));
      }
    }
  }
}

interface CompiledExpressionLike {
  Evaluate: (x: number, y: number, t: number, a: number, b: number, c: number) => number;
}
// 确定性向量入口：编译 + 采样 → 路径数 / 首末点 / 总点数。
import { compilePlot } from './PlotCompiler';

export function plotSampleProbe(
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
  const compiled = compilePlot(definition);
  const mesh = samplePlot(compiled, definition);
  const result: Record<string, Object> = {
    Paths: mesh.Paths.length
  };
  if (mesh.Paths.length > 0) {
    const first = mesh.Paths[0];
    const last = mesh.Paths[mesh.Paths.length - 1];
    result['FirstPoint'] = [first[0].X, first[0].Y];
    result['LastPoint'] = [last[last.length - 1].X, last[last.length - 1].Y];
  }
  let count = 0;
  for (let i = 0; i < mesh.Paths.length; i++) {
    count += mesh.Paths[i].length;
  }
  result['PointCount'] = count;
  if (mesh.Limited) {
    result['Limited'] = true;
  }
  return result;
}
