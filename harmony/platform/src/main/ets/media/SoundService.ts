// @parity src/InkCanvas.App/Views/CountdownWindow.cs（PlayFinishSound 适配）
// 倒计时结束提示音：播放打包 rawfile/TimerDownNotice.wav（与 WinUI3 同一资产）。
// 尽力而为——音频失败绝不影响计时器（与 C# try/catch 静默一致）。
import { media } from '@kit.MediaKit';
import { resourceManager } from '@kit.LocalizationKit';
import type { common } from '@kit.AbilityKit';
import { logWarn } from '../diagnostics/LogService';

export async function playCountdownFinish(context: common.UIAbilityContext): Promise<void> {
  let player: media.AVPlayer | null = null;
  let rawFd: resourceManager.RawFileDescriptor | null = null;
  try {
    rawFd = await context.resourceManager.getRawFd('TimerDownNotice.wav');
    player = await media.createAVPlayer();
    const p = player;
    const fd = rawFd;
    const cleanup = (): void => {
      try {
        p.release();
      } catch (e) {
        void e;
      }
      try {
        context.resourceManager.closeRawFd('TimerDownNotice.wav');
      } catch (e) {
        void e;
      }
    };
    p.on('stateChange', (state: media.AVPlayerState) => {
      if (state === 'initialized') {
        p.prepare().catch((e: Object) => {
          void e;
          cleanup();
        });
      } else if (state === 'prepared') {
        p.play().catch((e: Object) => {
          void e;
          cleanup();
        });
      } else if (state === 'completed' || state === 'error' || state === 'stopped') {
        cleanup();
      }
    });
    p.fdSrc = { fd: fd.fd, offset: fd.offset, length: fd.length };
  } catch (e) {
    logWarn('countdown finish sound unavailable: ' + String(e));
    if (player !== null) {
      try {
        player.release();
      } catch (e2) {
        void e2;
      }
    }
    if (rawFd !== null) {
      try {
        context.resourceManager.closeRawFd('TimerDownNotice.wav');
      } catch (e3) {
        void e3;
      }
    }
  }
}