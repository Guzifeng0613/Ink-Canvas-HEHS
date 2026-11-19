// @parity src/InkCanvas.App/Services/ShellController.cs
import {
  InkCanvasSurface,
  CanvasInkRenderer,
  pointerKind,
  PdfPageSource,
  StorageService,
  AutoSaveService,
  captureComponentToPng,
  openExternalUrl,
  supportsSystemMaterial,
  supportsSymbolGlyph,
  systemMotionReduced,
  setLogEnabled,
  playCountdownFinish,
  compressImageBuffer,
  COMPRESS_MAX_DIM,
  CountdownPiPService,
  CountdownPiPParams,
  writeAnnotatedPdf,
  readPdfGeometries,
  containPlacement,
  openInWps,
  openAppGalleryDetail,
  checkForUpdate,
  ownBundleName,
  WPS_PAD_BUNDLE,
  WPS_PHONE_BUNDLE,
  isTablet,
  is2in1,
  breakpointForWidth,
  isFullLayout
} from '@inkcanvas/platform';
import {
  FloatingBar,
  PenPalette,
  ShapePicker,
  IconGlyph,
  symbolForTool,
  overlayTransition,
  panelTransition,
  animateMotion,
  MOTION_STANDARD,
  sharedId,
  panelBackground,
  panelBlurStyle
} from '@inkcanvas/controls';
import {
  InkController,
  PointerSample,
  InkTool,
  InkEraserMode,
  buildScene,
  Invalidation
} from '@inkcanvas/ink';
import type { SceneStrokeInput, SceneSelectionInput, ISelectionElement } from '@inkcanvas/ink';
import type { PagePlacement } from '@inkcanvas/platform';
import { InkStroke, InkColor, InkDocument, parseSettings, stringifySettings, createDefaultSettings, matrixToArray, transformPointByMatrix, PenNib, calibrationMultiplier, toTaperKind, toPenStyle, toEraserRadius, resetToRecommendation, TaperKind, placeToolbar } from '@inkcanvas/core';
import type { ShapeKind, StrokePointInput, AppSettings, AdvancedTouchInput, Matrix3x2 } from '@inkcanvas/core';
import { CoursewareSession, CountdownEngine, LotteryEngine, BoardDocument, BoardBackground, renderMatrix4Values, applyReplaceRules, lotteryDisplayName, lotteryFlicker, lotteryDrawUnique, lotterySplitGroups, appendTransform, transformedBounds, snapshotGeometry, recordTransform, BoardSelectionElement, parseRoster } from '@inkcanvas/features';
import type { CoursewarePageFile } from '@inkcanvas/features';
import type { BoardAsset, ElementGeometry } from '@inkcanvas/features';
import { ReplayClock } from '@inkcanvas/core';
import { picker, fileIo } from '@kit.CoreFileKit';
import { KeyCode } from '@kit.InputKit';
import { promptAction, matrix4 } from '@kit.ArkUI';
import { common, ConfigurationConstant } from '@kit.AbilityKit';
import { util } from '@kit.ArkTS';
import { image } from '@kit.ImageKit';

// 应用内 UI 偏好（减少动效开关）。独立于 Settings.json，避免分叉跨端设置格式。
interface UiPrefs {
  reduceMotion: boolean;
}

// G3 选区工具条重染色板（9 色，与笔板深色套一致）。
interface SelectionColor {
  packed: number;
  hex: string;
}

const SELECTION_COLORS: SelectionColor[] = [
  { packed: 0xFF000000, hex: '#FF000000' },
  { packed: 0xFFFFFFFF, hex: '#FFFFFFFF' },
  { packed: 0xFFDC2626, hex: '#FFDC2626' },
  { packed: 0xFF9333CE, hex: '#FF9333CE' },
  { packed: 0xFFEA580C, hex: '#FFEA580C' },
  { packed: 0xFFEAB308, hex: '#FFEAB308' },
  { packed: 0xFF16A34A, hex: '#FF16A34A' },
  { packed: 0xFF0D9488, hex: '#FF0D9488' },
  { packed: 0xFF2563EB, hex: '#FF2563EB' }
];

// 更新日志静态条目（摘要自 CHANGELOG.md；HarmonyOS 移植版随 HAP 分发，不读外部文件）。
interface ChangelogEntry {
  version: string;
  title: string;
  items: string[];
}

const CHANGELOG_ENTRIES: ChangelogEntry[] = [
  {
    version: '3.4.0',
    title: 'WPF 1:1 还原轮 + 平台级缺陷修复',
    items: [
      '浮动栏四卡布局（笑脸/主栏/手势/PPT 退出牌），按钮顺序与选中态逐一对上游。',
      '笔/荧光笔独立菜单，图形菜单 24 项所见即所得预览；形状绘制语义 1:1（中心圆/两步长方体/克隆拖拽等）。',
      '440DIP 设置面板分组还原；计时/抽奖/指南/更新日志/是否窗按上游重建。',
      '画板三浮条 + 图片/视频元素手势（旋转/翻转/双指，单撤销单元）。',
      '平台级修复：辅助窗口 Z 序、窗口拖动、非 100% DPI 裁切、亚克力回退。',
      'HarmonyOS 移植：ArkTS 同构分层，API 26 主基线 + API 24 兼容，逻辑黄金向量互认。'
    ]
  },
  {
    version: '3.3.0',
    title: 'WPF 菜单、白板与 PPT 适配补齐',
    items: [
      '画笔/荧光笔/图形选择改为独立窗口，避免 Flyout 裁切。',
      '鼠标模式使用独立鼠标指针图标；菜单选笔后回到标注模式。',
      'Segoe Fluent Icons 字体随应用打包（HarmonyOS 侧改为 sys.symbol 映射）。'
    ]
  },
  {
    version: '3.1.0',
    title: '选区变换、回放与自动更新',
    items: [
      '选区拖动/缩放/旋转，整段拖动记为一次撤销；双指手势可只作用于选区。',
      '墨迹回放：按绘制顺序重放，0.5–4 倍速与进度拖动。',
      '自动更新与静默时间窗策略；操作指南与更新日志窗口。',
      '透明覆盖层 GPU 回读优化；因果笔锋修复抬笔闪烁。'
    ]
  },
  {
    version: '3.0.0',
    title: 'WinUI 3 迁移',
    items: [
      '从 WPF / .NET Framework 4.7.2 迁移到 WinUI 3（Windows App SDK）。',
      '墨迹引擎以 Win2D 重写；图形识别改为托管几何拟合。',
      'PPT / WPS 改用 COM 后期绑定；界面按 Fluent 重做，适配触摸与动画。'
    ]
  }
];

