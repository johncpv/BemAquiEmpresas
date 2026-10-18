import { describe, expect, it } from 'vitest'
// @ts-expect-error módulo .mjs sem tipos
import { findHexColors } from './no-hex-colors.mjs'

describe('regra de cor literal (FND-01 CA 13)', () => {
  it.each(['color: #fff;', "const c = '#2148C0'", 'border: 1px solid #2148c0aa;', 'style={{ color: "#abcd" }}'])(
    'acusa cor hex em %s',
    (code) => expect(findHexColors(code)).toHaveLength(1),
  )

  it.each(['color: var(--foreground);', "navigate('#/rota')", 'const id = "#root"', '// issue #12345 no comentário? não é cor', 'href="#secao"'])(
    'não acusa %s',
    (code) => expect(findHexColors(code)).toHaveLength(0),
  )
})

describe('cor literal ampliada (FND-09a CA 7)', () => {
  it.each(['<div className="bg-blue-600 p-2">', 'className="hover:text-red-500"', 'className="text-white"', 'color: rgb(10, 20, 30);', 'fill: hsl(210 40% 50%)', 'className="border-slate-200/50"'])(
    'acusa %s',
    (code) => expect(findHexColors(code).length).toBeGreaterThan(0),
  )

  it.each(['className="bg-primary text-primary-foreground"', 'className="text-ok-ink bg-ok-soft"', 'className="bg-background"', 'color: var(--crit);', 'className="border-input"'])(
    'aceita token: %s',
    (code) => expect(findHexColors(code)).toHaveLength(0),
  )
})
