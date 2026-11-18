// OFF-01 CA 1: depois do build, compara os manifestos e os service workers GERADOS do /fab e do /pdv. O do /fab continua igual (nome, escopo
// /fab, sw.js na raiz, sem nada do caixa no precache); o do /pdv é próprio (escopo /pdv/, sw em /pdv/sw.js, precache só de /pdv/, nunca /api).
// Uso: node scripts/check-pwa-build.mjs [pasta do build, padrão dist]
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

export function checkBuild(dist) {
  const errors = []
  const read = (p) => readFileSync(join(dist, p), 'utf8')
  const need = (p) => { if (!existsSync(join(dist, p))) errors.push(`falta ${p} no build`); return existsSync(join(dist, p)) }

  if (need('manifest.webmanifest')) {
    const fab = JSON.parse(read('manifest.webmanifest'))
    if (fab.name !== 'BemAqui Fábrica' || fab.scope !== '/fab' || fab.start_url !== '/fab' || fab.id !== '/fab') errors.push(`manifesto do /fab mudou: ${JSON.stringify({ name: fab.name, scope: fab.scope, start_url: fab.start_url, id: fab.id })}`)
  }

  if (need('pdv/manifest.webmanifest')) {
    const pdv = JSON.parse(read('pdv/manifest.webmanifest'))
    if (pdv.name !== 'BemAqui Caixa' || pdv.scope !== '/pdv/' || pdv.start_url !== '/pdv/' || pdv.id !== '/pdv/') errors.push(`manifesto do /pdv errado: ${JSON.stringify({ name: pdv.name, scope: pdv.scope, start_url: pdv.start_url, id: pdv.id })}`)
    for (const icon of pdv.icons ?? []) need(icon.src.replace(/^\//, ''))
  }

  if (need('sw.js')) {
    const sw = read('sw.js')
    if (sw.includes('pdv/')) errors.push('o service worker do /fab passou a guardar arquivos do /pdv')
  }

  if (need('pdv/sw.js')) {
    const sw = read('pdv/sw.js')
    const urls = [...sw.matchAll(/url:\s*"([^"]+)"/g)].map((m) => m[1])
    if (!urls.includes('/pdv/index.html')) errors.push('o service worker do /pdv não guarda /pdv/index.html (não abriria sem rede)')
    const outside = urls.filter((u) => !u.startsWith('/pdv/'))
    if (outside.length) errors.push(`o service worker do /pdv guarda fora de /pdv/: ${outside.join(', ')}`)
    if (/["']\/api\//.test(sw)) errors.push('o service worker do /pdv cita /api/')
    if (/skipWaiting\(\)\s*[,;]?\s*\w*\.?clientsClaim/.test(sw) && !sw.includes('SKIP_WAITING')) errors.push('o service worker do /pdv troca de versão sozinho (skipWaiting)')
  }

  if (need('index.html') && read('index.html').includes('pdv/manifest.webmanifest')) errors.push('o index.html do backoffice aponta o manifesto do caixa')
  if (need('pdv/index.html') && !read('pdv/index.html').includes('/pdv/manifest.webmanifest')) errors.push('o pdv/index.html não aponta o manifesto do caixa')
  return errors
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const errors = checkBuild(process.argv[2] ?? 'dist')
  if (errors.length) {
    console.error(errors.join('\n'))
    process.exit(1)
  }

  console.log('PWA: manifestos e service workers do /fab e do /pdv conferidos.')
}
