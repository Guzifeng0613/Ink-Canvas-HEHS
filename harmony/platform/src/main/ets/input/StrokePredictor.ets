// Pen Kit 报点预测封装（@kit.Penkit PointPredictor，起始 5.0.0(12)，
// SystemCapability.Stylus.Handwrite，无需权限）。
// 职责单一：给定原始 TouchEvent 返回一个预测点（组件坐标 vp，与 TouchObject.x/y 同空间）。
// 不持有文档状态、不渲染、不写缓存、不做坐标变换；不支持时全部方法空操作返回 null，
// 调用方可按 null 零开销降级，手指/鼠标路径与不支持该能力的设备行为完全不变。
import { PointPredictor } from '@kit.Penkit';
import { hasSysCap, SYSCAP_STYLUS_HANDWRITE } from '../compat/Capabilities';

export interface PredictedPoint {
  X: number;
  Y: number;
}

export class StrokePredictor {
  private predictor: PointPredictor | null = null;

  constructor() {
    try {
      if (hasSysCap(SYSCAP_STYLUS_HANDWRITE)) {
        this.predictor = new PointPredictor();
      }
    } catch (e) {
      // 设备声明支持但构造失败（如服务不可用）时按不支持处理。
      this.predictor = null;
    }
  }

  get isReady(): boolean {
    return this.predictor !== null;
  }

  // 仅在 TouchType.Move 且手写笔书写场景调用；任何异常都视为本帧无预测点。
  predict(event: TouchEvent): PredictedPoint | null {
    const predictor = this.predictor;
    if (predictor === null) {
      return null;
    }
    try {
      const point: TouchPoint = predictor.getPredictionPoint(event);
      if (point === null || point === undefined) {
        return null;
      }
      // TouchPoint.x/y 声明为 Dimension（number | string），运行时为数值坐标，统一收敛为 number。
      const px = Number(point.x);
      const py = Number(point.y);
      if (!Number.isFinite(px) || !Number.isFinite(py)) {
        return null;
      }
      return { X: px, Y: py };
    } catch (e) {
      return null;
    }
  }
}
