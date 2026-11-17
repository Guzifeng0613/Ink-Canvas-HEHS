// @parity src/InkCanvas.Core/Input/TouchContactClassifier.cs
export enum TouchIntent {
  Draw = 0,
  EraseByStroke = 1,
  EraseByPoint = 2,
  GestureOnly = 3,
  Ignore = 4
}

export interface AdvancedTouchInput {
  isSpecialScreen: boolean;
  isQuadIR: boolean;
  touchMultiplier: number;
  nibModeBoundsWidth: number;
  fingerModeBoundsWidth: number;
  nibModeBoundsWidthThresholdValue: number;
  fingerModeBoundsWidthThresholdValue: number;
  nibModeBoundsWidthEraserSize: number;
  fingerModeBoundsWidthEraserSize: number;
}

// 字段用小写以匹配向量 JSON 契约（C# 为 Intent/EraserDiameter）。
export class TouchDecision {
  intent: number;
  eraserDiameter: number;

  constructor(intent: number, eraserDiameter: number) {
    this.intent = intent;
    this.eraserDiameter = eraserDiameter;
  }
}

export interface TouchClassifyInput {
  contactWidth: number;
  advanced: AdvancedTouchInput | null;
  nibMode: boolean;
  forceEraser?: boolean;
  isDrawingShape?: boolean;
  forcePointEraser?: boolean;
  pptGestureEligible?: boolean;
}

export function measureContactWidth(width: number, height: number, isQuadIr: boolean): number {
  return isQuadIr ? Math.sqrt(width * height) : width;
}

export function boundsWidth(advanced: AdvancedTouchInput, nibMode: boolean): number {
  return nibMode ? advanced.nibModeBoundsWidth : advanced.fingerModeBoundsWidth;
}

export function classifyContact(input: TouchClassifyInput): TouchDecision {
  const advanced = input.advanced;
  if (advanced === null || advanced === undefined) {
    return new TouchDecision(TouchIntent.Draw, 5);
  }

  const width = boundsWidth(advanced, input.nibMode);
  const eraserEnabled = advanced.touchMultiplier !== 0 || !advanced.isSpecialScreen;
  const forceEraser = input.forceEraser === true;
  const isDrawingShape = input.isDrawingShape === true;
  const forcePointEraser = input.forcePointEraser === undefined ? true : input.forcePointEraser;
  const pptGestureEligible = input.pptGestureEligible === true;

  if (eraserEnabled && input.contactWidth > width) {
    if (!isDrawingShape && forceEraser) {
      return new TouchDecision(TouchIntent.Ignore, 0);
    }
    const threshold = input.nibMode
      ? advanced.nibModeBoundsWidthThresholdValue
      : advanced.fingerModeBoundsWidthThresholdValue;
    if (input.contactWidth > width * threshold) {
      let diameter = input.contactWidth * (input.nibMode
        ? advanced.nibModeBoundsWidthEraserSize
        : advanced.fingerModeBoundsWidthEraserSize);
      if (advanced.isSpecialScreen) {
        diameter *= advanced.touchMultiplier;
      }
      return new TouchDecision(TouchIntent.EraseByPoint, diameter);
    }
    if (pptGestureEligible) {
      return new TouchDecision(TouchIntent.GestureOnly, 5);
    }
    return new TouchDecision(TouchIntent.EraseByStroke, 5);
  }

  return new TouchDecision(TouchIntent.Draw, forcePointEraser ? 50 : 5);
}
