import type { MilestonePreview, ProjectDetail } from '@/api/generated/model'
import { formatDate, formatMoney } from '@/lib/format'

/** Passos do projeto de serviço (EXE-01a): o nome da rota da prévia é o mesmo. */
export type ServiceStep = 'start' | 'delivery' | 'acceptance' | 'revert'

/** Verbo do botão e o particípio do aviso de sucesso (EXE-01b CA 7): o mesmo verbo nos dois. */
export const STEP_VERBS: Record<ServiceStep, { verb: string; done: string; title: string }> = {
  start: { verb: 'Iniciar execução', done: 'Execução iniciada', title: 'Iniciar a execução?' },
  delivery: { verb: 'Registrar entrega', done: 'Entrega registrada', title: 'Registrar a entrega?' },
  acceptance: { verb: 'Registrar aceite do cliente', done: 'Aceite registrado', title: 'Registrar o aceite do cliente?' },
  revert: { verb: 'Desfazer aceite', done: 'Aceite desfeito', title: 'Desfazer o aceite?' },
}

/** Permissão de cada passo (EXE-01a): o servidor decide; o front só esconde o botão. */
export const STEP_PERMISSION: Record<ServiceStep, string> = {
  start: 'projects.edit',
  delivery: 'projects.approve',
  acceptance: 'projects.register_acceptance',
  revert: 'projects.register_acceptance',
}

/** Quem pode, quando o usuário não pode (EXE-01b CA 2). */
export const WHO_CAN: Record<'start' | 'delivery' | 'acceptance', string> = {
  start: 'Quem inicia: usuários com permissão de editar projetos.',
  delivery: 'Quem registra a entrega: usuários com permissão de aprovar projetos.',
  acceptance: 'Quem registra o aceite: usuários com a permissão Registrar aceite do cliente.',
}

export const SERVICE_DICAS = {
  aceite: 'O cliente confirma que recebeu o serviço. Se o contrato liga uma parcela ao aceite, ela passa a valer para cobrança.',
  aguardando: 'O serviço foi entregue e o contrato liga uma parcela ao aceite do cliente. Ela só passa a valer para cobrança quando o aceite for registrado.',
  legenda: 'P Planejado · I Em execução · E Entregue · A Aceito · C Encerrado. O A aparece como traço quando o contrato não pede o aceite do cliente e ele não foi registrado.',
} as const

export const REASON_MAX = 200

type Can = (permission: string) => boolean

export interface NextStep {
  /** Botão primário (um só); null quando não há passo ou o usuário não pode. */
  primary: ServiceStep | null
  /** Linha de contexto, com o "quem pode" quando falta a permissão. */
  context: string
  /** Chip âmbar "Aguardando aceite do cliente" (entregue sem aceite, com aceite exigido). */
  awaitingAcceptance: boolean
  /** Ações de "Mais ações" (CA 3 e 9). */
  more: ServiceStep[]
}

const date = (iso: string | null | undefined) => (iso ? formatDate(iso) : '—')

/** "libera a parcela Aceite (R$ 4.000,00)" pela prévia do servidor; sem parcela, null. */
export function releasesText(preview: MilestonePreview | null | undefined): string | null {
  const lines = preview?.lines ?? []
  if (lines.length === 0) return null
  const name = (l: MilestonePreview['lines'][number]) => (l.compatibility ? l.label : l.description) + (typeof l.amount === 'number' ? ` (${formatMoney(l.amount)})` : '')
  if (lines.length === 1) return `a parcela ${name(lines[0])}`
  return `as parcelas ${lines.slice(0, -1).map(name).join(', ')} e ${name(lines[lines.length - 1])}`
}

/**
 * Tabela fixa do próximo passo (EXE-01b CA 1 a 3, 9): um botão primário por situação, com a linha de contexto. O aceite vigente vem do DTO
 * (`acceptance`/`acceptedOn`); o encerramento usa a data de conclusão do contrato quando há.
 */
export function nextStep(p: ProjectDetail, can: Can, opts: { acceptancePreview?: MilestonePreview | null; closedOn?: string | null } = {}): NextStep {
  const accepted = !!p.acceptance || !!p.acceptedOn
  const active = p.contractStatus === 'Active' && p.serviceExecution !== false
  const result: NextStep = { primary: null, context: '', awaitingAcceptance: false, more: [] }
  const offer = (step: 'start' | 'delivery' | 'acceptance', context: string) => {
    if (!active) {
      result.context = context
      return
    }
    if (can(STEP_PERMISSION[step])) {
      result.primary = step
      result.context = context
    } else {
      result.context = `${context} ${WHO_CAN[step]}`
    }
  }

  switch (p.status) {
    case 'Planned':
      offer('start', 'Projeto planejado. Quando começar o trabalho, inicie a execução.')
      if (active && can(STEP_PERMISSION.delivery)) result.more.push('delivery')
      break
    case 'InProgress':
    case 'Fabricated':
    case 'Installing':
      offer('delivery', `Em execução desde ${date(p.startedOn)}.`)
      break
    case 'Delivered':
      if (accepted) {
        result.context = `Aceito em ${date(p.acceptedOn ?? p.acceptance?.occurredOn)}. O projeto encerra quando a última parcela for recebida.`
        if (active && can(STEP_PERMISSION.revert)) result.more.push('revert')
      } else {
        const releases = releasesText(opts.acceptancePreview)
        offer('acceptance', releases ? `Entregue em ${date(p.deliveredOn)}. Falta o aceite do cliente; libera ${releases}.` : `Entregue em ${date(p.deliveredOn)}. Falta o aceite do cliente.`)
        result.awaitingAcceptance = !!p.acceptanceRequired
      }
      break
    case 'Closed':
      result.context = opts.closedOn ? `Encerrado em ${date(opts.closedOn)}.` : 'Encerrado.'
      break
    default:
      result.context = 'Projeto cancelado.'
  }

  if (!active && p.contractStatus !== 'Active' && (p.status === 'Planned' || p.status === 'InProgress' || (p.status === 'Delivered' && !accepted))) {
    result.context = `${result.context} O contrato precisa estar ativo para registrar este passo.`
  }

  return result
}

/** Data anterior a hoje pede o motivo (EXE-01b CA 10). */
export const needsBackdateReason = (value: string, today: string) => !!value && value < today

/** Erros de data na tela, antes do servidor (CA 10): futura e antes do início (entrega) ou da entrega (aceite). */
export function dateError(value: string, today: string, step: ServiceStep, p: Pick<ProjectDetail, 'startedOn' | 'deliveredOn'>): string | null {
  if (!value) return 'Informe a data.'
  if (value > today) return 'A data não pode ser depois de hoje.'
  if (step === 'delivery' && p.startedOn && value < p.startedOn) return `A data não pode ser antes do início (${formatDate(p.startedOn)}).`
  if (step === 'acceptance' && p.deliveredOn && value < p.deliveredOn) return `O aceite não pode ser antes da entrega (${formatDate(p.deliveredOn)}).`
  return null
}

/** Linha da parcela no bloco de efeito (CA 7): "Parcela {n}, {marco}: R$ {valor}, vence {data}" (sem finance.view, só o marco). */
export function parcelLine(l: MilestonePreview['lines'][number]): string {
  const name = l.compatibility ? l.label : l.description
  const head = `Parcela ${l.sequence}, ${name}`
  if (typeof l.amount !== 'number') return head
  return `${head}: ${formatMoney(l.amount)}${l.dueDate ? `, vence ${formatDate(l.dueDate)}` : ''}`
}
