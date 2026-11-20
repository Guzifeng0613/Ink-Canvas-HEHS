// @parity src/InkCanvas.Features/Ppt/PptInkSession.cs（课件会话的纯逻辑部分）
// HarmonyOS 无 PPT COM：课件 = PDF 逐页 + 每页独立墨迹；manifest 与 WinUI3 PPT 存档同格式。
import {
  InkDocument,
  InkStroke,
  StrokeAttributes,
  parsePptManifest,
  stringifyPptManifest,
  parseIcink,
  stringifyIcink
} from '@inkcanvas/core';
import type { PptManifest, PptManifestSlide } from '@inkcanvas/core';

export interface CoursewarePageFile {
  File: string;
  Content: string;
}

export interface CoursewareBundle {
  Manifest: string;
  Pages: CoursewarePageFile[];
}

export class CoursewareSession {
  PageCount: number = 0;
  CurrentIndex: number = 0;
  private pages: Map<number, InkDocument> = new Map<number, InkDocument>();

  open(pageCount: number): void {
    this.pages = new Map<number, InkDocument>();
    this.PageCount = Math.max(0, pageCount);
    this.CurrentIndex = 0;
  }

  get Current(): InkDocument {
    let doc = this.pages.get(this.CurrentIndex);
    if (doc === undefined) {
      doc = new InkDocument();
      this.pages.set(this.CurrentIndex, doc);
    }
    return doc;
  }

  /** 指定页的墨迹文档（懒建；D8 批注写回按页枚举用）。 */
  pageDocument(index: number): InkDocument {
    let doc = this.pages.get(index);
    if (doc === undefined) {
      doc = new InkDocument();
      this.pages.set(index, doc);
    }
    return doc;
  }

  next(): boolean {
    if (this.CurrentIndex + 1 >= this.PageCount) {
      return false;
    }
    this.CurrentIndex += 1;
    return true;
  }

  previous(): boolean {
    if (this.CurrentIndex <= 0) {
      return false;
    }
    this.CurrentIndex -= 1;
    return true;
  }

  goto(index: number): boolean {
    if (index < 0 || index >= this.PageCount) {
      return false;
    }
    this.CurrentIndex = index;
    return true;
  }

  manifest(): PptManifest {
    const slides: PptManifestSlide[] = [];
    const keys: number[] = [];
    this.pages.forEach((doc: InkDocument, key: number) => {
      if (doc.Count > 0) {
        keys.push(key);
      }
    });
    keys.sort((a: number, b: number): number => a - b);
    for (let i = 0; i < keys.length; i++) {
      const index = keys[i];
      const slideId = index + 1;
      slides.push({ SlideId: slideId, SlideIndex: slideId, File: 'slide-' + slideId + '.icink' });
    }
    return {
      Version: 1,
      LastSlideIndex: this.PageCount > 0 ? this.CurrentIndex + 1 : null,
      LastShowPosition: null,
      Slides: slides
    };
  }

  saveBundle(): CoursewareBundle {
    const pages: CoursewarePageFile[] = [];
    const manifest = this.manifest();
    for (let i = 0; i < manifest.Slides.length; i++) {
      const slide = manifest.Slides[i];
      const doc = this.pages.get(slide.SlideIndex - 1);
      if (doc !== undefined) {
        pages.push({ File: slide.File, Content: stringifyIcink(doc.Strokes) });
      }
    }
    return { Manifest: stringifyPptManifest(manifest), Pages: pages };
  }

  loadBundle(bundle: CoursewareBundle, pageCount: number): void {
    this.open(pageCount);
    const manifest = parsePptManifest(bundle.Manifest);
    for (let i = 0; i < manifest.Slides.length; i++) {
      const slide = manifest.Slides[i];
      for (let p = 0; p < bundle.Pages.length; p++) {
        if (bundle.Pages[p].File === slide.File) {
          const doc = new InkDocument();
          doc.addRange(parseIcink(bundle.Pages[p].Content));
          this.pages.set(slide.SlideIndex - 1, doc);
        }
      }
    }
    if (manifest.LastSlideIndex !== null && manifest.LastSlideIndex >= 1 && manifest.LastSlideIndex <= pageCount) {
      this.CurrentIndex = manifest.LastSlideIndex - 1;
    }
  }
}

export interface CoursewareSummary {
  PageCount: number;
  Current: number;
  InkPages: number;
}

export function coursewareNavigate(pageCount: number, steps: number[]): CoursewareSummary {
  const session = new CoursewareSession();
  session.open(pageCount);
  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    if (step === 1) {
      session.next();
    } else if (step === -1) {
      session.previous();
    } else {
      session.goto(step);
    }
  }
  return { PageCount: session.PageCount, Current: session.CurrentIndex, InkPages: 0 };
}

export function coursewareManifestSummary(pageCount: number, current: number, inkPages: number[]): PptManifest {
  const session = new CoursewareSession();
  session.open(pageCount);
  for (let i = 0; i < inkPages.length; i++) {
    if (session.goto(inkPages[i])) {
      const stroke = new InkStroke(new StrokeAttributes());
      stroke.addPoint(0, 0);
      stroke.complete();
      session.Current.add(stroke);
    }
  }
  session.goto(current);
  return session.manifest();
}

// @parity src/InkCanvas.Features/Ppt/PptInkSession.cs（RecommendedPollInterval）
// C#：无事件订阅时宿主按此节奏轮询——放映中 200ms（低延迟墨迹切换）、空闲 1000ms。
// HarmonyOS 课件为 PDF 本地渲染、无 COM 附着；保留纯函数供宿主计时器对齐节奏语义（G17）。
export function recommendedPollIntervalMs(slideShowRunning: boolean): number {
  return slideShowRunning ? 200 : 1000;
}
