// 双层 Canvas 宿主：已提交层在下、活动层在上。平板与 2in1 使用同一组件。
import { CanvasInkRenderer } from './CanvasInkRenderer';

@Component
export struct InkCanvasSurface {
  private committedSettings: RenderingContextSettings = new RenderingContextSettings(true);
  private activeSettings: RenderingContextSettings = new RenderingContextSettings(true);
  private committedContext: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.committedSettings);
  private activeContext: CanvasRenderingContext2D = new CanvasRenderingContext2D(this.activeSettings);
  private readyCount: number = 0;
  onRendererReady: (renderer: CanvasInkRenderer) => void = (renderer: CanvasInkRenderer) => {
    void renderer;
  };

  build() {
    Stack() {
      Canvas(this.committedContext)
        .width('100%')
        .height('100%')
        .onReady(() => {
          this.checkReady();
        })
      Canvas(this.activeContext)
        .width('100%')
        .height('100%')
        .onReady(() => {
          this.checkReady();
        })
    }
    .width('100%')
    .height('100%')
  }

  private checkReady(): void {
    this.readyCount += 1;
    if (this.readyCount === 2) {
      this.onRendererReady(new CanvasInkRenderer(this.committedContext, this.activeContext));
    }
  }
}
