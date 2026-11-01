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
export const STATUS_TONES: Record<string, 'info' | 'ok' | 'neutral'> = { Draft: 'info', Closed: 'ok', Paid: 'ok' }

export const KIND_LABELS: Record<PayrollKind, string> = { Earning: 'Provento', Deduction: 'Desconto', Information: 'Informativa' }

/** Regra da rubrica na folha calculada (FLH-02a). */
export const CALCULATION_LABELS: Record<string, string> = {
  None: 'Sem regra (lançamento avulso)', BaseSalary: 'Salário', SickLeave: 'Afastamento pela previdência', Insalubrity: 'Insalubridade', HazardPay: 'Periculosidade',
  Absence: 'Faltas', Lateness: 'Atrasos', FamilyAllowance: 'Salário-família', TransportVoucher: 'Vale-transporte (desconto)', TransportVoucherSupplied: 'Vale-transporte fornecido (avulso)',
  Inss: 'INSS', Irrf: 'IRRF', Fgts: 'FGTS',
  OvertimeWorkday: 'Horas extras em dia útil', OvertimeRestDay: 'Horas extras em domingo ou folga', OvertimeHoliday: 'Horas extras em feriado', NightShift: 'Adicional noturno',
  Dsr: 'DSR sobre variáveis', DsrLoss: 'Perda do DSR', HourBankPayout: 'Banco de horas pago', HourBankDiscount: 'Banco de horas descontado',
}

/** Rubrica que entra como avulso na folha calculada: sem regra (ou o vale-transporte fornecido). */
export const isManualCalculation = (calculation: string | null | undefined) => !calculation || calculation === 'None' || calculation === 'TransportVoucherSupplied'

export const MODE_LABELS: Record<string, string> = { Entered: 'Folha lançada', Calculated: 'Folha calculada' }

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
  baseInss: 'A rubrica entra na base do INSS. Na folha lançada, a marca é informativa (os valores vêm da contadora); na folha calculada, a base do INSS é a soma das linhas com a marca (o desconto marcado subtrai).',
  informativa: 'Rubrica que aparece no holerite, mas não entra no líquido (ex.: o FGTS do mês, que a empresa deposita).',
  protegidos: 'Os valores da folha são dados pessoais protegidos pela LGPD. Só quem tem a permissão "Ver dados sensíveis" do RH consulta.',
  referencia: 'Quantidade que explica o valor: "30 dias", "10:00 h", "2 faltas". Até 20 caracteres.',
  dataPagamento: 'Vencimento dos títulos de salário. Sugerida pelo tipo: Mensal no 5º dia útil do mês seguinte, adiantamento no dia 20, 13º em 30/11 e 20/12. Dia não útil antecipa.',
  guia: 'A guia vai inteira para o grupo com a maior base do FGTS; a divisão exata fica com a contadora.',
  prevista: 'Folha prevista: os próximos meses da folha (salários, guias, 13º e férias) entram no fluxo de caixa como estimativa, até a folha real fechar.',
  quarentena: 'Conta trocada há pouco fica fora do pagamento automático até alguém do RH revisar com o funcionário.',
  calculada: 'Folha calculada: o sistema calcula salário, adicionais, faltas, afastamento, salário-família, vale-transporte, INSS, IRRF e FGTS pelo cadastro, pelo ponto fechado e pelas tabelas da competência, e mostra a memória de cada valor.',
  simulacao: 'Até a virada, a folha calculada é só simulação: roda em paralelo com a folha lançada (a da contadora), que continua a oficial. Compare as duas; o holerite calculado sai só como prévia e nunca vai para o funcionário.',
  retificacao: 'Corrigir uma folha fechada é uma retificação: nasce uma folha complementar em rascunho, ligada à fechada, só com as diferenças por funcionário e rubrica. A fechada nunca muda.',
  memoria: 'Como o sistema chegou ao valor: a base, a faixa, a tabela e a vigência usadas, os parâmetros e as entradas do cadastro e do ponto.',
  simplificado: 'Desconto simplificado: no IRRF, em vez de deduzir o INSS, os dependentes e a pensão, a lei permite deduzir um valor fixo por mês. O sistema calcula as duas formas e usa a que dá o menor imposto.',
  regra: 'A regra diz o que o sistema calcula nesta rubrica na folha calculada. Rubrica sem regra (prêmio, adiantamento, pensão, consignado) é lançada à mão como avulso, com o motivo.',
  deduzIrrf: 'O desconto entra na dedução legal do IRRF (como a pensão alimentícia judicial), comparada com o desconto simplificado.',
  dsr: 'DSR sobre variáveis: o descanso semanal remunerado sobre as horas extras, o adicional noturno e o banco pago. Variáveis ÷ dias úteis do mês × (domingos + feriados), com os feriados do calendário padrão.',
  perdaDsr: 'Perda do DSR: sem a semana inteira trabalhada, a falta injustificada (sem abono aprovado) tira o repouso daquela semana, um dia de salário (Lei 605/1949, art. 6º).',
  bancoHoras: 'Banco de horas: as extras de quem tem acordo vão para o saldo, que não vence sozinho. Só sai por decisão do RH na folha: "Pagar" (como hora extra de dia útil) ou "Descontar" (o saldo negativo, pelo valor da hora), sempre com o motivo.',
  avulso: 'Lançamento avulso: rubrica sem regra (prêmio, adiantamento, pensão, consignado, vale-transporte fornecido). Entra nas bases pelas marcas da rubrica e fica a cada recálculo. O motivo é obrigatório.',
}

/** Minutos do banco de horas como na tela do espelho: "50 h", "10 h 30 min", "−5 h"; com o sinal "+" quando pedido (FLH-02b). */
export function hoursLabel(minutes: number, signed = false) {
  const m = Math.abs(minutes)
  const h = Math.floor(m / 60)
  const rest = m % 60
  const text = h > 0 && rest === 0 ? `${h} h` : h > 0 ? `${h} h ${rest} min` : `${rest} min`
  return (minutes < 0 ? '−' : signed && minutes > 0 ? '+' : '') + text
}

/** "10", "10,5" ou "10:30" (horas) em minutos; null se não for um número de horas maior que zero (FLH-02b). */
export function parseHours(text: string): number | null {
  const t = text.trim()
  const clock = /^(\d{1,4}):([0-5]\d)$/.exec(t)
  if (clock) {
    const minutes = Number(clock[1]) * 60 + Number(clock[2])
    return minutes > 0 ? minutes : null
  }

  if (!/^\d{1,4}([.,]\d{1,2})?$/.test(t)) return null
  const minutes = Math.round(Number(t.replace(',', '.')) * 60)
  return minutes > 0 ? minutes : null
}
