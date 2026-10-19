/** Mesma fórmula do servidor (CAD-04a CA 3): salário × (1 + % encargos) ÷ horas mensais; sem salário ou horas, 0. */
export function hourlyCost(salary: number | null, chargesPct: number, monthlyHours: number) {
  if (!salary || salary <= 0 || monthlyHours <= 0) return 0
  return (salary * (1 + chargesPct / 100)) / monthlyHours
}

export const average = (min: number | null, max: number | null) => (min !== null && max !== null ? Math.round(((min + max) / 2) * 100) / 100 : min ?? max)

export const WEEKDAY_LABELS = ['', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado', 'Domingo']

export const MONTHS = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']

export const formatHours = (h: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(h)

/** Horas de um dia: saída − entrada − intervalo (CA 15). */
export function dayMinutes(start: string, end: string, breakMinutes: number) {
  const m = (t: string) => { const x = /^(\d{1,2}):(\d{2})$/.exec(t); return x ? Number(x[1]) * 60 + Number(x[2]) : null }
  const a = m(start)
  const b = m(end)
  return a === null || b === null || b <= a ? 0 : Math.max(0, b - a - (breakMinutes || 0))
}

export const DICAS = {
  custoHora: 'Quanto custa uma hora deste cargo, já com os encargos: salário de referência × (1 + % de encargos) ÷ horas mensais. É o valor usado no custo das ordens de serviço.',
  ajustado: 'O custo-hora foi informado à mão e não acompanha mudança do salário nem do % de encargos. Use "Voltar ao calculado" para seguir a fórmula de novo.',
  cbo: 'Classificação Brasileira de Ocupações: o código de 6 dígitos do cargo, usado no eSocial.',
  horas: 'Horas de trabalho no mês pelo contrato. Vêm da jornada (horas semanais × 5) e podem ser trocadas no cargo.',
  protegidos: 'A faixa salarial é dado pessoal protegido pela LGPD. Só quem tem a permissão "Ver dados sensíveis" do RH consulta.',
}