@Entry
@Component
struct Index {
  @State dark: boolean = false;
  @State paletteOpen: boolean = false;
  @State paletteHighlighter: boolean = false;
  @State shapeOpen: boolean = false;
  @State toolsOpen: boolean = false;
  @State selectedId: string = 'pen';
  @State widthMenu: number = 5;
  @State alpha: number = 140;
  @State taperIndex: number = 0;
  @State currentPacked: number = 0xFF000000;
  @State selectedShape: number = -1;
  @State collapsed: boolean = false;
  @State pptActive: boolean = false;
  @State mode: string = 'annotate';
  @State coursewarePage: image.PixelMap | null = null;
  @State coursewareIndex: number = 0;
  @State coursewareCount: number = 0;
  @State countdownOpen: boolean = false;
  @State countdownDisplay: number = 60;
  @State countdownRing: number = 1;
  @State countdownRunning: boolean = false;
  @State countdownCompact: boolean = false;
  @State countdownFullscreen: boolean = false;
  @State countdownFinished: boolean = false;
  @State countdownPiPActive: boolean = false;
  // G3 选区工具条：随选区放置（FloatingToolbarLayout 候选评分，差异化紧凑形态）。
  @State selectionToolbarOpen: boolean = false;
  @State selectionToolbarX: number = 0;
  @State selectionToolbarY: number = 0;
  @State lotteryOpen: boolean = false;
  @State lotteryNames: string = '';
  @State lotteryResult: string = '';
  @State lotteryNoRepeat: boolean = false;
  @State lotteryRolling: boolean = false;
  @State lotteryGroups: string[] = [];
  @State lotteryCount: number = 1;
  @State lotterySingle: boolean = false;
  @State lotteryReplace: string = '';
  @State lotteryReplaceOpen: boolean = false;
  @State replayOpen: boolean = false;
  @State replayPlaying: boolean = false;
  @State replaySpeed: number = 1;
  @State replayProgress: number = 0;
  @State guideOpen: boolean = false;
  @State settingsOpen: boolean = false;
  @State boardOpen: boolean = false;
  @State boardIndex: number = 0;
  @State boardPageCount: number = 1;
  @State boardBackground: number = 0;
  @State boardImagesVersion: number = 0;
  @State yesNoOpen: boolean = false;
  @State yesNoMessage: string = '';
  @State updateOpen: boolean = false;
  @State changelogOpen: boolean = false;
  @State settingsVersion: number = 0;
  @State reduceMotion: boolean = false;
  @State barDragX: number = 0;
  @State barDragY: number = 0;
  @State windowWidth: number = 0;
  @State windowHeight: number = 0;
  private systemReduceMotion: boolean = false;
  private blurEnabled: boolean = false;
  private symbolIcons: boolean = true;
  private renderer: CanvasInkRenderer | null = null;
  private controller: InkController = new InkController();
  private annotateDocument: InkDocument = new InkDocument();
  private pdfSource: PdfPageSource = new PdfPageSource();
  private courseware: CoursewareSession = new CoursewareSession();
  // D8：课件源 PDF 沙箱路径（保存批注 PDF 用）。
  private coursewareSourcePath: string = '';
  private storage: StorageService | null = null;
  private countdown: CountdownEngine = new CountdownEngine();
  private countdownPiP: CountdownPiPService = new CountdownPiPService();
  private lottery: LotteryEngine = new LotteryEngine();
  private countdownTimer: number = -1;
  private lotteryTimer: number = -1;
  private replayTimer: number = -1;
  private replayClock: ReplayClock | null = null;
  // G6 落点成形动画驱动（16ms 帧步进；动画清空后自停）。
  private dotTimer: number = -1;
  private settings: AppSettings = createDefaultSettings();
  private autoSave: AutoSaveService | null = null;
  private board: BoardDocument = new BoardDocument();
  private boardGridSettings: RenderingContextSettings = new RenderingContextSettings(true);
  private boardGridContext: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.boardGridSettings);
  // G1 套索擦覆盖层：闭合虚线多边形 + 35α 填充（视图 DIP）。
  private lassoSettings: RenderingContextSettings = new RenderingContextSettings(true);
  private lassoContext: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.lassoSettings);
  private pendingYesNo: string = '';
  private mediaPaths: Map<string, string> = new Map<string, string>();
  private boardPixelMaps: Map<string, image.PixelMap> = new Map<string, image.PixelMap>();
  // 视频进度保持：切页/退出时记录 currentTime，回到该页 onPrepared 恢复。
  private videoControllers: Map<string, VideoController> = new Map<string, VideoController>();
  private videoPositions: Map<string, number> = new Map<string, number>();
  private videoPlaying: Map<string, boolean> = new Map<string, boolean>();
  // 画板图片拖拽（Select/Pan 工具下命中图片时接管触摸；单次拖拽记一个页内撤销单元）。
  private dragAsset: BoardAsset | null = null;
  private dragBefore: ElementGeometry | null = null;
  private dragStartX: number = 0;
  private dragStartY: number = 0;
  private dragPointerId: number = -1;
  // 画笔光标（isShowCursor）：跟随指针的指示环。
  @State cursorX: number = 0;
  @State cursorY: number = 0;
  @State cursorVisible: boolean = false;
  // 触摸校准垫：采集接触宽度样本。
  @State calibrationOpen: boolean = false;
  @State calibrationSamples: number[] = [];

  aboutToDisappear(): void {
    this.stopCountdownTimer();
    this.stopLotteryTimer();
    this.stopReplayTimer();
    this.countdownPiP.stop();
  }

  aboutToAppear(): void {
    this.storage = new StorageService(getContext(this).filesDir);
    this.controller.Document = this.annotateDocument;
    this.controller.Invalidated = () => {
      this.refreshScene();
    };
    this.controller.ToolChanged = () => {
      this.refreshScene();
    };
    // G2 元素选区桥：画板模式注入当前页图片/视频为可选元素。
    this.controller.GetSelectableElements = (): ISelectionElement[] => {
      if (!this.boardOpen) {
        return [];
      }
      const result: ISelectionElement[] = [];
      const images = this.board.Current.Images;
      for (let i = 0; i < images.length; i++) {
        result.push(new BoardSelectionElement(this.board, images[i], false));
      }
      const media = this.board.Current.Media;
      for (let i = 0; i < media.length; i++) {
        result.push(new BoardSelectionElement(this.board, media[i], true));
      }
      return result;
    };
    // 选区变化：元素在场/离场需刷新画板 ForEach；选区工具条（G3）随动。
    this.controller.SelectionChanged = () => {
      // D1：元素在场/离场与选区变化都可能改变层序 → 置脏 + 画板重排。
      this.controller.MarkLayerOrderDirty();
      this.boardImagesVersion++;
      this.selectionToolbarOpen = this.controller.HasSelection;
      if (this.selectionToolbarOpen) {
        this.updateSelectionToolbarPosition();
      }
      this.refreshScene();
    };
    this.controller.CurrentAttributes.BaseWidth = 2.5;
    const settingsText = this.storage.readText('Settings.json');
    if (settingsText !== null) {
      this.settings = parseSettings(settingsText);
    }
    // 能力探测：系统材质 / Symbol 图标 / 系统减少动效（失败均回退保守值）。
    try {
      this.blurEnabled = supportsSystemMaterial();
    } catch (e) {
      this.blurEnabled = false;
    }
    try {
      this.symbolIcons = supportsSymbolGlyph();
    } catch (e) {
      this.symbolIcons = false;
    }
    try {
      this.systemReduceMotion = systemMotionReduced(getContext(this) as common.UIAbilityContext);
    } catch (e) {
      this.systemReduceMotion = false;
    }
    this.loadUiPrefs();
    this.applySettings();
    if (this.settings.startup.isFoldAtStartup) {
      this.collapsed = true;
    }
    this.lottery.PeopleCount = this.settings.randSettings.peopleCount;
    this.lotteryNoRepeat = this.settings.randSettings.isNotRepeatName;
    const launchMode = AppStorage.get<string>('launchMode');
    if (launchMode === 'board') { this.openBoard(); }
    this.rebuildAutoSave();
    this.controller.SlideFlipRequested = (next: boolean) => {
      if (this.mode === 'courseware' && this.settings.behavior.isEnableFingerGestureSlideShowControl) {
        this.showCoursewarePage(this.coursewareIndex + (next ? 1 : -1));
      }
    };
  }

  private loadUiPrefs(): void {
    if (this.storage === null) {
      return;
    }
    const text = this.storage.readText('UiPrefs.json');
    if (text === null) {
      return;
    }
    try {
      const prefs = JSON.parse(text) as UiPrefs;
      this.reduceMotion = prefs.reduceMotion === true;
    } catch (e) {
      void e;
    }
  }

  private persistUiPrefs(): void {
    if (this.storage === null) {
      return;
    }
    const prefs: UiPrefs = { reduceMotion: this.reduceMotion };
    this.storage.writeTextAtomic('UiPrefs.json', JSON.stringify(prefs));
  }

  private isMotionReduced(): boolean {
    return this.reduceMotion || this.systemReduceMotion;
  }

  // 断点布局：平板/2in1（md+）走完整布局，手机初步适配（更窄面板）。
  private fullLayout(): boolean {
    return isFullLayout(breakpointForWidth(this.windowWidth));
  }

  private rebuildAutoSave(): void {
    if (this.storage === null) {
      return;
    }
    this.autoSave = new AutoSaveService(this.storage, 'Auto Saved - Strokes',
      this.settings.automation.isAutoSaveStrokesAtClear,
      this.settings.automation.autoDelSavedFilesDaysThreshold);
    if (this.settings.automation.autoDelSavedFiles) {
      this.autoSave.prune();
    }
  }

  private applySettings(): void {
    this.dark = this.resolveDark();
    this.widthMenu = this.settings.canvas.inkWidth * 2;
    this.alpha = this.settings.canvas.inkAlpha;
    this.taperIndex = this.settings.canvas.inkStyle;
    this.controller.CurrentAttributes.BaseWidth = this.settings.canvas.inkWidth;
    this.controller.CurrentAttributes.HighlighterAlpha = this.settings.canvas.inkAlpha;
    // inkStyle 持久化为菜单索引（OriginalMenuCatalog.ToTaperKind 映射，含 4 种模拟压感笔锋）。
    this.controller.CurrentAttributes.TaperKind = toTaperKind(this.settings.canvas.inkStyle);
    // 笔/荧光笔独立宽度（highlighterWidth 缺省 = 跟随笔宽），切工具互换 BaseWidth。
    this.controller.ConfigurePenWidths(
      this.settings.canvas.inkWidth,
      typeof this.settings.canvas.highlighterWidth === 'number' ? this.settings.canvas.highlighterWidth : this.settings.canvas.inkWidth
    );
    this.applyGestures();
    this.controller.MultiTouchEnabled = this.settings.gesture.isEnableMultiTouchMode;
    this.controller.GesturesTargetSelection = this.settings.gesture.isEnableTwoFingerRotationOnSelection;
    this.controller.TransformCenterOption = this.settings.gesture.matrixTransformCenterPoint;
    const advanced: AdvancedTouchInput = {
      isSpecialScreen: this.settings.advanced.isSpecialScreen,
      isQuadIR: this.settings.advanced.isQuadIR,
      touchMultiplier: this.settings.advanced.touchMultiplier,
      nibModeBoundsWidth: this.settings.advanced.nibModeBoundsWidth,
      fingerModeBoundsWidth: this.settings.advanced.fingerModeBoundsWidth,
      nibModeBoundsWidthThresholdValue: this.settings.advanced.nibModeBoundsWidthThresholdValue,
      fingerModeBoundsWidthThresholdValue: this.settings.advanced.fingerModeBoundsWidthThresholdValue,
      nibModeBoundsWidthEraserSize: this.settings.advanced.nibModeBoundsWidthEraserSize,
      fingerModeBoundsWidthEraserSize: this.settings.advanced.fingerModeBoundsWidthEraserSize
    };
    this.controller.Palm.Advanced = advanced;
    this.controller.Palm.NibMode = this.settings.startup.isEnableNibMode;
    this.controller.CurrentAttributes.Nib = this.settings.canvas.penNib;
    // 画布/识别/日志/抽奖接线（C# AppServices.ApplySettings 对应项）。
    this.controller.EraserRadius = this.eraserRadiusForSize(this.settings.canvas.eraserSize);
    this.controller.RecognizeShapes = this.settings.inkToShape.isInkToShapeEnabled;
    setLogEnabled(this.settings.advanced.isLogEnabled);
    this.lottery.PeopleCount = this.settings.randSettings.peopleCount;
    this.lotteryNoRepeat = this.settings.randSettings.isNotRepeatName;
    if (this.controller.Tool === InkTool.Eraser) {
      this.applyEraserType();
    }
  }

  // C# OriginalMenuCatalog.ToEraserRadius：45 * {0.5, 0.8, 1, 1.25, 1.8}[clamp(size,0,4)]。
  private eraserRadiusForSize(size: number): number {
    return toEraserRadius(size);
  }

  // eraserType：0=图标切换（保持当前模式），1=面积擦，2=线条擦。
  private applyEraserType(): void {
    if (this.settings.canvas.eraserType === 1) {
      this.controller.EraserMode = InkEraserMode.Area;
    } else if (this.settings.canvas.eraserType === 2) {
      this.controller.EraserMode = InkEraserMode.WholeStroke;
    }
  }

  // theme：0=跟随系统，1=浅色，2=深色（与 C# WindowChrome.ApplyTheme 一致）。
  private resolveDark(): boolean {
    const theme = this.settings.appearance.theme;
    if (theme === 1) {
      return false;
    }
    if (theme === 2) {
      return true;
    }
    try {
      const context = getContext(this) as common.UIAbilityContext;
      const mode = context.config.colorMode;
      return mode === ConfigurationConstant.ColorMode.COLOR_MODE_DARK;
    } catch (e) {
      return false;
    }
  }

  // 设置变更统一入口：持久化 + 版本号驱动面板重渲染（settings 为私有对象，非 @State）。
  private touchSettings(): void {
    this.settingsVersion++;
    this.persistSettings();
  }

  private persistSettings(): void {
    if (this.storage === null) {
      return;
    }
    this.settings.canvas.inkWidth = this.widthMenu / 2;
    this.settings.canvas.inkAlpha = this.alpha;
    this.settings.canvas.inkStyle = this.taperIndex;
    this.storage.writeTextAtomic('Settings.json', stringifySettings(this.settings));
  }

  private async takeScreenshot(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    const stamp = 'shot-' + Date.now() + '.png';
    let relative = 'screenshots/' + stamp;
    if (this.settings.automation.isSaveScreenshotsInDateFolders) {
      const now = new Date();
      const pad = (n: number): string => n < 10 ? '0' + n : '' + n;
      relative = 'screenshots/' + now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate()) + '/' + stamp;
    }
    const path = this.storage.fullPathPublic(relative);
    const ok = await captureComponentToPng('inkRoot', path);
    if (ok && this.settings.automation.isAutoSaveStrokesAtScreenshot && this.autoSave !== null) {
      const strokes = this.controller.Document.Strokes;
      if (strokes.length >= this.settings.automation.minimumAutomationStrokeNumber) {
        this.autoSave.saveSnapshot(strokes, 'screenshot');
      }
    }
    promptAction.showToast({ message: ok ? '截图已保存' : '截图失败', duration: 2000 });
  }

  build() {
    Stack({ alignContent: Alignment.TopStart }) {
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor(this.boardOpen ? this.boardBackgroundColor() : (this.dark ? '#FF1B1B1B' : '#FFFAFAFA'))

      if (this.boardOpen && (this.boardBackground === 2 || this.boardBackground === 3)) {
        Canvas(this.boardGridContext)
          .width('100%')
          .height('100%')
          .onReady(() => { this.drawBoardGrid(); })
      }

      if (this.boardOpen) {
        ForEach(this.orderedBoardAssets(), (asset: BoardAsset) => {
          if (this.board.Current.Media.indexOf(asset) >= 0) {
            this.BoardVideo(asset)
          } else {
            this.BoardImage(asset)
          }
        }, (asset: BoardAsset) => asset.Id + ':' + this.boardImagesVersion)
      }

      if (this.mode === 'courseware' && this.coursewarePage !== null) {
        Image(this.coursewarePage)
          .objectFit(ImageFit.Contain)
          .width('100%')
          .height('100%')
      }

      Stack() {
        InkCanvasSurface({
          onRendererReady: (renderer: CanvasInkRenderer) => {
            this.renderer = renderer;
            this.renderer.resize(this.windowWidth, this.windowHeight);
            this.refreshScene();
          }
        })
      }
      .width('100%')
      .height('100%')
      .onTouch((event: TouchEvent) => {
        this.handleTouch(event);
      })

      // 常驻覆盖层画布（套索路径 / 落点动画预览；不参与命中测试）。
      Canvas(this.lassoContext)
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.None)
        .onReady(() => { this.drawLassoOverlay(); })
      // G3 选区工具条（ArkTS 差异化紧凑形态；位置走 placeToolbar 候选评分）。
      if (this.selectionToolbarOpen) {
        Row({ space: 6 }) {
          Text(this.controller.CloneMode ? '克隆开' : '克隆')
            .fontSize(12).fontColor(Color.White).padding(6)
            .backgroundColor(this.controller.CloneMode ? '#FF0067C0' : '#99000000').borderRadius(4)
            .onClick(() => {
              this.controller.CloneMode = !this.controller.CloneMode;
              this.refreshScene();
            })
          ForEach(SELECTION_COLORS, (color: SelectionColor) => {
            Circle()
              .width(20).height(20)
              .fill(color.hex)
              .stroke('#66FFFFFF')
              .strokeWidth(1)
              .onClick(() => { this.recolorSelection(color.packed); })
          }, (color: SelectionColor) => color.hex)
          Text('B+').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
            .onClick(() => { this.controller.ScaleSelectionWidth(1.25); })
          Text('B-').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
            .onClick(() => { this.controller.ScaleSelectionWidth(0.8); })
          Text('恢复').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
            .onClick(() => { this.controller.RestoreSelectionWidth(); })
          Text('顶层').fontSize(12).fontColor(Color.White).padding(6)
            .backgroundColor(this.controller.CanArrangeSelection(0) ? '#99000000' : '#4D000000').borderRadius(4)
            .onClick(() => { this.controller.ArrangeSelection(0); })
          Text('上移').fontSize(12).fontColor(Color.White).padding(6)
            .backgroundColor(this.controller.CanArrangeSelection(1) ? '#99000000' : '#4D000000').borderRadius(4)
            .onClick(() => { this.controller.ArrangeSelection(1); })
          Text('下移').fontSize(12).fontColor(Color.White).padding(6)
            .backgroundColor(this.controller.CanArrangeSelection(2) ? '#99000000' : '#4D000000').borderRadius(4)
            .onClick(() => { this.controller.ArrangeSelection(2); })
          Text('底层').fontSize(12).fontColor(Color.White).padding(6)
            .backgroundColor(this.controller.CanArrangeSelection(3) ? '#99000000' : '#4D000000').borderRadius(4)
            .onClick(() => { this.controller.ArrangeSelection(3); })
          Text('删除').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#FFC0392B').borderRadius(4)
            .onClick(() => { this.controller.DeleteSelection(); })
          Text('✕').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
            .onClick(() => { this.controller.ClearSelection(); })
        }
        .padding(6)
        .backgroundColor(panelBackground(this.dark, this.blurEnabled))
        .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
        .borderRadius(10)
        .position({ x: this.selectionToolbarX, y: this.selectionToolbarY })
        .transition(overlayTransition(this.isMotionReduced()))
      }

      if (this.cursorVisible && (this.settings.canvas.isShowCursor || this.controller.Tool === InkTool.Eraser)) {
        Circle()
          .width(this.cursorDiameter())
          .height(this.cursorDiameter())
          .fill(Color.Transparent)
          .stroke(this.dark ? Color.White : Color.Black)
          .strokeWidth(1)
          .position({ x: this.cursorX - this.cursorDiameter() / 2, y: this.cursorY - this.cursorDiameter() / 2 })
          .hitTestBehavior(HitTestMode.None)
      }

      if (this.calibrationOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 12 }) {
            Text('触摸大小校准').fontSize(18).fontColor(this.dark ? Color.White : Color.Black)
            Text('用手指在下方区域多次触摸，采集接触宽度后应用乘数。').fontSize(12).fontColor(this.dark ? '#A8FFFFFF' : '#9E000000')
            Stack()
              .width(240).height(150)
              .backgroundColor('#220067C0')
              .border({ width: 1, color: '#FF0067C0' })
              .borderRadius(8)
              .onTouch((event: TouchEvent) => {
                if (event.type === TouchType.Down) {
                  const t = event.changedTouches[0];
                  const w = t.width === undefined ? 0 : t.width;
                  if (w > 0) {
                    this.calibrationSamples.push(w);
                  }
                }
              })
            Text('已采集 ' + this.calibrationSamples.length + ' 次').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Row({ space: 10 }) {
              Text('应用校准').fontSize(14).fontColor(Color.White).padding({ left: 16, right: 16, top: 8, bottom: 8 }).backgroundColor('#FF0067C0').borderRadius(6)
                .onClick(() => { this.applyCalibration(); })
              Text('重置').fontSize(14).fontColor(Color.White).padding(8).backgroundColor('#FF888888').borderRadius(6)
                .onClick(() => { this.calibrationSamples = []; })
              Text('关闭').fontSize(14).fontColor(Color.White).padding(8).backgroundColor('#FF666666').borderRadius(6)
                .onClick(() => { this.calibrationOpen = false; this.calibrationSamples = []; })
            }
          }
          .padding(24)
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(16)
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.mode === 'courseware' && this.settings.behavior.isShowBottomPPTNavigationPanel) {
        Stack({ alignContent: Alignment.Bottom }) {
          Row({ space: 12 }) {
            Text('上一页')
              .fontSize(14)
              .fontColor(Color.White)
              .padding(8)
              .backgroundColor('#99000000')
              .borderRadius(6)
              .onClick(() => {
                this.showCoursewarePage(this.coursewareIndex - 1);
              })
            if (this.settings.behavior.isShowPPTNavigationBottom) {
              Text((this.coursewareIndex + 1) + ' / ' + this.coursewareCount)
                .fontSize(14)
                .fontColor(Color.White)
                .padding(8)
                .backgroundColor('#99000000')
                .borderRadius(6)
            }
            Text('下一页')
              .fontSize(14)
              .fontColor(Color.White)
              .padding(8)
              .backgroundColor('#99000000')
              .borderRadius(6)
              .onClick(() => {
                this.showCoursewarePage(this.coursewareIndex + 1);
              })
          }
          .margin({ bottom: 170 })
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.mode === 'courseware' && this.settings.behavior.isShowSidePPTNavigationPanel) {
        Stack({ alignContent: Alignment.Start }) {
          Column({ space: 8 }) {
            Text('▲')
              .fontSize(20).fontColor(Color.White).padding(10).backgroundColor('#99000000').borderRadius(6)
              .onClick(() => { this.showCoursewarePage(this.coursewareIndex - 1); })
            if (this.settings.behavior.isShowPPTNavigationSides) {
              Text((this.coursewareIndex + 1) + '/' + this.coursewareCount)
                .fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(6)
            }
            Text('▼')
              .fontSize(20).fontColor(Color.White).padding(10).backgroundColor('#99000000').borderRadius(6)
              .onClick(() => { this.showCoursewarePage(this.coursewareIndex + 1); })
          }
          .margin({ left: 12 })
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.paletteOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          PenPalette({
            highlighter: this.paletteHighlighter,
            dark: this.dark,
            lightColorScheme: !this.dark,
            widthMenu: this.paletteHighlighter && typeof this.settings.canvas.highlighterWidth === 'number'
              ? this.settings.canvas.highlighterWidth * 2
              : this.widthMenu,
            alpha: this.alpha,
            taperIndex: this.taperIndex,
            currentPacked: this.currentPacked,
            uiScale: this.settings.appearance.enableViewboxFloatingBarScaleTransform / 100,
            materialBlur: this.blurEnabled,
            showNibToggler: this.settings.appearance.isEnableDisPlayNibModeToggler,
            penNib: this.settings.canvas.penNib,
            onColor: (packed: number) => {
              this.currentPacked = packed;
              this.controller.CurrentAttributes.Color = InkColor.fromPackedArgb(packed);
              this.paletteOpen = false;
            },
            onWidth: (stored: number) => {
              if (this.paletteHighlighter) {
                // 荧光笔独立宽度（canvas.highlighterWidth，null=跟随笔宽；菜单上限 120）。
                this.settings.canvas.highlighterWidth = Math.min(stored, 60);
                this.controller.ConfigurePenWidths(this.settings.canvas.inkWidth, this.settings.canvas.highlighterWidth);
              } else {
                this.widthMenu = stored * 2;
                const highlighter = typeof this.settings.canvas.highlighterWidth === 'number'
                  ? this.settings.canvas.highlighterWidth
                  : stored;
                this.controller.ConfigurePenWidths(stored, highlighter);
              }
              this.controller.CurrentAttributes.BaseWidth = stored;
              this.persistSettings();
            },
            onAlpha: (value: number) => {
              this.alpha = value;
              this.controller.CurrentAttributes.HighlighterAlpha = value;
              this.persistSettings();
            },
            onTaper: (kind: number) => {
              // kind 为 TaperKind；taperIndex 持久化菜单索引（ToPenStyle 反向映射）。
              this.taperIndex = toPenStyle(kind as TaperKind);
              this.controller.CurrentAttributes.TaperKind = kind;
              this.persistSettings();
            },
            onToggleScheme: () => {
              this.dark = !this.dark;
              this.settings.appearance.theme = this.dark ? 2 : 1;
              this.persistSettings();
            },
            onNib: (nib: number) => {
              this.settings.canvas.penNib = nib;
              this.controller.CurrentAttributes.Nib = nib === 1 ? PenNib.Ellipse45 : PenNib.Round;
              this.persistSettings();
            },
            onClose: () => {
              this.paletteOpen = false;
            }
          })
            .margin({ bottom: 170 })
            .transition(overlayTransition(this.isMotionReduced()))
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.shapeOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          ShapePicker({
            dark: this.dark,
            selectedKind: this.selectedShape,
            uiScale: this.settings.appearance.enableViewboxFloatingBarScaleTransform / 100,
            materialBlur: this.blurEnabled,
            onSelect: (kind: number, longPress: boolean) => {
              this.selectedShape = kind;
              this.controller.SelectShape(kind as ShapeKind, longPress);
              if (!longPress) {
                this.shapeOpen = false;
                this.selectedId = 'shapes';
              }
            }
          })
            .margin({ bottom: 170 })
            .transition(overlayTransition(this.isMotionReduced()))
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.countdownOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 16 }) {
            if (this.countdownCompact) {
              Text(this.formatCountdown(this.countdownDisplay))
                .fontSize(56)
                .fontWeight(FontWeight.Bold)
                .fontColor(this.countdownFinished ? '#FFE64545' : (this.countdownFullscreen ? Color.White : (this.dark ? Color.White : Color.Black)))
                .onClick(() => { this.countdownCompact = false; })
              Text('点击退出紧凑').fontSize(12).fontColor('#999999')
            } else {
              Stack({ alignContent: Alignment.Center }) {
                Progress({ value: this.countdownRing * 100, total: 100, type: ProgressType.Ring })
                  .width(this.countdownFullscreen ? 280 : 180)
                  .height(this.countdownFullscreen ? 280 : 180)
                  .color(this.countdownFinished ? '#FFE64545' : '#FF0067C0')
                  .backgroundColor('#FFEAEAEA')
                  .style({ strokeWidth: this.countdownFullscreen ? 18 : 12, enableSmoothEffect: true })
                Column({ space: 4 }) {
                  Text(this.formatCountdown(this.countdownDisplay))
                    .fontSize(this.countdownFullscreen ? 64 : 40)
                    .fontWeight(FontWeight.Bold)
                    .fontColor(this.countdownFinished ? '#FFE64545' : (this.countdownFullscreen ? Color.White : (this.dark ? Color.White : Color.Black)))
                  if (this.countdownFinished) {
                    Text('时间到').fontSize(16).fontColor('#FFE64545')
                  }
                }
              }
            }
            if (!this.countdownCompact) {
              Row({ space: 10 }) {
                Text(this.countdownRunning ? '暂停' : '开始')
                  .fontSize(16).fontColor(Color.White).padding({ left: 16, right: 16, top: 10, bottom: 10 })
                  .backgroundColor('#FF0067C0').borderRadius(8)
                  .onClick(() => { this.toggleCountdownPause(); })
                Text('重置')
                  .fontSize(16).fontColor(Color.White).padding({ left: 16, right: 16, top: 10, bottom: 10 })
                  .backgroundColor('#FF666666').borderRadius(8)
                  .onClick(() => {
                    this.countdown.reset(Date.now());
                    this.countdownDisplay = this.countdown.Display;
                    this.countdownRing = this.countdown.Ring;
                    this.countdownRunning = this.countdown.IsRunning && !this.countdown.IsPaused;
                    this.countdownFinished = false;
                  })
                Text('-1分').fontSize(14).fontColor(this.countdownFullscreen ? Color.White : (this.dark ? Color.White : Color.Black))
                  .onClick(() => { this.countdown.configure(0, Math.max(0, Math.floor(this.countdown.ConfigSeconds / 60) - 1), this.countdown.ConfigSeconds % 60); this.countdownDisplay = this.countdown.ConfigSeconds; this.countdownFinished = false; })
                Text('+1分').fontSize(14).fontColor(this.countdownFullscreen ? Color.White : (this.dark ? Color.White : Color.Black))
                  .onClick(() => { this.countdown.configure(0, Math.floor(this.countdown.ConfigSeconds / 60) + 1, this.countdown.ConfigSeconds % 60); this.countdownDisplay = this.countdown.ConfigSeconds; this.countdownFinished = false; })
              }
              Row({ space: 10 }) {
                Text('紧凑').fontSize(13).fontColor(Color.White).padding(8).backgroundColor('#FF888888').borderRadius(6)
                  .onClick(() => { this.countdownCompact = true; })
                Text(this.countdownFullscreen ? '退出全屏' : '全屏').fontSize(13).fontColor(Color.White).padding(8).backgroundColor('#FF888888').borderRadius(6)
                  .onClick(() => { this.countdownFullscreen = !this.countdownFullscreen; })
                Text(this.countdownPiPActive ? '退出悬浮' : '悬浮窗').fontSize(13).fontColor(Color.White).padding(8).backgroundColor('#FF0067C0').borderRadius(6)
                  .onClick(() => { this.startCountdownPiP(); })
                Text('关闭').fontSize(13).fontColor(Color.White).padding(8).backgroundColor('#FF666666').borderRadius(6)
                  .onClick(() => { this.countdownOpen = false; this.countdownFullscreen = false; this.stopCountdownTimer(); })
              }
            }
          }
          .padding(28)
          .backgroundColor(this.countdownFullscreen ? Color.Transparent : panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(this.countdownFullscreen ? BlurStyle.NONE : panelBlurStyle(this.blurEnabled))
          .borderRadius(16)
          .geometryTransition(sharedId('countdown', this.isMotionReduced()))
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .backgroundColor(this.countdownFullscreen ? '#E6000000' : Color.Transparent)
        .hitTestBehavior(this.countdownFullscreen ? HitTestMode.Default : HitTestMode.Transparent)
      }

      if (this.lotteryOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 12 }) {
            Text('抽奖').fontSize(20).fontWeight(FontWeight.Medium).fontColor(this.dark ? Color.White : Color.Black)
            if (this.lotteryGroups.length === 0) {
              TextArea({ text: this.lotteryNames, placeholder: '每行一个名字（留空按人数抽号）' })
                .width(280).height(110)
                .onChange((value: string) => { this.lotteryNames = value; })
              if (this.lotteryReplaceOpen) {
                TextArea({ text: this.lotteryReplace, placeholder: '替换规则：每行 旧名-->新名（整行精确匹配）' })
                  .width(280).height(70)
                  .onChange((value: string) => { this.lotteryReplace = value; })
              }
              Row({ space: 8 }) {
                Text('人数').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
                Text('-').fontSize(16).fontColor(Color.White).padding({ left: 10, right: 10, top: 4, bottom: 4 }).backgroundColor('#FF888888').borderRadius(4)
                  .onClick(() => { this.lotteryCount = Math.max(1, this.lotteryCount - 1); })
                Text(this.lotteryCount.toString()).fontSize(15).fontColor(this.dark ? Color.White : Color.Black)
                Text('+').fontSize(16).fontColor(Color.White).padding({ left: 10, right: 10, top: 4, bottom: 4 }).backgroundColor('#FF888888').borderRadius(4)
                  .onClick(() => { this.lotteryCount = Math.min(99, this.lotteryCount + 1); })
              }
              Row({ space: 8 }) {
                Text(this.lotteryNoRepeat ? '不重复：开' : '不重复：关').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
                  .onClick(() => {
                    this.lotteryNoRepeat = !this.lotteryNoRepeat;
                    this.settings.randSettings.isNotRepeatName = this.lotteryNoRepeat;
                    this.persistSettings();
                  })
                Text(this.lotterySingle ? '单次：开' : '单次：关').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
                  .onClick(() => { this.lotterySingle = !this.lotterySingle; })
                Text(this.lotteryReplaceOpen ? '收起替换' : '替换名单').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
                  .onClick(() => { this.lotteryReplaceOpen = !this.lotteryReplaceOpen; })
                Text('导入名单').fontSize(13).fontColor(Color.White).padding(6).backgroundColor('#FF0067C0').borderRadius(4)
                  .onClick(() => { this.importRoster(); })
              }
            }
            if (this.lotteryGroups.length > 0) {
              Row({ space: 16 }) {
                ForEach(this.lotteryGroups, (group: string, idx: number) => {
                  Text(group)
                    .fontSize(this.lotteryGroups.length > 1 ? 22 : 32)
                    .fontColor('#FF0067C0')
                    .textAlign(TextAlign.Center)
                }, (group: string, idx: number) => idx.toString() + ':' + group)
              }
            } else {
              Text(this.lotteryResult).fontSize(32).fontColor('#FF0067C0')
            }
            Row({ space: 10 }) {
              Text(this.lotteryRolling ? '抽选中…' : '抽选')
                .fontSize(16).fontColor(Color.White).padding({ left: 20, right: 20, top: 10, bottom: 10 })
                .backgroundColor(this.lotteryRolling ? '#FF999999' : '#FF0067C0').borderRadius(8)
                .onClick(() => { this.rollLottery(); })
              if (this.lotteryGroups.length > 0) {
                Text('再抽').fontSize(14).fontColor(Color.White).padding(8).backgroundColor('#FF888888').borderRadius(6)
                  .onClick(() => { this.lotteryGroups = []; this.lotteryResult = ''; })
              }
              Text('关闭').fontSize(14).fontColor(Color.White).padding(8).backgroundColor('#FF666666').borderRadius(6)
                .onClick(() => { this.lotteryOpen = false; this.stopLotteryTimer(); this.lotteryRolling = false; })
            }
          }
          .padding(24)
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(16)
          .geometryTransition(sharedId('lottery', this.isMotionReduced()))
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.replayOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          Row({ space: 10 }) {
            Text(this.replayPlaying ? '暂停' : '播放')
              .fontSize(14).fontColor(Color.White).padding(8)
              .backgroundColor('#FF0067C0').borderRadius(6)
              .onClick(() => { this.toggleReplay(); })
            ForEach([0.5, 1, 2, 4], (speed: number) => {
              Text(speed + 'x')
                .fontSize(14)
                .fontColor(this.replaySpeed === speed ? Color.White : (this.dark ? Color.White : Color.Black))
                .backgroundColor(this.replaySpeed === speed ? '#FF0067C0' : Color.Transparent)
                .padding(6).borderRadius(4)
                .onClick(() => { this.replaySpeed = speed; if (this.replayClock !== null) { this.replayClock.Speed = speed; } })
            }, (speed: number) => speed.toString())
            Text('关闭').fontSize(14).fontColor(Color.White).padding(8)
              .backgroundColor('#666666').borderRadius(6)
              .onClick(() => { this.closeReplay(); })
          }
          .padding(10)
          .backgroundColor('#99000000')
          .borderRadius(8)
          .margin({ bottom: 170 })
          .transition(overlayTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.guideOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 6 }) {
            Text('操作指南').fontSize(20).fontColor(this.dark ? Color.White : Color.Black)
            Text('Alt+D 批注 / Alt+E 橡皮 / Alt+S 选择').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Text('Alt+B 画板 / Alt+V 收纳 / Alt+Q 鼠标').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Text('Ctrl+Z 撤销 / Ctrl+Y 重做 / Ctrl+E 清空').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Text('双指：平移/缩放/旋转（手势面板配置）').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Text('课件：工具 → 课件（PDF），PPT 请先导出 PDF').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
              .onClick(() => { this.guideOpen = false; })
          }
          .padding(24)
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(12)
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.settingsOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Scroll() {
            Column({ space: 10 }) {
              Text('设置').fontSize(20).fontColor(this.dark ? Color.White : Color.Black)
              this.SettingsStartup()
              this.SettingsCanvas()
              this.SettingsGesture()
              this.SettingsInkToShape()
              this.SettingsAppearance()
              this.SettingsBehavior()
              this.SettingsAdvanced()
              this.SettingsAutomation()
              this.SettingsRand()
              this.SettingsAbout()
              Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
                .onClick(() => { this.settingsOpen = false; })
            }
            .padding(20)
            .alignItems(HorizontalAlign.Start)
          }
          .width('92%')
          .constraintSize({ maxWidth: this.fullLayout() ? 440 : 360 })
          .height('82%')
          .constraintSize({ maxHeight: 620 })
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(12)
          // settings 为私有对象（非 @State）：读取版本号建立渲染依赖，
          // touchSettings() 递增后整面板以最新设置值重渲染。
          .opacity(this.settingsVersion >= 0 ? 1 : 0)
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.boardOpen) {
        Stack({ alignContent: Alignment.TopStart }) {
          Column({ space: 8 }) {
            // 页面浮条
            Row({ space: 6 }) {
              Text('画板 ' + (this.boardIndex + 1) + '/' + this.boardPageCount).fontSize(13).fontColor(Color.White)
              Text('上一页').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.boardNavigate(-1); })
              Text('下一页').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.boardNavigate(1); })
              Text('新增').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.addPage(); this.syncBoard(); })
              Text('删除').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.removePage(this.boardIndex); this.syncBoard(); })
              Text('清页').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.clearBoardPage(); })
              Text('衍至新页').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.cloneSelectionToNewPage(); })
            }
            .padding(8).backgroundColor('#CC1A1A1A').borderRadius(8)
            // 背景浮条
            Row({ space: 6 }) {
              Text('黑').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.Background = BoardBackground.Black; this.boardBackground = 0; })
              Text('白').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.Background = BoardBackground.White; this.boardBackground = 1; })
              Text('网格').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.Background = BoardBackground.Grid; this.boardBackground = 2; this.drawBoardGrid(); })
              Text('横线').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.Background = BoardBackground.Lined; this.boardBackground = 3; this.drawBoardGrid(); })
            }
            .padding(8).backgroundColor('#CC1A1A1A').borderRadius(8)
            // 文件浮条
            Row({ space: 6 }) {
              Text('插图').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.insertBoardImage(); })
              Text('视频').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.insertBoardVideo(); })
              Text('保存').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.saveBoard(); })
              Text('打开').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.loadBoard(); })
              Text('退出画板').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.exitBoard(); })
            }
            .padding(8).backgroundColor('#CC1A1A1A').borderRadius(8)
          }
          .margin({ left: 12, top: 12 })
          .alignItems(HorizontalAlign.Start)
          .scale({ x: this.settings.appearance.enableViewboxBlackBoardScaleTransform / 100, y: this.settings.appearance.enableViewboxBlackBoardScaleTransform / 100 })
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.yesNoOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 12 }) {
            Text(this.yesNoMessage).fontSize(16).fontColor(this.dark ? Color.White : Color.Black)
            Row({ space: 12 }) {
              Text('是').fontSize(14).fontColor(Color.White).padding(10)
                .backgroundColor('#FF0067C0').borderRadius(6)
                .onClick(() => { this.yesNoOpen = false; this.handleAction(this.pendingYesNo); })
              Text('否').fontSize(14).fontColor(Color.White).padding(10)
                .backgroundColor('#666666').borderRadius(6)
                .onClick(() => { this.yesNoOpen = false; this.pendingYesNo = ''; })
            }
          }
          .padding(24)
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(12)
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.updateOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 10 }) {
            Text('检查更新').fontSize(18).fontColor(this.dark ? Color.White : Color.Black)
            Text('当前版本 3.4.0（HarmonyOS 移植）').fontSize(13).fontColor(this.dark ? Color.White : Color.Black)
            Text('HarmonyOS 应用经应用市场更新；自更新通道不可用。').fontSize(12).fontColor(this.dark ? '#A8FFFFFF' : '#9E000000')
            Row({ space: 10 }) {
              Text('检查更新').fontSize(14).fontColor(Color.White).padding({ left: 16, right: 16, top: 8, bottom: 8 })
                .backgroundColor('#FF0067C0').borderRadius(6)
                .onClick(() => { this.doCheckUpdate(); })
              Text('前往应用市场').fontSize(14).fontColor(Color.White).padding({ left: 16, right: 16, top: 8, bottom: 8 })
                .backgroundColor('#FF888888').borderRadius(6)
                .onClick(() => { this.openMarket(); })
            }
            Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
              .onClick(() => { this.updateOpen = false; })
          }
          .padding(24)
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(12)
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.changelogOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Scroll() {
            Column({ space: 12 }) {
              Text('更新日志').fontSize(20).fontColor(this.dark ? Color.White : Color.Black)
              Text('Ink Canvas Artistry · HarmonyOS 移植（摘要自 CHANGELOG.md）')
                .fontSize(12).fontColor(this.dark ? '#A8FFFFFF' : '#9E000000')
              ForEach(CHANGELOG_ENTRIES, (entry: ChangelogEntry) => {
                Column({ space: 6 }) {
                  Text(entry.version + ' — ' + entry.title)
                    .fontSize(15)
                    .fontWeight(FontWeight.Bold)
                    .fontColor('#FF0067C0')
                  ForEach(entry.items, (item: string) => {
                    Text('• ' + item)
                      .fontSize(13)
                      .fontColor(this.dark ? Color.White : Color.Black)
                      .width('100%')
                  }, (item: string) => item)
                }
                .alignItems(HorizontalAlign.Start)
                .width('100%')
              }, (entry: ChangelogEntry) => entry.version)
              Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
                .onClick(() => { this.changelogOpen = false; })
            }
            .padding(20)
            .alignItems(HorizontalAlign.Start)
          }
          .width('92%')
          .constraintSize({ maxWidth: this.fullLayout() ? 440 : 360 })
          .height('76%')
          .constraintSize({ maxHeight: 560 })
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(12)
          .transition(panelTransition(this.isMotionReduced()))
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.toolsOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          Column({ space: 6 }) {
            this.ToolEntry('课件（PDF）', 'courseware')
            this.ToolEntry('PPT（WPS）', 'pptWps')
            if (this.mode === 'courseware') {
              this.ToolEntry('保存批注 PDF', 'saveAnnotatedPdf')
            }
            this.ToolEntry('截图', 'screenshot')
            this.ToolEntry('Desmos', 'desmos')
            this.ToolEntry('检查更新', 'update')
            this.ToolEntry('计时器', 'countdown')
            this.ToolEntry('抽奖', 'lottery')
            this.ToolEntry('回放', 'replay')
            this.ToolEntry('更新日志', 'changelog')
            this.ToolEntry('设置', 'settings')
            this.ToolEntry('指南', 'guide')
            this.ToolEntry('关闭', 'closeTools')
          }
          .padding(12)
          .backgroundColor(panelBackground(this.dark, this.blurEnabled))
          .backgroundBlurStyle(panelBlurStyle(this.blurEnabled))
          .borderRadius(8)
          .margin({ bottom: 170 })
          .transition(overlayTransition(this.isMotionReduced()))
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      Stack({ alignContent: Alignment.Bottom }) {
        FloatingBar({
          dark: this.dark,
          showLabels: this.settings.appearance.isEnableDisPlayFloatBarText,
          uiScale: this.settings.appearance.enableViewboxFloatingBarScaleTransform / 100,
          selectedId: this.selectedId,
          folded: this.collapsed,
          pptActive: this.pptActive,
          materialBlur: this.blurEnabled,
          symbolIcons: this.symbolIcons,
          reduceMotion: this.isMotionReduced(),
          showEraserButton: this.settings.appearance.isShowEraserButton,
          showHideControlButton: this.settings.appearance.isShowHideControlButton,
          showLRSwitchButton: this.settings.appearance.isShowLRSwitchButton,
          showExitButton: this.settings.appearance.isShowExitButton,
          showFingerToggle: this.settings.appearance.isShowModeFingerToggleSwitch,
          colorfulBackground: this.settings.appearance.isColorfulViewboxFloatingBar,
          transparentButtons: this.settings.appearance.isTransparentButtonBackground,
          onAction: (id: string) => {
            this.handleAction(id);
          },
          onDrag: (dx: number, dy: number) => {
            this.barDragX = dx;
            this.barDragY = dy;
          },
          onDragFinished: () => {
          }
        })
          .margin({ bottom: this.settings.appearance.floatingBarBottomMargin })
          .translate({ x: this.barDragX, y: this.barDragY })
      }
      .width('100%')
      .height('100%')
      .hitTestBehavior(HitTestMode.Transparent)
    }
    .id('inkRoot')
    .focusable(true)
    .defaultFocus(true)
    .width('100%')
    .height('100%')
    .onKeyEvent((event: KeyEvent) => {
      this.handleKey(event);
    })
    .onAreaChange((oldValue: Area, newValue: Area) => {
      this.windowWidth = Number(newValue.width);
      this.windowHeight = Number(newValue.height);
      if (this.renderer !== null) {
        this.renderer.resize(this.windowWidth, this.windowHeight);
        this.refreshScene();
      }
      if (this.boardOpen && (this.boardBackground === 2 || this.boardBackground === 3)) {
        this.drawBoardGrid();
      }
    })
    .onTouch((event: TouchEvent) => {
      this.handleTouch(event);
    })
  }

  @Builder
  BoardVideo(asset: BoardAsset) {
    if (this.boardMediaPath(asset) !== '') {
      Video({ src: this.boardMediaPath(asset), controller: this.videoControllerFor(asset) })
        .controls(true)
        .objectFit(ImageFit.Fill)
        .position({ x: asset.Left, y: asset.Top })
        .width(asset.Width)
        .height(asset.Height)
        .transform(this.boardAssetMatrix(asset))
        .onPrepared(() => {
          this.restoreVideoPosition(asset);
        })
        .onUpdate((info: PlaybackInfo) => {
          this.videoPositions.set(asset.Id, info.time);
        })
        .onStart(() => {
          this.videoPlaying.set(asset.Id, true);
        })
        .onPause(() => {
          this.videoPlaying.set(asset.Id, false);
        })
        .onFinish(() => {
          this.videoPlaying.set(asset.Id, false);
        })
    }
  }

  @Builder
  BoardImage(asset: BoardAsset) {
    if (this.decodeBoardAsset(asset) !== null) {
      Image(this.decodeBoardAsset(asset))
        .objectFit(ImageFit.Fill)
        .position({ x: asset.Left, y: asset.Top })
        .width(asset.Width)
        .height(asset.Height)
        .opacity(asset.Opacity === undefined ? 1 : asset.Opacity)
        .transform(this.boardAssetMatrix(asset))
    }
  }

  @Builder
  ToolEntry(label: string, id: string) {
    Row({ space: 8 }) {
      IconGlyph({
        symbol: symbolForTool(id),
        fallback: label.substring(0, 1),
        size: 16,
        color: this.dark ? Color.White : Color.Black,
        enabled: this.symbolIcons
      })
      Text(label)
        .fontSize(14)
        .fontColor(this.dark ? Color.White : Color.Black)
        .layoutWeight(1)
        .textAlign(TextAlign.Center)
    }
    .width(160)
    .padding(8)
    .onClick(() => {
      this.handleAction(id);
    })
  }

  // ---- 设置面板：通用行构建器 ------------------------------------------

  @Builder
  SettingSection(title: string) {
    Text(title)
      .fontSize(15)
      .fontColor('#FF0067C0')
      .margin({ top: 6 })
  }

  @Builder
  SettingToggle(label: string, isOn: boolean, onChanged: (value: boolean) => void) {
    Row() {
      Text(label)
        .fontSize(14)
        .fontColor(this.dark ? Color.White : Color.Black)
        .layoutWeight(1)
      Toggle({ type: ToggleType.Switch, isOn: isOn })
        .onChange((value: boolean) => {
          onChanged(value);
        })
    }
    .width('100%')
    .constraintSize({ minHeight: 40 })
  }

  @Builder
  SettingSlider(label: string, value: number, min: number, max: number, step: number,
    onChanged: (value: number) => void) {
    Column({ space: 2 }) {
      Text(label + '：' + (step < 1 ? value.toFixed(2) : Math.round(value).toString()))
        .fontSize(13)
        .fontColor(this.dark ? Color.White : Color.Black)
      Slider({ value: value, min: min, max: max, step: step })
        .width('100%')
        .onChange((v: number) => {
          onChanged(v);
        })
    }
    .width('100%')
    .alignItems(HorizontalAlign.Start)
  }

  @Builder
  SettingOptions(label: string, options: string[], selected: number, onPick: (index: number) => void) {
    Column({ space: 4 }) {
      Text(label)
        .fontSize(14)
        .fontColor(this.dark ? Color.White : Color.Black)
      Flex({ wrap: FlexWrap.Wrap }) {
        ForEach(options, (option: string, index: number) => {
          Text(option)
            .fontSize(12)
            .fontColor(selected === index ? Color.White : (this.dark ? Color.White : Color.Black))
            .backgroundColor(selected === index ? '#FF0067C0' : (this.dark ? '#33FFFFFF' : '#1A000000'))
            .borderRadius(4)
            .padding({ left: 10, right: 10, top: 8, bottom: 8 })
            .constraintSize({ minHeight: 40 })
            .margin({ right: 6, bottom: 4 })
            .onClick(() => {
              onPick(index);
            })
        }, (option: string, index: number) => index.toString() + option)
      }
      .width('100%')
    }
    .width('100%')
    .alignItems(HorizontalAlign.Start)
  }

  // 不可用项：显示说明而非假开关（值仍保留在 Settings.json 供跨端互读）。
  @Builder
  SettingDeferred(label: string, note: string) {
    Column({ space: 2 }) {
      Text(label + '（不可用）')
        .fontSize(13)
        .fontColor(this.dark ? '#A8FFFFFF' : '#9E000000')
      Text(note)
        .fontSize(11)
        .fontColor(this.dark ? '#66FFFFFF' : '#66000000')
        .width('100%')
    }
    .width('100%')
    .alignItems(HorizontalAlign.Start)
  }

  @Builder
  SettingButton(label: string, buttonLabel: string, onTap: () => void) {
    Row() {
      Text(label)
        .fontSize(13)
        .fontColor(this.dark ? Color.White : Color.Black)
        .layoutWeight(1)
      Text(buttonLabel)
        .fontSize(13)
        .fontColor(Color.White)
        .padding({ left: 12, right: 12, top: 6, bottom: 6 })
        .backgroundColor('#FF0067C0')
        .borderRadius(4)
        .onClick(() => {
          onTap();
        })
    }
    .width('100%')
    .constraintSize({ minHeight: 40 })
  }

  @Builder
  SettingInfo(note: string) {
    Text(note)
      .fontSize(11)
      .fontColor(this.dark ? '#A8FFFFFF' : '#9E000000')
      .width('100%')
  }

  // ---- 设置面板：分组（名称与顺序对 C# SettingsWindow.BuildSections） ----

  @Builder
  SettingsStartup() {
    this.SettingSection('启动选项')
    this.SettingInfo('HarmonyOS 无三方自更新/自启动通道：更新经应用市场；开机自启为唯一 not-ported 功能。')
    this.SettingDeferred('自动检查更新', '无 HAP 自更新 API，请经应用市场更新。')
    this.SettingDeferred('静默更新', '自更新不可用；静默时间窗策略仅保留配置值。')
    this.SettingDeferred('静默更新起始/终止时间',
      '当前值：' + this.settings.startup.isAutoUpdateWithSilenceStartTime + ' – ' +
      this.settings.startup.isAutoUpdateWithSilenceEndTime + '（仅保留配置）。')
    this.SettingDeferred('开机时运行', '无三方自启 API；替代路径：桌面快捷方式/服务卡片。')
    this.SettingToggle('开机运行后收纳到侧边栏', this.settings.startup.isFoldAtStartup, (v: boolean) => {
      this.settings.startup.isFoldAtStartup = v;
      this.touchSettings();
    })
    this.SettingToggle('启用笔尖模式', this.settings.startup.isEnableNibMode, (v: boolean) => {
      this.settings.startup.isEnableNibMode = v;
      this.controller.Palm.NibMode = v;
      this.touchSettings();
    })
  }

  @Builder
  SettingsCanvas() {
    this.SettingSection('画板选项')
    this.SettingToggle('上传图片压缩至 1080P', this.settings.canvas.isCompressPicturesUploaded, (v: boolean) => {
      this.settings.canvas.isCompressPicturesUploaded = v;
      this.touchSettings();
    })
    this.SettingToggle('显示画笔光标', this.settings.canvas.isShowCursor, (v: boolean) => {
      this.settings.canvas.isShowCursor = v;
      if (!v) {
        this.cursorVisible = false;
      }
      this.touchSettings();
    })
    this.SettingOptions('橡皮大小', ['小', '较小', '中（默认）', '较大', '大'],
      this.settings.canvas.eraserSize, (i: number) => {
        this.settings.canvas.eraserSize = i;
        this.controller.EraserRadius = this.eraserRadiusForSize(i);
        this.touchSettings();
      })
    this.SettingToggle('退出画板模式后隐藏墨迹', this.settings.canvas.hideStrokeWhenSelecting, (v: boolean) => {
      this.settings.canvas.hideStrokeWhenSelecting = v;
      this.touchSettings();
      this.refreshScene();
    })
    this.SettingOptions('笔迹风格', ['点集笔锋', '速率笔锋', '关闭笔锋', '平滑硬笔', '柔和压感', '自然笔锋', '书法笔锋'],
      this.settings.canvas.inkStyle, (i: number) => {
        this.settings.canvas.inkStyle = i;
        this.taperIndex = i;
        this.controller.CurrentAttributes.TaperKind = toTaperKind(i);
        this.touchSettings();
      })
    this.SettingOptions('笔尖', ['圆形', '椭圆 · 右斜 45°'],
      this.settings.canvas.penNib, (i: number) => {
        this.settings.canvas.penNib = i;
        this.controller.CurrentAttributes.Nib = i;
        this.touchSettings();
      })
    this.SettingSlider('画笔粗细（菜单 1–30，存储为半值）', this.widthMenu, 1, 30, 1, (v: number) => {
      this.widthMenu = v;
      this.controller.CurrentAttributes.BaseWidth = v / 2;
      this.touchSettings();
    })
    this.SettingOptions('橡皮模式', ['图标切换', '面积擦', '线条擦'],
      this.settings.canvas.eraserType, (i: number) => {
        this.settings.canvas.eraserType = i;
        if (this.controller.Tool === InkTool.Eraser) {
          this.applyEraserType();
        }
        this.touchSettings();
      })
    this.SettingToggle('白板背景配色', this.settings.canvas.usingWhiteboard, (v: boolean) => {
      this.settings.canvas.usingWhiteboard = v;
      this.board.Background = v ? BoardBackground.White : BoardBackground.Black;
      if (this.boardOpen) {
        this.boardBackground = this.board.Background;
      }
      this.touchSettings();
    })
    this.SettingSlider('荧光笔透明度', this.alpha, 20, 240, 20, (v: number) => {
      this.alpha = v;
      this.controller.CurrentAttributes.HighlighterAlpha = v;
      this.touchSettings();
    })
  }

  @Builder
  SettingsGesture() {
    this.SettingSection('手势选项')
    this.SettingOptions('缩放/旋转变换中心点', ['画布中心点（默认）', '全部内容的中心点', '手势操作的中心点'],
      this.settings.gesture.matrixTransformCenterPoint, (i: number) => {
        this.settings.gesture.matrixTransformCenterPoint = i;
        this.controller.TransformCenterOption = i;
        this.touchSettings();
      })
    this.SettingToggle('自动开关两指缩放功能', this.settings.gesture.AutoSwitchTwoFingerGesture, (v: boolean) => {
      this.settings.gesture.AutoSwitchTwoFingerGesture = v;
      this.touchSettings();
    })
    this.SettingToggle('允许双指旋转选中的墨迹', this.settings.gesture.isEnableTwoFingerRotationOnSelection, (v: boolean) => {
      this.settings.gesture.isEnableTwoFingerRotationOnSelection = v;
      this.controller.GesturesTargetSelection = v;
      this.touchSettings();
    })
    this.SettingToggle('启用多点触控模式', this.settings.gesture.isEnableMultiTouchMode, (v: boolean) => {
      this.settings.gesture.isEnableMultiTouchMode = v;
      this.controller.MultiTouchEnabled = v;
      this.touchSettings();
    })
    this.SettingInfo('true = 多指书写（禁手势）；false = 第二指触发手势（Settings.json 互读语义）。')
    this.SettingToggle('双指缩放', this.settings.gesture.isEnableTwoFingerZoom, (v: boolean) => {
      this.settings.gesture.isEnableTwoFingerZoom = v;
      this.applyGestures();
      this.touchSettings();
    })
    this.SettingToggle('双指平移', this.settings.gesture.isEnableTwoFingerTranslate, (v: boolean) => {
      this.settings.gesture.isEnableTwoFingerTranslate = v;
      this.applyGestures();
      this.touchSettings();
    })
    this.SettingToggle('双指旋转', this.settings.gesture.isEnableTwoFingerRotation, (v: boolean) => {
      this.settings.gesture.isEnableTwoFingerRotation = v;
      this.applyGestures();
      this.touchSettings();
    })
  }

  @Builder
  SettingsInkToShape() {
    this.SettingSection('墨迹识别选项')
    this.SettingToggle('启用墨迹识别', this.settings.inkToShape.isInkToShapeEnabled, (v: boolean) => {
      this.settings.inkToShape.isInkToShapeEnabled = v;
      this.controller.RecognizeShapes = v;
      this.touchSettings();
    })
  }

  @Builder
  SettingsAppearance() {
    this.SettingSection('外观选项')
    this.SettingOptions('颜色主题', ['浅色主题', '深色主题', '跟随系统'],
      this.settings.appearance.theme === 1 ? 0 : (this.settings.appearance.theme === 2 ? 1 : 2),
      (i: number) => {
        const values: number[] = [1, 2, 0];
        this.settings.appearance.theme = values[i];
        this.dark = this.resolveDark();
        this.touchSettings();
      })
    this.SettingToggle('浮动工具栏显示文字注释', this.settings.appearance.isEnableDisPlayFloatBarText, (v: boolean) => {
      this.settings.appearance.isEnableDisPlayFloatBarText = v;
      this.touchSettings();
    })
    this.SettingToggle('浮动工具栏背景色（彩色渐变）', this.settings.appearance.isColorfulViewboxFloatingBar, (v: boolean) => {
      this.settings.appearance.isColorfulViewboxFloatingBar = v;
      this.touchSettings();
    })
    this.SettingSlider('浮动工具栏底部边距', this.settings.appearance.floatingBarBottomMargin, 0, 200, 1, (v: number) => {
      this.settings.appearance.floatingBarBottomMargin = v;
      this.touchSettings();
    })
    this.SettingSlider('浮动工具栏 UI 缩放 (%)', this.settings.appearance.enableViewboxFloatingBarScaleTransform, 50, 150, 1, (v: number) => {
      this.settings.appearance.enableViewboxFloatingBarScaleTransform = v;
      this.touchSettings();
    })
    this.SettingSlider('画板 UI 缩放 (%)', this.settings.appearance.enableViewboxBlackBoardScaleTransform, 50, 200, 5, (v: number) => {
      this.settings.appearance.enableViewboxBlackBoardScaleTransform = v;
      this.touchSettings();
    })
    this.SettingToggle('按钮背景透明', this.settings.appearance.isTransparentButtonBackground, (v: boolean) => {
      this.settings.appearance.isTransparentButtonBackground = v;
      this.touchSettings();
    })
    this.SettingToggle('显示退出按钮', this.settings.appearance.isShowExitButton, (v: boolean) => {
      this.settings.appearance.isShowExitButton = v;
      this.touchSettings();
    })
    this.SettingToggle('显示橡皮按钮', this.settings.appearance.isShowEraserButton, (v: boolean) => {
      this.settings.appearance.isShowEraserButton = v;
      this.touchSettings();
    })
    this.SettingToggle('显示隐藏控制按钮', this.settings.appearance.isShowHideControlButton, (v: boolean) => {
      this.settings.appearance.isShowHideControlButton = v;
      this.touchSettings();
    })
    this.SettingToggle('显示左右切换按钮', this.settings.appearance.isShowLRSwitchButton, (v: boolean) => {
      this.settings.appearance.isShowLRSwitchButton = v;
      this.touchSettings();
    })
    this.SettingToggle('显示手指模式切换按钮', this.settings.appearance.isShowModeFingerToggleSwitch, (v: boolean) => {
      this.settings.appearance.isShowModeFingerToggleSwitch = v;
      this.touchSettings();
    })
    this.SettingToggle('调色盘显示笔尖模式切换', this.settings.appearance.isEnableDisPlayNibModeToggler, (v: boolean) => {
      this.settings.appearance.isEnableDisPlayNibModeToggler = v;
      this.touchSettings();
    })
    this.SettingToggle('减少动效（应用内）', this.reduceMotion, (v: boolean) => {
      this.reduceMotion = v;
      this.persistUiPrefs();
    })
    this.SettingInfo(this.systemReduceMotion
      ? '系统动效缩放为 0，已自动减少动效。'
      : '系统动效缩放未读取到减少信号；可手动开启（探测需真机核对）。')
  }

  @Builder
  SettingsBehavior() {
    this.SettingSection('PPT 选项（课件模式）')
    this.SettingInfo('课件批注模式为 PDF；原始 PPT 经「PPT（WPS）」工具跳转 WPS 鸿蒙版查看/放映。COM 级控制不可用。')
    this.SettingToggle('显示底部翻页面板', this.settings.behavior.isShowBottomPPTNavigationPanel, (v: boolean) => {
      this.settings.behavior.isShowBottomPPTNavigationPanel = v;
      this.touchSettings();
    })
    this.SettingToggle('显示底部页码', this.settings.behavior.isShowPPTNavigationBottom, (v: boolean) => {
      this.settings.behavior.isShowPPTNavigationBottom = v;
      this.touchSettings();
    })
    this.SettingToggle('侧边翻页面板', this.settings.behavior.isShowSidePPTNavigationPanel, (v: boolean) => {
      this.settings.behavior.isShowSidePPTNavigationPanel = v;
      this.touchSettings();
    })
    this.SettingToggle('侧边页码', this.settings.behavior.isShowPPTNavigationSides, (v: boolean) => {
      this.settings.behavior.isShowPPTNavigationSides = v;
      this.touchSettings();
    })
    this.SettingInfo('WPS 支持：工具面板「PPT（WPS）」拉起 WPS 鸿蒙版（手机/Pad 包自适应）；未安装引导应用市场。放映控制待 WPS 官方开放。')
    this.SettingDeferred('进入放映时自动批注', '无放映状态检测。')
    this.SettingDeferred('放映模式双指手势', '放映专属手势门控尚未移植（与基线一致）。')
    this.SettingToggle('允许手指手势翻页', this.settings.behavior.isEnableFingerGestureSlideShowControl, (v: boolean) => {
      this.settings.behavior.isEnableFingerGestureSlideShowControl = v;
      this.touchSettings();
    })
    this.SettingDeferred('自动幻灯片截屏', '无自动放映截屏。')
    this.SettingToggle('自动保存课件墨迹', this.settings.behavior.isAutoSaveStrokesInPowerPoint, (v: boolean) => {
      this.settings.behavior.isAutoSaveStrokesInPowerPoint = v;
      this.touchSettings();
    })
    this.SettingDeferred('记忆上次播放位置 / 提示隐藏幻灯片 / 提示自动播放', '无 PPT 状态通知。')
    this.SettingToggle('选择时不清除墨迹（课件）', this.settings.behavior.isNoClearStrokeOnSelectWhenInPowerPoint, (v: boolean) => {
      this.settings.behavior.isNoClearStrokeOnSelectWhenInPowerPoint = v;
      this.settings.behavior.isShowStrokeOnSelectInPowerPoint = v;
      this.touchSettings();
      this.refreshScene();
    })
    this.SettingToggle('鼠标模式时显示课件墨迹', this.settings.behavior.isShowStrokeOnSelectInPowerPoint, (v: boolean) => {
      this.settings.behavior.isShowStrokeOnSelectInPowerPoint = v;
      this.touchSettings();
      this.refreshScene();
    })
  }

  @Builder
  SettingsAdvanced() {
    this.SettingSection('高级选项')
    this.SettingInfo('# 特殊屏幕模式调节触控屏触摸大小倍数；倍数为 0 即禁用手势橡皮。')
    this.SettingToggle('特殊屏幕模式', this.settings.advanced.isSpecialScreen, (v: boolean) => {
      this.settings.advanced.isSpecialScreen = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingSlider('特殊屏幕触摸大小倍数', this.settings.advanced.touchMultiplier, 0, 5, 0.01, (v: number) => {
      this.settings.advanced.touchMultiplier = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingButton('触摸大小倍数校准', '打开校准垫', () => {
      this.settingsOpen = false;
      this.calibrationSamples = [];
      this.calibrationOpen = true;
    })
    this.SettingSlider('笔尖模式 BoundsWidth', this.settings.advanced.nibModeBoundsWidth, 1, 50, 1, (v: number) => {
      this.settings.advanced.nibModeBoundsWidth = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingSlider('手指模式 BoundsWidth', this.settings.advanced.fingerModeBoundsWidth, 1, 50, 1, (v: number) => {
      this.settings.advanced.fingerModeBoundsWidth = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingSlider('笔尖模式 ThresholdValue', this.settings.advanced.nibModeBoundsWidthThresholdValue, 0, 10, 0.01, (v: number) => {
      this.settings.advanced.nibModeBoundsWidthThresholdValue = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingSlider('手指模式 ThresholdValue', this.settings.advanced.fingerModeBoundsWidthThresholdValue, 0, 10, 0.01, (v: number) => {
      this.settings.advanced.fingerModeBoundsWidthThresholdValue = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingSlider('笔尖模式 EraserSize', this.settings.advanced.nibModeBoundsWidthEraserSize, 0.01, 10, 0.01, (v: number) => {
      this.settings.advanced.nibModeBoundsWidthEraserSize = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingSlider('手指模式 EraserSize', this.settings.advanced.fingerModeBoundsWidthEraserSize, 0.01, 10, 0.01, (v: number) => {
      this.settings.advanced.fingerModeBoundsWidthEraserSize = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingToggle('四边红外模式', this.settings.advanced.isQuadIR, (v: boolean) => {
      this.settings.advanced.isQuadIR = v;
      this.applySettings();
      this.touchSettings();
    })
    this.SettingToggle('记录日志', this.settings.advanced.isLogEnabled, (v: boolean) => {
      this.settings.advanced.isLogEnabled = v;
      setLogEnabled(v);
      this.touchSettings();
    })
    this.SettingToggle('关闭软件时二次弹窗确认', this.settings.advanced.isSecondConfimeWhenShutdownApp, (v: boolean) => {
      this.settings.advanced.isSecondConfimeWhenShutdownApp = v;
      this.touchSettings();
    })
    this.SettingDeferred('阻止系统边缘手势', '无边缘手势抑制 API；沉浸式全屏下系统手势由系统决定。')
    this.SettingInfo('特殊屏幕与触摸参数按原 WPF 算法移植；请在实际触控屏与压感笔上复核（需真机验证）。')
  }

  @Builder
  SettingsAutomation() {
    this.SettingSection('自动选项')
    this.SettingInfo('无法获取其他应用前台状态：自动收纳/进程查杀类设置仅保留值。')
    this.SettingDeferred('进入“希沃白板”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('自动收纳忽略桌面 EN 批注窗口', '窗口识别排除规则不可用。')
    this.SettingDeferred('进入“希沃视频展台”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“希沃轻白板”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“希沃品课”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“鸿合白板”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“鸿合视频展台”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“文香白板”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“中原旧白板”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入“微软 Whiteboard”时自动收纳', '前台应用识别不可用。')
    this.SettingDeferred('进入 PPT 放映时自动收纳', '无放映状态检测；手动收纳可用（Alt+V）。')
    this.SettingDeferred('自动查杀希沃“PPT 小工具”', '无进程查杀能力。')
    this.SettingDeferred('自动查杀希沃白板 5', '无进程查杀能力。')
    this.SettingToggle('清屏时自动保存墨迹', this.settings.automation.isAutoSaveStrokesAtClear, (v: boolean) => {
      this.settings.automation.isAutoSaveStrokesAtClear = v;
      this.touchSettings();
      this.rebuildAutoSave();
    })
    this.SettingToggle('截图分日期文件夹保存', this.settings.automation.isSaveScreenshotsInDateFolders, (v: boolean) => {
      this.settings.automation.isSaveScreenshotsInDateFolders = v;
      this.touchSettings();
    })
    this.SettingToggle('截图时自动保存墨迹', this.settings.automation.isAutoSaveStrokesAtScreenshot, (v: boolean) => {
      this.settings.automation.isAutoSaveStrokesAtScreenshot = v;
      this.touchSettings();
    })
    this.SettingToggle('切换到鼠标模式后自动清屏', this.settings.automation.isAutoClearWhenExitingWritingMode, (v: boolean) => {
      this.settings.automation.isAutoClearWhenExitingWritingMode = v;
      this.touchSettings();
    })
    this.SettingSlider('自动保存所需最少墨迹数（0 不限制）', this.settings.automation.minimumAutomationStrokeNumber, 0, 10, 1, (v: number) => {
      this.settings.automation.minimumAutomationStrokeNumber = Math.round(v);
      this.touchSettings();
    })
    Column({ space: 4 }) {
      Text('墨迹、截图自动保存位置')
        .fontSize(14)
        .fontColor(this.dark ? Color.White : Color.Black)
      TextInput({ text: this.settings.automation.autoSavedStrokesLocation })
        .width('100%')
        .onChange((v: string) => {
          this.settings.automation.autoSavedStrokesLocation = v;
          this.touchSettings();
        })
      Text('HarmonyOS 版实际保存于应用沙箱（Auto Saved - Strokes）；此值用于跨端设置互读。')
        .fontSize(11)
        .fontColor(this.dark ? '#66FFFFFF' : '#66000000')
        .width('100%')
    }
    .width('100%')
    .alignItems(HorizontalAlign.Start)
    this.SettingToggle('定期自动删除过期文件', this.settings.automation.autoDelSavedFiles, (v: boolean) => {
      this.settings.automation.autoDelSavedFiles = v;
      this.touchSettings();
      this.rebuildAutoSave();
    })
    this.SettingOptions('保存时间设置（天）：当前 ' + this.settings.automation.autoDelSavedFilesDaysThreshold,
      ['1', '3', '5', '7', '15', '30', '60', '100', '365'],
      [1, 3, 5, 7, 15, 30, 60, 100, 365].indexOf(this.settings.automation.autoDelSavedFilesDaysThreshold),
      (i: number) => {
        const days: number[] = [1, 3, 5, 7, 15, 30, 60, 100, 365];
        this.settings.automation.autoDelSavedFilesDaysThreshold = days[i];
        this.touchSettings();
        this.rebuildAutoSave();
      })
  }

  @Builder
  SettingsRand() {
    this.SettingSection('抽奖（移植附加）')
    this.SettingSlider('抽取人数', this.settings.randSettings.peopleCount, 1, 200, 1, (v: number) => {
      this.settings.randSettings.peopleCount = Math.round(v);
      this.lottery.PeopleCount = Math.round(v);
      this.touchSettings();
    })
    this.SettingToggle('不重复抽取', this.settings.randSettings.isNotRepeatName, (v: boolean) => {
      this.settings.randSettings.isNotRepeatName = v;
      this.lotteryNoRepeat = v;
      this.touchSettings();
    })
  }

  @Builder
  SettingsAbout() {
    this.SettingSection('关于 Ink Canvas Artistry')
    this.SettingInfo('当前版本：3.4.0（HarmonyOS 移植）· 开源协议 GPL-3.0')
    this.SettingInfo('上游：InkCanvas/Ink-Canvas-Artistry ← WXRIW/Ink-Canvas。')
    this.SettingButton('重置设置（恢复推荐默认）', '重置', () => {
      // C# ResetToRecommendation：就地改写并保留自动删除配置；随后重新应用并保存。
      resetToRecommendation(this.settings);
      this.applySettings();
      this.touchSettings();
      this.refreshScene();
      promptAction.showToast({ message: '已恢复推荐设置', duration: 2000 });
    })
    Row({ space: 8 }) {
      Text('检查更新')
        .fontSize(13).fontColor(Color.White).padding(10)
        .backgroundColor('#FF0067C0').borderRadius(6)
        .onClick(() => { this.settingsOpen = false; this.updateOpen = true; })
      Text('操作指南')
        .fontSize(13).fontColor(Color.White).padding(10)
        .backgroundColor('#FF0067C0').borderRadius(6)
        .onClick(() => { this.settingsOpen = false; this.guideOpen = true; })
      Text('更新日志')
        .fontSize(13).fontColor(Color.White).padding(10)
        .backgroundColor('#FF0067C0').borderRadius(6)
        .onClick(() => { this.settingsOpen = false; this.changelogOpen = true; })
    }
  }

  // D8：保存批注 PDF——当前会话各页墨迹写为标准 INK 批注的 PDF 副本（沙箱）+ sidecar .icink（权威层）。
  private async saveAnnotatedCoursewarePdf(): Promise<void> {
    if (this.storage === null || this.coursewareSourcePath.length === 0) {
      promptAction.showToast({ message: '请先打开课件 PDF', duration: 2000 });
      return;
    }
    const geometries = readPdfGeometries(this.coursewareSourcePath);
    if (geometries === null) {
      promptAction.showToast({ message: 'PDF Kit 不可用（仅中国大陆设备），已保留 .icink 批注层', duration: 2500 });
      this.saveCoursewareBundle();
      return;
    }
    const pages = new Map<number, InkStroke[]>();
    const placements = new Map<number, PagePlacement>();
    for (let i = 0; i < this.courseware.PageCount; i++) {
      const doc = this.courseware.pageDocument(i);
      if (doc.Strokes.length > 0) {
        pages.set(i, doc.Strokes);
        const size = this.pdfSource.pageSize(i);
        const dipWidth = size[0] * 96 / 72;
        const dipHeight = size[1] * 96 / 72;
        placements.set(i, containPlacement(this.windowWidth, this.windowHeight, dipWidth, dipHeight));
      }
    }
    const stamp = Date.now();
    const target = this.storage.fullPathPublic('annotated-' + stamp + '.pdf');
    const result = await writeAnnotatedPdf(this.coursewareSourcePath, target, pages, geometries, placements);
    // sidecar：会话级 .icink 清单照常保存（权威可编辑层）。
    this.saveCoursewareBundle();
    if (result.Ok) {
      promptAction.showToast({
        message: '批注 PDF 已保存（' + result.Annotations + ' 条墨迹' + (result.Limited ? '，部分页跳过' : '') + '）',
        duration: 2500
      });
    } else if (result.Annotations > 0) {
      promptAction.showToast({ message: '批注写入失败，.icink 批注层已保存', duration: 2500 });
    } else {
      promptAction.showToast({ message: '无可写墨迹，.icink 批注层已保存', duration: 2000 });
    }
  }

  private saveCoursewareBundle(): void {
    if (this.storage === null || this.courseware.PageCount === 0) {
      return;
    }
    const bundle = this.courseware.saveBundle();
    this.storage.writeTextAtomic('courseware/manifest.json', bundle.Manifest);
    for (let i = 0; i < bundle.Pages.length; i++) {
      this.storage.writeTextAtomic('courseware/' + bundle.Pages[i].File, bundle.Pages[i].Content);
    }
  }

  private loadCoursewareBundle(): void {
    if (this.storage === null) {
      return;
    }
    const manifest = this.storage.readText('courseware/manifest.json');
    if (manifest === null) {
      return;
    }
    const files = this.storage.list('courseware');
    const pages: CoursewarePageFile[] = [];
    for (let i = 0; i < files.length; i++) {
      if (files[i].endsWith('.icink')) {
        const content = this.storage.readText('courseware/' + files[i]);
        if (content !== null) {
          pages.push({ File: files[i], Content: content });
        }
      }
    }
    this.courseware.loadBundle({ Manifest: manifest, Pages: pages }, this.courseware.PageCount);
  }

  private handleKey(event: KeyEvent): void {
    if (event.type !== KeyType.Down) {
      return;
    }
    let alt = false;
    let ctrl = false;
    try {
      if (event.getModifierKeyState !== undefined) {
        alt = event.getModifierKeyState(['Alt']);
        ctrl = event.getModifierKeyState(['Ctrl']);
      }
    } catch (e) {
      return;
    }
    const code = event.keyCode;
    if (ctrl && code === KeyCode.KEYCODE_Z) {
      this.undoAction();
    } else if (ctrl && code === KeyCode.KEYCODE_Y) {
      this.redoAction();
    } else if (ctrl && code === KeyCode.KEYCODE_E) {
      this.handleAction('clear');
    } else if (alt && code === KeyCode.KEYCODE_D) {
      this.handleAction('pen');
    } else if (alt && code === KeyCode.KEYCODE_E) {
      this.handleAction('eraserCircle');
    } else if (alt && code === KeyCode.KEYCODE_S) {
      this.handleAction('select');
    } else if (alt && code === KeyCode.KEYCODE_B) {
      this.handleAction('board');
    } else if (alt && code === KeyCode.KEYCODE_V) {
      this.handleAction('fold');
    } else if (alt && code === KeyCode.KEYCODE_Q) {
      this.handleAction('mouse');
    }
  }

  private boardMediaPath(asset: BoardAsset): string {
    const cached = this.mediaPaths.get(asset.Id);
    if (cached !== undefined) {
      return cached;
    }
    if (this.storage === null) {
      return '';
    }
    try {
      const helper = new util.Base64Helper();
      const bytes = helper.decodeSync(asset.Data);
      const relative = 'board-media-' + asset.Id + '.mp4';
      if (this.storage.writeBytes(relative, bytes.buffer as ArrayBuffer)) {
        const path = this.storage.fullPathPublic(relative);
        this.mediaPaths.set(asset.Id, path);
        return path;
      }
    } catch (e) {
      return '';
    }
    return '';
  }

  private decodeBoardAsset(asset: BoardAsset): image.PixelMap | null {
    const cached = this.boardPixelMaps.get(asset.Id);
    if (cached !== undefined) {
      return cached;
    }
    try {
      const helper = new util.Base64Helper();
      const bytes = helper.decodeSync(asset.Data);
      const source = image.createImageSource(bytes.buffer as ArrayBuffer);
      const pixelMap = source.createPixelMapSync();
      source.release();
      this.boardPixelMaps.set(asset.Id, pixelMap);
      return pixelMap;
    } catch (e) {
      return null;
    }
  }

  private releaseBoardPixelMaps(): void {
    this.boardPixelMaps.forEach((pixelMap: image.PixelMap) => {
      try { pixelMap.release(); } catch (e) { void e; }
    });
    this.boardPixelMaps.clear();
  }

  // BoardAsset.Transform（行向量 3x2）→ Matrix4：经 renderMatrix4Values 共轭到
  // ArkUI .transform() 的组件中心锚点，精确复刻 C# RenderTransformOrigin(0,0)。
  private boardAssetMatrix(asset: BoardAsset): matrix4.Matrix4Transit {
    const v = renderMatrix4Values(asset.Left, asset.Top, asset.Width, asset.Height, asset.Transform);
    return matrix4.init([
      v[0], v[1], v[2], v[3],
      v[4], v[5], v[6], v[7],
      v[8], v[9], v[10], v[11],
      v[12], v[13], v[14], v[15]
    ]);
  }

  private videoControllerFor(asset: BoardAsset): VideoController {
    const cached = this.videoControllers.get(asset.Id);
    if (cached !== undefined) {
      return cached;
    }
    const controller = new VideoController();
    this.videoControllers.set(asset.Id, controller);
    return controller;
  }

  // 回到该页时恢复播放位置；离开前正在播放则继续播放（需真机核对首帧/seek 行为）。
  private restoreVideoPosition(asset: BoardAsset): void {
    const controller = this.videoControllers.get(asset.Id);
    if (controller === undefined) {
      return;
    }
    try {
      const position = this.videoPositions.get(asset.Id);
      if (position !== undefined && position > 0) {
        controller.setCurrentTime(position);
      }
      if (this.videoPlaying.get(asset.Id) === true) {
        controller.start();
      }
    } catch (e) {
      void e;
    }
  }

  private async insertBoardImage(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    let result: picker.PhotoSelectResult;
    try {
      const photoPicker = new picker.PhotoViewPicker();
      result = await photoPicker.select({ MIMEType: picker.PhotoViewMIMETypes.IMAGE_TYPE, maxSelectNumber: 1 });
    } catch (e) {
      return;
    }
    if (result.photoUris.length === 0) {
      return;
    }
    const id = 'img-' + Date.now();
    const path = this.storage.importUriToSandbox(result.photoUris[0], id + '.png');
    if (path === null) {
      return;
    }
    const file = fileIo.openSync(path, fileIo.OpenMode.READ_ONLY);
    const stat = fileIo.statSync(file.fd);
    let buffer = new ArrayBuffer(stat.size);
    fileIo.readSync(file.fd, buffer);
    fileIo.closeSync(file);
    if (this.settings.canvas.isCompressPicturesUploaded) {
      buffer = await compressImageBuffer(buffer, COMPRESS_MAX_DIM);
    }
    const helper = new util.Base64Helper();
    const encoded = helper.encodeSync(new Uint8Array(buffer));
    let data = '';
    for (let i = 0; i < encoded.length; i++) {
      data += String.fromCharCode(encoded[i]);
    }
    this.board.Current.Images.push({
      Id: id,
      Name: id + '.png',
      Data: data,
      ContentType: 'image/png',
      Left: Math.max(0, (this.windowWidth - 320) / 2),
      Top: Math.max(0, (this.windowHeight - 240) / 2),
      Width: 320,
      Height: 240,
      LayerOrder: null,
      Opacity: 1,
      Transform: null
    });
    // G11：插图记为页内可撤销单元（撤销移除、重做放回）。
    const page = this.board.Current;
    const inserted = page.Images[page.Images.length - 1];
    page.History.push({
      name: 'insert image',
      redo: (): void => {
        if (page.Images.indexOf(inserted) < 0) {
          page.Images.push(inserted);
        }
      },
      undo: (): void => {
        const index = page.Images.indexOf(inserted);
        if (index >= 0) {
          page.Images.splice(index, 1);
        }
      }
    });
    this.boardImagesVersion++;
  }

  // 插入画板视频（G12）：选视频 → base64 → Media 资产；BoardVideo 渲染 + 进度保持已有。
  private async insertBoardVideo(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    let result: picker.PhotoSelectResult;
    try {
      const videoPicker = new picker.PhotoViewPicker();
      result = await videoPicker.select({ MIMEType: picker.PhotoViewMIMETypes.VIDEO_TYPE, maxSelectNumber: 1 });
    } catch (e) {
      return;
    }
    if (result.photoUris.length === 0) {
      return;
    }
    const id = 'vid-' + Date.now();
    const path = this.storage.importUriToSandbox(result.photoUris[0], id + '.mp4');
    if (path === null) {
      promptAction.showToast({ message: '视频导入失败', duration: 2000 });
      return;
    }
    const file = fileIo.openSync(path, fileIo.OpenMode.READ_ONLY);
    const stat = fileIo.statSync(file.fd);
    const buffer = new ArrayBuffer(stat.size);
    fileIo.readSync(file.fd, buffer);
    fileIo.closeSync(file);
    const helper = new util.Base64Helper();
    const encoded = helper.encodeSync(new Uint8Array(buffer));
    let data = '';
    for (let i = 0; i < encoded.length; i++) {
      data += String.fromCharCode(encoded[i]);
    }
    this.board.Current.Media.push({
      Id: id,
      Name: id + '.mp4',
      Data: data,
      ContentType: 'video/mp4',
      Left: Math.max(0, (this.windowWidth - 360) / 2),
      Top: Math.max(0, (this.windowHeight - 240) / 2),
      Width: 360,
      Height: 240,
      LayerOrder: null,
      PosterData: '',
      NaturalWidth: 0,
      NaturalHeight: 0,
      Transform: null
    });
    // G11：插视频记为页内可撤销单元。
    const page = this.board.Current;
    const inserted = page.Media[page.Media.length - 1];
    page.History.push({
      name: 'insert video',
      redo: (): void => {
        if (page.Media.indexOf(inserted) < 0) {
          page.Media.push(inserted);
        }
      },
      undo: (): void => {
        const index = page.Media.indexOf(inserted);
        if (index >= 0) {
          page.Media.splice(index, 1);
        }
      }
    });
    this.boardImagesVersion++;
  }

  private applyGestures(): void {
    this.controller.ConfigureGestures(
      this.settings.gesture.isEnableTwoFingerZoom,
      this.settings.gesture.isEnableTwoFingerTranslate,
      this.settings.gesture.isEnableTwoFingerRotation
    );
  }

  private boardBackgroundColor(): string {
    if (this.boardBackground === 1) {
      return '#FFFAFAFA';
    }
    return '#FF1F3B2C';
  }

  // G1 套索擦覆盖层：闭合路径 + 虚线描边 + 35α 填充（视图 DIP，与控制器 LassoPath 同坐标）。
  private drawLassoOverlay(): void {
    const context = this.lassoContext;
    context.clearRect(0, 0, this.windowWidth, this.windowHeight);
    if (!this.controller.IsLassoErasing) {
      return;
    }
    const path = this.controller.LassoPath;
    if (path.length < 2) {
      return;
    }
    context.beginPath();
    context.moveTo(path[0].X, path[0].Y);
    for (let i = 1; i < path.length; i++) {
      context.lineTo(path[i].X, path[i].Y);
    }
    context.closePath();
    context.fillStyle = '#230067C0';
    context.fill();
    context.strokeStyle = '#FF0067C0';
    context.setLineDash([8, 6]);
    context.lineWidth = 1.5;
    context.stroke();
    context.setLineDash([]);
  }

  // G6 落点成形动画：覆盖层画 Preview 首点（世界→视图，半径随过渡宽度）。
  private drawDotAnimations(): void {
    const animations = this.controller.DotAnimations;
    if (animations.length === 0) {
      return;
    }
    const context = this.lassoContext;
    const view = this.controller.ViewTransform;
    for (let i = 0; i < animations.length; i++) {
      const preview = animations[i].Preview;
      if (preview.Count === 0) {
        continue;
      }
      const p = preview.Points[0];
      const screen = transformPointByMatrix(view, p.X, p.Y);
      const scale = Math.sqrt(Math.abs(view.M11 * view.M22 - view.M12 * view.M21));
      const radius = Math.max(0.5, p.Width / 2) * Math.max(0.1, scale);
      context.beginPath();
      context.arc(screen.X, screen.Y, radius, 0, Math.PI * 2);
      const packed = preview.Attributes.Color.packedArgb();
      context.fillStyle = '#' + (packed >>> 0).toString(16).padStart(8, '0');
      context.fill();
    }
  }

  private ensureDotTimer(): void {
    if (this.dotTimer >= 0 || this.controller.DotAnimations.length === 0) {
      return;
    }
    this.dotTimer = setInterval(() => {
      this.controller.AdvanceDotAnimations(16);
      if (this.controller.DotAnimations.length === 0 && this.dotTimer >= 0) {
        clearInterval(this.dotTimer);
        this.dotTimer = -1;
      }
      this.refreshScene();
    }, 16);
  }

  private drawBoardGrid(): void {
    const spacing = this.board.GridSpacing;
    const width = this.windowWidth;
    const height = this.windowHeight;
    this.boardGridContext.clearRect(0, 0, width, height);
    this.boardGridContext.strokeStyle = this.boardBackground === 3 ? '#33000000' : '#22FFFFFF';
    this.boardGridContext.lineWidth = 1;
    if (this.boardBackground === 2) {
      for (let x = spacing; x < width; x += spacing) {
        this.boardGridContext.beginPath();
        this.boardGridContext.moveTo(x, 0);
        this.boardGridContext.lineTo(x, height);
        this.boardGridContext.stroke();
      }
    }
    for (let y = spacing; y < height; y += spacing) {
      this.boardGridContext.beginPath();
      this.boardGridContext.moveTo(0, y);
      this.boardGridContext.lineTo(width, y);
      this.boardGridContext.stroke();
    }
  }

  private openBoard(): void {
    this.annotateDocument = this.controller.Document;
    this.controller.BindDocument(this.board.Current.Document);
    this.boardOpen = true;
    this.mode = 'board';
    // 自动开关两指缩放：进入画板临时启用两指缩放（离开恢复设置值）。
    if (this.settings.gesture.AutoSwitchTwoFingerGesture) {
      this.controller.ConfigureGestures(true, this.settings.gesture.isEnableTwoFingerTranslate, this.settings.gesture.isEnableTwoFingerRotation);
    }
    this.syncBoard();
    this.refreshScene();
  }

  private exitBoard(): void {
    this.releaseBoardPixelMaps();
    this.boardOpen = false;
    this.mode = 'annotate';
    this.controller.BindDocument(this.annotateDocument);
    if (this.settings.gesture.AutoSwitchTwoFingerGesture) {
      this.applyGestures();
    }
    this.refreshScene();
  }

  // 切换到鼠标模式：若启用「退出书写模式自动清屏」且当前在书写且有墨迹，则清空。
  private enterMouseMode(): void {
    const wasWriting = this.controller.Tool === InkTool.Pen || this.controller.Tool === InkTool.Highlighter;
    if (this.settings.automation.isAutoClearWhenExitingWritingMode && wasWriting && this.controller.Document.Count > 0) {
      if (this.autoSave !== null &&
        this.controller.Document.Strokes.length >= this.settings.automation.minimumAutomationStrokeNumber) {
        this.autoSave.saveSnapshot(this.controller.Document.Strokes, 'clear');
      }
      this.controller.ClearSelection();
      this.controller.Clear();
    }
    this.controller.Tool = InkTool.Pan;
    this.selectedId = 'mouse';
    this.paletteOpen = false;
    this.shapeOpen = false;
  }

  // 手指/笔尖模式快速切换（浮条按钮，等价设置「启用笔尖模式」）。
  private toggleFingerMode(): void {
    const next = !this.settings.startup.isEnableNibMode;
    this.settings.startup.isEnableNibMode = next;
    this.controller.Palm.NibMode = next;
    this.persistSettings();
    promptAction.showToast({ message: next ? '笔尖模式' : '手指模式', duration: 1200 });
  }

  // 退出应用：二次确认（advanced.isSecondConfimeWhenShutdownApp）后 terminateSelf。
  private requestExit(): void {
    if (this.settings.advanced.isSecondConfimeWhenShutdownApp && this.pendingYesNo !== 'exit') {
      this.yesNoMessage = '退出 InkCanvas？';
      this.pendingYesNo = 'exit';
      this.yesNoOpen = true;
      return;
    }
    this.pendingYesNo = '';
    this.terminateApp();
  }

  private terminateApp(): void {
    const context = getContext(this) as common.UIAbilityContext;
    context.terminateSelf().catch((e: Object) => {
      void e;
    });
  }

  // 画笔光标直径：橡皮按擦除半径，书写按笔宽。
  private cursorDiameter(): number {
    if (this.controller.Tool === InkTool.Eraser) {
      return Math.max(8, this.eraserRadiusForSize(this.settings.canvas.eraserSize) * 2);
    }
    return Math.max(8, this.controller.CurrentAttributes.BaseWidth * 2);
  }

  // G3 选区工具条放置：选区 AABB → 视图矩形 → FloatingToolbarLayout 候选评分（G16）。
  private updateSelectionToolbarPosition(): void {
    const bounds = this.controller.SelectionBounds;
    if (bounds.isEmpty()) {
      this.selectionToolbarOpen = false;
      return;
    }
    const view = this.controller.ViewTransform;
    const corners: number[][] = [
      [bounds.Left, bounds.Top], [bounds.Right, bounds.Top],
      [bounds.Right, bounds.Bottom], [bounds.Left, bounds.Bottom]
    ];
    let left = Number.MAX_VALUE;
    let top = Number.MAX_VALUE;
    let right = -Number.MAX_VALUE;
    let bottom = -Number.MAX_VALUE;
    for (let i = 0; i < corners.length; i++) {
      const p = transformPointByMatrix(view, corners[i][0], corners[i][1]);
      left = Math.min(left, p.X);
      top = Math.min(top, p.Y);
      right = Math.max(right, p.X);
      bottom = Math.max(bottom, p.Y);
    }
    const placed = placeToolbar(
      [0, 0, this.windowWidth, this.windowHeight],
      [left, top, right, bottom],
      [0, 0, 0, 0], 320, 44, 10);
    this.selectionToolbarX = placed[0];
    this.selectionToolbarY = placed[1];
  }

  // 选区工具条动作（G3：Recolor/ScaleWidth/RestoreWidth/Delete/克隆开关/取消）。
  private recolorSelection(packed: number): void {
    this.controller.RecolorSelection(InkColor.fromPackedArgb(packed));
  }

  // D1：画板元素按 DrawingObjects 层序渲染（墨迹仍整体画在独立画布——单画布架构下的
  // 已记录简化；元素间 z 序完整保留）。空层序时回退 图片→视频 页序。
  private orderedBoardAssets(): BoardAsset[] {
    const images = this.board.Current.Images;
    const media = this.board.Current.Media;
    const byId = new Map<string, BoardAsset>();
    for (let i = 0; i < images.length; i++) {
      byId.set(images[i].Id, images[i]);
    }
    for (let i = 0; i < media.length; i++) {
      byId.set(media[i].Id, media[i]);
    }
    const result: BoardAsset[] = [];
    const objects = this.controller.DrawingObjects;
    for (let i = 0; i < objects.length; i++) {
      const element = objects[i].Element;
      if (element !== null && element.isPresent()) {
        const asset = byId.get(element.id());
        if (asset !== undefined) {
          result.push(asset);
          byId.delete(element.id());
        }
      }
    }
    // 未入层序体系（无 GetSelectableElements 数据）的剩余元素按页序补尾。
    const rest: BoardAsset[] = [];
    for (let i = 0; i < images.length; i++) {
      if (byId.has(images[i].Id)) {
        rest.push(images[i]);
      }
    }
    for (let i = 0; i < media.length; i++) {
      if (byId.has(media[i].Id)) {
        rest.push(media[i]);
      }
    }
    return result.concat(rest);
  }

  // 应用触摸校准：乘数 = 目标参考宽度 / 平均接触宽度，写入 advanced.touchMultiplier。
  private applyCalibration(): void {
    const m = calibrationMultiplier(this.calibrationSamples, this.settings.advanced.nibModeBoundsWidthThresholdValue);
    this.settings.advanced.touchMultiplier = m;
    if (this.controller.Palm.Advanced !== null) {
      this.controller.Palm.Advanced.touchMultiplier = m;
    }
    this.persistSettings();
    this.calibrationOpen = false;
    this.calibrationSamples = [];
    promptAction.showToast({ message: '校准乘数 ' + m.toFixed(2), duration: 2000 });
  }

  private syncBoard(): void {
    this.releaseBoardPixelMaps();
    this.boardImagesVersion++;
    this.boardIndex = this.board.CurrentIndex;
    this.boardPageCount = this.board.PageCount;
    this.boardBackground = this.board.Background;
    this.controller.BindDocument(this.board.Current.Document);
    this.refreshScene();
  }

  private boardNavigate(delta: number): void {
    this.board.navigate(delta);
    this.syncBoard();
  }

  // 原子清空当前画板页（墨迹+图片+视频，单页内撤销单元整体恢复）。
  private clearBoardPage(): void {
    if (this.board.clearCurrentPage()) {
      this.releaseBoardPixelMaps();
      this.boardImagesVersion++;
      this.refreshScene();
    }
  }

  // 衍至新页：选中墨迹克隆到新页（单撤销单元在新页）。元素选区桥（G2）未建，图片/视频暂不随行。
  private cloneSelectionToNewPage(): void {
    if (this.controller.Selection.length === 0) {
      promptAction.showToast({ message: '请先框选墨迹', duration: 1500 });
      return;
    }
    if (this.board.cloneSelectionToNewPage(this.controller.Selection.slice(), [], [], 'c' + Date.now())) {
      this.syncBoard();
    }
  }

  private async saveBoard(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    try {
      const docPicker = new picker.DocumentViewPicker();
      const uris = await docPicker.save({ newFileNames: ['board.icink'] });
      if (uris.length === 0) {
        return;
      }
      const file = fileIo.openSync(uris[0], fileIo.OpenMode.CREATE | fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.TRUNC);
      fileIo.writeSync(file.fd, this.board.saveBoardJson());
      fileIo.closeSync(file);
      promptAction.showToast({ message: '画板已保存', duration: 2000 });
    } catch (e) {
      promptAction.showToast({ message: '保存失败', duration: 2000 });
    }
  }

  private async loadBoard(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    try {
      const docPicker = new picker.DocumentViewPicker();
      const uris = await docPicker.select({ maxSelectNumber: 1, fileSuffixFilters: ['.icink'] });
      if (uris.length === 0) {
        return;
      }
      const path = this.storage.importUriToSandbox(uris[0], 'board-load.icink');
      if (path === null) {
        return;
      }
      const text = this.storage.readText('board-load.icink');
      if (text !== null) {
        this.board.loadJson(text);
        this.releaseBoardPixelMaps();
        this.boardImagesVersion++;
        this.syncBoard();
      }
    } catch (e) {
      promptAction.showToast({ message: '画板文件无效或打开失败', duration: 2000 });
    }
  }

  private formatCountdown(totalSeconds: number): string {
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    const sec = totalSeconds % 60;
    const pad = (n: number): string => n < 10 ? '0' + n : '' + n;
    return pad(h) + ':' + pad(m) + ':' + pad(sec);
  }

  private startCountdownTimer(): void {
    this.stopCountdownTimer();
    this.countdownTimer = setInterval(() => {
      this.countdown.tick(Date.now());
      this.countdownDisplay = this.countdown.Display;
      this.countdownRing = this.countdown.Ring;
      this.pushCountdownPiP();
      if (!this.countdown.IsRunning) {
        this.countdownRunning = false;
        this.countdownFinished = true;
        this.stopCountdownTimer();
        this.pushCountdownPiP();
        playCountdownFinish(getContext(this) as common.UIAbilityContext);
      }
    }, this.countdown.tickIntervalMs());
  }

  private stopCountdownTimer(): void {
    if (this.countdownTimer >= 0) {
      clearInterval(this.countdownTimer);
      this.countdownTimer = -1;
    }
  }

  // 开始/暂停（应用内按钮与 PiP 系统控制面板共用）。
  private toggleCountdownPause(): void {
    this.countdownFinished = false;
    this.countdown.startPause(Date.now());
    this.countdownRunning = this.countdown.IsRunning && !this.countdown.IsPaused;
    if (this.countdown.IsRunning) {
      this.startCountdownTimer();
    } else {
      this.stopCountdownTimer();
    }
    this.pushCountdownPiP();
  }

  private countdownPiPParams(): CountdownPiPParams {
    const params = new CountdownPiPParams();
    params.display = this.formatCountdown(this.countdownDisplay);
    params.ring = this.countdownRing;
    params.finished = this.countdownFinished;
    return params;
  }

  private pushCountdownPiP(): void {
    if (this.countdownPiP.IsActive) {
      this.countdownPiP.update(this.countdownPiPParams());
    }
  }

  // 顶层置顶 → 画中画悬浮窗（前台启动；退桌面自动拉起）。空 surface/触摸/后台保活需真机核对。
  private async startCountdownPiP(): Promise<void> {
    if (this.countdownPiP.IsActive) {
      await this.countdownPiP.stop();
      this.countdownPiPActive = false;
      return;
    }
    if (!CountdownPiPService.isSupported()) {
      promptAction.showToast({ message: '此设备不支持画中画悬浮窗', duration: 2000 });
      return;
    }
    const ok = await this.countdownPiP.start(
      this.getUIContext(),
      this.countdownPiPParams(),
      () => { this.toggleCountdownPause(); },
      () => { this.countdownPiPActive = false; }
    );
    this.countdownPiPActive = ok;
    if (!ok) {
      promptAction.showToast({ message: '悬浮窗启动失败', duration: 2000 });
    }
  }

  private stopLotteryTimer(): void {
    if (this.lotteryTimer >= 0) {
      clearTimeout(this.lotteryTimer);
      this.lotteryTimer = -1;
    }
  }

  // 抽奖滚动：5 次闪烁（150ms）→ 最终抽选 → 分组输出；单次模式 1.5s 后自动关闭。
  // 镜像 LotteryWindow.RollAsync（flicker 唯一/清空、NoRepeat 持久池 vs 本次唯一、 thirds 分组）。
  private rollLottery(): void {
    if (this.lotteryRolling) {
      return;
    }
    const names = applyReplaceRules(this.lotteryNames, this.lotteryReplace);
    this.lottery.loadNames(names.join('\n'));
    this.lottery.PeopleCount = names.length > 0 ? names.length : Math.max(1, this.settings.randSettings.peopleCount);
    this.lottery.NoRepeat = this.lotteryNoRepeat;
    const pool = names.length > 0 ? names.length : Math.max(1, this.lottery.PeopleCount);
    const count = Math.min(Math.max(1, this.lotteryCount), pool);
    this.lotteryRolling = true;
    this.lotteryGroups = [];
    this.lotteryResult = '';

    const flickerRandoms: number[] = [];
    for (let i = 0; i < 64; i++) {
      flickerRandoms.push(Math.random());
    }
    const flicker = lotteryFlicker(pool, flickerRandoms);
    let step = 0;
    const flickerStep = (): void => {
      if (step < flicker.length) {
        this.lotteryResult = lotteryDisplayName(names, flicker[step]);
        step++;
        this.lotteryTimer = setTimeout(flickerStep, 150);
        return;
      }
      this.finishLotteryDraw(names, pool, count);
    };
    flickerStep();
  }

  private finishLotteryDraw(names: string[], pool: number, count: number): void {
    // D5：多次抽取走引擎 draw（部分 Fisher-Yates：抽取内唯一 + 不重复池 + 历史封顶 20）。
    const outputs = this.lottery.draw(count);
    this.lotteryGroups = lotterySplitGroups(outputs);
    this.lotteryResult = outputs.length === 0 ? '已抽完' : this.lotteryGroups[0];
    this.lotteryRolling = false;
    if (this.lotterySingle) {
      this.lotteryTimer = setTimeout(() => {
        this.lotteryOpen = false;
        this.stopLotteryTimer();
      }, 1500);
    }
  }

  // D5：从 CSV/TSV/每行一名文件导入名单（UTF-8 优先，失败回退 GBK 解码）。
  private async importRoster(): Promise<void> {
    let uris: string[] = [];
    try {
      const docPicker = new picker.DocumentViewPicker();
      uris = await docPicker.select({ maxSelectNumber: 1, fileSuffixFilters: ['.csv', '.txt', '.tsv'] });
    } catch (e) {
      return;
    }
    if (uris.length === 0) {
      return;
    }
    const path = this.storage === null ? null : this.storage.importUriToSandbox(uris[0], 'roster.txt');
    if (path === null) {
      promptAction.showToast({ message: '名单导入失败', duration: 2000 });
      return;
    }
    let text = '';
    try {
      const file = fileIo.openSync(path, fileIo.OpenMode.READ_ONLY);
      const stat = fileIo.statSync(file.fd);
      const buffer = new ArrayBuffer(stat.size);
      fileIo.readSync(file.fd, buffer);
      fileIo.closeSync(file);
      const bytes = new Uint8Array(buffer);
      try {
        text = new util.TextDecoder('utf-8').decodeToString(bytes);
      } catch (e) {
        text = new util.TextDecoder('gbk').decodeToString(bytes);
      }
    } catch (e) {
      promptAction.showToast({ message: '名单读取失败', duration: 2000 });
      return;
    }
    const names = parseRoster(text);
    if (names.length === 0) {
      promptAction.showToast({ message: '未解析到名字（支持每行一名或带姓名列的 CSV）', duration: 2500 });
      return;
    }
    this.lotteryNames = names.join('\n');
    this.lotteryGroups = [];
    this.lotteryResult = '';
    promptAction.showToast({ message: '已导入 ' + names.length + ' 个名字', duration: 2000 });
  }

  private openReplay(): void {
    // G9：回放融合到控制器（display-only Playback；源笔画与历史不动）。
    if (!this.controller.BeginPlayback()) {
      return;
    }
    const playback = this.controller.Playback;
    if (playback === null) {
      return;
    }
    this.replayClock = new ReplayClock(playback.DurationMs);
    this.replayClock.Speed = this.replaySpeed;
    this.replayOpen = true;
    this.replayPlaying = false;
    this.replayProgress = 0;
    this.refreshScene();
  }

  private toggleReplay(): void {
    if (this.replayClock === null) {
      return;
    }
    this.replayClock.toggle();
    this.replayPlaying = this.replayClock.IsPlaying;
    if (this.replayPlaying) {
      this.startReplayTimer();
    } else {
      this.stopReplayTimer();
    }
  }

  private startReplayTimer(): void {
    this.stopReplayTimer();
    this.replayTimer = setInterval(() => {
      if (this.replayClock === null) {
        return;
      }
      this.replayClock.advance(33);
      this.controller.SeekPlayback(this.replayClock.ElapsedMs);
      this.replayProgress = this.replayClock.Progress;
      this.replayPlaying = this.replayClock.IsPlaying;
      this.refreshScene();
      if (!this.replayClock.IsPlaying) {
        this.stopReplayTimer();
      }
    }, 33);
  }

  private stopReplayTimer(): void {
    if (this.replayTimer >= 0) {
      clearInterval(this.replayTimer);
      this.replayTimer = -1;
    }
  }

  private closeReplay(): void {
    this.stopReplayTimer();
    this.controller.EndPlayback();
    this.replayOpen = false;
    this.replayClock = null;
    this.refreshScene();
  }

  private handleTouch(event: TouchEvent): void {
    const timeMs = event.timestamp / 1000000;
    const touches = event.changedTouches;
    // ArkUI SourceTool.Pen=2；仅手写笔通道的非零压感才视为真实硬件压感，
    // 手指/鼠标/模拟器继续走 0.5 回退，宽度行为与 C# 速度笔锋保持一致。
    // pressure/tiltX/tiltY 定义在 BaseEvent 上（API9，pressure 已归一化到 [0,1]），
    // TouchObject 自身不携带倾角；同一次 TouchEvent 内各触点共享该笔状态。
    const isStylus = event.sourceTool === 2;
    const hasPressure = isStylus && event.pressure > 0;
    const pressure = event.pressure > 0 ? event.pressure : 0.5;
    const tiltX = event.tiltX;
    const tiltY = event.tiltY;
    for (let i = 0; i < touches.length; i++) {
      const touch = touches[i];
      const width = touch.width === undefined ? 0 : touch.width;
      const height = touch.height === undefined ? 0 : touch.height;
      const sample = new PointerSample(
        touch.id,
        touch.x,
        touch.y,
        pressure,
        width,
        height,
        pointerKind(event.source, event.sourceTool),
        timeMs,
        false,
        false,
        tiltX,
        tiltY,
        hasPressure
      );
      if (this.settings.canvas.isShowCursor) {
        this.cursorX = touch.x;
        this.cursorY = touch.y;
        this.cursorVisible = event.type === TouchType.Down || event.type === TouchType.Move;
      }
      if (this.routeBoardImageDrag(event.type, touch.id, touch.x, touch.y)) {
        continue;
      }
      if (event.type === TouchType.Down) {
        this.controller.onPointerDown(sample);
      } else if (event.type === TouchType.Move) {
        this.controller.onPointerMove(sample);
      } else if (event.type === TouchType.Up) {
        this.controller.onPointerUp(sample);
      } else if (event.type === TouchType.Cancel) {
        this.controller.onPointerCanceled(sample);
      }
    }
  }

  // 画板元素拖拽路由：Select/Pan 工具下，Down 命中图片则接管该指针；Move 平移；Up/Cancel 记撤销。
  // 命中检测用 transformedBounds（屏幕空间，与 BoardImage 渲染坐标一致）。返回 true 表示已接管。
  private routeBoardImageDrag(type: TouchType, id: number, x: number, y: number): boolean {
    if (!this.boardOpen) {
      return false;
    }
    const tool = this.controller.Tool;
    if (tool !== InkTool.Select && tool !== InkTool.Pan) {
      return false;
    }
    if (type === TouchType.Down) {
      if (this.dragAsset !== null) {
        return false;
      }
      const hit = this.hitBoardImage(x, y);
      if (hit === null) {
        return false;
      }
      this.dragAsset = hit;
      this.dragBefore = snapshotGeometry(hit);
      this.dragStartX = x;
      this.dragStartY = y;
      this.dragPointerId = id;
      return true;
    }
    if (this.dragAsset === null || id !== this.dragPointerId || this.dragBefore === null) {
      return false;
    }
    if (type === TouchType.Move) {
      const dx = x - this.dragStartX;
      const dy = y - this.dragStartY;
      this.dragAsset.Transform = appendTransform(this.dragBefore.Transform, [1, 0, 0, 1, dx, dy]);
      this.boardImagesVersion++;
      return true;
    }
    if (type === TouchType.Up || type === TouchType.Cancel) {
      recordTransform(this.board.Current.History, this.dragAsset, this.dragBefore, snapshotGeometry(this.dragAsset), 'move image');
      this.boardImagesVersion++;
      this.dragAsset = null;
      this.dragBefore = null;
      this.dragPointerId = -1;
      return true;
    }
    return false;
  }

  private hitBoardImage(x: number, y: number): BoardAsset | null {
    const images = this.board.Current.Images;
    for (let i = images.length - 1; i >= 0; i--) {
      const asset = images[i];
      const bounds = transformedBounds(asset.Left, asset.Top, asset.Width, asset.Height, asset.Transform);
      if (x >= bounds[0] && x <= bounds[2] && y >= bounds[1] && y <= bounds[3]) {
        return asset;
      }
    }
    return null;
  }

  // 撤销/重做协调：画板模式下元素变换历史优先（存在时），否则墨迹撤销。
  // 简化说明：C# 页内 History 与墨迹撤销按时间统一；此处元素历史优先，见 manifest board-images 备注。
  private undoAction(): void {
    if (this.boardOpen && this.board.Current.History.CanUndo) {
      this.board.Current.History.undo();
      this.boardImagesVersion++;
      return;
    }
    this.controller.Undo();
  }

  private redoAction(): void {
    if (this.boardOpen && this.board.Current.History.CanRedo) {
      this.board.Current.History.redo();
      this.boardImagesVersion++;
      return;
    }
    this.controller.Redo();
  }

  private toSceneStroke(stroke: InkStroke, preview: Matrix3x2 | null = null): SceneStrokeInput {
    const points: StrokePointInput[] = [];
    for (let i = 0; i < stroke.Count; i++) {
      const point = stroke.Points[i];
      if (preview !== null) {
        const transformed = transformPointByMatrix(preview, point.X, point.Y);
        points.push({ X: transformed.X, Y: transformed.Y, Width: point.Width });
      } else {
        points.push({ X: point.X, Y: point.Y, Width: point.Width });
      }
    }
    return {
      BaseWidth: stroke.Attributes.BaseWidth,
      ColorPacked: stroke.Attributes.Color.packedArgb(),
      IsHighlighter: stroke.Attributes.IsHighlighter,
      HighlighterAlpha: stroke.Attributes.HighlighterAlpha,
      TaperKind: stroke.Attributes.TaperKind,
      Points: points
    };
  }

  private refreshScene(): void {
    if (this.renderer === null) {
      return;
    }
    const committed: SceneStrokeInput[] = [];
    let strokes = this.controller.Document.Strokes;
    // G9：画布内回放期间只画 Playback 可见前缀（源笔画隐藏、历史不动）。
    if (this.replayOpen && this.controller.Playback !== null) {
      strokes = this.controller.Playback.VisibleStrokes;
    } else if (this.controller.DotAnimations.length > 0) {
      // G6：成形动画期间跳过已提交圆点，由覆盖层画过渡预览。
      const visible: InkStroke[] = [];
      for (let i = 0; i < strokes.length; i++) {
        if (!this.controller.IsAnimatingDot(strokes[i])) {
          visible.push(strokes[i]);
        }
      }
      strokes = visible;
    }
    // 鼠标模式墨迹可见性（C# ShellController.ToggleClickThrough 语义）：
    // 课件内看 behavior.isShowStrokeOnSelectInPowerPoint，否则看 canvas.hideStrokeWhenSelecting。
    if (this.controller.Tool === InkTool.Pan && !this.replayOpen) {
      const inkVisible = this.mode === 'courseware'
        ? this.settings.behavior.isShowStrokeOnSelectInPowerPoint
        : !this.settings.canvas.hideStrokeWhenSelecting;
      if (!inkVisible) {
        strokes = [];
      }
    }
    const previewActive = this.controller.PreviewStrokes !== null;
    const previewMatrix = this.controller.SelectionPreviewTransform;
    for (let i = 0; i < strokes.length; i++) {
      const usePreview = previewActive && this.controller.IsPreviewStroke(strokes[i]);
      committed.push(this.toSceneStroke(strokes[i], usePreview ? previewMatrix : null));
    }
    const activeStroke = this.controller.ActiveStroke;
    const active = activeStroke === null ? null : this.toSceneStroke(activeStroke);
    let selection: SceneSelectionInput | null = null;
    if (this.controller.Selection.length > 0) {
      const frame = this.controller.selectionFrameSummary();
      const b = frame.Bounds;
      const m = frame.Transform;
      selection = {
        Left: b[0], Top: b[1], Right: b[2], Bottom: b[3],
        RotationRadians: Math.atan2(m[1], m[0])
      };
    }
    this.renderer.render(buildScene(committed, active, selection, matrixToArray(this.controller.ViewTransform)), Invalidation.full());
    this.drawLassoOverlay();
    this.drawDotAnimations();
    this.ensureDotTimer();
  }

  private async openCourseware(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    let uris: string[] = [];
    try {
      const docPicker = new picker.DocumentViewPicker();
      uris = await docPicker.select({ maxSelectNumber: 1, fileSuffixFilters: ['.pdf'] });
    } catch (e) {
      return;
    }
    if (uris.length === 0) {
      return;
    }
    const path = this.storage.importUriToSandbox(uris[0], 'courseware.pdf');
    if (path === null || !this.pdfSource.open(path)) {
      promptAction.showToast({ message: 'PDF 打开失败', duration: 2000 });
      return;
    }
    this.coursewareSourcePath = path;
    if (this.mode !== 'courseware') {
      this.annotateDocument = this.controller.Document;
    }
    this.courseware.open(this.pdfSource.PageCount);
    this.loadCoursewareBundle();
    this.mode = 'courseware';
    this.pptActive = true;
    this.toolsOpen = false;
    this.showCoursewarePage(this.courseware.CurrentIndex);
  }

  // PPT 放映对接 WPS 鸿蒙版：选 .ppt/.pptx/.ppsx → 导入沙箱 → 文件打开 Want 拉起 WPS。
  // 无公开放映 intent（调研 14），由 WPS 内放映；未安装则引导应用市场。需真机核对 WPS 响应。
  private async openPptInWps(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    let uris: string[] = [];
    try {
      const docPicker = new picker.DocumentViewPicker();
      uris = await docPicker.select({ maxSelectNumber: 1, fileSuffixFilters: ['.ppt', '.pptx', '.ppsx'] });
    } catch (e) {
      return;
    }
    if (uris.length === 0) {
      return;
    }
    const uri = uris[0];
    const dot = uri.lastIndexOf('.');
    const ext = dot >= 0 ? uri.substring(dot + 1) : 'pptx';
    const path = this.storage.importUriToSandbox(uri, 'lesson.' + ext);
    if (path === null) {
      promptAction.showToast({ message: '文件导入失败', duration: 2000 });
      return;
    }
    const context = getContext(this) as common.UIAbilityContext;
    const ok = await openInWps(context, path, ext);
    if (!ok) {
      promptAction.showToast({ message: '未检测到 WPS，前往应用市场安装', duration: 2500 });
      const bundle = (isTablet() || is2in1()) ? WPS_PAD_BUNDLE : WPS_PHONE_BUNDLE;
      await openAppGalleryDetail(context, bundle);
    }
  }

  // 自更新 → 市场更新：前台 checkAppUpdate + showUpdateDialog（需已上架/真机/中国境内）。
  private async doCheckUpdate(): Promise<void> {
    const outcome = await checkForUpdate(getContext(this) as common.UIAbilityContext);
    promptAction.showToast({ message: outcome.message, duration: 2500 });
  }

  private async openMarket(): Promise<void> {
    const context = getContext(this) as common.UIAbilityContext;
    const ok = await openAppGalleryDetail(context, ownBundleName(context));
    if (!ok) {
      promptAction.showToast({ message: '无法打开应用市场', duration: 2000 });
    }
  }

  private showCoursewarePage(index: number): void {
    if (!this.courseware.goto(index)) {
      return;
    }
    this.coursewareIndex = index;
    this.coursewareCount = this.courseware.PageCount;
    if (this.coursewarePage !== null) {
      this.coursewarePage.release();
    }
    this.coursewarePage = this.pdfSource.renderPage(index);
    this.controller.BindDocument(this.courseware.Current);
    this.refreshScene();
  }

  private exitCourseware(): void {
    if (this.settings.behavior.isAutoSaveStrokesInPowerPoint) {
      this.saveCoursewareBundle();
    }
    this.mode = 'annotate';
    this.pptActive = false;
    if (this.coursewarePage !== null) {
      this.coursewarePage.release();
      this.coursewarePage = null;
    }
    this.pdfSource.close();
    this.controller.BindDocument(this.annotateDocument);
    this.refreshScene();
  }

  private handleAction(id: string): void {
    if (id === 'pen') {
      this.controller.Tool = InkTool.Pen;
      this.controller.CurrentAttributes.IsHighlighter = false;
      this.paletteHighlighter = false;
      this.paletteOpen = true;
      this.selectedId = 'pen';
    } else if (id === 'highlighter') {
      this.controller.Tool = InkTool.Highlighter;
      this.controller.CurrentAttributes.IsHighlighter = true;
      this.paletteHighlighter = true;
      this.paletteOpen = true;
      this.selectedId = 'highlighter';
    } else if (id === 'shapes') {
      this.shapeOpen = !this.shapeOpen;
    } else if (id === 'eraserCircle') {
      this.controller.Tool = InkTool.Eraser;
      this.controller.EraserMode = InkEraserMode.Area;
      this.settings.canvas.eraserType = 1;
      this.persistSettings();
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'eraserStroke') {
      this.controller.Tool = InkTool.Eraser;
      this.controller.EraserMode = InkEraserMode.WholeStroke;
      this.settings.canvas.eraserType = 2;
      this.persistSettings();
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'eraserLasso') {
      // G1 套索擦：会话级模式（不落 eraserType 设置；与 C# EraserPalette 套索入口一致）。
      this.controller.Tool = InkTool.Eraser;
      this.controller.EraserMode = InkEraserMode.Lasso;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'select') {
      this.controller.Tool = InkTool.Select;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'mouse') {
      this.enterMouseMode();
    } else if (id === 'fingerMode') {
      this.toggleFingerMode();
    } else if (id === 'exit') {
      this.requestExit();
    } else if (id === 'clear' || id === 'clearAndMouse') {
      if (this.controller.Document.Count > 0 && this.pendingYesNo !== id) {
        this.yesNoMessage = '清空当前墨迹？';
        this.pendingYesNo = id;
        this.yesNoOpen = true;
        return;
      }
      this.pendingYesNo = '';
      if (this.autoSave !== null &&
        this.controller.Document.Strokes.length >= this.settings.automation.minimumAutomationStrokeNumber) {
        this.autoSave.saveSnapshot(this.controller.Document.Strokes, 'clear');
      }
      this.controller.ClearSelection();
      this.controller.Clear();
      if (id === 'clearAndMouse') {
        this.controller.Tool = InkTool.Pan;
        this.selectedId = 'mouse';
      }
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'undo') {
      this.undoAction();
    } else if (id === 'redo') {
      this.redoAction();
    } else if (id === 'toggleCollapse') {
      animateMotion(this.getUIContext(), this.isMotionReduced(), MOTION_STANDARD, () => {
        this.collapsed = !this.collapsed;
      });
    } else if (id === 'fold') {
      animateMotion(this.getUIContext(), this.isMotionReduced(), MOTION_STANDARD, () => {
        this.collapsed = true;
      });
    } else if (id === 'gesture') {
      this.selectedId = 'gesture';
    } else if (id === 'tools') {
      this.paletteOpen = false;
      this.shapeOpen = false;
      this.toolsOpen = !this.toolsOpen;
      this.selectedId = 'tools';
    } else if (id === 'closeTools') {
      this.toolsOpen = false;
    } else if (id === 'desmos') {
      this.toolsOpen = false;
      openExternalUrl(getContext(this) as common.UIAbilityContext, 'https://www.desmos.com/calculator');
    } else if (id === 'update') {
      this.toolsOpen = false;
      this.updateOpen = true;
    } else if (id === 'screenshot') {
      this.toolsOpen = false;
      this.takeScreenshot();
    } else if (id === 'countdown') {
      this.toolsOpen = false;
      this.countdownOpen = true;
    } else if (id === 'lottery') {
      this.toolsOpen = false;
      this.lotteryOpen = true;
    } else if (id === 'replay') {
      this.toolsOpen = false;
      this.openReplay();
    } else if (id === 'settings') {
      this.toolsOpen = false;
      this.settingsOpen = true;
    } else if (id === 'guide') {
      this.toolsOpen = false;
      this.guideOpen = true;
    } else if (id === 'changelog') {
      this.toolsOpen = false;
      this.changelogOpen = true;
    } else if (id === 'courseware') {
      this.openCourseware();
    } else if (id === 'saveAnnotatedPdf') {
      this.toolsOpen = false;
      this.saveAnnotatedCoursewarePdf();
    } else if (id === 'pptWps') {
      this.toolsOpen = false;
      this.openPptInWps();
    } else if (id === 'pptExit') {
      this.exitCourseware();
    } else if (id === 'board') {
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
      if (this.boardOpen) { this.exitBoard(); } else { this.openBoard(); }
    }
  }
}
