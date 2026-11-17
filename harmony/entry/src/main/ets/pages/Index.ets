import { MODULE as CORE_MODULE } from '@inkcanvas/core';

@Entry
@Component
struct Index {
  @State message: string = 'Ink Canvas HarmonyOS';

  build() {
    Column() {
      Text(this.message)
        .fontSize(24)
        .fontWeight(FontWeight.Medium)
      Text('core: ' + CORE_MODULE)
        .fontSize(14)
        .margin({ top: 8 })
    }
    .width('100%')
    .height('100%')
    .justifyContent(FlexAlign.Center)
  }
}