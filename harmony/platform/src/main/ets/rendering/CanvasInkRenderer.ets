// @parity src/InkCanvas.Ink/Rendering/InkSceneRenderer.cs（ArkUI Canvas 实现）
// 双层渲染 + OffscreenCanvas 离屏缓存：
// - committed（已提交笔画）绘制在视口尺寸的 OffscreenCanvas 上持久缓存，视图矩阵在离屏内应用，
//   产出的 ImageBitmap 是屏幕空间位图；内容不变的帧（高频书写中）零路径填充、零位图分配，
//   仅在缓存发布帧把位图 drawImage 到下层可见 Canvas。
// - active（活动笔画/选区）留在上层可见 Canvas，按前后帧世界脏盒求并集后局部 clear/clip 重绘；
//   选区显示、视图变化等复杂场景保守回退全屏重绘。
// 说明：transferToImageBitmap 后离屏后备位图是否清空在文档中未明确，增量追加前统一先把旧位图
// drawImage 回离屏画布做语义兜底（复制语义下为同像素覆盖，移交语义下等价于恢复），两种实现均正确。
// 注意：真实 fillRule/性能需在模拟器与真机核对（见 porting notes）。
import { DrawCommand, Invalidation, InkRenderer, InkScene } from '@inkcanvas/ink';

type Any2DContext = CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D;

const MATRIX_EPSILON: number = 1e-6;
const WORLD_BOX_PADDING_VP: number = 1;
const SCREEN_BOX_PADDING_VP: number = 1.5;

export class CanvasInkRenderer implements InkRenderer {
  readonly Kind: string = 'arkui-canvas';
  private committedLayer: CanvasRenderingContext2D;
  private active: CanvasRenderingContext2D;
  private widthVp: number = 0;
  private heightVp: number = 0;

  // 离屏缓存状态
  private offscreen: OffscreenCanvas | null = null;
  private offCtx: OffscreenCanvasRenderingContext2D | null = null;
  private cacheBitmap: ImageBitmap | null = null;
  private cacheDirty: boolean = true;
  private cachedView: number[] | null = null;
  private cachedSignatures: string[] = [];

  // 活动层脏矩形状态（世界坐标）
  private prevActiveBox: number[] | null = null;
  private prevSelectionVisible: boolean = false;

  // Pen Kit 报点预测临时段：世界坐标扁平点列 [x0,y0,x1,y1,...]。
  // 只存在于活动层，每帧由宿主用最新 TouchEvent 整体替换；绝不进入 OffscreenCanvas 离屏缓存、
  // InkDocument 或任何持久化。抬笔/取消/能力不可用时宿主调用 clearPrediction 清空。
  private prediction: number[] = [];
  private predictionColor: number = 0;
  private predictionWidth: number = 0;
  private prevPredictionBox: number[] | null = null;

  constructor(committed: CanvasRenderingContext2D, active: CanvasRenderingContext2D) {
    this.committedLayer = committed;
    this.active = active;
  }

  // 更新本帧预测段（世界坐标）。传空数组等价于本帧不绘制预测。
  setPrediction(pointsWorld: number[], colorPacked: number, widthVp: number): void {
    this.prediction = pointsWorld;
    this.predictionColor = colorPacked;
    this.predictionWidth = widthVp;
  }

  clearPrediction(): void {
    this.prediction = [];
    this.predictionWidth = 0;
  }

  resize(widthVp: number, heightVp: number): void {
    const sizeChanged = this.widthVp !== widthVp || this.heightVp !== heightVp;
    this.widthVp = widthVp;
    this.heightVp = heightVp;
    this.committedLayer.setTransform(1, 0, 0, 1, 0, 0);
    this.committedLayer.clearRect(0, 0, widthVp, heightVp);
    this.active.setTransform(1, 0, 0, 1, 0, 0);
    this.active.clearRect(0, 0, widthVp, heightVp);
    // 尺寸变化必须重建离屏画布（位图尺寸不可变），所有缓存状态作废。
    if (sizeChanged || this.offscreen === null) {
      this.destroyOffscreen();
      this.cacheDirty = true;
      this.cachedView = null;
      this.cachedSignatures = [];
      this.prevActiveBox = null;
      this.prevSelectionVisible = false;
      this.prevPredictionBox = null;
      this.prediction = [];
    }
  }

