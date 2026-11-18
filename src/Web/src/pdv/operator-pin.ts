import type { PosPinLoginInput } from '@/api/generated/model'

/**
 * Corpo da entrada por PIN no caixa (PDV-01 CA 11). A assinatura do aparelho cobre o corpo exato, então a ordem dos campos aqui é a mesma que o
 * cliente gerado serializa (JSON.stringify do objeto). O PIN novo só vai quando o servidor pediu a troca (pin_change_required).
 */
export function pinBody(userId: string, pin: string, newPin?: string): PosPinLoginInput {
  return newPin ? { userId, pin, newPin, newPinConfirmation: newPin } : { userId, pin }
}

export const PIN_CHANGE_REQUIRED = 'pin_change_required'
