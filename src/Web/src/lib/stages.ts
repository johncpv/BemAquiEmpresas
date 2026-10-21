/** Etapas padrão da Konebras (P4): Projeto → Corte → Montagem → Qualidade → Expedição. */
export const DEFAULT_STAGES = ['Projeto', 'Corte', 'Montagem', 'Qualidade', 'Expedição'] as const

/** Letras das etapas padrão (D10): o código da etapa no Kanban e no display de andar. */
export const DEFAULT_LETTERS = ['P', 'C', 'M', 'Q', 'E'] as const

/** Acima disto o indicador de etapa vira o formato compacto "M · 3/12", sem os segmentos (D17 b, padrão de tela 3 da Fase 3). */
export const STAGE_COMPACT_ABOVE = 8

/** Estado de cada segmento, como no protótipo (.shaft i.done / i.now): concluída apagada, atual em âmbar, futura na linha. */
export function segmentState(i: number, current: number): 'done' | 'now' | 'next' {
  return i < current ? 'done' : i === current ? 'now' : 'next'
}