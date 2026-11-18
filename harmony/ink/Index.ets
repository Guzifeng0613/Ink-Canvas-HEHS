export const MODULE: string = 'ink';
export { DrawCommand, Invalidation, InkScene, SelectionOverlay } from './src/main/ets/Renderer';
export type { InkRenderer, HandlePoint } from './src/main/ets/Renderer';
export { buildScene, sceneSummary } from './src/main/ets/SceneBuilder';
export type { SceneStrokeInput, ScenePointInput, SceneSelectionInput, SceneSummaryResult } from './src/main/ets/SceneBuilder';
