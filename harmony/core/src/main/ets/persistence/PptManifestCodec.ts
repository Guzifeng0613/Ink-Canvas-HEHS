// @parity src/InkCanvas.Features/Ppt/PptInkFileStore.cs (manifest 部分)
// PPT 墨迹 manifest：Version/LastSlideIndex/LastShowPosition/Slides[SlideId,SlideIndex,File]。
// 读取不校验 Version；Position 文本文件由平台层处理。
export interface PptManifestSlide {
  SlideId: number;
  SlideIndex: number;
  File: string;
}

export interface PptManifest {
  Version: number;
  LastSlideIndex: number | null;
  LastShowPosition: number | null;
  Slides: PptManifestSlide[];
}

function asDict(value: object): Record<string, Object> {
  return value as Record<string, Object>;
}

function numOrNull(value: Object | undefined): number | null {
  return typeof value === 'number' && isFinite(value as number) ? Math.trunc(value as number) : null;
}

function num(value: Object | undefined, fallback: number): number {
  return typeof value === 'number' && isFinite(value as number) ? Math.trunc(value as number) : fallback;
}

export function parsePptManifest(text: string): PptManifest {
  const root = JSON.parse(text) as object;
  if (root === null || typeof root !== 'object' || Array.isArray(root)) {
    return { Version: 1, LastSlideIndex: null, LastShowPosition: null, Slides: [] };
  }
  const dict = asDict(root);
  const slides: PptManifestSlide[] = [];
  const rawSlides = dict['Slides'];
  if (Array.isArray(rawSlides)) {
    const list = rawSlides as Object[];
    for (let i = 0; i < list.length; i++) {
      const item = list[i];
      if (item === null || typeof item !== 'object' || Array.isArray(item)) {
        continue;
      }
      const slide = asDict(item);
      const file = slide['File'];
      slides.push({
        SlideId: num(slide['SlideId'], 0),
        SlideIndex: num(slide['SlideIndex'], 0),
        File: typeof file === 'string' ? file as string : ''
      });
    }
  }
  return {
    Version: num(dict['Version'], 1),
    LastSlideIndex: numOrNull(dict['LastSlideIndex']),
    LastShowPosition: numOrNull(dict['LastShowPosition']),
    Slides: slides
  };
}

export function stringifyPptManifest(manifest: PptManifest): string {
  const slides: Object[] = [];
  for (let i = 0; i < manifest.Slides.length; i++) {
    const slide = manifest.Slides[i];
    slides.push({ SlideId: slide.SlideId, SlideIndex: slide.SlideIndex, File: slide.File });
  }
  return JSON.stringify({
    Version: manifest.Version,
    LastSlideIndex: manifest.LastSlideIndex,
    LastShowPosition: manifest.LastShowPosition,
    Slides: slides
  });
}

export function pptManifestRoundtrip(text: string): PptManifest {
  return parsePptManifest(stringifyPptManifest(parsePptManifest(text)));
}
