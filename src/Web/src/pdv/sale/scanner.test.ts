import { describe, expect, it } from 'vitest'
import { parseCodeInput } from './code-input'
import { ScanDetector } from './scanner'

/** Teclas com o intervalo dado entre elas, devolvendo o último passo. */
function type(detector: ScanDetector, text: string, gapMs: number, start = 1_000) {
  const steps = [...text].map((key, i) => detector.push(key, start + i * gapMs))
  return { steps, enter: detector.push('Enter', start + text.length * gapMs) }
}

describe('leitor em modo teclado (PDV-02 CA 4)', () => {
  it('rajada rápida terminada em Enter é leitura, com a 1ª tecla marcando o começo', () => {
    const { steps, enter } = type(new ScanDetector(), '7891234567895', 8)
    expect(steps[0]).toEqual({ kind: 'start' })
    expect(steps.slice(1).every((s) => s.kind === 'continue')).toBe(true)
    expect(enter).toEqual({ kind: 'scan', code: '7891234567895' })
  })

  it('gente digitando (mais de 50 ms entre teclas) não é leitura', () => {
    expect(type(new ScanDetector(), '7891234567895', 120).enter).toEqual({ kind: 'ignore' })
  })

  it('rajada curta demais ou Enter atrasado não é leitura', () => {
    expect(type(new ScanDetector(), '123', 5).enter).toEqual({ kind: 'ignore' })
    const detector = new ScanDetector()
    for (const [i, key] of [...'78912345'].entries()) detector.push(key, i * 5)
    expect(detector.push('Enter', 500)).toEqual({ kind: 'ignore' })
  })

  it('uma pausa no meio recomeça a rajada; Shift não quebra', () => {
    const detector = new ScanDetector()
    detector.push('a', 0)
    expect(detector.push('7', 400)).toEqual({ kind: 'start' })
    detector.push('Shift', 405)
    for (const [i, key] of [...'891234'].entries()) detector.push(key, 410 + i * 5)
    expect(detector.push('Enter', 445)).toEqual({ kind: 'scan', code: '7891234' })
  })
})

describe('campo de código (PDV-02 CA 6)', () => {
  it('3*código define a quantidade; vírgula vale no pesável; texto é busca', () => {
    expect(parseCodeInput('3*7891234567895')).toEqual({ quantity: '3', text: '7891234567895', isCode: true })
    expect(parseCodeInput(' 1,5 * 2000003 ')).toEqual({ quantity: '1,5', text: '2000003', isCode: true })
    expect(parseCodeInput('7891234567895')).toEqual({ quantity: null, text: '7891234567895', isCode: true })
    expect(parseCodeInput('2*arroz')).toEqual({ quantity: '2', text: 'arroz', isCode: false })
  })
})
