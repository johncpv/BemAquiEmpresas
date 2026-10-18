// Falha se houver cor literal fora do arquivo de tokens (FND-01 CA 13, FND-09a CA 7): hex, rgb()/hsl() e classes de
// cor fixa do Tailwind (ex.: bg-blue-600, text-white). Toda cor vem de src/styles/tokens.css.
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export const TOKENS_FILE = ['src', 'styles', 'tokens.css'].join(sep)
const EXTENSIONS = ['.ts', '.tsx', '.css']
// "#abc", "#aabbcc", "#aabbccdd" isolados; ignora "#/rota" e âncoras com letras além de a-f.
const HEX = /(?<![\w&/])#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})(?![\w-])/g
const FUNCTION = /\b(?:rgba?|hsla?|oklch|oklab|hwb|lab|lch)\(\s*[\d.]/g
const PALETTE = 'slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose'
const UTILITY = 'bg|text|border|ring|ring-offset|fill|stroke|from|via|to|outline|decoration|shadow|accent|caret|divide|placeholder'
const TAILWIND = new RegExp(String.raw`(?<![\w-])(?:[a-z-]+:)*(?:${UTILITY})-(?:(?:${PALETTE})-\d{2,3}|black|white)(?:\/\d+)?(?![\w-])`, 'g')

export function findHexColors(source) {
  const found = []
  source.split(/\r?\n/).forEach((line, i) => {
    for (const regex of [HEX, FUNCTION, TAILWIND]) {
      for (const m of line.matchAll(regex)) found.push({ line: i + 1, match: m[0] })
    }
  })
  return found
}

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

export function check(root) {
  const violations = []
  for (const file of walk(join(root, 'src'))) {
    const rel = relative(root, file)
    if (rel === TOKENS_FILE || !EXTENSIONS.some((e) => rel.endsWith(e)) || rel.includes('.test.')) continue
    for (const v of findHexColors(readFileSync(file, 'utf8'))) violations.push(`${rel}:${v.line} cor literal ${v.match} (use um token de src/styles/tokens.css)`)
  }
  return violations
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const violations = check(process.cwd())
  if (violations.length) {
    console.error(violations.join('\n'))
    process.exit(1)
  }
}
