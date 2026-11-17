// @parity src/InkCanvas.Core/Update/UpdatePolicy.cs
export interface StartupPolicyInput {
  isAutoUpdate: boolean;
  isAutoUpdateWithSilence: boolean;
  startTime: string;
  endTime: string;
}

function tryMinutes(text: string): number | null {
  if (text === null || text === undefined || text.trim().length === 0) {
    return null;
  }
  const parts = text.trim().split(':');
  if (parts.length !== 2) {
    return null;
  }
  if (!/^[0-9]+$/.test(parts[0]) || !/^[0-9]+$/.test(parts[1])) {
    return null;
  }
  const hour = Number(parts[0]);
  const minute = Number(parts[1]);
  if (hour < 0 || hour > 23 || minute < 0 || minute > 59) {
    return null;
  }
  return hour * 60 + minute;
}

export function mayInstallSilently(settings: StartupPolicyInput, hour: number, minute: number): boolean {
  if (settings === null || settings === undefined || !settings.isAutoUpdate || !settings.isAutoUpdateWithSilence) {
    return false;
  }
  const start = tryMinutes(settings.startTime);
  const end = tryMinutes(settings.endTime);
  if (start === null || end === null) {
    return false;
  }
  if (start === end) {
    return true;
  }
  const minutes = hour * 60 + minute;
  return start < end
    ? minutes >= start && minutes < end
    : minutes >= start || minutes < end;
}
