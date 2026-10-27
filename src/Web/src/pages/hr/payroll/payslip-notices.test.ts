import { afterEach, describe, expect, it } from 'vitest'
import { clearInviteSuggestion, noticeLabel, peekInviteSuggestion, plural, saveInviteSuggestion } from './payslip-notices'

describe('aviso do holerite (RH-02b CA 9)', () => {
  it('rótulo e tom de cada situação', () => {
    expect(noticeLabel({ status: 'Queued' })).toEqual({ text: 'Na fila', tone: 'info' })
    expect(noticeLabel({ status: 'Sent', sentAt: '2026-11-03T15:00:00Z' })).toEqual({ text: 'Avisado em 03/11/2026', tone: 'ok' })
    expect(noticeLabel({ status: 'Failed', error: 'Caixa cheia' })).toEqual({ text: 'Falhou: Caixa cheia', tone: 'crit' })
    expect(noticeLabel({ status: 'NoUser' }).text).toBe('Sem usuário')
    expect(noticeLabel({ status: 'Terminated' }).text).toBe('Desligado')
    expect(noticeLabel({ status: 'NotNotified' }).text).toBe('Não avisado')
  })

  it('plural', () => {
    expect(plural(1, 'aviso', 'avisos')).toBe('1 aviso')
    expect(plural(3, 'aviso', 'avisos')).toBe('3 avisos')
  })
})

describe('convite sugerido (CA 7)', () => {
  afterEach(() => sessionStorage.clear())

  it('guarda na sessão do navegador e é limpa por quem abre o convite', () => {
    saveInviteSuggestion('Bia Lima', 'bia@exemplo.com')
    expect(peekInviteSuggestion()).toEqual({ name: 'Bia Lima', email: 'bia@exemplo.com' })
    clearInviteSuggestion()
    expect(peekInviteSuggestion()).toBeNull()
  })
})
