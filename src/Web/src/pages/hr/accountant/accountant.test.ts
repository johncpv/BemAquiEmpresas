import { describe, expect, it } from 'vitest'
import { currentMonth, mailtoContadora, monthOf, packageFileName, validMonth } from './accountant'

describe('exportação para a contadora (RH-03)', () => {
  const today = new Date(2026, 9, 27)

  it('competência: só o mês atual ou um anterior', () => {
    expect(currentMonth(today)).toBe('2026-10')
    expect(validMonth('2026-10', today)).toBe(true)
    expect(validMonth('2024-03', today)).toBe(true)
    expect(validMonth('2026-11', today)).toBe(false)
    expect(validMonth('2026-13', today)).toBe(false)
    expect(validMonth('', today)).toBe(false)
    expect(monthOf('2026-09-01')).toBe('2026-09')
    expect(packageFileName('2026-09', 'v1')).toBe('contadora-2026-09-v1.zip')
  })

  it('"Avisar a contadora": e-mail pronto, sem anexo, sem destinatário e com o que substitui', () => {
    const link = mailtoContadora('2026-09', '01/10/2026 09:30')
    expect(link.startsWith('mailto:?subject=')).toBe(true)
    const body = decodeURIComponent(link.split('&body=')[1])
    expect(decodeURIComponent(link.split('subject=')[1].split('&')[0])).toBe('Pacote do RH de setembro de 2026')
    expect(body).toContain('substitui o pacote que enviei antes (gerado em 01/10/2026 09:30)')
    expect(decodeURIComponent(mailtoContadora('2026-09', null))).not.toContain('substitui')
  })
})
