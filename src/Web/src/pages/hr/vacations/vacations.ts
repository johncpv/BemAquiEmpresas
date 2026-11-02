/** Férias (FLH-03): rótulos, tons, dicas e as contas do diálogo "Programar férias", sem chamada à API. */
import type { StatusTone } from '@/components/ui/status-chip'

export const VACATION_DICAS = {
  concessivo: 'Os 12 meses depois do fim do período aquisitivo em que a empresa precisa dar as férias. Férias gozadas depois dele são pagas em dobro (CLT art. 137).',
  abono: 'Venda de até 1/3 dos dias de férias: o funcionário trabalha esses dias e recebe o valor deles com o 1/3. O abono e o seu 1/3 não têm INSS, FGTS nem IRRF.',
  fracionamento: 'As férias de um período podem ser divididas em até 3 partes: uma com pelo menos 14 dias e as outras com pelo menos 5 (CLT art. 134). Os números estão em Configurações → Parâmetros → RH.',
  aquisitivo: 'Cada 12 meses de trabalho desde a admissão dão direito a férias. Faltas injustificadas diminuem os dias, e afastamento longo pela previdência reinicia o período.',
} as const

const TONES: Record<string, StatusTone> = { ok: 'ok', warn: 'warn', crit: 'crit', info: 'info' }

/** Tom do chip do período, como a API manda ("Vence em" é alerta, "Vencido" é crítico). */
export const periodTone = (tone: string): StatusTone => TONES[tone] ?? 'neutral'

export const SCHEDULE_TONES: Record<string, StatusTone> = { Scheduled: 'info', Paid: 'ok', Taken: 'neutral', Cancelled: 'neutral' }

const utc = (iso: string, plus = 0) => {
  const [y, m, d] = iso.split('-').map(Number)
  return Date.UTC(y, m - 1, d + plus)
}

/** Data ISO + n dias (sem fuso: datas de negócio). */
export const addDays = (iso: string, days: number): string => new Date(utc(iso, days)).toISOString().slice(0, 10)

/** Fim do gozo: início + dias − 1. */
export const vacationEnd = (start: string, days: number) => addDays(start, Math.max(days, 1) - 1)

/** Retorno ao trabalho: o dia seguinte ao fim. */
export const vacationReturn = (start: string, days: number) => addDays(start, Math.max(days, 1))

/** Dias entre hoje e o início (para o aviso de 30 dias). */
export const daysUntil = (today: string, start: string): number => Math.round((utc(start) - utc(today)) / 86_400_000)

/** Erro local do diálogo, antes da API (a validação completa com os parâmetros é do servidor). */
export function scheduleDraftError(d: { periodId: string; startsOn: string; days: string; soldDays: string }, maxSold: number, balance: number): string | null {
  if (!d.periodId) return 'Escolha o período aquisitivo.'
  if (!d.startsOn) return 'Informe o início das férias.'
  const days = Number(d.days)
  const sold = Number(d.soldDays || '0')
  if (!Number.isInteger(days) || days < 1) return 'Informe os dias de férias.'
  if (!Number.isInteger(sold) || sold < 0) return 'Os dias vendidos não podem ser negativos.'
  if (sold > maxSold) return `O abono pecuniário é de até ${maxSold} dias.`
  if (days + sold > balance) return `O período tem ${balance} dias de saldo.`
  return null
}
