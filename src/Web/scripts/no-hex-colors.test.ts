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
