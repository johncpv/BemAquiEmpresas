/** Formatação em pt-BR (CLAUDE.md): dinheiro em R$ e datas exibidas em America/Sao_Paulo. */
const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const brlNoCents = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0, minimumFractionDigits: 0 })

export function formatMoney(value: number, cents = true): string {
  return (cents ? brl : brlNoCents).format(value)
}

const dateTime = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', dateStyle: 'short', timeStyle: 'short' })
const date = new Intl.DateTimeFormat('pt-BR', { timeZone: 'America/Sao_Paulo', dateStyle: 'short' })

/** Instante UTC (timestamptz) mostrado no horário de Brasília. */
export function formatDateTime(value: string | Date): string {
  return dateTime.format(typeof value === 'string' ? new Date(value) : value)
}

/** Data de negócio (date, sem hora): não converte fuso, para não mudar o dia. */
export function formatDate(value: string): string {
  const [y, m, d] = value.split('-').map(Number)
  return date.format(new Date(Date.UTC(y, m - 1, d, 12)))
}
