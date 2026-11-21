// @parity src/InkCanvas.Pdf/PdfAnnotationFile.cs（写回路径适配）
// D8 PDF 批注写回：每笔笔画一条标准 INK 批注（InkAnnotationInfo：inkPoints + lineColor，
// 宽度/透明度借基类 border.borderWidth / opacity），官方临时副本模式保存（文档不可同时读写）。
// 保真限制（调研 18 §3/§4）：单路径等宽、无 Nib/Taper/ShapeSource/LayerOrder/ObjectFrame——
// 无损可编辑往返由课件会话的 sidecar .icink（既有权威层）承担，PDF 内批注为「任何阅读器可见」层。
// API 全部 ≤12（基线 24 安全）；PDF Kit 仅中国大陆——canIUse 特性探测 + 失败降级（仅 sidecar）。
import { pdfService } from '@kit.PDFKit';
import { fileIo } from '@kit.CoreFileKit';
import { logWarn } from '../diagnostics/LogService';
import { InkStroke } from '@inkcanvas/core';
import { PdfPageGeometry } from '@inkcanvas/core';
import type { PdfPoint as LayoutPoint } from '@inkcanvas/core';

export interface AnnotatedPdfResult {
  Ok: boolean;
  Annotations: number;
  Limited: boolean;
}

/** 世界坐标墨迹 → PDF 用户空间点的换算上下文（页面 contain-fit 放置）。 */
export interface PagePlacement {
  // 页 DIP 尺寸
  DipWidth: number;
  DipHeight: number;
  // 页在窗口中的 contain-fit：世界点 → 页 DIP = ((x-offX)/scale, (y-offY)/scale)
  Scale: number;
  OffsetX: number;
  OffsetY: number;
}

/** 由窗口与页 DIP 尺寸计算 contain-fit 放置（objectFit.Contain 同式）。 */
export function containPlacement(windowWidth: number, windowHeight: number, dipWidth: number, dipHeight: number): PagePlacement {
  const scale = Math.min(windowWidth / dipWidth, windowHeight / dipHeight);
  return {
    DipWidth: dipWidth,
    DipHeight: dipHeight,
    Scale: scale,
    OffsetX: (windowWidth - dipWidth * scale) / 2,
    OffsetY: (windowHeight - dipHeight * scale) / 2
  };
}

/**
 * 把当前课件会话按页批注写入 PDF 副本（targetPath 沙箱路径）。
 * pages：页索引 → 世界坐标笔画；geometry：该页的 PDF 几何（Left/Bottom/Right/Top pt + 旋转）。
 * 官方模式：复制源到临时文件 → loadDocument(临时) → 逐页 addAnnotation → saveDocument(target)。
 */
