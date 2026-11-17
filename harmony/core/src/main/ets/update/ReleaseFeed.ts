// @parity src/InkCanvas.Core/Update/ReleaseFeed.cs
import { AppVersion } from '../versioning/AppVersion';

export class ReleaseInfo {
  Tag: string;
  Version: AppVersion;
  Name: string;
  Notes: string;
  PageUrl: string;
  InstallerUrl: string;
  InstallerSize: number;

  constructor(tag: string, version: AppVersion, name: string, notes: string,
    pageUrl: string, installerUrl: string, installerSize: number) {
    this.Tag = tag;
    this.Version = version;
    this.Name = name;
    this.Notes = notes;
    this.PageUrl = pageUrl;
    this.InstallerUrl = installerUrl;
    this.InstallerSize = installerSize;
  }
}

export interface ReleaseSummary {
  Tag: string;
  Version: number[];
  Name: string;
  Notes: string;
  PageUrl: string;
  InstallerUrl: string;
  InstallerSize: number;
}

function asDict(value: object): Record<string, Object> {
  return value as Record<string, Object>;
}

function str(obj: object, key: string): string {
  const value = asDict(obj)[key];
  return typeof value === 'string' ? value as string : '';
}

function boolProp(obj: object, key: string): boolean {
  return asDict(obj)[key] === true;
}

function tryParseJson(json: string): object | null {
  if (json === null || json === undefined || json.trim().length === 0) {
    return null;
  }
  try {
    return JSON.parse(json) as object;
  } catch (e) {
    return null;
  }
}

class InstallerPick {
  url: string | null = null;
  size: number = 0;
}

function pickInstaller(obj: object): InstallerPick {
  const pick = new InstallerPick();
  const assets = asDict(obj)['assets'];
  if (!Array.isArray(assets)) {
    return pick;
  }
  const list = assets as Object[];
  for (let i = 0; i < list.length; i++) {
    const asset = list[i];
    if (asset === null || typeof asset !== 'object') {
      continue;
    }
    const name = str(asset, 'name');
    if (name.length === 0 || !name.toLowerCase().endsWith('.exe')) {
      continue;
    }
    const url = str(asset, 'browser_download_url');
    if (url.length === 0) {
      continue;
    }
    const sizeValue = asDict(asset)['size'];
    const size = typeof sizeValue === 'number' && isFinite(sizeValue as number) ? Math.trunc(sizeValue as number) : 0;
    if (name.toLowerCase().indexOf('setup') >= 0) {
      pick.url = url;
      pick.size = size;
      return pick;
    }
    if (pick.url === null) {
      pick.url = url;
    }
    if (pick.size === 0) {
      pick.size = size;
    }
  }
  return pick;
}

function fromElement(obj: object): ReleaseInfo | null {
  const tag = str(obj, 'tag_name');
  const version = AppVersion.tryParse(tag);
  if (version === null) {
    return null;
  }
  const pick = pickInstaller(obj);
  if (pick.url === null) {
    return null;
  }
  return new ReleaseInfo(tag, version, str(obj, 'name'), str(obj, 'body'),
    str(obj, 'html_url'), pick.url, pick.size);
}

export function parseLatest(json: string): ReleaseInfo | null {
  const root = tryParseJson(json);
  if (root === null || Array.isArray(root) || typeof root !== 'object') {
    return null;
  }
  return fromElement(root);
}

export function parseNewestStable(json: string): ReleaseInfo | null {
  const root = tryParseJson(json);
  if (root === null || !Array.isArray(root)) {
    return null;
  }
  let best: ReleaseInfo | null = null;
  const list = root as Object[];
  for (let i = 0; i < list.length; i++) {
    const item = list[i];
    if (item === null || typeof item !== 'object' || Array.isArray(item)) {
      continue;
    }
    if (boolProp(item, 'draft') || boolProp(item, 'prerelease')) {
      continue;
    }
    const info = fromElement(item);
    if (info === null) {
      continue;
    }
    if (best === null || info.Version.compareTo(best.Version) > 0) {
      best = info;
    }
  }
  return best;
}

function toSummary(info: ReleaseInfo | null): ReleaseSummary | null {
  if (info === null) {
    return null;
  }
  return {
    Tag: info.Tag,
    Version: [info.Version.Major, info.Version.Minor, info.Version.Patch, info.Version.Build],
    Name: info.Name,
    Notes: info.Notes,
    PageUrl: info.PageUrl,
    InstallerUrl: info.InstallerUrl,
    InstallerSize: info.InstallerSize
  };
}

export function parseLatestSummary(json: string): ReleaseSummary | null {
  return toSummary(parseLatest(json));
}

export function parseNewestStableSummary(json: string): ReleaseSummary | null {
  return toSummary(parseNewestStable(json));
}
