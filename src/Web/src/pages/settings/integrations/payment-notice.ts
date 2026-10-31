import type { ReconcileReport } from '@/api/generated/model'

/** Faixa âmbar do aviso não ativo (BNK-02, Telas e estados). */
export const NOTICE_OFF_TEXT = 'Sem o aviso de pagamento, os boletos pagos só baixam na conferência das 7h e das 13h.'

export const NOTICE_HINTS = {
  notice: 'O Inter avisa o sistema quando um boleto é pago. O aviso só serve para o sistema reconsultar o boleto no banco: o valor e a data vêm sempre da consulta, nunca do aviso.',
  mode: 'Onde vai a senha do endereço do aviso: no fim do endereço depois de "?t=" ou como último pedaço do caminho. Depende do que o Inter aceita; os dois são seguros.',
}

export function modeLabel(mode: string): string {
  return mode === 'PathToken' ? 'Senha no caminho do endereço' : 'Senha no fim do endereço (?t=)'
}

/** "12 consultados · 3 baixados · 1 a conferir" (relatório da conferência diária, CA 11). */
export function reportText(report: Pick<ReconcileReport, 'consulted' | 'settled' | 'divergent' | 'unknown' | 'failed'>): string {
  const parts = [`${report.consulted} consultados`, `${report.settled} baixados`, `${report.divergent} a conferir`]
  if (report.unknown > 0) parts.push(`${report.unknown} fora do sistema`)
  if (report.failed > 0) parts.push(`${report.failed} com falha`)
  return parts.join(' · ')
}
