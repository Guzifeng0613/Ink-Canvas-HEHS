// @parity src/InkCanvas.Features/Ppt/PptInteropService.cs（WPS COM → 鸿蒙 WPS 文件打开适配）
// PPT 放映对接 WPS 鸿蒙版：无 COM/无公开放映 intent（调研 14 §2.3 按不存在处理），
// 走系统标准文件打开隐式 Want（viewData + fileUri + UTD + FLAG_AUTH_READ_URI_PERMISSION）。
// WPS bundleName（官方 harmony.wps.cn JS 核验）：手机 cn.wps.mobileoffice.hap / Pad cn.wps.office.hap。
// 放映控制标记不可用；.ppsx 自动放映为推断（未验证）。安装检测靠 startAbility catch 16000019。
// 研究依据：docs/superpowers/research/harmony/14-wps-market-integration.md §2。
import { common, Want, wantConstant } from '@kit.AbilityKit';
import { fileUri } from '@kit.CoreFileKit';
import { BusinessError } from '@kit.BasicServicesKit';
import { is2in1, isTablet } from '../device/DeviceService';

export const WPS_PHONE_BUNDLE: string = 'cn.wps.mobileoffice.hap';
export const WPS_PAD_BUNDLE: string = 'cn.wps.office.hap';

// PPT 相关 UTD（上游 uniform-data-type-list 预置列表）。
export function presentationUtd(ext: string): string {
  const e = ext.toLowerCase();
  if (e === 'ppt') {
    return 'com.microsoft.powerpoint.ppt';
  }
  if (e === 'ppsx') {
    return 'org.openxmlformats.presentationml.slideshow';
  }
  if (e === 'dps') {
    return 'com.kingsoft.office.presentation.dps';
  }
  return 'org.openxmlformats.presentationml.presentation';
}

// 标准文件打开 Want；bundleName 留空走系统选择框（最稳健），指定则限定包内隐式匹配（abilityName 必留空）。
// 返回 0 成功；否则 BusinessError.code（16000019 无匹配 ability / 16000001 指定 ability 不存在）。
export async function openInOfficeApp(
  context: common.UIAbilityContext,
  filePath: string,
  utd: string,
  bundleName: string | null
): Promise<number> {
  try {
    const uri = fileUri.getUriFromPath(filePath);
    const want: Want = {
      action: 'ohos.want.action.viewData',
      uri: uri,
      type: utd,
      flags: wantConstant.Flags.FLAG_AUTH_READ_URI_PERMISSION
    };
    if (bundleName !== null) {
      want.bundleName = bundleName;
    }
    await context.startAbility(want);
    return 0;
  } catch (err) {
    return (err as BusinessError).code;
  }
}

// 定向 WPS：按设备类型优先 Pad/PC 包或手机包，16000019/16000001 时回退另一包。
// 返回 true 已拉起；false 无可用 WPS（调用方引导安装）。其他错误码也返回 false。
export async function openInWps(
  context: common.UIAbilityContext,
  filePath: string,
  ext: string
): Promise<boolean> {
  const utd = presentationUtd(ext);
  const padFirst = is2in1() || isTablet();
  const order: string[] = padFirst
    ? [WPS_PAD_BUNDLE, WPS_PHONE_BUNDLE]
    : [WPS_PHONE_BUNDLE, WPS_PAD_BUNDLE];
  for (let i = 0; i < order.length; i++) {
    const code = await openInOfficeApp(context, filePath, utd, order[i]);
    if (code === 0) {
      return true;
    }
    if (code !== 16000019 && code !== 16000001) {
      return false;
    }
  }
  return false;
}

// 不指定应用的系统选择框打开（用户可选任意办公应用）。
export async function openWithChooser(
  context: common.UIAbilityContext,
  filePath: string,
  ext: string
): Promise<boolean> {
  const code = await openInOfficeApp(context, filePath, presentationUtd(ext), null);
  return code === 0;
}