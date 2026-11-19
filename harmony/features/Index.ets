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
export {
  applyReplaceRules,
  lotteryDisplayName,
  lotteryFlicker,
  lotteryDrawUnique,
  lotterySplitGroups
} from './src/main/ets/tools/LotteryRoll';
