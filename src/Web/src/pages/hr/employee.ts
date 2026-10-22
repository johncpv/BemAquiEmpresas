import type { EmployeeLeaveType, EmployeeStatus } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatMoney } from '@/lib/format'

export const STATUS_LABELS: Record<EmployeeStatus, string> = { Active: 'Ativo', OnLeave: 'Afastado', Terminated: 'Desligado' }

/** Afastado em âmbar (alerta), desligado neutro (CAD-04c1). */
export const STATUS_TONES: Record<EmployeeStatus, StatusTone> = { Active: 'ok', OnLeave: 'warn', Terminated: 'neutral' }

/** Tipos de afastamento (CAD-04c1, decisão do PO de 21/10/2026). Férias ficam para a folha. */
export const LEAVE_TYPE_LABELS: Record<EmployeeLeaveType, string> = {
  Illness: 'Doença',
  WorkAccident: 'Acidente de trabalho',
  Maternity: 'Maternidade',
  Paternity: 'Paternidade',
  Military: 'Serviço militar',
  Other: 'Outros',
}

export const LEAVE_REASON_MAX = 200

export interface LeaveDraft { type: EmployeeLeaveType | ''; startDate: string; expectedEndDate: string; reason: string }

/** Diálogo vem com o início em hoje (propositivo, CAD-04c1). */
export const emptyLeaveDraft = (today: string): LeaveDraft => ({ type: '', startDate: today, expectedEndDate: '', reason: '' })

/** Primeiro erro do afastamento, na ordem dos campos; o servidor confere de novo (CA 1 e 5). */
export function leaveDraftError(d: LeaveDraft): string | undefined {
  if (!d.type) return 'Escolha o tipo do afastamento.'
  if (!d.startDate) return 'Informe o início do afastamento.'
  if (!d.expectedEndDate) return 'Informe o fim previsto do afastamento.'
  if (d.expectedEndDate < d.startDate) return 'O fim previsto precisa ser igual ou depois do início.'
  if (d.reason.trim().length > LEAVE_REASON_MAX) return 'O motivo aceita até 200 caracteres.'
  return undefined
}

/** Volta prevista: o dia seguinte ao fim previsto (aaaa-mm-dd). */
export function returnDate(expectedEndDate: string) {
  const [y, m, d] = expectedEndDate.split('-').map(Number)
  const next = new Date(Date.UTC(y, m - 1, d + 1))
  return next.toISOString().slice(0, 10)
}

export const STATUS_OPTIONS = (Object.keys(STATUS_LABELS) as EmployeeStatus[]).map((value) => ({ value, label: STATUS_LABELS[value] }))

export const EMPLOYEE_DICAS = {
  protegidos: 'Só quem tem a permissão Ver dados sensíveis vê estes dados.',
  pis: 'Número do PIS/PASEP/NIT, com 11 dígitos. O último é o dígito verificador.',
  ctps: 'Carteira de Trabalho: número e série. Na carteira digital, o número é o CPF.',
  matricula: 'Código do funcionário na empresa, se houver. É único e aparece nas listas e no ponto.',
  afastado: 'Há um afastamento vigente hoje. A situação volta a Ativo sozinha no dia seguinte ao fim previsto. O tipo e o motivo só aparecem com a permissão Ver dados sensíveis.',
  motivoAfastamento: 'Texto curto para o RH se lembrar do caso. Não informe CID nem diagnóstico: dado de saúde é sensível pela LGPD.',
}

/** Rascunho da criação (CAD-04b CA 1): salário e jornada vêm do cargo enquanto não forem editados. */
export interface EmployeeDraft {
  name: string
  cpf: string
  birthDate: string
  admissionDate: string
  positionId: string
  workScheduleId: string
  baseSalary: string | null
  registration: string
  touched: { salary: boolean; schedule: boolean }
}

export const emptyEmployeeDraft = (today: string): EmployeeDraft => ({
  name: '', cpf: '', birthDate: '', admissionDate: today, positionId: '', workScheduleId: '', baseSalary: null, registration: '', touched: { salary: false, schedule: false },
})

export interface PositionChoice { id: string; workScheduleId: string | null; referenceSalary: number | null }

/** Escolha do cargo: sugere a jornada e o salário de referência, sem sobrescrever o que o usuário já trocou. */
export function withPosition(draft: EmployeeDraft, position: PositionChoice | undefined): EmployeeDraft {
  const next = { ...draft, positionId: position?.id ?? '' }
  if (!draft.touched.schedule) next.workScheduleId = position?.workScheduleId ?? ''
  if (!draft.touched.salary) next.baseSalary = position?.referenceSalary === null || position?.referenceSalary === undefined ? null : String(position.referenceSalary)
  return next
}

export function employeeDraftErrors(d: EmployeeDraft): Partial<Record<keyof EmployeeDraft, string>> {
  const errors: Partial<Record<keyof EmployeeDraft, string>> = {}
  if (!d.name.trim()) errors.name = 'Informe o nome.'
  if (d.cpf.length !== 11) errors.cpf = 'Informe o CPF com 11 dígitos.'
  if (!d.birthDate) errors.birthDate = 'Informe a data de nascimento.'
  if (!d.admissionDate) errors.admissionDate = 'Informe a data de admissão.'
  if (!d.positionId) errors.positionId = 'Escolha o cargo.'
  if (d.baseSalary === null || d.baseSalary === '' || Number(d.baseSalary) <= 0) errors.baseSalary = 'Informe o salário base.'
  return errors
}

/** Hoje em America/Sao_Paulo, como aaaa-mm-dd. */
export const todayIso = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())

/** Aviso âmbar do salário fora da faixa do cargo (CA 5); sem faixa completa, nada. */
export function outsideRange(salary: string | null, range: { salaryMin: number | null; salaryMax: number | null } | null) {
  if (!range || range.salaryMin === null || range.salaryMax === null || salary === null || salary === '') return undefined
  const value = Number(salary)
  return value < range.salaryMin || value > range.salaryMax ? `Salário fora da faixa do cargo (${formatMoney(range.salaryMin)}–${formatMoney(range.salaryMax)}).` : undefined
}