  render(scene: InkScene, invalidation: Invalidation): void {
    // 外部当前统一传入 Invalidation.full()；渲染器内部以视图矩阵 + 命令签名做更细的缓存决策，
    // 参数保留以兼容未来调用方下发精确脏盒。
    void invalidation;
    if (this.widthVp <= 0 || this.heightVp <= 0) {
      return;
    }
    this.ensureOffscreen();
    const offCtx = this.offCtx;
    if (offCtx === null || this.offscreen === null) {
      return;
    }
    const v = scene.View;
    const signatures: string[] = [];
    for (let i = 0; i < scene.Committed.length; i++) {
      signatures.push(CanvasInkRenderer.commandSignature(scene.Committed[i]));
    }
    const viewChanged = this.cachedView === null || !CanvasInkRenderer.viewsEqual(this.cachedView, v);
    let mode: number; // 0=full 1=append 2=reuse
    if (viewChanged || this.cacheDirty) {
      mode = 0;
    } else {
      mode = this.classifyCommands(signatures);
    }

    let cachePublished = false;
    if (mode === 0) {
      this.rebuildOffscreen(offCtx, scene, v);
      this.publishCache();
      cachePublished = true;
    } else if (mode === 1) {
      // 前缀匹配的纯新增：先恢复旧位图，再在其上按当前视图矩阵追加新命令。
      offCtx.setTransform(1, 0, 0, 1, 0, 0);
      if (this.cacheBitmap !== null) {
        offCtx.drawImage(this.cacheBitmap, 0, 0, this.widthVp, this.heightVp);
      }
      offCtx.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
      this.drawCommands(offCtx, scene.Committed.slice(this.cachedSignatures.length), false);
      this.publishCache();
      cachePublished = true;
    }
    this.cachedView = v.slice();
    this.cachedSignatures = signatures;
    this.cacheDirty = false;

    // 下层可见 Canvas：仅在缓存发布时重贴位图；复用帧保留持久位图，不产生任何绘制调用。
    if (cachePublished && this.cacheBitmap !== null) {
      this.committedLayer.setTransform(1, 0, 0, 1, 0, 0);
      this.committedLayer.clearRect(0, 0, this.widthVp, this.heightVp);
      this.committedLayer.drawImage(this.cacheBitmap, 0, 0, this.widthVp, this.heightVp);
    }

    this.renderActiveLayer(scene, v, mode === 0);
  }

  // 0=full（数量减少或任一既有命令变化），1=append（前缀全匹配且仅尾部新增），2=reuse（完全一致）
  private classifyCommands(signatures: string[]): number {
    const oldLen = this.cachedSignatures.length;
    const newLen = signatures.length;
    if (newLen === oldLen) {
      for (let i = 0; i < oldLen; i++) {
        if (this.cachedSignatures[i] !== signatures[i]) {
          return 0;
        }
      }
      return 2;
    }
    if (newLen < oldLen) {
      return 0;
    }
    for (let i = 0; i < oldLen; i++) {
      if (this.cachedSignatures[i] !== signatures[i]) {
        return 0;
      }
    }
    return 1;
  }

  private rebuildOffscreen(
    offCtx: OffscreenCanvasRenderingContext2D,
    scene: InkScene,
    v: number[]
  ): void {
    offCtx.setTransform(1, 0, 0, 1, 0, 0);
    offCtx.clearRect(0, 0, this.widthVp, this.heightVp);
    offCtx.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
    this.drawCommands(offCtx, scene.Committed, false);
  }

  private publishCache(): void {
    if (this.offscreen === null) {
      return;
    }
    if (this.cacheBitmap !== null) {
      this.cacheBitmap.close();
      this.cacheBitmap = null;
    }
    this.cacheBitmap = this.offscreen.transferToImageBitmap();
  }