export async function writeAnnotatedPdf(
  sourcePath: string,
  targetPath: string,
  pages: Map<number, InkStroke[]>,
  geometries: PdfPageGeometry[],
  placements: Map<number, PagePlacement>
): Promise<AnnotatedPdfResult> {
  if (!canIUse('SystemCapability.OfficeService.PDFService.Core')) {
    logWarn('PDF Kit unavailable (mainland-only capability); sidecar-only mode');
    return { Ok: false, Annotations: 0, Limited: false };
  }
  let temporary = '';
  try {
    // 官方过渡模式：源 → 临时副本 → 加载临时 → 保存到目标。
    temporary = targetPath + '.tmp-src.pdf';
    const source = fileIo.openSync(sourcePath, fileIo.OpenMode.READ_ONLY);
    const stat = fileIo.statSync(source.fd);
    const buffer = new ArrayBuffer(stat.size);
    fileIo.readSync(source.fd, buffer);
    fileIo.closeSync(source);
    const sink = fileIo.openSync(temporary, fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.CREATE | fileIo.OpenMode.TRUNC);
    fileIo.writeSync(sink.fd, buffer);
    fileIo.closeSync(sink);

    const doc = new pdfService.PdfDocument();
    if (doc.loadDocument(temporary) !== pdfService.ParseResult.PARSE_SUCCESS) {
      doc.releaseDocument();
      return { Ok: false, Annotations: 0, Limited: false };
    }
    let annotations = 0;
    let limited = false;
    const pageCount = doc.getPageCount();
    pages.forEach((strokes: InkStroke[], pageIndex: number): void => {
      if (pageIndex < 0 || pageIndex >= pageCount || strokes.length === 0) {
        return;
      }
      const geometry = geometries[pageIndex];
      const placement = placements.get(pageIndex);
      if (geometry === undefined || placement === undefined) {
        limited = true;
        return;
      }
      const page = doc.getPage(pageIndex);
      for (let s = 0; s < strokes.length; s++) {
        const stroke = strokes[s];
        if (stroke.Count === 0) {
          continue;
        }
        const info = new pdfService.InkAnnotationInfo();
        const inkPoints: pdfService.PdfPoint[] = [];
        for (let j = 0; j < stroke.Count; j++) {
          const world = stroke.Points[j];
          // 世界 → 页 DIP → PDF 用户空间（几何含旋转）。
          const dip: LayoutPoint = {
            X: (world.X - placement.OffsetX) / placement.Scale,
            Y: (world.Y - placement.OffsetY) / placement.Scale
          };
          const pdf = geometry.ToPdf(dip);
          const mapped = new pdfService.PdfPoint();
          mapped.x = pdf.X;
          mapped.y = pdf.Y;
          inkPoints.push(mapped);
        }
        info.inkPoints = inkPoints;
        // lineColor：ARGB 数值；透明度按荧光笔 alpha 映射 opacity。
        info.lineColor = stroke.Attributes.Color.packedArgb();
        info.opacity = stroke.Attributes.IsHighlighter
          ? Math.min(1, Math.max(0.08, stroke.Attributes.HighlighterAlpha / 255))
          : 1;
        const border = new pdfService.PdfBorder();
        border.borderWidth = stroke.Attributes.BaseWidth * 72 / 96 / placement.Scale;
        border.borderColor = stroke.Attributes.Color.packedArgb();
        info.border = border;
        info.content = 'Ink Canvas 批注';
        page.addAnnotation(info);
        annotations += 1;
      }
    });
    const saved = doc.saveDocument(targetPath);
    doc.releaseDocument();
    return { Ok: saved, Annotations: annotations, Limited: limited };
  } catch (e) {
    logWarn('annotated pdf write failed: ' + String(e));
    return { Ok: false, Annotations: 0, Limited: true };
  } finally {
    if (temporary.length > 0) {
      try {
        fileIo.unlinkSync(temporary);
      } catch (e) {
        void e;
      }
    }
  }
}

/** 页几何枚举（PDF pt + 旋转；HarmonyOS 无 UserUnit——按 1）。 */
export function readPdfGeometries(path: string): PdfPageGeometry[] | null {
  if (!canIUse('SystemCapability.OfficeService.PDFService.Core')) {
    return null;
  }
  const doc = new pdfService.PdfDocument();
  if (doc.loadDocument(path) !== pdfService.ParseResult.PARSE_SUCCESS) {
    doc.releaseDocument();
    return null;
  }
  const geometries: PdfPageGeometry[] = [];
  const count = doc.getPageCount();
  for (let i = 0; i < count; i++) {
    const page = doc.getPage(i);
    let left = 0;
    let bottom = 0;
    let width = page.getWidth();
    let height = page.getHeight();
    try {
      const crop = page.getBox(pdfService.BoxType.BOX_CROP);
      left = crop.left;
      bottom = crop.bottom;
      width = crop.right - crop.left;
      height = crop.top - crop.bottom;
    } catch (e) {
      // 回退整页尺寸。
      void e;
    }
    // RotationAngle 枚举值即度数（0/90/180/270）。
    const rotation = page.getRotation() as number;
    geometries.push(new PdfPageGeometry(left, bottom, left + width, bottom + height, rotation, 1));
  }
  doc.releaseDocument();
  return geometries;
}