// @parity src/InkCanvas.Core/Theming/ThemeResolver.cs
export enum AppTheme {
  Light = 0,
  Dark = 1
}

export function getMainWindowTheme(currentMode: number, usingWhiteboard: boolean, requestedThemeIsLight: boolean): number {
  if (currentMode !== 0) {
    return usingWhiteboard ? AppTheme.Light : AppTheme.Dark;
  }
  return requestedThemeIsLight ? AppTheme.Light : AppTheme.Dark;
}

export function getBoardTheme(usingWhiteboard: boolean): number {
  return usingWhiteboard ? AppTheme.Light : AppTheme.Dark;
}
