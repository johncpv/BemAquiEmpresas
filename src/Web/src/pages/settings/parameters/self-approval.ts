/** Autoaprovação da OP (FIN-10a, ADR 0054): as três opções, na ordem da tela, com a descrição de uma linha. */
export type SelfApprovalModeName = 'Never' | 'OwnerOnly' | 'AnyApprover'

export const SELF_APPROVAL_QUESTION = 'Quem solicitou a ordem pode aprová-la?'

export const SELF_APPROVAL_OPTIONS: readonly { value: SelfApprovalModeName; label: string; description: string }[] = [
  { value: 'Never', label: 'Nunca', description: 'Outra pessoa precisa aprovar.' },
  { value: 'OwnerOnly', label: 'Só o Proprietário', description: 'Quem é do Proprietário aprova a própria ordem, e fica registrado.' },
  { value: 'AnyApprover', label: 'Qualquer aprovador (só no 1º nível)', description: 'Quem tem alçada no 1º nível aprova a própria ordem, e fica registrado. Os outros níveis continuam com outra pessoa.' },
]

export const SELF_APPROVAL_DICA_NEVER = 'Com um único Proprietário, ele continua aprovando as próprias ordens (sem ele a empresa travaria). Com mais de um, outra pessoa aprova.'

export const ONLY_OWNER_NOTE = 'Só o Proprietário muda esta regra.'

/** Modo lido da API; ausente (API antiga) vale Nunca. */
export function modeOf(value: string | null | undefined): SelfApprovalModeName {
  return value === 'OwnerOnly' || value === 'AnyApprover' ? value : 'Never'
}

/** O código do autenticador só é pedido quando o modo muda (CA 14). */
export function needsCode(seen: SelfApprovalModeName, chosen: SelfApprovalModeName): boolean {
  return seen !== chosen
}

export function labelOf(mode: SelfApprovalModeName): string {
  return SELF_APPROVAL_OPTIONS.find((o) => o.value === mode)?.label ?? 'Nunca'
}
