// OFF-01 CA 7: dado local do caixa só pela porta LocalStore (src/pdv/local-store/). IndexedDB e Dexie direto em qualquer outro lugar reprovam
// o lint. Exceção única: a chave do aparelho (src/pdv/device-signing.ts, PDV-01, ADR 0048), que guarda um CryptoKey não exportável no banco
// próprio "bemaqui-pdv" e é anterior à porta. Testes ficam fora (o teste da porta usa o IndexedDB falso).
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

export const LOCAL_STORE_DIR = ['src', 'pdv', 'local-store'].join(sep) + sep
export const ALLOWED = [['src', 'pdv', 'device-signing.ts'].join(sep)]
const RAW = /(?<![\w.$])(?:window\.|self\.|globalThis\.)?indexedDB\b|\bIDB(?:Factory|Database|ObjectStore|Transaction|KeyRange|Request|OpenDBRequest|Index|Cursor)\b|from\s+['"]dexie['"]|import\(\s*['"]dexie['"]\s*\)|require\(\s*['"]dexie['"]\s*\)/g

export function findRawStorage(source) {
  const found = []
  source.split(/\r?\n/).forEach((line, i) => {
    if (/^\s*(\/\/|\*|\/\*)/.test(line)) return // comentário
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
    if (!/\.(ts|tsx)$/.test(rel) || rel.includes('.test.') || rel.startsWith(LOCAL_STORE_DIR) || ALLOWED.includes(rel)) continue
    if (rel.startsWith(['src', 'api', 'generated'].join(sep))) continue
    for (const v of findRawStorage(readFileSync(file, 'utf8'))) violations.push(`${rel}:${v.line} ${v.match.trim()} fora do LocalStore (use a porta em src/pdv/local-store/)`)
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
