// @parity src/InkCanvas.Core/Update/UpdatePolicy.cs（自更新 → 应用市场更新适配）
// 自更新迁移为市场更新：HarmonyOS 无静默自下载/自安装（三方无 INSTALL_BUNDLE 权限）。
// 合规路径 = updateManager.checkAppUpdate + showUpdateDialog（API 12+，@kit.AppGalleryKit），
// 或打开 AppGallery 详情页（action appdetail + store:// URI）让用户手动更新。
// 硬约束：应用已上架、本地版本低于在架、签名一致、前台调用、仅中国境内、不支持模拟器。
// 研究依据：docs/superpowers/research/harmony/14-wps-market-integration.md §3。
import { common, Want } from '@kit.AbilityKit';
import { updateManager } from '@kit.AppGalleryKit';
import { BusinessError, deviceInfo } from '@kit.BasicServicesKit';
import { logWarn } from '../diagnostics/LogService';

export function ownBundleName(context: common.UIAbilityContext): string {
  return context.abilityInfo.bundleName;
}

// 打开应用市场详情页（Deep Linking，WPS 官网生产使用的 store:// 形式）。
export async function openAppGalleryDetail(context: common.UIAbilityContext, bundleName: string): Promise<boolean> {
  try {
    const want: Want = {
      action: 'ohos.want.action.appdetail',
      uri: 'store://appgallery.huawei.com/app/detail?id=' + bundleName
    };
    await context.startAbility(want);
    return true;
  } catch (err) {
    logWarn('appgallery detail failed: ' + (err as BusinessError).code);
    return false;
  }
}

export interface UpdateCheckOutcome {
  status: string;
  message: string;
}

// 前台检查更新；有新版则弹市场升级框（文案不可自定义，下载安装由市场接管）。失败静默降级。
export async function checkForUpdate(context: common.UIAbilityContext): Promise<UpdateCheckOutcome> {
  if (deviceInfo.sdkApiVersion < 12 || !canIUse('SystemCapability.AppGalleryService.Distribution.Update')) {
    return { status: 'unsupported', message: '此设备不支持应用市场更新检查' };
  }
  try {
    const result = await updateManager.checkAppUpdate(context);
    if (result.updateAvailable === updateManager.UpdateAvailableCode.LATER_VERSION_EXIST) {
      const code = await updateManager.showUpdateDialog(context);
      if (code === updateManager.ShowUpdateResultCode.SHOW_DIALOG_SUCCESS) {
        return { status: 'update', message: '发现新版本，已弹出更新提示' };
      }
      return { status: 'update', message: '发现新版本，更新提示弹出失败' };
    }
    return { status: 'latest', message: '已是最新版本' };
  } catch (err) {
    const e = err as BusinessError;
    logWarn('checkAppUpdate failed: ' + e.code);
    return { status: 'error', message: '更新检查失败（' + e.code + '）：需已上架/前台/中国境内，模拟器不支持' };
  }
}