type Change = { old?: unknown; new?: unknown; before?: unknown; after?: unknown; changed?: boolean }

const show = (v: unknown) => (v === null || v === undefined || v === '' ? '—' : typeof v === 'object' ? JSON.stringify(v) : String(v))

/**
 * Linhas "campo: antes → depois" do JSON da auditoria (FND-07): {campo: {old, new}} do interceptor, {changed: true} para
 * segredo (só "alterado") e {before, after} dos eventos de serviço. Dado pessoal já chega mascarado do servidor.
 */
export function describeChanges(json: string): string[] {
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return []
  }

  if (!parsed || typeof parsed !== 'object') return []
  const root = parsed as Record<string, unknown> & Change
  if ('before' in root || 'after' in root) return [`${show(root.before)} → ${show(root.after)}`]

  return Object.entries(root).map(([field, value]) => {
    const c = (value ?? {}) as Change
    if (c.changed) return `${field}: alterado`
    if ('old' in c || 'new' in c) return `${field}: ${show(c.old)} → ${show(c.new)}`
    return `${field}: ${show(value)}`
  })
}
