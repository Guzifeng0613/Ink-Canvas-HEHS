// @parity src/InkCanvas.Features/Ppt/PptInteropService.cs（PDF 替代路径）
// PDF Kit：逐页渲染为 PixelMap，供课件背景 Image 使用；syscap 已核实。
import { pdfService } from '@kit.PDFKit';
import { image } from '@kit.ImageKit';

export class PdfPageSource {
  private doc: pdfService.PdfDocument | null = null;

  open(path: string): boolean {
    this.close();
    const doc = new pdfService.PdfDocument();
    const result = doc.loadDocument(path);
    if (result !== pdfService.ParseResult.PARSE_SUCCESS) {
      doc.releaseDocument();
      return false;
    }
    this.doc = doc;
    return true;
  }

  get PageCount(): number {
    return this.doc === null ? 0 : this.doc.getPageCount();
  }

  pageSize(index: number): number[] {
    if (this.doc === null || index < 0 || index >= this.doc.getPageCount()) {
      return [0, 0];
    }
    const page = this.doc.getPage(index);
    return [page.getWidth(), page.getHeight()];
  }

  renderPage(index: number): image.PixelMap | null {
    if (this.doc === null || index < 0 || index >= this.doc.getPageCount()) {
      return null;
    }
    const page = this.doc.getPage(index);
    try {
      return page.getPagePixelMap();
    } finally {
      page.release();
    }
  }

  close(): void {
    if (this.doc !== null) {
      this.doc.releaseDocument();
      this.doc = null;
    }
  }
}
