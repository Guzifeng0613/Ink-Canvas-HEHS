// Node 向量运行器专用：把 `@kit.ArkTS` 的 util.TextEncoder/TextDecoder 映射到 Node 等价实现。
// ArkTS API：TextEncoder.create('utf-8').encodeInto(text) → Uint8Array；
//            TextDecoder.create('utf-8').decodeToString(bytes) → string。
// 真机侧由 @kit.ArkTS 提供；本文件不参与 HAP 构建（纯 JS，无类型注解）。
import { TextEncoder as NodeEncoder, TextDecoder as NodeDecoder } from 'node:util';
import { webcrypto } from 'node:crypto';

class ArkTextEncoder {
  constructor() {
    this.encoder = new NodeEncoder();
  }
  encodeInto(input) {
    return this.encoder.encode(input);
  }
}

class ArkTextDecoder {
  constructor() {
    this.decoder = new NodeDecoder('utf-8');
  }
  decodeToString(input) {
    return this.decoder.decode(input);
  }
}

export const util = {
  TextEncoder: {
    create: () => new ArkTextEncoder()
  },
  TextDecoder: {
    create: () => new ArkTextDecoder()
  },
  generateRandomUUID: () => webcrypto.randomUUID()
};