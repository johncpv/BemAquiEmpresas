/** Folha lançada (RH-02a): rótulos, competência e os totais com a mesma regra do servidor (ADR 0019 §9). */

export type PayrollKind = 'Earning' | 'Deduction' | 'Information'

export const RUN_TYPES = [
  { value: 'Monthly', label: 'Mensal' },
  { value: 'Advance', label: 'Adiantamento' },
  { value: 'Vacation', label: 'Férias' },
  { value: 'Thirteenth1', label: '13º 1ª parcela' },
  { value: 'Thirteenth2', label: '13º 2ª parcela' },
  { value: 'Termination', label: 'Rescisão' },
] as const

export type RunType = (typeof RUN_TYPES)[number]['value']

export const typeLabel = (type: string) => RUN_TYPES.find((t) => t.value === type)?.label ?? type

/** Férias e Rescisão: quem lança escolhe os funcionários (CA 4). */
export const choosesEmployees = (type: string) => type === 'Vacation' || type === 'Termination'

export const STATUS_LABELS: Record<string, string> = { Draft: 'Em lançamento', Closed: 'Fechada', Paid: 'Paga' }
export const STATUS_TONES: Record<string, 'info' | 'ok' | 'neutral'> = { Draft: 'info', Closed: 'ok', Paid: 'neutral' }

export const KIND_LABELS: Record<PayrollKind, string> = { Earning: 'Provento', Deduction: 'Desconto', Information: 'Informativa' }

export const CHARGE_LABELS: Record<string, string> = { Fgts: 'FGTS', Inss: 'INSS', Irrf: 'IRRF retido', Union: 'Contribuição sindical', Other: 'Outros' }

const MONTHS = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']

/** "2026-10-01" ou "2026-10" vira "outubro de 2026". */
export function periodLabel(period: string) {
  const [y, m] = period.split('-').map(Number)
  return `${MONTHS[m - 1]} de ${y}`
}

/** Competência proposta (R-PRP-1): o mês corrente em "aaaa-mm". */
export function currentPeriod(today = new Date()) {
  return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`
}

const round2 = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100

export interface LineValue {
  kind: PayrollKind | null
  amount: number | null
}

/** Totais ao vivo (CA 6): proventos − descontos; a informativa fica fora do líquido. */
export function totals(lines: LineValue[]) {
  let gross = 0
  let deductions = 0
  for (const l of lines) {
    const amount = l.amount && l.amount > 0 ? round2(l.amount) : 0
    if (l.kind === 'Earning') gross += amount
    else if (l.kind === 'Deduction') deductions += amount
  }
  gross = round2(gross)
  deductions = round2(deductions)
  return { gross, deductions, net: round2(gross - deductions) }
}

/** Variação em % (CA 11), com o sinal, em pt-BR. */
export const formatPct = (v: number) => `${v > 0 ? '+' : ''}${new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 }).format(v)}%`

export const CSV_HEADER = 'matricula_ou_cpf;codigo_rubrica;referencia;valor'

export const PAYROLL_DICAS = {
  lancada: 'Folha lançada: os valores vêm prontos da contadora (digitados ou importados). O sistema soma e confere, mas não calcula INSS, IRRF, FGTS, férias nem 13º.',
  aConfirmar: 'Valor sugerido pelo sistema. Confira com a contadora e clique em "Marcar como conferido". Se o valor mudar depois, volta para "A confirmar".',
  baseInss: 'Marca informativa: a rubrica entra na base do INSS. Nesta fase os valores vêm da contadora; a marca será usada quando a folha for calculada pelo sistema.',
  informativa: 'Rubrica que aparece no holerite, mas não entra no líquido (ex.: o FGTS do mês, que a empresa deposita).',
  protegidos: 'Os valores da folha são dados pessoais protegidos pela LGPD. Só quem tem a permissão "Ver dados sensíveis" do RH consulta.',
  referencia: 'Quantidade que explica o valor: "30 dias", "10:00 h", "2 faltas". Até 20 caracteres.',
}
