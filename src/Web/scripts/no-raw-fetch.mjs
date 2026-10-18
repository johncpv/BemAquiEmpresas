// FND-09c CA 2 / CLAUDE.md: chamadas à API só pelo cliente gerado pelo Orval. fetch/axios só no mutator (src/api/http.ts).
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export const MUTATOR = ['src', 'api', 'http.ts'].join(sep)
const RAW = /(?<![\w.])fetch\s*\(|from\s+['"]axios['"]|require\(\s*['"]axios['"]\s*\)|new\s+XMLHttpRequest\b/g

export function findRawCalls(source) {
  const found = []
  source.split(/\r?\n/).forEach((line, i) => {
    for (const m of line.matchAll(RAW)) found.push({ line: i + 1, match: m[0] })
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
    if (rel === MUTATOR || !/\.(ts|tsx)$/.test(rel) || rel.includes('.test.')) continue
    for (const v of findRawCalls(readFileSync(file, 'utf8'))) violations.push(`${rel}:${v.line} ${v.match.trim()} fora do mutator (use o cliente gerado: pnpm gen:api)`)
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
