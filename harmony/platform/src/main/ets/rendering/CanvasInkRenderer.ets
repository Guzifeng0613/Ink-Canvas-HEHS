// @parity src/InkCanvas.Ink/Rendering/InkSceneRenderer.cs（ArkUI Canvas 实现）
// 双层 Canvas：已提交层 + 活动层；一次 fill 每个 contour（优先 nonzero）。
// 注意：真实 fillRule/性能需在模拟器与真机核对（见 porting notes）。
import { DrawCommand, Invalidation, InkRenderer, InkScene } from '@inkcanvas/ink';

export class CanvasInkRenderer implements InkRenderer {
  readonly Kind: string = 'arkui-canvas';
  private committed: CanvasRenderingContext2D;
  private active: CanvasRenderingContext2D;
  private widthVp: number = 0;
  private heightVp: number = 0;

  constructor(committed: CanvasRenderingContext2D, active: CanvasRenderingContext2D) {
    this.committed = committed;
    this.active = active;
  }

  resize(widthVp: number, heightVp: number): void {
    this.widthVp = widthVp;
    this.heightVp = heightVp;
    this.committed.clearRect(0, 0, widthVp, heightVp);
    this.active.clearRect(0, 0, widthVp, heightVp);
  }

  render(scene: InkScene, invalidation: Invalidation): void {
    const v = scene.View;
    this.committed.setTransform(1, 0, 0, 1, 0, 0);
    this.committed.clearRect(0, 0, this.widthVp, this.heightVp);
    this.committed.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
    this.drawCommands(this.committed, scene.Committed, false);
    this.active.setTransform(1, 0, 0, 1, 0, 0);
    this.active.clearRect(0, 0, this.widthVp, this.heightVp);
    this.active.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
    if (scene.Active !== null) {
      this.drawCommands(this.active, [scene.Active], false);
    }
    if (scene.Selection !== null) {
      this.drawSelection(scene);
    }
    void invalidation;
  }

  private drawCommands(context: CanvasRenderingContext2D, commands: DrawCommand[], clear: boolean = true): void {
    if (clear) {
      context.clearRect(0, 0, this.widthVp, this.heightVp);
    }
    for (let i = 0; i < commands.length; i++) {
      const command = commands[i];
      context.fillStyle = CanvasInkRenderer.cssColor(command.Color);
      for (let c = 0; c < command.Contours.length; c++) {
        const contour = command.Contours[c];
        if (contour.length < 6) {
          continue;
        }
        // 防御：NaN/Infinity 轮廓在 Canvas 里会被近似成原点，表现为一条连到画布角落的长线。
        let valid = true;
        for (let k = 0; k + 1 < contour.length; k += 2) {
          if (!Number.isFinite(contour[k]) || !Number.isFinite(contour[k + 1])) {
            valid = false;
            break;
          }
        }
        if (!valid) {
          continue;
        }
        context.beginPath();
        context.moveTo(contour[0], contour[1]);
        for (let p = 2; p + 1 < contour.length; p += 2) {
          context.lineTo(contour[p], contour[p + 1]);
        }
        context.closePath();
        context.fill('nonzero');
      }
    }
  }

  private drawSelection(scene: InkScene): void {
    const selection = scene.Selection;
    if (selection === null) {
      return;
    }
    const left = selection.Bounds[0];
    const top = selection.Bounds[1];
    const right = selection.Bounds[2];
    const bottom = selection.Bounds[3];
    const centerX = (left + right) / 2;
    const centerY = (top + bottom) / 2;
    const context = this.active;
    context.save();
    context.translate(centerX, centerY);
    context.rotate(selection.RotationRadians);
    context.strokeStyle = 'rgba(0,120,212,1)';
    context.lineWidth = 1.5;
    context.strokeRect(left - centerX, top - centerY, right - left, bottom - top);
    context.fillStyle = '#FFFFFF';
    for (let i = 0; i < selection.Handles.length; i++) {
      const handle = selection.Handles[i];
      const radius = i === 4 ? 7 : 6;
      context.beginPath();
      context.arc(handle.X - centerX, handle.Y - centerY, radius, 0, Math.PI * 2);
      context.fill();
      context.stroke();
    }
    context.restore();
  }

  private static cssColor(packed: number): string {
    const n = packed >>> 0;
    const a = ((n >>> 24) & 0xff) / 255;
    const r = (n >>> 16) & 0xff;
    const g = (n >>> 8) & 0xff;
    const b = n & 0xff;
    return 'rgba(' + r + ',' + g + ',' + b + ',' + a.toFixed(3) + ')';
  }
}
