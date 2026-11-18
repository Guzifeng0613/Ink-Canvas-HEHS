export const MODULE: string = 'features';
export {
  readTransform,
  readTransformArray,
  writeTransform,
  appendTransform,
  sameTransform,
  transformedBounds,
  renderMatrix
} from './src/main/ets/board/BoardElementMath';
export {
  BoardBackground,
  BoardPage,
  BoardDocument,
  boardDocumentSummary,
  boardRoundtrip,
  boardSavePageProbe
} from './src/main/ets/board/BoardDocument';
export type { BoardAsset, BoardDocumentSummary, BoardSavePageProbe } from './src/main/ets/board/BoardDocument';
export { CoursewareSession, coursewareNavigate, coursewareManifestSummary } from './src/main/ets/courseware/CoursewareSession';
export type { CoursewareBundle, CoursewarePageFile, CoursewareSummary } from './src/main/ets/courseware/CoursewareSession';
