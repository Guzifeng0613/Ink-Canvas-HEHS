// @parity src/InkCanvas.Features/Persistence/Stores.cs (AutoSaveService)
import { StorageService } from '../storage/StorageService';
import { stringifyIcink } from '@inkcanvas/core';
import type { InkStroke } from '@inkcanvas/core';

export class AutoSaveService {
  private storage: StorageService;
  private rootDir: string;
  private daysThreshold: number;
  private enabled: boolean;

  constructor(storage: StorageService, rootDir: string, enabled: boolean, daysThreshold: number) {
    this.storage = storage;
    this.rootDir = rootDir;
    this.enabled = enabled;
    this.daysThreshold = Math.max(1, daysThreshold);
  }

  private stamp(now: Date): string {
    const pad = (n: number): string => n < 10 ? '0' + n : '' + n;
    return pad(now.getHours()) + pad(now.getMinutes()) + pad(now.getSeconds());
  }

  private dateDir(now: Date): string {
    const pad = (n: number): string => n < 10 ? '0' + n : '' + n;
    return now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate());
  }

  saveSnapshot(strokes: InkStroke[], reason: string): string | null {
    if (!this.enabled || strokes.length === 0) {
      return null;
    }
    const now = new Date();
    const safeReason = reason.replace(/[^a-zA-Z0-9_-]/g, '_');
    const relative = this.rootDir + '/' + this.dateDir(now) + '/' + this.stamp(now) + '-' + safeReason + '.icink';
    const ok = this.storage.writeTextAtomic(relative, stringifyIcink(strokes));
    return ok ? relative : null;
  }

  prune(): number {
    if (!this.enabled) {
      return 0;
    }
    const cutoff = Date.now() - this.daysThreshold * 24 * 60 * 60 * 1000;
    let removed = 0;
    const dirs = this.storage.list(this.rootDir);
    for (let i = 0; i < dirs.length; i++) {
      const dir = dirs[i];
      const files = this.storage.list(this.rootDir + '/' + dir);
      for (let f = 0; f < files.length; f++) {
        const relative = this.rootDir + '/' + dir + '/' + files[f];
        const text = this.storage.readText(relative);
        if (text === null) {
          continue;
        }
        const parsed = Date.parse(dir);
        if (!isNaN(parsed) && parsed < cutoff) {
          if (this.storage.delete(relative)) {
            removed++;
          }
        }
      }
    }
    return removed;
  }
}
