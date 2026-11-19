// 手写笔手势订阅封装：@kit.Penkit 的 stylusInteraction（双击 doubleTap / 轻捏 squeeze）。
// 接口起始版本 5.1.1(19)，SystemCapability.Stylus.StylusService，Stage 模型，无需任何权限。
// 声明依据：D:\HarmonyVM\DevEcoStudio\sdk\default\hms\ets\api\@hms.officeservice.stylusInteraction.d.ts
// （@kit.Penkit 经 hms\ets\kits\@kit.Penkit.d.ts 同名再导出 stylusInteraction）。
//
// HSP 缺失降级（实测运行时坑）：@kit.Penkit 由共享 HSP 提供，标准模拟器镜像不带该 HSP；
// 顶层静态 import 会在 ES module 实例化阶段、早于 canIUse/try-catch 执行时加载失败并 SIGABRT 闪退。
// 因此这里：① 仅用 import type 取类型，编译后擦除、零运行时 HSP 依赖；
// ② 运行期以常量字符串动态 import 懒加载，HSP 缺失时在 catch 永久标记 unavailable，后续 on/off 全部跳过；
// ③ hasSysCap 能力门控 + 调用 try/catch 双保险。设备不支持或异常时静默降级：不崩溃、不打错误级别日志干扰。
import type { stylusInteraction } from '@kit.Penkit';
import { BusinessError, Callback } from '@kit.BasicServicesKit';
import { hasSysCap, SYSCAP_STYLUS_SERVICE } from '../compat/Capabilities';

// 事件类型与 SDK 声明一致：DoubleTapEvent / SqueezeEvent 均只含 timestamp（ms，距系统启动）。
export type StylusDoubleTapEvent = stylusInteraction.DoubleTapEvent;
export type StylusSqueezeEvent = stylusInteraction.SqueezeEvent;

export type StylusDoubleTapCallback = (event: StylusDoubleTapEvent) => void;
export type StylusSqueezeCallback = (event: StylusSqueezeEvent) => void;

export type StylusGestureType = 'doubleTap' | 'squeeze';

// 运行期需要的 on/off 子集镜像 SDK 签名；事件字段以 SDK 声明为准（timestamp: number）。
interface StylusInteractionApi {
  on(type: 'doubleTap', receiver: Callback<StylusDoubleTapEvent>): void;
  on(type: 'squeeze', receiver: Callback<StylusSqueezeEvent>): void;
  off(type: 'doubleTap', receiver?: Callback<StylusDoubleTapEvent>): void;
  off(type: 'squeeze', receiver?: Callback<StylusSqueezeEvent>): void;
}

type LoadState = 'idle' | 'loading' | 'ready' | 'unavailable';

let loadState: LoadState = 'idle';
let interaction: StylusInteractionApi | null = null;
let loadPromise: Promise<StylusInteractionApi | null> | null = null;

// 懒加载 stylusInteraction；同一页面生命周期内只发起一次动态 import。
// HSP 缺失/能力不支持时永久返回 null，调用方全部走静默降级路径。
function loadInteraction(): Promise<StylusInteractionApi | null> {
  if (loadState === 'unavailable') {
    return Promise.resolve(null);
  }
  if (interaction !== null) {
    return Promise.resolve(interaction);
  }
  if (loadPromise !== null) {
    return loadPromise;
  }
  try {
    if (!hasSysCap(SYSCAP_STYLUS_SERVICE)) {
      loadState = 'unavailable';
      return Promise.resolve(null);
    }
  } catch (e) {
    void e;
    loadState = 'unavailable';
    return Promise.resolve(null);
  }
  loadState = 'loading';
  // 常量字符串动态 import：编译期自动入依赖树，无需改 build-profile runtimeOnly 配置。
  loadPromise = import('@kit.Penkit').then((ns) => {
    const loaded = ns.stylusInteraction as StylusInteractionApi | null;
    interaction = loaded;
    loadState = loaded !== null ? 'ready' : 'unavailable';
    return loaded;
  }).catch((err: BusinessError) => {
    void err;
    interaction = null;
    loadState = 'unavailable';
    return null;
  });
  return loadPromise;
}

// 手写笔手势订阅/释放句柄。on 与 off 成对调用；页面销毁时统一走 release()。
export class StylusBinding {
  private doubleTapReceiver: StylusDoubleTapCallback | null = null;
  private squeezeReceiver: StylusSqueezeCallback | null = null;
  private detached: boolean = false;

  on(type: StylusGestureType, cb: StylusDoubleTapCallback | StylusSqueezeCallback): void {
    if (loadState === 'unavailable' || this.detached) {
      return;
    }
    if (type === 'doubleTap') {
      this.doubleTapReceiver = cb as StylusDoubleTapCallback;
    } else {
      this.squeezeReceiver = cb as StylusSqueezeCallback;
    }
    void loadInteraction().then((api) => {
      // 加载完成前已 off/release，或 HSP 不可用：不再挂接。
      if (this.detached || api === null) {
        return;
      }
      try {
        if (type === 'doubleTap' && this.doubleTapReceiver !== null) {
          api.on('doubleTap', this.doubleTapReceiver);
        } else if (type === 'squeeze' && this.squeezeReceiver !== null) {
          api.on('squeeze', this.squeezeReceiver);
        }
      } catch (e) {
        void e;
      }
    });
  }

  off(type: StylusGestureType): void {
    if (type === 'doubleTap') {
      const receiver = this.doubleTapReceiver;
      this.doubleTapReceiver = null;
      this.detach('doubleTap', receiver);
    } else {
      const receiver = this.squeezeReceiver;
      this.squeezeReceiver = null;
      this.detach('squeeze', receiver);
    }
  }

  // 页面销毁：成对释放两类手势监听，避免泄漏。
  release(): void {
    this.detached = true;
    this.off('doubleTap');
    this.off('squeeze');
  }

  private detach(type: 'doubleTap' | 'squeeze',
    receiver: StylusDoubleTapCallback | StylusSqueezeCallback | null): void {
    if (receiver === null || interaction === null) {
      return;
    }
    try {
      if (type === 'doubleTap') {
        interaction.off('doubleTap', receiver as Callback<StylusDoubleTapEvent>);
      } else {
        interaction.off('squeeze', receiver as Callback<StylusSqueezeEvent>);
      }
    } catch (e) {
      void e;
    }
  }
}
