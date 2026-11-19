export const MODULE: string = 'ink';
export { Invalidation, InkScene } from './src/main/ets/Renderer';
export type { DrawCommand, SelectionOverlay } from './src/main/ets/Renderer';
export type { InkRenderer, HandlePoint } from './src/main/ets/Renderer';
export { buildScene, sceneSummary } from './src/main/ets/SceneBuilder';
export type { SceneStrokeInput, ScenePointInput, SceneSelectionInput, SceneSummaryResult } from './src/main/ets/SceneBuilder';
export { InkController, runControllerScenario } from './src/main/ets/InkController';
export {
  InkTool,
  InkEraserMode,
  OptionalOperation,
  TransformCenterOption,
  PointerKind,
  PointerSample,
  ControllerOptions
} from './src/main/ets/ControllerTypes';
export type {
  ShapeAttributesInput,
  VoidCallback,
  SlideFlipCallback,
  TimeSource,
  AskRemoveAsymptotesCallback,
  ScenarioAttributes,
  ScenarioPoint,
  ScenarioPointer,
  ScenarioSeed,
  ScenarioAdvanced,
  ScenarioPalm,
  ScenarioInitial,
  ScenarioOp,
  ControllerScenario,
  ControllerTraceStep,
  ControllerStrokeSummary,
  ControllerFrameSummary,
  ControllerFinalState,
  ControllerScenarioResult,
  ISelectionElement,
  ElementSnapshot
} from './src/main/ets/ControllerTypes';
