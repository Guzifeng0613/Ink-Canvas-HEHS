// @parity src/InkCanvas.App/Views/CountdownWindow.cs（顶层置顶窗 → 鸿蒙画中画悬浮窗）
// 顶层置顶的 HarmonyOS 原生替代：PiP 画中画（PiPWindow，API 11；typeNode+customUI API 12）。
// 三方拿不到 TYPE_FLOAT（SYSTEM_FLOAT_WINDOW 为 system_basic 受限权限），PiP 是唯一无需权限的跨应用置顶路径。
// 圆环+数字画在 customUIController（BuilderNode）层，surface 留空——空 surface 显示/触摸/后台保活需真机核对。
// 研究依据：docs/superpowers/research/harmony/15-floating-window-motion.md §2。
import { PiPWindow, typeNode, NodeController, BuilderNode, FrameNode, UIContext } from '@kit.ArkUI';
import { common } from '@kit.AbilityKit';
import { deviceInfo } from '@kit.BasicServicesKit';
import { logWarn } from '../diagnostics/LogService';

export class CountdownPiPParams {
  display: string = '00:00';
  ring: number = 1;
  finished: boolean = false;
}

@Builder
function buildCountdownPiP(p: CountdownPiPParams) {
  Stack({ alignContent: Alignment.Center }) {
    Progress({ value: p.ring * 100, total: 100, type: ProgressType.Ring })
      .width('80%')
      .height('80%')
      .color(p.finished ? '#FFE64545' : '#FF0067C0')
      .backgroundColor('#FFEAEAEA')
      .style({ strokeWidth: 10, enableSmoothEffect: true })
    Text(p.display)
      .fontSize(40)
      .fontWeight(FontWeight.Bold)
      .fontColor(Color.White)
  }
  .width('100%')
  .height('100%')
  .backgroundColor('#CC1A1A2E')
}

class CountdownPiPNodeController extends NodeController {
  private node: BuilderNode<[CountdownPiPParams]> | null = null;
  private params: CountdownPiPParams;

  constructor(params: CountdownPiPParams) {
    super();
    this.params = params;
  }

  makeNode(uiContext: UIContext): FrameNode | null {
    this.node = new BuilderNode<[CountdownPiPParams]>(uiContext);
    this.node.build(wrapBuilder<[CountdownPiPParams]>(buildCountdownPiP), this.params);
    return this.node.getFrameNode();
  }

  update(params: CountdownPiPParams): void {
    this.params = params;
    if (this.node !== null) {
      this.node.update(params);
    }
  }

  dispose(): void {
    if (this.node !== null) {
      this.node.dispose();
      this.node = null;
    }
  }
}

export class CountdownPiPService {
  private controller: PiPWindow.PiPController | null = null;
  private nodeController: CountdownPiPNodeController | null = null;
  private contentNode: typeNode.XComponent | null = null;
  private onPauseToggle: (() => void) | null = null;
  private onStopped: (() => void) | null = null;
  private active: boolean = false;

  // 设备/版本门槛：API 12+（typeNode+customUI）、SessionManager syscap、设备 PiP 开关。
  static isSupported(): boolean {
    if (deviceInfo.sdkApiVersion < 12) {
      return false;
    }
    if (!canIUse('SystemCapability.Window.SessionManager')) {
      return false;
    }
    try {
      return PiPWindow.isPiPEnabled();
    } catch (e) {
      void e;
      return false;
    }
  }

  get IsActive(): boolean {
    return this.active;
  }

  // 必须前台调用 startPiP（后台被系统禁止）；setAutoStartEnabled 让退桌面时自动拉起。
  async start(
    uiContext: UIContext,
    params: CountdownPiPParams,
    onPauseToggle: () => void,
    onStopped: () => void
  ): Promise<boolean> {
    if (!CountdownPiPService.isSupported() || this.active) {
      return false;
    }
    try {
      const xcController = new XComponentController();
      this.contentNode = typeNode.createNode(uiContext, 'XComponent', {
        type: XComponentType.SURFACE,
        controller: xcController
      });
      this.nodeController = new CountdownPiPNodeController(params);
      this.onPauseToggle = onPauseToggle;
      this.onStopped = onStopped;
      const config: PiPWindow.PiPConfiguration = {
        context: uiContext.getHostContext() as common.UIAbilityContext,
        componentController: xcController,
        templateType: PiPWindow.PiPTemplateType.VIDEO_PLAY,
        contentWidth: 400,
        contentHeight: 400,
        customUIController: this.nodeController
      };
      const pip = await PiPWindow.create(config, this.contentNode);
      this.controller = pip;
      pip.on('stateChange', (state: PiPWindow.PiPState, reason: string) => {
        void reason;
        if (state === PiPWindow.PiPState.STOPPED || state === PiPWindow.PiPState.ERROR) {
          this.active = false;
          this.disposeNode();
          if (this.onStopped !== null) {
            this.onStopped();
          }
        }
      });
      // 复用系统播放/暂停控件为倒计时 暂停/继续（自定义层触摸未验证，交互走系统控制面板）。
      pip.on('controlEvent', (c: PiPWindow.ControlEventParam) => {
        if (c.controlType === PiPWindow.PiPControlType.VIDEO_PLAY_PAUSE && this.onPauseToggle !== null) {
          this.onPauseToggle();
        }
      });
      pip.setAutoStartEnabled(true);
      await pip.startPiP();
      this.active = true;
      return true;
    } catch (e) {
      logWarn('countdown PiP start failed: ' + String(e));
      this.disposeNode();
      this.active = false;
      return false;
    }
  }

  update(params: CountdownPiPParams): void {
    if (this.nodeController !== null) {
      this.nodeController.update(params);
    }
  }

  async stop(): Promise<void> {
    if (this.controller !== null) {
      try {
        await this.controller.stopPiP();
      } catch (e) {
        void e;
      }
    }
    this.active = false;
    this.disposeNode();
  }

  private disposeNode(): void {
    if (this.nodeController !== null) {
      this.nodeController.dispose();
      this.nodeController = null;
    }
    this.contentNode = null;
    this.controller = null;
  }
}