import type { StatusTone } from '@/components/ui/status-chip'

/** Passo em edição (CA 1): dias em relação ao vencimento e o texto literal. */
export interface StepDraft {
  key: string
  days: string
  text: string
}

export const MAX_TEXT = 1000
export const MIN_DAYS = -30
export const MAX_DAYS = 90

/** O que cada campo do texto vira no e-mail (CA 2). */
export const PLACEHOLDER_HINTS: Record<string, string> = {
  '{empresa}': 'Nome da sua empresa',
  '{cliente}': 'Nome do cliente',
  '{numero}': 'Número do título',
  '{valor}': 'Saldo do título',
  '{vencimento}': 'Data de vencimento',
  '{linha_digitavel}': 'Linha digitável do boleto (vazio sem boleto)',
  '{dias_de_atraso}': 'Dias de atraso (0 antes do vencimento)',
}

export const COLLECTION_DICAS = {
  campos: 'Clique num campo para colocá-lo no texto. No envio, cada campo vira o dado do título. Só estes campos valem; o texto sai como está, sem formatação.',
  pausar: 'Para os lembretes automáticos enquanto você negocia com o cliente. Os passos que caírem na pausa não são enviados depois. Fica no histórico.',
  regua: 'Lembretes por e-mail antes e depois do vencimento, um por passo. Título pago, cancelado ou com boleto pago aguardando conferência não recebe.',
}

/** Rótulo da linha do tempo, igual ao do servidor: "3 dias antes", "No vencimento", "10 dias depois". */
export function stepLabel(days: number): string {
  if (days === 0) return 'No vencimento'
  if (days === -1) return '1 dia antes'
  if (days === 1) return '1 dia depois'
  return days < 0 ? `${-days} dias antes` : `${days} dias depois`
}

/** Mesma regra do servidor (CA 2): só os campos da lista; chave solta também é recusada. */
export function textError(text: string, placeholders: readonly string[]): string | null {
  if (!text.trim()) return 'Escreva o texto do lembrete.'
  if (text.length > MAX_TEXT) return 'O texto do lembrete pode ter até 1.000 caracteres.'
  const rest = text.replace(/\{[^{}]*\}/g, (m) => (placeholders.includes(m) ? '' : '\u0000'))
  return rest.includes('\u0000') || rest.includes('{') || rest.includes('}') ? `Use só os campos da lista: ${placeholders.join(', ')}.` : null
}

/** Erro dos dias do passo, ou null. */
export function daysError(value: string, all: StepDraft[], key: string): string | null {
  if (!/^-?\d{1,2}$/.test(value.trim())) return 'Informe os dias (negativo é antes do vencimento).'
  const days = Number(value)
  if (days < MIN_DAYS || days > MAX_DAYS) return 'Os dias do passo vão de 30 antes a 90 depois do vencimento.'
  if (all.some((s) => s.key !== key && s.days.trim() !== '' && Number(s.days) === days)) return 'Dois passos no mesmo dia: mude os dias de um deles.'
  return null
}

/** Coloca o campo na posição do cursor (ou no fim). */
export function insertAt(text: string, token: string, start?: number | null, end?: number | null): { text: string; caret: number } {
  const s = start ?? text.length
  const e = end ?? s
  const next = text.slice(0, s) + token + text.slice(e)
  return { text: next, caret: s + token.length }
}

/** Passos na ordem da linha do tempo (dias crescentes); os sem dia válido ficam no fim. */
export function timeline(steps: StepDraft[]): { key: string; days: number; label: string }[] {
  return steps
    .filter((s) => /^-?\d{1,2}$/.test(s.days.trim()))
    .map((s) => ({ key: s.key, days: Number(s.days), label: stepLabel(Number(s.days)) }))
    .sort((a, b) => a.days - b.days)
}

/** Situação do lembrete (padrão 6): enviado, aguardando, adiado, ignorado com motivo, falhou. */
export function noticeStatus(status: string): { label: string; tone: StatusTone } {
  switch (status) {
    case 'Sent': return { label: 'Enviado', tone: 'ok' }
    case 'Queued': return { label: 'Aguardando', tone: 'info' }
    case 'Deferred': return { label: 'Adiado', tone: 'warn' }
    case 'Failed': return { label: 'Falhou', tone: 'crit' }
    default: return { label: 'Ignorado', tone: 'neutral' }
  }
}

/** Mensagem ao ligar (propositivo): quantos títulos recebem lembrete no próximo dia de envio. */
export function nextRunText(count: number, date: string, today: string): string {
  const when = date === today ? 'hoje' : `em ${date.slice(8, 10)}/${date.slice(5, 7)}`
  if (count === 0) return `Nenhum título recebe lembrete ${when}.`
  return count === 1 ? `1 título recebe lembrete ${when}.` : `${count.toLocaleString('pt-BR')} títulos recebem lembrete ${when}.`
}

/** Horários de envio de 30 em 30 minutos, de 07:00 a 20:00 (São Paulo). */
export const SEND_TIMES: string[] = Array.from({ length: 27 }, (_, i) => {
  const minutes = 7 * 60 + i * 30
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${minutes % 60 === 0 ? '00' : '30'}`
})
