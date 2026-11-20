/**
 * Texto da faixa de estado fixa no topo do /pdv (OFF-01 CA 2 e 13). Função pura, para a faixa e os testes falarem a mesma coisa:
 * - sem rede e com fila: "Sem conexão: N vendas a enviar" e a idade da mais antiga;
 * - sem rede e sem fila: "Sem conexão" e a hora da última sincronização;
 * - com rede e com fila: "N vendas a enviar" e a idade da mais antiga (a fila real: a OFF-02 sobe e tira o que o servidor confirmou);
 * - com rede e sem fila: "Sincronizado às HH:MM".
 * Com a janela sem sincronizar da loja (PP-10) vinda da foto, mostra também até quando o caixa vende sem conexão. O bloqueio é da OFF-03.
 */
export type StatusTone = 'ok' | 'warn' | 'crit'

export interface StatusInput {
  online: boolean
  pendingCount: number
  /** ISO do evento mais antigo da fila. */
  oldestPendingAt: string | null
  /** ISO da última sincronização que deu certo. */
  lastSyncAt: string | null
  /** Janela da loja em dias, se a foto trouxe. */
  offlineWindowDays: number | null
  now: Date
  /** OFF-02: a última subida falhou por um motivo que pede ação (relógio, registro); null quando subiu ou só faltou rede. */
  uploadIssue?: string | null
}

export interface StatusView {
  tone: StatusTone
  title: string
  details: string[]
}

const time = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

/** "às 14:05" no mesmo dia; "em 17/11 às 14:05" em outro dia. */
export function whenText(iso: string, now: Date): string {
  const d = new Date(iso)
  return sameDay(d, now) ? `às ${time(iso)}` : `em ${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })} às ${time(iso)}`
}

/** Idade curta: "agora", "há 5 min", "há 2 h 10 min", "há 3 dias". */
export function ageText(iso: string, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - new Date(iso).getTime()) / 60_000))
  if (minutes < 1) return 'agora'
  if (minutes < 60) return `há ${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return minutes % 60 ? `há ${hours} h ${minutes % 60} min` : `há ${hours} h`
  const days = Math.floor(hours / 24)
  return days === 1 ? 'há 1 dia' : `há ${days} dias`
}

const sales = (n: number) => (n === 1 ? '1 venda a enviar' : `${n} vendas a enviar`)

export function statusView(input: StatusInput): StatusView {
  const { online, pendingCount, oldestPendingAt, lastSyncAt, offlineWindowDays, now, uploadIssue } = input
  const details: string[] = []
  if (pendingCount > 0 && oldestPendingAt) details.push(`a mais antiga ${ageText(oldestPendingAt, now)}`)
  if (lastSyncAt && (!online || pendingCount > 0)) details.push(`última sincronização ${whenText(lastSyncAt, now)}`)
  if (!lastSyncAt) details.push('ainda não sincronizou')
  if (lastSyncAt && offlineWindowDays && !online) {
    const until = new Date(new Date(lastSyncAt).getTime() + offlineWindowDays * 86_400_000)
    details.push(`vende sem conexão até ${until.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })} às ${time(until.toISOString())}`)
  }

  if (uploadIssue && online) details.push(uploadIssue)
  if (!online) return { tone: pendingCount > 0 ? 'crit' : 'warn', title: pendingCount > 0 ? `Sem conexão: ${sales(pendingCount)}` : 'Sem conexão', details }
  if (uploadIssue) return { tone: 'crit', title: pendingCount > 0 ? sales(pendingCount) : 'Conectado', details }
  if (pendingCount > 0) return { tone: 'warn', title: sales(pendingCount), details }
  return { tone: 'ok', title: lastSyncAt ? `Sincronizado ${whenText(lastSyncAt, now)}` : 'Conectado', details }
}
