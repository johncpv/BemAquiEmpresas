/** Regras do passo "Perfil de negócio" e "Segmento" do provisionamento (PERF-02 CA 1 a 3), sem React, para testar à parte. */

export interface ProfileOption {
  code: string
  name: string
  modules: { code: string; name: string }[]
  segments: { code: string; name: string }[]
}

/** Segmento escolhido por perfil; `null` é "em geral" (só onde o perfil aceita ficar sem segmento). */
export type SegmentChoice = Record<string, string | null>

/** Segmento que já vem escolhido (CA 2): "Balcão" no Comércio; nos demais, nenhum. */
export const DEFAULT_SEGMENT: Record<string, string> = { retail: 'counter' }

/** Comércio precisa de segmento (o pacote de dados iniciais depende dele); Serviços pode ficar "em geral". */
export const SEGMENT_REQUIRED = new Set(['retail'])

/** Perfis marcados que têm segmento: só eles aparecem no passo "Segmento" (CA 2). */
export const profilesWithSegments = (catalog: ProfileOption[], selected: string[]) =>
  catalog.filter((p) => selected.includes(p.code) && p.segments.length > 0)

/** Ao marcar ou desmarcar um perfil, o segmento padrão entra ou sai junto. */
export function toggleProfile(selected: string[], segments: SegmentChoice, code: string): [string[], SegmentChoice] {
  if (selected.includes(code)) {
    const next = { ...segments }
    delete next[code]
    return [selected.filter((c) => c !== code), next]
  }
  return [[...selected, code], { ...segments, [code]: DEFAULT_SEGMENT[code] ?? null }]
}

/** O que vai para a API: um item por perfil marcado, com o segmento (ou nulo). */
export const profileChoices = (selected: string[], segments: SegmentChoice) =>
  selected.map((code) => ({ code, segment: segments[code] ?? null }))

/**
 * Módulos dos perfis marcados que o plano não cobre (CA 3): vão aparecer com cadeado. Sem os módulos do plano (plano novo, que nasce
 * completo), não há aviso.
 */
export function uncoveredModules(catalog: ProfileOption[], selected: string[], planModules: string[] | undefined) {
  if (!planModules) return []
  const plan = new Set(planModules)
  const seen = new Map<string, string>()
  for (const p of catalog.filter((x) => selected.includes(x.code))) {
    for (const m of p.modules) if (!plan.has(m.code)) seen.set(m.code, m.name)
  }
  return [...seen.values()]
}
