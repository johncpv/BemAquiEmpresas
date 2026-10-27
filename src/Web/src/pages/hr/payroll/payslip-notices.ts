/** Holerite em PDF e aviso aos funcionários (RH-02b): rótulos da situação do aviso e textos. */
import { formatInstantDate } from '@/lib/format'

export interface NoticeLike {
  status: string
  notifiedAt?: string | null
  sentAt?: string | null
  error?: string | null
}

type Tone = 'ok' | 'warn' | 'crit' | 'info' | 'neutral'

/** Situação do aviso (CA 9): Na fila, Avisado em {data}, Falhou: {motivo}, Sem usuário, Desligado. */
export function noticeLabel(n: NoticeLike): { text: string; tone: Tone } {
  switch (n.status) {
    case 'Queued':
      return { text: 'Na fila', tone: 'info' }
    case 'Sent':
      return { text: `Avisado em ${formatInstantDate(n.sentAt ?? n.notifiedAt ?? '')}`, tone: 'ok' }
    case 'Failed':
      return { text: `Falhou: ${n.error ?? 'envio não concluído'}`, tone: 'crit' }
    case 'NoUser':
      return { text: 'Sem usuário', tone: 'warn' }
    case 'Terminated':
      return { text: 'Desligado', tone: 'neutral' }
    default:
      return { text: 'Não avisado', tone: 'neutral' }
  }
}

/** "1 aviso" / "3 avisos". */
export const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`

/** Sugestão do convite (CA 7, propositivo): guardada só na sessão do navegador, nunca na URL. */
export const INVITE_SUGGESTION_KEY = 'bq-convite-sugerido'

export function saveInviteSuggestion(name: string, email: string | null | undefined) {
  try {
    sessionStorage.setItem(INVITE_SUGGESTION_KEY, JSON.stringify({ name, email: email ?? '' }))
  } catch {
    // sem armazenamento: o convite abre vazio
  }
}

export function clearInviteSuggestion() {
  try {
    sessionStorage.removeItem(INVITE_SUGGESTION_KEY)
  } catch {
    // sem armazenamento: nada a limpar
  }
}

/** Lê sem apagar (o inicializador do estado roda duas vezes no modo estrito); quem abre o convite limpa depois. */
export function peekInviteSuggestion(): { name: string; email: string } | null {
  try {
    const raw = sessionStorage.getItem(INVITE_SUGGESTION_KEY)
    if (!raw) return null
    const v = JSON.parse(raw) as { name?: unknown; email?: unknown }
    return typeof v.name === 'string' ? { name: v.name, email: typeof v.email === 'string' ? v.email : '' } : null
  } catch {
    return null
  }
}

export const PAYSLIP_DICAS = {
  canal: 'O holerite fica só aqui, com o seu login; o e-mail só avisa.',
  aviso: 'O e-mail só avisa que o holerite está disponível, sem anexo e sem valor. O funcionário vê o holerite em "Meus holerites", com o próprio login.',
  alterado: 'O holerite mudou depois do aviso (a folha foi reaberta e fechada de novo). Avise o funcionário de novo.',
}