  private renderActiveLayer(scene: InkScene, v: number[], forceFull: boolean): void {
    const selectionVisible = scene.Selection !== null;
    const selectionToggled = selectionVisible !== this.prevSelectionVisible;
    const activeBox = scene.Active !== null
      ? CanvasInkRenderer.commandBox(scene.Active, WORLD_BOX_PADDING_VP)
      : null;
    const currPredictionBox = this.predictionAabb();
    // 当前帧活动层总脏盒 = 活体笔画盒 ∪ 预测段盒。
    const currBox = activeBox === null
      ? currPredictionBox
      : (currPredictionBox === null ? activeBox : CanvasInkRenderer.unionBox(activeBox, currPredictionBox));
    const fullRedraw = forceFull || selectionVisible || selectionToggled;

    if (fullRedraw) {
      this.active.setTransform(1, 0, 0, 1, 0, 0);
      this.active.clearRect(0, 0, this.widthVp, this.heightVp);
      this.active.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
      if (scene.Active !== null) {
        this.drawCommands(this.active, [scene.Active], false);
      }
      this.drawPrediction(v);
      if (scene.Selection !== null) {
        this.drawSelection(scene);
      }
    } else {
      let dirtyWorld: number[] | null = null;
      if (this.prevActiveBox !== null && currBox !== null) {
        dirtyWorld = CanvasInkRenderer.unionBox(this.prevActiveBox, currBox);
      } else if (currBox !== null) {
        dirtyWorld = currBox.slice();
      } else if (this.prevActiveBox !== null) {
        dirtyWorld = this.prevActiveBox.slice();
      }
      // 上一帧预测段所在区域也必须纳入清除/重绘范围，避免预测线拖影。
      if (dirtyWorld === null) {
        if (this.prevPredictionBox !== null) {
          dirtyWorld = this.prevPredictionBox.slice();
        }
      } else if (this.prevPredictionBox !== null) {
        dirtyWorld = CanvasInkRenderer.unionBox(dirtyWorld, this.prevPredictionBox);
      }
      if (dirtyWorld === null) {
        // 前后帧均无活动笔画：上层保持空位图，无需任何绘制。
        this.prevActiveBox = null;
        this.prevPredictionBox = null;
        this.prevSelectionVisible = selectionVisible;
        return;
      }
      const screenBox = this.worldToScreenBox(dirtyWorld, v);
      if (screenBox === null) {
        // 脏盒失效的防御性回退：全屏重绘。
        this.active.setTransform(1, 0, 0, 1, 0, 0);
        this.active.clearRect(0, 0, this.widthVp, this.heightVp);
      } else {
        this.active.setTransform(1, 0, 0, 1, 0, 0);
        this.active.clearRect(screenBox[0], screenBox[1],
          screenBox[2] - screenBox[0], screenBox[3] - screenBox[1]);
      }
      this.active.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
      if (scene.Active !== null) {
        this.active.save();
        if (screenBox !== null) {
          this.active.rect(dirtyWorld[0], dirtyWorld[1],
            dirtyWorld[2] - dirtyWorld[0], dirtyWorld[3] - dirtyWorld[1]);
          this.active.clip();
        }
        this.drawCommands(this.active, [scene.Active], false);
        this.drawPrediction(v);
        this.active.restore();
      } else {
        // 无活体笔画但仍有预测段（理论上仅边界时序），在同一裁剪盒内补画。
        this.drawPrediction(v);
      }
    }

    this.prevActiveBox = activeBox;
    this.prevPredictionBox = currPredictionBox;
    this.prevSelectionVisible = selectionVisible;
  }

  // 预测段世界坐标轴对齐包围盒；点数不足（不足一条线段）时返回 null。
  private predictionAabb(): number[] | null {
    if (this.prediction.length < 4) {
      return null;
    }
    let minX = Number.POSITIVE_INFINITY;
    let minY = Number.POSITIVE_INFINITY;
    let maxX = Number.NEGATIVE_INFINITY;
    let maxY = Number.NEGATIVE_INFINITY;
    for (let i = 0; i + 1 < this.prediction.length; i += 2) {
      const x = this.prediction[i];
      const y = this.prediction[i + 1];
      if (x < minX) {
        minX = x;
      }
      if (y < minY) {
        minY = y;
      }
      if (x > maxX) {
        maxX = x;
      }
      if (y > maxY) {
        maxY = y;
      }
    }
    if (!Number.isFinite(minX) || !Number.isFinite(minY)) {
      return null;
    }
    // 线宽一半作为额外填充，避免圆角线帽被裁掉。
    const pad = WORLD_BOX_PADDING_VP + this.predictionWidth * 0.5;
    return [minX - pad, minY - pad, maxX + pad, maxY + pad];
  }

  // 在活动层按当前视图矩阵描边预测段；调用方已设置好 transform（局部分支已 clip 到脏盒）。
  private drawPrediction(v: number[]): void {
    if (this.prediction.length < 4 || this.predictionWidth <= 0) {
      return;
    }
    const context = this.active;
    context.save();
    context.setTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
    context.strokeStyle = CanvasInkRenderer.cssColor(this.predictionColor);
    context.lineWidth = this.predictionWidth;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    context.beginPath();
    context.moveTo(this.prediction[0], this.prediction[1]);
    for (let i = 2; i + 1 < this.prediction.length; i += 2) {
      context.lineTo(this.prediction[i], this.prediction[i + 1]);
    }
    context.stroke();
    context.restore();
  }

  private ensureOffscreen(): void {
    if (this.offscreen !== null && this.offCtx !== null) {
      return;
    }
    // OffscreenCanvas 构造默认单位为 vp，与可见 CanvasRenderingContext2D 坐标系一致。
    const canvas = new OffscreenCanvas(this.widthVp, this.heightVp);
    const context = canvas.getContext('2d', new RenderingContextSettings(true));
    this.offscreen = canvas;
    this.offCtx = context;
    this.cacheDirty = true;
  }

