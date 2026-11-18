// 纯逻辑能力矩阵（可被 Node 向量验证，不 import 任何 @kit）。
export const SYSCAP_ARKUI_FULL: string = 'SystemCapability.ArkUI.ArkUI.Full';
export const SYSCAP_STYLUS_HANDWRITE: string = 'SystemCapability.Stylus.Handwrite';
export const SYSCAP_STYLUS_SERVICE: string = 'SystemCapability.Stylus.StylusService';
export const SYSCAP_INPUT_POINTER: string = 'SystemCapability.MultimodalInput.Input.Pointer';

export interface CapabilityMatrix {
  Api26: boolean;
  SystemMaterial: boolean;
  StylusBoost: boolean;
  OverlayManagerTiers: boolean;
  ContainerReader: boolean;
}

export function capabilityMatrix(apiVersion: number): CapabilityMatrix {
  const api26 = apiVersion >= 26;
  return {
    Api26: api26,
    SystemMaterial: api26,
    StylusBoost: api26,
    OverlayManagerTiers: api26,
    ContainerReader: api26
  };
}
