// @parity src/InkCanvas.App/App.xaml.cs
import { AbilityConstant, UIAbility, Want } from '@kit.AbilityKit';
import { window } from '@kit.ArkUI';
import { hilog } from '@kit.PerformanceAnalysisKit';

export default class EntryAbility extends UIAbility {
  onCreate(want: Want, launchParam: AbilityConstant.LaunchParam): void {
    hilog.info(0x0000, 'InkCanvas', 'onCreate');
    const mode = want.parameters !== undefined ? want.parameters['mode'] : undefined;
    AppStorage.setOrCreate('launchMode', typeof mode === 'string' ? mode : 'annotate');
  }

  onWindowStageCreate(windowStage: window.WindowStage): void {
    windowStage.loadContent('pages/Index', (err) => {
      if (err.code) {
        hilog.error(0x0000, 'InkCanvas', 'loadContent failed: %{public}s', JSON.stringify(err));
      }
    });
    try {
      const main = windowStage.getMainWindowSync();
      main.setWindowLayoutFullScreen(true);
      main.setWindowSystemBarEnable([]);
    } catch (e) {
      hilog.warn(0x0000, 'InkCanvas', 'immersive setup failed: %{public}s', String(e));
    }
    try {
      const main = windowStage.getMainWindowSync();
      main.setWindowLayoutFullScreen(true);
      main.setWindowSystemBarEnable([]);
    } catch (e) {
      hilog.warn(0x0000, 'InkCanvas', 'immersive setup failed: %{public}s', String(e));
    }
  }
}