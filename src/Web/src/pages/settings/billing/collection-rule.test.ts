import { describe, expect, it } from 'vitest'
import { daysError, insertAt, nextRunText, noticeStatus, SEND_TIMES, stepLabel, textError, timeline } from './collection-rule'

const LIST = ['{empresa}', '{cliente}', '{numero}', '{valor}', '{vencimento}', '{linha_digitavel}', '{dias_de_atraso}']

describe('régua de cobrança (FIN-09)', () => {
  it('rótulos da linha do tempo', () => {
    expect(stepLabel(-3)).toBe('3 dias antes')
    expect(stepLabel(0)).toBe('No vencimento')
    expect(stepLabel(1)).toBe('1 dia depois')
    expect(stepLabel(10)).toBe('10 dias depois')
  })

  it('texto só aceita os campos da lista (CA 2)', () => {
    expect(textError('Olá {cliente}, o {numero} vence em {vencimento}.', LIST)).toBeNull()
    expect(textError('Texto com <b>HTML</b>', LIST)).toBeNull()
    expect(textError('Olá {nome}', LIST)).toBe(`Use só os campos da lista: ${LIST.join(', ')}.`)
    expect(textError('Olá {{ cliente }}', LIST)).not.toBeNull()
    expect(textError('{% if x %}', LIST)).not.toBeNull()
    expect(textError(' ', LIST)).toBe('Escreva o texto do lembrete.')
    expect(textError('x'.repeat(1001), LIST)).toBe('O texto do lembrete pode ter até 1.000 caracteres.')
  })

  it('dias do passo: limites e sem repetir (CA 1)', () => {
    const steps = [{ key: 'a', days: '-3', text: '' }, { key: 'b', days: '0', text: '' }]
    expect(daysError('-3', steps, 'a')).toBeNull()
    expect(daysError('0', steps, 'a')).toBe('Dois passos no mesmo dia: mude os dias de um deles.')
    expect(daysError('-31', steps, 'a')).toBe('Os dias do passo vão de 30 antes a 90 depois do vencimento.')
    expect(daysError('abc', steps, 'a')).not.toBeNull()
  })

  it('campo clicável entra no cursor', () => {
    expect(insertAt('Olá , tudo bem?', '{cliente}', 4, 4)).toEqual({ text: 'Olá {cliente}, tudo bem?', caret: 13 })
    expect(insertAt('Olá', '{numero}')).toEqual({ text: 'Olá{numero}', caret: 11 })
  })

  it('linha do tempo em ordem de dias', () => {
    expect(timeline([{ key: 'x', days: '10', text: '' }, { key: 'y', days: '-3', text: '' }, { key: 'z', days: '', text: '' }]).map((s) => s.days)).toEqual([-3, 10])
  })

  it('situação do lembrete e a mensagem ao ligar', () => {
    expect(noticeStatus('Sent')).toEqual({ label: 'Enviado', tone: 'ok' })
    expect(noticeStatus('Skipped').label).toBe('Ignorado')
    expect(noticeStatus('Deferred').tone).toBe('warn')
    expect(nextRunText(3, '2026-11-02', '2026-10-31')).toBe('3 títulos recebem lembrete em 02/11.')
    expect(nextRunText(1, '2026-10-31', '2026-10-31')).toBe('1 título recebe lembrete hoje.')
    expect(nextRunText(0, '2026-10-31', '2026-10-31')).toBe('Nenhum título recebe lembrete hoje.')
    expect(SEND_TIMES[0]).toBe('07:00')
    expect(SEND_TIMES.at(-1)).toBe('20:00')
  })
})
