import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
// @ts-expect-error módulo .mjs sem tipos
import { check, contrast, hueDistance } from './check-contrast.mjs'

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
