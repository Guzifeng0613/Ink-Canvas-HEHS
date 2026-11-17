// @parity src/InkCanvas.Core/Versioning/AppVersion.cs
export class AppVersion {
  Major: number;
  Minor: number;
  Patch: number;
  Build: number;

  constructor(major: number = 0, minor: number = 0, patch: number = 0, build: number = 0) {
    this.Major = major;
    this.Minor = minor;
    this.Patch = patch;
    this.Build = build;
  }

  static tryParse(text: string): AppVersion | null {
    if (text === null || text === undefined || text.trim().length === 0) {
      return null;
    }
    let value = text.trim();
    if (value.length > 0 && (value.charAt(0) === 'v' || value.charAt(0) === 'V')) {
      value = value.substring(1);
    }
    const parts = value.split('.');
    if (parts.length < 2 || parts.length > 4) {
      return null;
    }
    const numbers: number[] = [0, 0, 0, 0];
    for (let i = 0; i < parts.length; i++) {
      const part = parts[i].trim();
      if (!/^[+-]?[0-9]+$/.test(part)) {
        return null;
      }
      const n = Number(part);
      if (!isFinite(n) || n < 0 || n > 2147483647) {
        return null;
      }
      numbers[i] = Math.trunc(n);
    }
    return new AppVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
  }

  compareTo(other: AppVersion): number {
    if (this.Major !== other.Major) {
      return this.Major < other.Major ? -1 : 1;
    }
    if (this.Minor !== other.Minor) {
      return this.Minor < other.Minor ? -1 : 1;
    }
    if (this.Patch !== other.Patch) {
      return this.Patch < other.Patch ? -1 : 1;
    }
    if (this.Build !== other.Build) {
      return this.Build < other.Build ? -1 : 1;
    }
    return 0;
  }

  format(): string {
    return this.Build === 0
      ? this.Major + '.' + this.Minor + '.' + this.Patch
      : this.Major + '.' + this.Minor + '.' + this.Patch + '.' + this.Build;
  }
}

export interface VersionParseSummary {
  ok: boolean;
  major: number;
  minor: number;
  patch: number;
  build: number;
  formatted: string;
}

export function parseVersion(text: string): VersionParseSummary {
  const version = AppVersion.tryParse(text);
  if (version === null) {
    return { ok: false, major: 0, minor: 0, patch: 0, build: 0, formatted: '' };
  }
  return {
    ok: true,
    major: version.Major,
    minor: version.Minor,
    patch: version.Patch,
    build: version.Build,
    formatted: version.format()
  };
}

export function compareVersions(a: number[], b: number[]): number {
  const left = new AppVersion(a[0], a[1], a[2], a[3]);
  const right = new AppVersion(b[0], b[1], b[2], b[3]);
  return Math.sign(left.compareTo(right));
}
