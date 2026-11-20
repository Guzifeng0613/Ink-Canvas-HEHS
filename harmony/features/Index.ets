export const MODULE: string = 'features';
export {
  readTransform,
  readTransformArray,
  writeTransform,
  appendTransform,
  sameTransform,
  transformedBounds,
  renderMatrix,
  renderMatrix4Values
} from './src/main/ets/board/BoardElementMath';
export {
  BoardBackground,
  BoardPage,
  BoardDocument,
  boardDocumentSummary,
  boardRoundtrip,
  boardSavePageProbe,
  boardPageOpsProbe
} from './src/main/ets/board/BoardDocument';
export type { BoardAsset, BoardDocumentSummary, BoardSavePageProbe, BoardPageCounts } from './src/main/ets/board/BoardDocument';
export {
  BoardElementHistory,
  snapshotGeometry,
  sameGeometry,
  applyGeometry,
  recordTransform,
  elementTransformProbe
} from './src/main/ets/board/BoardElementHistory';
export type { ElementGeometry, ElementAssetLike, ElementUndoEntry } from './src/main/ets/board/BoardElementHistory';
export { BoardSelectionElement, boardElementSelectionProbe } from './src/main/ets/board/BoardSelectionElement';
export { CoursewareSession, coursewareNavigate, coursewareManifestSummary, recommendedPollIntervalMs } from './src/main/ets/courseware/CoursewareSession';
export type { CoursewareBundle, CoursewarePageFile, CoursewareSummary } from './src/main/ets/courseware/CoursewareSession';
export { CountdownEngine, countdownInterval, countdownDisplay, countdownTick } from './src/main/ets/tools/CountdownEngine';
export type { CountdownTickResult } from './src/main/ets/tools/CountdownEngine';
export { LotteryEngine, lotterySequence } from './src/main/ets/tools/LotteryEngine';
export type { LotteryDraw } from './src/main/ets/tools/LotteryEngine';
export { parseRoster, splitFields, rosterProbe } from './src/main/ets/tools/ClassroomRoster';
export { normalizeExpression, compileExpression, FormatError } from './src/main/ets/analytic/MathExpression';
export type { Evaluator, CompiledExpression } from './src/main/ets/analytic/MathExpression';
export {
  PlotKind, CompiledPlotKind, createDefaultPlotDefinition, validatePlotDefinition, compilePlot
} from './src/main/ets/analytic/PlotCompiler';
export type { PlotDefinition, CompiledPlot } from './src/main/ets/analytic/PlotCompiler';
export { samplePlot, PLOT_WIDTH, PLOT_HEIGHT, MAXIMUM_PLOT_POINTS, MAXIMUM_PLOT_EVALUATIONS } from './src/main/ets/analytic/PlotSampler';
export type { PlotPoint, PlotMesh } from './src/main/ets/analytic/PlotSampler';
export {
  applyReplaceRules,
  lotteryDisplayName,
  lotteryFlicker,
  lotteryDrawUnique,
  lotterySplitGroups
} from './src/main/ets/tools/LotteryRoll';
