// Contraste dos tokens (FND-09a CA 3 a 6, revisão §2.4): falha o CI se algum par ficar abaixo do limite (WCAG 2.2).
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

/** Blocos do tokens.css: claro (:root) e escuro forçado (:root[data-theme="dark"], igual ao automático e à TV). */
export function parseThemes(css) {
  const block = (selector) => {
    const start = css.indexOf(selector)
    const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start))
    return Object.fromEntries([...body.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-fA-F]{6})/g)].map((m) => [m[1], m[2].toLowerCase()]))
  }
  return { claro: block(':root {'), escuro: block(':root[data-theme="dark"]') }
}

const channel = (v) => {
  const c = v / 255
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}
const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16))
const luminance = (hex) => {
  const [r, g, b] = rgb(hex).map(channel)
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

export function contrast(a, b) {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (l1 + 0.05) / (l2 + 0.05)
}

/** Distância de matiz em graus: --signal e --warn precisam ser cores distintas (CA 6). */
export function hueDistance(a, b) {
  const hue = (hex) => {
    const [r, g, b2] = rgb(hex).map((v) => v / 255)
    const max = Math.max(r, g, b2)
    const d = max - Math.min(r, g, b2)
    if (d === 0) return 0
    const h = max === r ? ((g - b2) / d) % 6 : max === g ? (b2 - r) / d + 2 : (r - g) / d + 4
    return (h * 60 + 360) % 360
  }
  const diff = Math.abs(hue(a) - hue(b))
  return Math.min(diff, 360 - diff)
}

/**
 * Daltonismo (FIN-07 CA 13): simula protanopia e deuteranopia (Viénot, Brettel e Mollon, 1999, em RGB linear) e mede a
 * distância ΔE (CIE76, Lab D65) entre duas cores como a pessoa as veria.
 */
const MATRICES = {
  protanopia: [[0.11238, 0.88762, 0], [0.11238, 0.88762, 0], [0.00401, -0.00401, 1]],
  deuteranopia: [[0.29275, 0.70725, 0], [0.29275, 0.70725, 0], [-0.02234, 0.02234, 1]],
}
const lab = (linear) => {
  const [r, g, b] = linear
  const xyz = [0.4124 * r + 0.3576 * g + 0.1805 * b, 0.2126 * r + 0.7152 * g + 0.0722 * b, 0.0193 * r + 0.1192 * g + 0.9505 * b]
  const f = (t) => (t > 0.008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116)
  const [fx, fy, fz] = [xyz[0] / 0.95047, xyz[1], xyz[2] / 1.08883].map(f)
  return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)]
}
export function cvdDistance(a, b, kind) {
  const sim = (hex) => {
    const c = rgb(hex).map(channel)
    const m = kind ? MATRICES[kind] : [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    return lab(m.map((row) => Math.max(0, Math.min(1, row[0] * c[0] + row[1] * c[1] + row[2] * c[2]))))
  }
  const [l1, l2] = [sim(a), sim(b)]
  return Math.hypot(l1[0] - l2[0], l1[1] - l2[1], l1[2] - l2[2])
}

/** Série de gráfico na faixa de matiz do âmbar (--signal) confunde com destaque de estado (FIN-07 CA 13, D18). */
export const AMBER_HUE_DISTANCE = 25
/** ΔE mínimo entre o --chart-3 e as outras séries, também com protanopia e deuteranopia. */
export const CVD_MIN_DISTANCE = 12

// [frente, fundo, mínimo, motivo]
export const PAIRS = [
  ['foreground', 'background', 4.5, 'texto'],
  ['foreground', 'card', 4.5, 'texto no cartão'],
  ['muted-foreground', 'card', 4.5, 'texto secundário'],
  ['muted-foreground', 'background', 4.5, 'texto secundário no fundo'],
  ['primary-foreground', 'primary', 4.5, 'botão principal'],
  ['primary', 'card', 3, 'link e ícone de acento'],
  ['success-foreground', 'ok', 4.5, 'botão de sucesso (CA 3)'],
  ['critical-foreground', 'crit', 4.5, 'botão destrutivo'],
  ['ok-ink', 'ok-soft', 4.5, 'chip ok (CA 4)'],
  ['warn-ink', 'warn-soft', 4.5, 'chip alerta (CA 4)'],
  ['crit-ink', 'crit-soft', 4.5, 'chip crítico (CA 4)'],
  ['info-ink', 'info-soft', 4.5, 'chip informativo'],
  ['input', 'background', 3, 'borda de campo (CA 5)'],
  ['input', 'card', 3, 'borda de campo no cartão (CA 5)'],
  ['ring', 'background', 3, 'anel de foco'],
  ['rail-ring', 'rail', 3, 'foco sobre o trilho (CA 5)'],
  ['rail-ink', 'rail', 4.5, 'rótulo do dock'],
  ['rail-dim', 'rail', 4.5, 'rótulo secundário do dock'],
  ['signal', 'rail', 3, 'letra da etapa e item ativo'],
  ['signal-foreground', 'signal', 4.5, 'marca'],
  ...[1, 2, 3, 4, 5, 6].map((n) => [`chart-${n}`, 'card', 3, `série ${n} do gráfico (FND-09e CA 11)`]),
]

export function check(css) {
  const problems = []
  for (const [theme, tokens] of Object.entries(parseThemes(css))) {
    for (const [fg, bg, min, why] of PAIRS) {
      if (!tokens[fg] || !tokens[bg]) {
        problems.push(`${theme}: token ausente (--${fg} ou --${bg})`)
        continue
      }
      const ratio = contrast(tokens[fg], tokens[bg])
      if (ratio < min) problems.push(`${theme}: ${why} --${fg} sobre --${bg} = ${ratio.toFixed(2)}:1 (mínimo ${min}:1)`)
    }
    // Distintas no matiz ou na luminosidade: no escuro o protótipo tinha as duas quase iguais (revisão §2.4).
    if (hueDistance(tokens.signal, tokens.warn) < 12 && contrast(tokens.signal, tokens.warn) < 1.5) {
      problems.push(`${theme}: --signal e --warn precisam ser cores distintas (CA 6)`)
    }
    // FIN-07 CA 13: o âmbar é só do destaque de estado e do item ativo, nunca de série de gráfico.
    for (const n of [1, 2, 3, 4, 5, 6]) {
      const series = tokens[`chart-${n}`]
      if (series && tokens.signal && hueDistance(series, tokens.signal) < AMBER_HUE_DISTANCE) {
        problems.push(`${theme}: --chart-${n} está na faixa de matiz do âmbar (--signal); use um tom frio`)
      }
    }
    for (const n of [1, 2, 4, 5, 6]) {
      for (const kind of [undefined, 'protanopia', 'deuteranopia']) {
        if (tokens['chart-3'] && tokens[`chart-${n}`] && cvdDistance(tokens['chart-3'], tokens[`chart-${n}`], kind) < CVD_MIN_DISTANCE) {
          problems.push(`${theme}: --chart-3 e --chart-${n} ficam parecidas${kind ? ` com ${kind}` : ''}`)
        }
      }
    }
  }
  return problems
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const problems = check(readFileSync(join(process.cwd(), 'src', 'styles', 'tokens.css'), 'utf8'))
  if (problems.length) {
    console.error(problems.join('\n'))
    process.exit(1)
  }
  console.log('Contraste dos tokens: ok nos temas claro e escuro.')
}
