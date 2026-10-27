#!/usr/bin/env node
// Verificação estática do compose de homologação/produção (FND-10 CA 8 a 10 e nomes únicos na stack_stack). Entrada: saída de
//   docker compose -f <arquivo> --profile backup config --format json
// Falha (código 1) com a lista do que está fora da regra.
import { readFileSync } from 'node:fs'

const file = process.argv[2]
if (!file) {
  console.error('Uso: node scripts/check-compose.mjs <compose.json>')
  process.exit(64)
}

const compose = JSON.parse(readFileSync(file, 'utf8'))
const services = compose.services ?? {}
const stackNetwork = Object.entries(compose.networks ?? {}).find(([, n]) => n.name === 'stack_stack')?.[0]
// api e web têm nome próprio do ambiente (bqe-<ambiente>-api|web): o nome do serviço vira apelido na stack_stack,
// onde "api", "web", "postgres" e "redis" já são de outros projetos (ADR 0015).
const isEdge = (name) => /^bqe-[a-z0-9]+-(api|web)$/.test(name)
const hardened = ['migrator', 'backup', 'importer', ...Object.keys(services).filter(isEdge)]
const generic = new Set(['postgres', 'clamav', 'redis', 'api', 'web', 'db', 'database'])
const problems = []

for (const name of hardened) {
  const s = services[name]
  if (!s) {
    problems.push(`${name}: serviço ausente (use --profile backup --profile import no "config")`)
    continue
  }
  const user = String(s.user ?? '')
  if (!user || /^(0|root)(:|$)/.test(user)) problems.push(`${name}: precisa de "user:" não-root (está "${user || 'vazio'}")`)
  if (s.read_only !== true) problems.push(`${name}: precisa de read_only: true`)
  if (!(s.cap_drop ?? []).includes('ALL')) problems.push(`${name}: precisa de cap_drop: [ALL]`)
  if ((s.cap_add ?? []).length > 0) problems.push(`${name}: não pode ter cap_add`)
  if (!(s.security_opt ?? []).some((o) => /^no-new-privileges(:true)?$/.test(o))) problems.push(`${name}: precisa de security_opt no-new-privileges`)
  const limits = s.deploy?.resources?.limits ?? {}
  if (!limits.cpus || !limits.memory) problems.push(`${name}: precisa de limite de CPU e memória`)
  if (s.privileged) problems.push(`${name}: não pode ser privileged`)
}

const postgres = services.postgres
if (!postgres) problems.push('postgres: serviço ausente')
else {
  if (postgres.network_mode) problems.push('postgres: não pode usar network_mode')
}

for (const [name, s] of Object.entries(services)) {
  const networks = Object.keys(s.networks ?? {})
  if (stackNetwork && networks.includes(stackNetwork) && !isEdge(name)) problems.push(`${name}: só bqe-<ambiente>-api e -web podem estar na stack_stack`)
  for (const [key, value] of Object.entries(s.environment ?? {})) {
    if (/(__Host|^PGHOST)$/.test(key) && generic.has(String(value))) problems.push(`${name}: ${key}=${value} é nome genérico; use o nome do container (ex.: bqe-homolog-postgres)`)
  }
  if (s.network_mode === 'host') problems.push(`${name}: network_mode host não é permitido`)
  if ((s.ports ?? []).length > 0) problems.push(`${name}: o acesso externo é só pelo stack-nginx; remova ports:`)
}

// EmissorFiscal (ADR 0027, adendo de 27/10/2026): SaaS multi-cliente, falado pela API pública em HTTPS; o aviso chega pelo stack-nginx.
// Sem rede privada com ele: a bqe-fiscal não volta, o endereço configurado é HTTPS e nenhum webhook entra por host interno.
if (Object.values(compose.networks ?? {}).some((n) => n.name === 'bqe-fiscal')) problems.push('rede bqe-fiscal: o EmissorFiscal é falado pela API pública, sem rede privada (ADR 0027, adendo de 27/10/2026)')
for (const [name, s] of Object.entries(services)) {
  const env = s.environment ?? {}
  const isApi = /^bqe-[a-z0-9]+-api$/.test(name)
  for (const [key, value] of Object.entries(env)) {
    if (/^Fiscal__Emissor__BaseUrl$/.test(key) && !/^https:\/\//.test(String(value))) problems.push(`${name}: ${key} precisa ser HTTPS (API pública do Emissor)`)
    if (/^Webhooks__InternalHosts__/.test(key)) problems.push(`${name}: ${key} não existe mais; o webhook chega só pelo stack-nginx`)
  }
  if (isApi && String(env.ASPNETCORE_ENVIRONMENT ?? 'Production') === 'Development') problems.push(`${name}: ASPNETCORE_ENVIRONMENT=Development expõe o OpenAPI`)
}

if (!stackNetwork) problems.push('rede externa stack_stack não declarada')
if (Object.keys(services).filter(isEdge).length !== 2) problems.push('esperado um serviço bqe-<ambiente>-api e um bqe-<ambiente>-web')

if (problems.length > 0) {
  console.error('Compose fora das regras de endurecimento:\n- ' + problems.join('\n- '))
  process.exit(1)
}
console.log(`Compose ok: ${Object.keys(services).length} serviços verificados.`)
