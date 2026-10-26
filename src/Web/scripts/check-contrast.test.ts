import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
// @ts-expect-error módulo .mjs sem tipos
import { AMBER_HUE_DISTANCE, check, contrast, CVD_MIN_DISTANCE, cvdDistance, hueDistance, parseThemes } from './check-contrast.mjs'

describe('contraste dos tokens (FND-09a CA 3 a 6)', () => {
  it('o tokens.css passa nos dois temas', () => {
    expect(check(readFileSync('src/styles/tokens.css', 'utf8'))).toEqual([])
  })

  it('o botão de sucesso do protótipo no escuro falhava (texto branco sobre verde, 2,12:1)', () => {
    expect(contrast('#ffffff', '#5cc68a')).toBeLessThan(2.2)
  })

  it('no protótipo --led e --warn do escuro eram quase iguais', () => {
    expect(hueDistance('#f2a93b', '#f0a848')).toBeLessThan(12)
    expect(contrast('#f2a93b', '#f0a848')).toBeLessThan(1.5)
  })
})

describe('séries de gráfico (FIN-07 CA 13, D18)', () => {
  const css = readFileSync('src/styles/tokens.css', 'utf8')

  it('o --chart-3 antigo (âmbar) agora reprova nos dois temas', () => {
    const old = css.replace(/--chart-3: #[0-9a-f]{6};/, '--chart-3: #a35a00;').replace(/(:root\[data-theme="dark"\][^}]*--chart-3: )#[0-9a-f]{6}/, '$1#f0b25a')
    const problems = check(old) as string[]
    expect(problems.some((p) => p.startsWith('claro: --chart-3 está na faixa de matiz do âmbar'))).toBe(true)
    expect(problems.some((p) => p.startsWith('escuro: --chart-3 está na faixa de matiz do âmbar'))).toBe(true)
  })

  it('nenhuma série na faixa do âmbar; --chart-3 com contraste >= 3:1 e distinto das outras com protanopia e deuteranopia', () => {
    for (const tokens of Object.values(parseThemes(css)) as Record<string, string>[]) {
      for (const n of [1, 2, 3, 4, 5, 6]) expect(hueDistance(tokens[`chart-${n}`], tokens.signal)).toBeGreaterThanOrEqual(AMBER_HUE_DISTANCE)
      for (const state of ['ok', 'warn', 'crit', 'info']) expect(hueDistance(tokens['chart-3'], tokens[state])).toBeGreaterThanOrEqual(AMBER_HUE_DISTANCE)
      expect(contrast(tokens['chart-3'], tokens.card)).toBeGreaterThanOrEqual(3)
      for (const n of [1, 2, 4, 5, 6]) {
        for (const kind of [undefined, 'protanopia', 'deuteranopia']) expect(cvdDistance(tokens['chart-3'], tokens[`chart-${n}`], kind)).toBeGreaterThanOrEqual(CVD_MIN_DISTANCE)
      }
    }
  })
})