  private destroyOffscreen(): void {
    if (this.cacheBitmap !== null) {
      this.cacheBitmap.close();
      this.cacheBitmap = null;
    }
    this.offscreen = null;
    this.offCtx = null;
  }

  private drawCommands(context: Any2DContext, commands: DrawCommand[], clear: boolean = true): void {
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

  // 命令身份指纹：仅读取轮廓元数据与首末坐标，不做几何计算，用于缓存命中判定。
  private static commandSignature(command: DrawCommand): string {
    let totalPoints = 0;
    let firstX = 0;
    let firstY = 0;
    let lastX = 0;
    let lastY = 0;
    for (let c = 0; c < command.Contours.length; c++) {
      const contour = command.Contours[c];
      totalPoints += contour.length / 2;
      if (c === 0 && contour.length >= 2) {
        firstX = contour[0];
        firstY = contour[1];
      }
      if (contour.length >= 2) {
        lastX = contour[contour.length - 2];
        lastY = contour[contour.length - 1];
      }
    }
    return command.Color + '#' + command.Contours.length + '#' + totalPoints + '#'
      + firstX.toFixed(2) + ',' + firstY.toFixed(2) + '#' + lastX.toFixed(2) + ',' + lastY.toFixed(2);
  }

  private static viewsEqual(a: number[], b: number[]): boolean {
    if (a.length !== 6 || b.length !== 6) {
      return false;
    }
    for (let i = 0; i < 6; i++) {
      if (Math.abs(a[i] - b[i]) > MATRIX_EPSILON) {
        return false;
      }
    }
    return true;
  }

  private static commandBox(command: DrawCommand, padding: number): number[] | null {
    let minX = Number.POSITIVE_INFINITY;
    let minY = Number.POSITIVE_INFINITY;
    let maxX = Number.NEGATIVE_INFINITY;
    let maxY = Number.NEGATIVE_INFINITY;
    for (let c = 0; c < command.Contours.length; c++) {
      const contour = command.Contours[c];
      for (let p = 0; p + 1 < contour.length; p += 2) {
        const x = contour[p];
        const y = contour[p + 1];
        if (x < minX) {
          minX = x;
        }
        if (y < minY) {
          minY = y;
        }
        if (x > maxX) {
          maxX = x;
        }
        if (y > maxY) {
          maxY = y;
        }
      }
    }
    if (!Number.isFinite(minX) || !Number.isFinite(minY)) {
      return null;
    }
    return [minX - padding, minY - padding, maxX + padding, maxY + padding];
  }

  private static unionBox(a: number[], b: number[]): number[] {
    return [
      Math.min(a[0], b[0]),
      Math.min(a[1], b[1]),
      Math.max(a[2], b[2]),
      Math.max(a[3], b[3])
    ];
  }

  // 世界坐标脏盒经仿射视图矩阵映射为屏幕空间轴对齐包围盒，并与视口求交。
  private worldToScreenBox(world: number[], v: number[]): number[] | null {
    const cornersX: number[] = [world[0], world[2], world[0], world[2]];
    const cornersY: number[] = [world[1], world[1], world[3], world[3]];
    let minX = Number.POSITIVE_INFINITY;
    let minY = Number.POSITIVE_INFINITY;
    let maxX = Number.NEGATIVE_INFINITY;
    let maxY = Number.NEGATIVE_INFINITY;
    for (let i = 0; i < 4; i++) {
      const x = cornersX[i];
      const y = cornersY[i];
      const sx = v[0] * x + v[2] * y + v[4];
      const sy = v[1] * x + v[3] * y + v[5];
      if (!Number.isFinite(sx) || !Number.isFinite(sy)) {
        return null;
      }
      if (sx < minX) {
        minX = sx;
      }
      if (sy < minY) {
        minY = sy;
      }
      if (sx > maxX) {
        maxX = sx;
      }
      if (sy > maxY) {
        maxY = sy;
      }
    }
    minX = Math.max(0, minX - SCREEN_BOX_PADDING_VP);
    minY = Math.max(0, minY - SCREEN_BOX_PADDING_VP);
    maxX = Math.min(this.widthVp, maxX + SCREEN_BOX_PADDING_VP);
    maxY = Math.min(this.heightVp, maxY + SCREEN_BOX_PADDING_VP);
    if (maxX <= minX || maxY <= minY) {
      return null;
    }
    return [minX, minY, maxX, maxY];
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
