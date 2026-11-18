// @parity src/InkCanvas.Platform/System/SoftwareLauncher.cs（外部链接替代）
import { common, Want } from '@kit.AbilityKit';

export function openExternalUrl(context: common.UIAbilityContext, url: string): void {
  const want: Want = { action: 'ohos.want.action.viewData', uri: url };
  context.startAbility(want).catch((e: Object) => {
    void e;
  });
}
