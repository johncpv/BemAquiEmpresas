import type { StatusTone } from '@/components/ui/status-chip'

/** Situação do dia no espelho (RH-01c), como a API devolve. */
export type DayStatus =
  | 'Normal' | 'Overtime' | 'Late' | 'Absent' | 'Justified' | 'Compensated' | 'OnLeave' | 'Holiday' | 'DayOff' | 'Inconsistent' | 'NotEmployed' | 'Upcoming'

/** Chip da situação do dia (estado na forma): falta é crítica, inconsistência e atraso são alerta, feriado e folga neutros. */
export function dayTone(status: string): StatusTone {
  switch (status) {
    case 'Normal':
    case 'Justified':
      return 'ok'
    case 'Overtime':
    case 'Compensated':
    case 'OnLeave':
      return 'info'
    case 'Late':
    case 'Inconsistent':
      return 'warn'
    case 'Absent':
      return 'crit'
    default:
      return 'neutral'
  }
}

/** Tom do chip da linha da visão geral (a API manda ok, alert, critical ou info). */
export function situationTone(tone: string): StatusTone {
  return tone === 'alert' ? 'warn' : tone === 'critical' ? 'crit' : tone === 'info' ? 'info' : 'ok'
}

/** "8 h 48 min", "45 min", "—" no zero; negativo com "−". */
export function minutes(value: number | null | undefined, zero = '—'): string {
  if (value === null || value === undefined || value === 0) return zero
  const sign = value < 0 ? '−' : ''
  const m = Math.abs(value)
  const h = Math.floor(m / 60)
  const rest = m % 60
  return sign + (h > 0 && rest === 0 ? `${h} h` : h > 0 ? `${h} h ${rest} min` : `${rest} min`)
}

/** Saldo do banco com sinal: "+3 h 20 min". */
export function signed(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return value > 0 ? `+${minutes(value)}` : minutes(value, '0 min')
}

/** Período "2026-10" da data "2026-10-01" ou do dia de hoje. */
export function monthOf(day: string): string {
  return day.slice(0, 7)
}

/** Mês anterior e seguinte de "2026-10". */
export function shiftMonth(period: string, delta: number): string {
  const [y, m] = period.split('-').map(Number)
  const d = new Date(Date.UTC(y, m - 1 + delta, 1))
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`
}

/** "seg 05/10" do dia (data de negócio, sem fuso). */
export function shortDay(day: string): string {
  const [y, m, d] = day.split('-').map(Number)
  const weekday = new Intl.DateTimeFormat('pt-BR', { weekday: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, d, 12))).replace('.', '')
  return `${weekday} ${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}`
}

/** Dicas dos termos do espelho (CLAUDE.md, ajuda contextual). */
export const TIMESHEET_DICAS = {
  tolerancia: 'Variação de horário que não conta como extra nem atraso: até 5 minutos por marcação e 10 no dia, salvo a convenção coletiva. O valor está em Configurações → Parâmetros → RH, a confirmar com a contadora.',
  banco: 'Com o banco de horas, a diferença do dia (para mais ou para menos) vai para o saldo do funcionário com acordo, sem vencer sozinho. A decisão de pagar fica na folha.',
  acordo: 'O banco de horas só vale com acordo assinado com o funcionário (CLT art. 59). Informe a data do acordo: o banco vale a partir dela.',
  periodo: 'Intervalo de dias apurado no espelho. O padrão é o mês civil; a empresa pode usar outro corte (por exemplo, de 21 a 20) em Configurações → Parâmetros → RH.',
  inconsistente: 'Dia com marcação ímpar, fora de sequência ou intervalo menor que o da jornada. O trabalhado conta só os pares fechados. Lance a marcação que faltou ou marque o dia como revisado, com comentário, antes de fechar o período.',
} as const

/** Linha da semana: os dias em ordem com o separador de semana (segunda-feira). */
export function isMonday(day: string): boolean {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d, 12)).getUTCDay() === 1
}
