import type { EmployeeLeaveState, AbsenceReason as GeneratedLeaveType, EmployeeStatus, LeaveDetail } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'
import { formatMoney } from '@/lib/format'

export const STATUS_LABELS: Record<EmployeeStatus, string> = { Active: 'Ativo', OnLeave: 'Afastado', Terminated: 'Desligado' }

/** Afastado em âmbar (alerta), desligado neutro (CAD-04c1). */
export const STATUS_TONES: Record<EmployeeStatus, StatusTone> = { Active: 'ok', OnLeave: 'warn', Terminated: 'neutral' }

/**
 * Lista única de ausências (RH-01b CA 7, PO 21/10/2026): o afastamento usa os motivos que valem para afastamento; o abono do ponto, os que
 * valem para abono. Doença virou "Atestado médico" e Outros, "Falta justificada". Férias ficam para a folha.
 */
/** O cliente gerado admite nulo no enum; nas telas o tipo é sempre um da lista. */
export type LeaveType = NonNullable<GeneratedLeaveType>

export const LEAVE_TYPE_LABELS: Record<LeaveType, string> = {
  MedicalCertificate: 'Atestado médico',
  WorkAccident: 'Acidente de trabalho',
  Maternity: 'Maternidade',
  Paternity: 'Paternidade',
  Military: 'Serviço militar',
  AttendanceNote: 'Declaração de comparecimento',
  TimeBankOff: 'Folga compensada pelo banco de horas',
  JustifiedAbsence: 'Falta justificada',
  UnjustifiedAbsence: 'Falta não justificada',
}

/** Os motivos que valem para afastamento, na ordem da lista única. */
export const LEAVE_TYPES: readonly LeaveType[] = ['MedicalCertificate', 'WorkAccident', 'Maternity', 'Paternity', 'Military', 'JustifiedAbsence']

export const LEAVE_REASON_MAX = 200

/** Atestado médico e acidente de trabalho são dado de saúde: só com hr.view_health (CAD-04c1b, PO 22/10/2026). */
export const HEALTH_LEAVE_TYPES: readonly LeaveType[] = ['MedicalCertificate', 'WorkAccident']

export const isHealthLeave = (type: LeaveType | '' | null | undefined) => !!type && HEALTH_LEAVE_TYPES.includes(type)

/** Tipos que o usuário pode escolher: sem hr.view_health, os de saúde não aparecem (o servidor recusa com 403). */
export const leaveTypeOptions = (canHealth: boolean) =>
  LEAVE_TYPES.filter((t) => canHealth || !isHealthLeave(t)).map((value) => ({ value, label: LEAVE_TYPE_LABELS[value] }))

/** Estado do afastamento no dia (CAD-04c1b): o servidor calcula; em curso em âmbar, previsto informativo. */
export const LEAVE_STATE_LABELS: Record<EmployeeLeaveState, string> = { Scheduled: 'Previsto', Current: 'Vigente', Ended: 'Encerrado', Cancelled: 'Cancelado' }

export const LEAVE_STATE_TONES: Record<EmployeeLeaveState, StatusTone> = { Scheduled: 'info', Current: 'warn', Ended: 'neutral', Cancelled: 'neutral' }

/**
 * Título do afastamento na linha do tempo: sem hr.view_health o servidor não manda o tipo de nenhum afastamento
 * (healthProtected, PO 22/10/2026), e aparece só "Afastado".
 */
export const leaveTitle = (leave: Pick<LeaveDetail, 'type' | 'healthProtected'>) =>
  leave.healthProtected || !leave.type ? 'Afastado' : `Afastamento: ${LEAVE_TYPE_LABELS[leave.type]}`

/** O que cada estado permite (CAD-04c1b CA 1, 4 e 6): editar previsto ou em curso; encerrar em curso; cancelar previsto. */
export function leaveActions(leave: Pick<LeaveDetail, 'state' | 'healthProtected'>, canHealth: boolean) {
  const open = leave.state === 'Scheduled' || leave.state === 'Current'
  return {
    edit: open && (canHealth || !leave.healthProtected),
    end: leave.state === 'Current',
    cancel: leave.state === 'Scheduled',
  }
}

export interface LeaveDraft { type: LeaveType | ''; startDate: string; expectedEndDate: string; reason: string }

/** Diálogo vem com o início em hoje (propositivo, CAD-04c1). */
export const emptyLeaveDraft = (today: string): LeaveDraft => ({ type: '', startDate: today, expectedEndDate: '', reason: '' })

/** Edição vem com o que está gravado (CAD-04c1b CA 1). */
export const leaveDraftFrom = (leave: Pick<LeaveDetail, 'type' | 'startDate' | 'expectedEndDate' | 'reason'>): LeaveDraft =>
  ({ type: leave.type ?? '', startDate: leave.startDate, expectedEndDate: leave.expectedEndDate, reason: leave.reason ?? '' })

/** Dia anterior (aaaa-mm-dd): na volta antes do previsto, o último dia afastado. */
export function previousDay(day: string) {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d - 1)).toISOString().slice(0, 10)
}

/** Encerrar antes (CAD-04c1b CA 4): a volta fica depois do início, até o fim previsto e nunca depois de hoje. O servidor confere de novo. */
export function leaveReturnError(leave: Pick<LeaveDetail, 'startDate' | 'expectedEndDate'>, back: string, today: string): string | undefined {
  if (!back) return 'Informe a data da volta.'
  if (back <= leave.startDate) return 'A volta precisa ser depois do início do afastamento.'
  if (back > leave.expectedEndDate) return 'A volta precisa ser até o fim previsto. Para estender o afastamento, use "Editar".'
  if (back > today) return 'A volta não pode ser depois de hoje.'
  return undefined
}

/** Cancelar (CAD-04c1b CA 6): motivo obrigatório, até 200, sem CID. */
export function leaveCancelError(reason: string): string | undefined {
  if (!reason.trim()) return 'Informe o motivo do cancelamento.'
  if (reason.trim().length > LEAVE_REASON_MAX) return 'O motivo aceita até 200 caracteres.'
  return undefined
}

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
  afastado: 'Há um afastamento vigente hoje. A situação volta a Ativo sozinha no dia seguinte ao fim previsto. As datas aparecem com a permissão Ver dados sensíveis; o tipo e o motivo, só com Ver dados de saúde.',
  motivoAfastamento: 'Texto curto para o RH se lembrar do caso. Não informe CID nem diagnóstico: dado de saúde é sensível pela LGPD.',
  saude: 'O tipo e o motivo dos afastamentos só aparecem para quem tem a permissão Ver dados de saúde, porque podem revelar dado de saúde (LGPD). Sem ela, você vê só as datas e não escolhe Atestado médico nem Acidente de trabalho.',
  volta: 'Primeiro dia de trabalho depois do afastamento. O último dia afastado passa a ser a véspera da volta.',
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
