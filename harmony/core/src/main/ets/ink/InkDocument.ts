// @parity src/InkCanvas.Core/Ink/InkDocument.cs
import { InkStroke } from './InkStroke';

export class InkDocument {
  private strokes: InkStroke[] = [];

  get Strokes(): InkStroke[] {
    return this.strokes;
  }

  get Count(): number {
    return this.strokes.length;
  }

  add(stroke: InkStroke): void {
    this.strokes.push(stroke);
  }

  remove(stroke: InkStroke): boolean {
    const index = this.strokes.indexOf(stroke);
    if (index < 0) {
      return false;
    }
    this.strokes.splice(index, 1);
    return true;
  }

  clear(): void {
    this.strokes = [];
  }

  clone(): InkDocument {
    const copy = new InkDocument();
    for (let i = 0; i < this.strokes.length; i++) {
      copy.add(this.strokes[i].clone());
    }
    return copy;
  }
}
