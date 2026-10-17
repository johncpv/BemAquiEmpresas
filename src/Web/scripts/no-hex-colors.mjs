// Falha se houver cor hex literal fora do arquivo de tokens (FND-01, CA 13).
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export const TOKENS_FILE = ['src', 'styles', 'tokens.css'].join(sep)
const EXTENSIONS = ['.ts', '.tsx', '.css']
// "#abc", "#aabbcc", "#aabbccdd" isolados; ignora "#/rota" e âncoras com letras além de a-f.
const HEX = /(?<![\w&/])#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})(?![\w-])/g

export function findHexColors(source) {
  const found = []
  source.split(/\r?\n/).forEach((line, i) => {
    for (const m of line.matchAll(HEX)) found.push({ line: i + 1, match: m[0] })
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
