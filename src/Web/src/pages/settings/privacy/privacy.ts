import type { StatusTone } from '@/components/ui/status-chip'

/** Tipos do pedido do titular (LGPD art. 18, LGPD-01a CA 1), na ordem do formulário. */
export const KINDS = [
  { value: 'Confirmation', label: 'Confirmação' },
  { value: 'Access', label: 'Acesso' },
  { value: 'Correction', label: 'Correção' },
  { value: 'Anonymization', label: 'Anonimização' },
  { value: 'Deletion', label: 'Eliminação' },
  { value: 'Portability', label: 'Portabilidade' },
  { value: 'ConsentRevocation', label: 'Revogação de consentimento' },
  { value: 'SharingInformation', label: 'Informação sobre compartilhamento' },
] as const

export const SUBJECT_TYPES = [
  { value: 'Party', label: 'Terceiro (pessoa física)' },
  { value: 'Contact', label: 'Contato de terceiro' },
  { value: 'Employee', label: 'Funcionário' },
  { value: 'User', label: 'Usuário do sistema' },
  { value: 'Unknown', label: 'Titular não encontrado' },
] as const

export const CHANNELS = [
  { value: 'Email', label: 'E-mail' },
  { value: 'Phone', label: 'Telefone' },
  { value: 'InPerson', label: 'Pessoalmente' },
  { value: 'Letter', label: 'Carta' },
  { value: 'Other', label: 'Outro' },
] as const

export const IDENTITY_METHODS = [
  { value: 'DocumentPresented', label: 'Documento apresentado' },
  { value: 'RegisteredEmail', label: 'Confirmação pelo e-mail do cadastro' },
  { value: 'RegisteredPhone', label: 'Confirmação pelo telefone do cadastro' },
  { value: 'InPerson', label: 'Presencialmente' },
] as const

const label = (list: readonly { value: string; label: string }[], value?: string | null) => list.find((i) => i.value === value)?.label ?? value ?? '—'

export const kindLabel = (value?: string | null) => label(KINDS, value)
export const subjectTypeLabel = (value?: string | null) => label(SUBJECT_TYPES, value)
export const channelLabel = (value?: string | null) => label(CHANNELS, value)
export const identityMethodLabel = (value?: string | null) => label(IDENTITY_METHODS, value)

/** Situação do pedido como chip (CA 4). */
export function statusOf(status?: string | null): { label: string; tone: StatusTone } {
  if (status === 'Done') return { label: 'Concluído', tone: 'ok' }
  if (status === 'Refused') return { label: 'Recusado', tone: 'neutral' }
  return { label: 'Aberto', tone: 'info' }
}

/** Chip do prazo (CA 3): alerta a 3 dias ou menos, crítico vencido. */
export function dueChipOf(chip?: string | null): { label: string; tone: StatusTone } | null {
  if (chip === 'critical') return { label: 'Vencido', tone: 'crit' }
  if (chip === 'warning') return { label: 'Vence em breve', tone: 'warn' }
  return null
}

/** Situação da exportação (CA 8): o estado de demora e o erro com o módulo. */
export function exportOf(status?: string | null): { label: string; tone: StatusTone } | null {
  if (status === 'Running') return { label: 'Gerando a exportação…', tone: 'info' }
  if (status === 'Ready') return { label: 'Exportação pronta', tone: 'ok' }
  if (status === 'Failed') return { label: 'Exportação falhou', tone: 'crit' }
  if (status === 'Expired') return { label: 'Arquivo expirado', tone: 'neutral' }
  return null
}

/** Tipos que exigem a identidade conferida (CA 5) e os que exportam (CA 8). */
export const requiresIdentity = (kind?: string | null) => ['Access', 'Portability', 'Correction', 'Anonymization', 'Deletion'].includes(kind ?? '')
export const exportsData = (kind?: string | null) => kind === 'Access' || kind === 'Portability'

/** O próximo passo sugerido pelo tipo (propositivo): conferir, exportar, abrir o cadastro ou responder. */
export function nextStepOf(kind: string | null | undefined, identityVerified: boolean, exportStatus?: string | null): string {
  if (requiresIdentity(kind) && !identityVerified) return 'Confira a identidade do titular antes de agir.'
  if (exportsData(kind)) return exportStatus === 'Ready' ? 'Baixe a exportação, envie ao titular e conclua o pedido.' : 'Exporte os dados do titular.'
  if (kind === 'Correction') return 'Abra o cadastro, corrija e conclua o pedido.'
  if (kind === 'Anonymization' || kind === 'Deletion') return 'A anonimização entra numa próxima versão. Responda ao titular e conclua o pedido.'
  return 'Responda ao titular e conclua o pedido.'
}
