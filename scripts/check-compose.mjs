#!/usr/bin/env node
// Verificação estática do compose de homologação/produção (FND-10 CA 8 a 10). Entrada: saída de
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
const hardened = ['api', 'web', 'migrator', 'backup']
const onStack = new Set(['api', 'web'])
const problems = []

for (const name of hardened) {
  const s = services[name]
  if (!s) {
    problems.push(`${name}: serviço ausente (use --profile backup no "config")`)
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
  if (stackNetwork && networks.includes(stackNetwork) && !onStack.has(name)) problems.push(`${name}: só api e web podem estar na stack_stack`)
  if (s.network_mode === 'host') problems.push(`${name}: network_mode host não é permitido`)
  if ((s.ports ?? []).length > 0) problems.push(`${name}: o acesso externo é só pelo stack-nginx; remova ports:`)
}

if (!stackNetwork) problems.push('rede externa stack_stack não declarada')

if (problems.length > 0) {
  console.error('Compose fora das regras de endurecimento:\n- ' + problems.join('\n- '))
  process.exit(1)
}
console.log(`Compose ok: ${Object.keys(services).length} serviços verificados.`)
