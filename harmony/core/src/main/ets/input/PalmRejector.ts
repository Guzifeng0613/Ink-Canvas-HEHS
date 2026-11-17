// @parity src/InkCanvas.Platform/Input/PalmRejector.cs
import {
  TouchDecision,
  classifyContact,
  measureContactWidth
} from './TouchContactClassifier';
import type { AdvancedTouchInput } from './TouchContactClassifier';

export enum OverlayPointerKind {
  Pen = 0,
  Touch = 1,
  Mouse = 2
}

const DEFAULT_ADVANCED: AdvancedTouchInput = {
  isSpecialScreen: false,
  isQuadIR: false,
  touchMultiplier: 0.25,
  nibModeBoundsWidth: 10,
  fingerModeBoundsWidth: 30,
  nibModeBoundsWidthThresholdValue: 2.5,
  fingerModeBoundsWidthThresholdValue: 2.5,
  nibModeBoundsWidthEraserSize: 0.8,
  fingerModeBoundsWidthEraserSize: 0.8
};

export interface PalmEvent {
  type: string;
  id: number;
  kind: number;
  contactWidth: number;
  contactHeight: number;
}

export interface PalmOptions {
  palmContactThreshold: number;
  penHasPriority: boolean;
  advanced: AdvancedTouchInput | null;
  nibMode: boolean;
  forceEraser: boolean;
  forcePointEraser: boolean;
  pptGestureEligible: boolean;
}

export interface PalmEventResult {
  shouldDraw: boolean;
  shouldErase: boolean;
  intent: number;
  eraserDiameter: number;
}

export interface PalmSimResult {
  results: PalmEventResult[];
  isPenActive: boolean;
  activePalms: number[];
}

export class PalmRejector {
  PalmContactThreshold: number = 30;
  PenHasPriority: boolean = true;
  Advanced: AdvancedTouchInput | null = null;
  NibMode: boolean = false;
  ForceEraser: boolean = false;
  ForcePointEraser: boolean = true;
  PptGestureEligible: boolean = false;

  private activePenId: number | null = null;
  private palms: number[] = [];

  get IsPenActive(): boolean {
    return this.activePenId !== null;
  }

  get ActivePalms(): number[] {
    return this.palms.slice();
  }

  isPalmContact(width: number, height: number): boolean {
    return Math.max(width, height) >= this.PalmContactThreshold;
  }

  onDown(id: number, kind: number, contactWidth: number, contactHeight: number): void {
    if (kind === OverlayPointerKind.Pen) {
      this.activePenId = id;
    } else if (kind === OverlayPointerKind.Touch && this.isPalmContact(contactWidth, contactHeight)) {
      if (this.palms.indexOf(id) < 0) {
        this.palms.push(id);
      }
    }
  }

  onUp(id: number): void {
    if (this.activePenId === id) {
      this.activePenId = null;
    }
    const index = this.palms.indexOf(id);
    if (index >= 0) {
      this.palms.splice(index, 1);
    }
  }

  classifyContact(id: number, kind: number, contactWidth: number, contactHeight: number): TouchDecision {
    const advanced = this.Advanced === null ? DEFAULT_ADVANCED : this.Advanced;
    const width = measureContactWidth(contactWidth, contactHeight, advanced.isQuadIR);
    return classifyContact({
      contactWidth: width,
      advanced,
      nibMode: this.NibMode,
      forceEraser: this.ForceEraser,
      isDrawingShape: false,
      forcePointEraser: this.ForcePointEraser,
      pptGestureEligible: this.PptGestureEligible
    });
  }

  shouldDraw(id: number, kind: number): boolean {
    if (kind === OverlayPointerKind.Pen) {
      return true;
    }
    if (this.palms.indexOf(id) >= 0) {
      return false;
    }
    if (this.PenHasPriority && this.IsPenActive) {
      return false;
    }
    return true;
  }

  shouldErase(id: number, kind: number): boolean {
    return kind === OverlayPointerKind.Touch && this.palms.indexOf(id) >= 0;
  }

  reset(): void {
    this.activePenId = null;
    this.palms = [];
  }
}

export function simulatePalm(events: PalmEvent[], options: PalmOptions): PalmSimResult {
  const rejector = new PalmRejector();
  rejector.PalmContactThreshold = options.palmContactThreshold;
  rejector.PenHasPriority = options.penHasPriority;
  rejector.Advanced = options.advanced;
  rejector.NibMode = options.nibMode;
  rejector.ForceEraser = options.forceEraser;
  rejector.ForcePointEraser = options.forcePointEraser;
  rejector.PptGestureEligible = options.pptGestureEligible;

  const results: PalmEventResult[] = [];
  for (let i = 0; i < events.length; i++) {
    const event = events[i];
    if (event.type === 'down') {
      rejector.onDown(event.id, event.kind, event.contactWidth, event.contactHeight);
    } else {
      rejector.onUp(event.id);
    }
    const decision = rejector.classifyContact(event.id, event.kind, event.contactWidth, event.contactHeight);
    results.push({
      shouldDraw: rejector.shouldDraw(event.id, event.kind),
      shouldErase: rejector.shouldErase(event.id, event.kind),
      intent: decision.intent,
      eraserDiameter: decision.eraserDiameter
    });
  }
  const activePalms = rejector.ActivePalms.sort((a: number, b: number): number => a - b);
  return { results, isPenActive: rejector.IsPenActive, activePalms };
}
