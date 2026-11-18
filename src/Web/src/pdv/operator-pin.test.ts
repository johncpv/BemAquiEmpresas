import { describe, expect, it } from 'vitest'
import { pinBody } from './operator-pin'

describe('entrada por PIN no caixa (PDV-01 CA 11)', () => {
  it('monta o corpo que a assinatura cobre, com o PIN novo só na troca', () => {
    expect(JSON.stringify(pinBody('u1', '482915'))).toBe('{"userId":"u1","pin":"482915"}')
    expect(JSON.stringify(pinBody('u1', '482915', '730284'))).toBe('{"userId":"u1","pin":"482915","newPin":"730284","newPinConfirmation":"730284"}')
  })
})
