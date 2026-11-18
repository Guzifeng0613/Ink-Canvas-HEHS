// @parity src/InkCanvas.App/Services/ShellController.cs
import { InkCanvasSurface, CanvasInkRenderer, pointerKind, PdfPageSource, StorageService, AutoSaveService, captureComponentToPng, openExternalUrl } from '@inkcanvas/platform';
import { FloatingBar, PenPalette, ShapePicker } from '@inkcanvas/controls';
import {
  InkController,
  PointerSample,
  InkTool,
  InkEraserMode,
  buildScene,
  Invalidation
} from '@inkcanvas/ink';
import type { SceneStrokeInput, SceneSelectionInput } from '@inkcanvas/ink';
import { InkStroke, InkColor, InkDocument, parseSettings, stringifySettings, createDefaultSettings } from '@inkcanvas/core';
import type { ShapeKind, StrokePointInput, AppSettings } from '@inkcanvas/core';
import { CoursewareSession, CountdownEngine, LotteryEngine, BoardDocument, BoardBackground } from '@inkcanvas/features';
import type { BoardAsset } from '@inkcanvas/features';
import { ReplayClock, InkReplaySession } from '@inkcanvas/core';
import { picker, fileIo } from '@kit.CoreFileKit';
import { KeyCode } from '@kit.InputKit';
import { common } from '@kit.AbilityKit';
import { util } from '@kit.ArkTS';
import { image } from '@kit.ImageKit';

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
  @State lotteryOpen: boolean = false;
  @State lotteryNames: string = '';
  @State lotteryResult: string = '';
  @State lotteryNoRepeat: boolean = false;
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
  @State windowWidth: number = 0;
  @State windowHeight: number = 0;
  private renderer: CanvasInkRenderer | null = null;
  private controller: InkController = new InkController();
  private annotateDocument: InkDocument = new InkDocument();
  private pdfSource: PdfPageSource = new PdfPageSource();
  private courseware: CoursewareSession = new CoursewareSession();
  private storage: StorageService | null = null;
  private countdown: CountdownEngine = new CountdownEngine();
  private lottery: LotteryEngine = new LotteryEngine();
  private countdownTimer: number = -1;
  private replayTimer: number = -1;
  private replayClock: ReplayClock | null = null;
  private replaySession: InkReplaySession | null = null;
  private settings: AppSettings = createDefaultSettings();
  private autoSave: AutoSaveService | null = null;
  private board: BoardDocument = new BoardDocument();
  private boardGridSettings: RenderingContextSettings = new RenderingContextSettings(true);
  private boardGridContext: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.boardGridSettings);
  private pendingYesNo: string = '';
  private mediaPaths: Map<string, string> = new Map<string, string>();

  aboutToDisappear(): void {
    this.stopCountdownTimer();
    this.stopReplayTimer();
  }

  aboutToAppear(): void {
    this.storage = new StorageService(getContext(this).filesDir);
    this.controller.Document = this.annotateDocument;
    this.controller.Invalidated = () => {
      this.refreshScene();
    };
    this.controller.CurrentAttributes.BaseWidth = 2.5;
    const settingsText = this.storage.readText('Settings.json');
    if (settingsText !== null) {
      this.settings = parseSettings(settingsText);
    }
    this.applySettings();
    const launchMode = AppStorage.get<string>('launchMode');
    if (launchMode === 'board') { this.openBoard(); }
    this.autoSave = new AutoSaveService(this.storage, 'Auto Saved - Strokes', true, this.settings.automation.autoDelSavedFilesDaysThreshold);
  }

  private applySettings(): void {
    this.dark = this.settings.appearance.theme === 2;
    this.widthMenu = this.settings.canvas.inkWidth * 2;
    this.alpha = this.settings.canvas.inkAlpha;
    this.taperIndex = this.settings.canvas.inkStyle;
    this.controller.CurrentAttributes.BaseWidth = this.settings.canvas.inkWidth;
    this.controller.CurrentAttributes.HighlighterAlpha = this.settings.canvas.inkAlpha;
    this.controller.CurrentAttributes.TaperKind = this.settings.canvas.inkStyle === 1 ? 2 : (this.settings.canvas.inkStyle === 2 ? 0 : 1);
    this.applyGestures();
  }

  private persistSettings(): void {
    if (this.storage === null) {
      return;
    }
    this.settings.appearance.theme = this.dark ? 2 : 1;
    this.settings.canvas.inkWidth = this.widthMenu / 2;
    this.settings.canvas.inkAlpha = this.alpha;
    this.settings.canvas.inkStyle = this.taperIndex;
    this.storage.writeTextAtomic('Settings.json', stringifySettings(this.settings));
  }

  private async takeScreenshot(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    const path = this.storage.fullPathPublic('screenshots/shot-' + Date.now() + '.png');
    await captureComponentToPng('inkRoot', path);
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
        ForEach(this.board.Current.Images, (asset: BoardAsset) => {
          this.BoardImage(asset)
        }, (asset: BoardAsset) => asset.Id + ':' + this.boardImagesVersion)
        ForEach(this.board.Current.Media, (asset: BoardAsset) => {
          this.BoardVideo(asset)
        }, (asset: BoardAsset) => asset.Id + ':' + this.boardImagesVersion)
      }

      if (this.mode === 'courseware' && this.coursewarePage !== null) {
        Image(this.coursewarePage)
          .objectFit(ImageFit.Contain)
          .width('100%')
          .height('100%')
      }

      InkCanvasSurface({
        onRendererReady: (renderer: CanvasInkRenderer) => {
          this.renderer = renderer;
          this.renderer.resize(this.windowWidth, this.windowHeight);
          this.refreshScene();
        }
      })

      if (this.mode === 'courseware') {
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
            Text((this.coursewareIndex + 1) + ' / ' + this.coursewareCount)
              .fontSize(14)
              .fontColor(Color.White)
              .padding(8)
              .backgroundColor('#99000000')
              .borderRadius(6)
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

      if (this.paletteOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          PenPalette({
            highlighter: this.paletteHighlighter,
            dark: this.dark,
            lightColorScheme: !this.dark,
            widthMenu: this.widthMenu,
            alpha: this.alpha,
            taperIndex: this.taperIndex,
            currentPacked: this.currentPacked,
            onColor: (packed: number) => {
              this.currentPacked = packed;
              this.controller.CurrentAttributes.Color = InkColor.fromPackedArgb(packed);
              this.paletteOpen = false;
            },
            onWidth: (stored: number) => {
              this.widthMenu = stored * 2;
              this.controller.CurrentAttributes.BaseWidth = stored;
              this.persistSettings();
            },
            onAlpha: (value: number) => {
              this.alpha = value;
              this.controller.CurrentAttributes.HighlighterAlpha = value;
              this.persistSettings();
            },
            onTaper: (kind: number) => {
              this.taperIndex = kind === 1 ? 0 : (kind === 2 ? 1 : 2);
              this.controller.CurrentAttributes.TaperKind = kind;
              this.persistSettings();
            },
            onToggleScheme: () => {
              this.dark = !this.dark;
            },
            onClose: () => {
              this.paletteOpen = false;
            }
          }).margin({ bottom: 170 })
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
            onSelect: (kind: number, longPress: boolean) => {
              this.selectedShape = kind;
              this.controller.SelectShape(kind as ShapeKind, longPress);
              if (!longPress) {
                this.shapeOpen = false;
                this.selectedId = 'shapes';
              }
            }
          }).margin({ bottom: 170 })
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.countdownOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 12 }) {
            Text(this.formatCountdown(this.countdownDisplay))
              .fontSize(48)
              .fontColor(this.dark ? Color.White : Color.Black)
            Row({ space: 12 }) {
              Text(this.countdownRunning ? '暂停' : '开始')
                .fontSize(16).fontColor(Color.White).padding(10)
                .backgroundColor('#FF0067C0').borderRadius(6)
                .onClick(() => {
                  this.countdown.startPause(Date.now());
                  this.countdownRunning = this.countdown.IsRunning && !this.countdown.IsPaused;
                  if (this.countdown.IsRunning) { this.startCountdownTimer(); } else { this.stopCountdownTimer(); }
                })
              Text('重置')
                .fontSize(16).fontColor(Color.White).padding(10)
                .backgroundColor('#666666').borderRadius(6)
                .onClick(() => {
                  this.countdown.reset(Date.now());
                  this.countdownDisplay = this.countdown.Display;
                  this.countdownRing = this.countdown.Ring;
                  this.countdownRunning = this.countdown.IsRunning && !this.countdown.IsPaused;
                })
              Text('-1分').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
                .onClick(() => { this.countdown.configure(0, Math.max(0, Math.floor(this.countdown.ConfigSeconds / 60) - 1), this.countdown.ConfigSeconds % 60); this.countdownDisplay = this.countdown.ConfigSeconds; })
              Text('+1分').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
                .onClick(() => { this.countdown.configure(0, Math.floor(this.countdown.ConfigSeconds / 60) + 1, this.countdown.ConfigSeconds % 60); this.countdownDisplay = this.countdown.ConfigSeconds; })
            }
            Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
              .onClick(() => { this.countdownOpen = false; this.stopCountdownTimer(); })
          }
          .padding(24)
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(12)
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.lotteryOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Column({ space: 10 }) {
            Text('抽奖').fontSize(20).fontColor(this.dark ? Color.White : Color.Black)
            TextArea({ text: this.lotteryNames, placeholder: '每行一个名字（留空按人数抽号）' })
              .width(260).height(120)
              .onChange((value: string) => { this.lotteryNames = value; })
            Row({ space: 8 }) {
              Text(this.lotteryNoRepeat ? '不重复：开' : '不重复：关')
                .fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
                .onClick(() => { this.lotteryNoRepeat = !this.lotteryNoRepeat; })
              Text('抽选').fontSize(16).fontColor(Color.White).padding(10)
                .backgroundColor('#FF0067C0').borderRadius(6)
                .onClick(() => {
                  this.lottery.loadNames(this.lotteryNames);
                  this.lottery.NoRepeat = this.lotteryNoRepeat;
                  const picked = this.lottery.pick();
                  this.lotteryResult = picked === null ? '已抽完' : picked;
                })
            }
            Text(this.lotteryResult).fontSize(32).fontColor('#FF0067C0')
            Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
              .onClick(() => { this.lotteryOpen = false; })
          }
          .padding(24)
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(12)
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
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(12)
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.settingsOpen) {
        Stack({ alignContent: Alignment.Center }) {
          Scroll() {
            Column({ space: 10 }) {
              Text('设置').fontSize(20).fontColor(this.dark ? Color.White : Color.Black)
              Text('外观').fontSize(15).fontColor('#FF0067C0')
              Row() {
                Text('深色主题').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.dark })
                  .onChange((isOn: boolean) => { this.dark = isOn; this.persistSettings(); })
              }.width(300)
              Row() {
                Text('浮条文字').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.settings.appearance.isEnableDisPlayFloatBarText })
                  .onChange((isOn: boolean) => { this.settings.appearance.isEnableDisPlayFloatBarText = isOn; this.persistSettings(); })
              }.width(300)
              Text('画布').fontSize(15).fontColor('#FF0067C0')
              Row() {
                Text('笔宽 ' + this.widthMenu).fontSize(14).fontColor(this.dark ? Color.White : Color.Black).width(90)
                Slider({ value: this.widthMenu, min: 1, max: 30, step: 1 })
                  .layoutWeight(1)
                  .onChange((value: number) => { this.widthMenu = value; this.controller.CurrentAttributes.BaseWidth = value / 2; this.persistSettings(); })
              }.width(300)
              Row() {
                Text('荧光透明度 ' + this.alpha).fontSize(14).fontColor(this.dark ? Color.White : Color.Black).width(110)
                Slider({ value: this.alpha, min: 20, max: 240, step: 20 })
                  .layoutWeight(1)
                  .onChange((value: number) => { this.alpha = value; this.controller.CurrentAttributes.HighlighterAlpha = value; this.persistSettings(); })
              }.width(300)
              Text('手势').fontSize(15).fontColor('#FF0067C0')
              Row() {
                Text('双指缩放').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.settings.gesture.isEnableTwoFingerZoom })
                  .onChange((isOn: boolean) => { this.settings.gesture.isEnableTwoFingerZoom = isOn; this.applyGestures(); this.persistSettings(); })
              }.width(300)
              Row() {
                Text('双指平移').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.settings.gesture.isEnableTwoFingerTranslate })
                  .onChange((isOn: boolean) => { this.settings.gesture.isEnableTwoFingerTranslate = isOn; this.applyGestures(); this.persistSettings(); })
              }.width(300)
              Row() {
                Text('双指旋转').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.settings.gesture.isEnableTwoFingerRotation })
                  .onChange((isOn: boolean) => { this.settings.gesture.isEnableTwoFingerRotation = isOn; this.applyGestures(); this.persistSettings(); })
              }.width(300)
              Text('自动保存').fontSize(15).fontColor('#FF0067C0')
              Row() {
                Text('清空时自动保存').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.settings.automation.isAutoSaveStrokesAtClear })
                  .onChange((isOn: boolean) => { this.settings.automation.isAutoSaveStrokesAtClear = isOn; this.persistSettings(); })
              }.width(300)
              Row() {
                Text('定期清理').fontSize(14).fontColor(this.dark ? Color.White : Color.Black).layoutWeight(1)
                Toggle({ type: ToggleType.Switch, isOn: this.settings.automation.autoDelSavedFiles })
                  .onChange((isOn: boolean) => { this.settings.automation.autoDelSavedFiles = isOn; this.persistSettings(); })
              }.width(300)
              Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
                .onClick(() => { this.settingsOpen = false; })
            }
            .padding(20)
          }
          .width(360)
          .height(480)
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(12)
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.boardOpen) {
        Stack({ alignContent: Alignment.TopStart }) {
          Column({ space: 6 }) {
            Text('画板 ' + (this.boardIndex + 1) + '/' + this.boardPageCount).fontSize(14).fontColor(Color.White)
            Row({ space: 6 }) {
              Text('上一页').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.boardNavigate(-1); })
              Text('下一页').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.boardNavigate(1); })
              Text('新增').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.addPage(); this.syncBoard(); })
              Text('删除').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.board.removePage(this.boardIndex); this.syncBoard(); })
            }
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
            Row({ space: 6 }) {
              Text('插图').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.insertBoardImage(); })
              Text('保存').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.saveBoard(); })
              Text('打开').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.loadBoard(); })
              Text('退出画板').fontSize(12).fontColor(Color.White).padding(6).backgroundColor('#99000000').borderRadius(4)
                .onClick(() => { this.exitBoard(); })
            }
          }
          .padding(10)
          .margin({ left: 12, top: 12 })
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
                .onClick(() => { this.yesNoOpen = false; })
            }
          }
          .padding(24)
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(12)
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
            Text('关闭').fontSize(14).fontColor(this.dark ? Color.White : Color.Black)
              .onClick(() => { this.updateOpen = false; })
          }
          .padding(24)
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(12)
        }
        .width('100%').height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      if (this.toolsOpen) {
        Stack({ alignContent: Alignment.Bottom }) {
          Column({ space: 6 }) {
            this.ToolEntry('课件（PDF）', 'courseware')
            this.ToolEntry('截图', 'screenshot')
            this.ToolEntry('Desmos', 'desmos')
            this.ToolEntry('检查更新', 'update')
            this.ToolEntry('计时器', 'countdown')
            this.ToolEntry('抽奖', 'lottery')
            this.ToolEntry('回放', 'replay')
            this.ToolEntry('设置', 'settings')
            this.ToolEntry('指南', 'guide')
            this.ToolEntry('关闭', 'closeTools')
          }
          .padding(12)
          .backgroundColor(this.dark ? '#F21F1F1F' : '#F2F0F3F9')
          .borderRadius(8)
          .margin({ bottom: 170 })
        }
        .width('100%')
        .height('100%')
        .hitTestBehavior(HitTestMode.Transparent)
      }

      Stack({ alignContent: Alignment.Bottom }) {
        FloatingBar({
          dark: this.dark,
          showLabels: false,
          selectedId: this.selectedId,
          folded: this.collapsed,
          pptActive: this.pptActive,
          onAction: (id: string) => {
            this.handleAction(id);
          }
        }).margin({ bottom: 100 })
      }
      .width('100%')
      .height('100%')
      .hitTestBehavior(HitTestMode.Transparent)
    }
    .id('inkRoot')
    .focusable(true)
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
      }
    })
    .onTouch((event: TouchEvent) => {
      this.handleTouch(event);
    })
  }

  @Builder
  BoardVideo(asset: BoardAsset) {
    if (this.boardMediaPath(asset) !== '') {
      Video({ src: this.boardMediaPath(asset) })
        .controls(true)
        .objectFit(ImageFit.Fill)
        .position({ x: asset.Left, y: asset.Top })
        .width(asset.Width)
        .height(asset.Height)
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
    }
  }

  @Builder
  ToolEntry(label: string, id: string) {
    Text(label)
      .fontSize(14)
      .fontColor(this.dark ? Color.White : Color.Black)
      .width(160)
      .padding(8)
      .textAlign(TextAlign.Center)
      .onClick(() => {
        this.handleAction(id);
      })
  }

  private handleKey(event: KeyEvent): void {
    if (event.type !== KeyType.Down) {
      return;
    }
    const alt = event.getModifierKeyState !== undefined && event.getModifierKeyState(['alt']);
    const ctrl = event.getModifierKeyState !== undefined && event.getModifierKeyState(['ctrl']);
    const code = event.keyCode;
    if (ctrl && code === KeyCode.KEYCODE_Z) {
      this.controller.Undo();
    } else if (ctrl && code === KeyCode.KEYCODE_Y) {
      this.controller.Redo();
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
    try {
      const helper = new util.Base64Helper();
      const bytes = helper.decodeSync(asset.Data);
      const source = image.createImageSource(bytes.buffer as ArrayBuffer);
      const pixelMap = source.createPixelMapSync();
      source.release();
      return pixelMap;
    } catch (e) {
      return null;
    }
  }

  private async insertBoardImage(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    const photoPicker = new picker.PhotoViewPicker();
    const result = await photoPicker.select({ MIMEType: picker.PhotoViewMIMETypes.IMAGE_TYPE, maxSelectNumber: 1 });
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
    const buffer = new ArrayBuffer(stat.size);
    fileIo.readSync(file.fd, buffer);
    fileIo.closeSync(file);
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
      Transform: null
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
    this.controller.Document = this.board.Current.Document;
    this.controller.ClearSelection();
    this.boardOpen = true;
    this.mode = 'board';
    this.syncBoard();
    this.refreshScene();
  }

  private exitBoard(): void {
    this.boardOpen = false;
    this.mode = 'annotate';
    this.controller.Document = this.annotateDocument;
    this.controller.ClearSelection();
    this.refreshScene();
  }

  private syncBoard(): void {
    this.boardIndex = this.board.CurrentIndex;
    this.boardPageCount = this.board.PageCount;
    this.boardBackground = this.board.Background;
    this.controller.Document = this.board.Current.Document;
    this.controller.ClearSelection();
    this.refreshScene();
  }

  private boardNavigate(delta: number): void {
    this.board.navigate(delta);
    this.syncBoard();
  }

  private async saveBoard(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    const docPicker = new picker.DocumentViewPicker();
    const uris = await docPicker.save({ newFileNames: ['board.icink'] });
    if (uris.length === 0) {
      return;
    }
    const file = fileIo.openSync(uris[0], fileIo.OpenMode.CREATE | fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.TRUNC);
    fileIo.writeSync(file.fd, this.board.saveBoardJson());
    fileIo.closeSync(file);
  }

  private async loadBoard(): Promise<void> {
    if (this.storage === null) {
      return;
    }
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
      this.syncBoard();
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
      if (!this.countdown.IsRunning) {
        this.countdownRunning = false;
        this.stopCountdownTimer();
      }
    }, this.countdown.tickIntervalMs());
  }

  private stopCountdownTimer(): void {
    if (this.countdownTimer >= 0) {
      clearInterval(this.countdownTimer);
      this.countdownTimer = -1;
    }
  }

  private openReplay(): void {
    const strokes = this.controller.Document.Strokes;
    if (strokes.length === 0) {
      return;
    }
    this.replaySession = new InkReplaySession(strokes, 12);
    this.replayClock = new ReplayClock(this.replaySession.DurationMs);
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
    this.replayOpen = false;
    this.replaySession = null;
    this.replayClock = null;
    this.refreshScene();
  }

  private handleTouch(event: TouchEvent): void {
    const timeMs = event.timestamp / 1000000;
    const touches = event.changedTouches;
    for (let i = 0; i < touches.length; i++) {
      const touch = touches[i];
      const pressure = event.pressure > 0 ? event.pressure : 0.5;
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
        timeMs
      );
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

  private toSceneStroke(stroke: InkStroke): SceneStrokeInput {
    const points: StrokePointInput[] = [];
    for (let i = 0; i < stroke.Count; i++) {
      const point = stroke.Points[i];
      points.push({ X: point.X, Y: point.Y, Width: point.Width });
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
    if (this.replayOpen && this.replaySession !== null && this.replayClock !== null) {
      strokes = this.replaySession.visibleAt(this.replayClock.ElapsedMs);
    }
    for (let i = 0; i < strokes.length; i++) {
      committed.push(this.toSceneStroke(strokes[i]));
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
    this.renderer.render(buildScene(committed, active, selection), Invalidation.full());
  }

  private async openCourseware(): Promise<void> {
    if (this.storage === null) {
      return;
    }
    const docPicker = new picker.DocumentViewPicker();
    const uris = await docPicker.select({ maxSelectNumber: 1, fileSuffixFilters: ['.pdf'] });
    if (uris.length === 0) {
      return;
    }
    const path = this.storage.importUriToSandbox(uris[0], 'courseware.pdf');
    if (path === null || !this.pdfSource.open(path)) {
      return;
    }
    this.annotateDocument = this.controller.Document;
    this.courseware.open(this.pdfSource.PageCount);
    this.mode = 'courseware';
    this.pptActive = true;
    this.toolsOpen = false;
    this.showCoursewarePage(0);
  }

  private showCoursewarePage(index: number): void {
    if (!this.courseware.goto(index)) {
      return;
    }
    this.coursewareIndex = index;
    this.coursewareCount = this.courseware.PageCount;
    this.coursewarePage = this.pdfSource.renderPage(index);
    this.controller.Document = this.courseware.Current;
    this.controller.ClearSelection();
    this.refreshScene();
  }

  private exitCourseware(): void {
    this.mode = 'annotate';
    this.pptActive = false;
    this.coursewarePage = null;
    this.pdfSource.close();
    this.controller.Document = this.annotateDocument;
    this.controller.ClearSelection();
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
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'eraserStroke') {
      this.controller.Tool = InkTool.Eraser;
      this.controller.EraserMode = InkEraserMode.WholeStroke;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'select') {
      this.controller.Tool = InkTool.Select;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'mouse') {
      this.controller.Tool = InkTool.Pan;
      this.selectedId = id;
      this.paletteOpen = false;
      this.shapeOpen = false;
    } else if (id === 'clear' || id === 'clearAndMouse') {
      if (this.controller.Document.Count > 0 && this.pendingYesNo !== id) {
        this.yesNoMessage = '清空当前墨迹？';
        this.pendingYesNo = id;
        this.yesNoOpen = true;
        return;
      }
      this.pendingYesNo = '';
      if (this.autoSave !== null) {
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
      this.controller.Undo();
    } else if (id === 'redo') {
      this.controller.Redo();
    } else if (id === 'toggleCollapse') {
      this.collapsed = !this.collapsed;
    } else if (id === 'fold') {
      this.collapsed = true;
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
    } else if (id === 'courseware') {
      this.openCourseware();
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
