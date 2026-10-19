import { getParty, updateParty } from '@/api/generated/bemaqui'
import type { PartyDetail, PartyRoleKind } from '@/api/generated/model'

/** PUT completo a partir do detalhe, trocando só o que mudou (papéis, situação fiscal etc.). */
export function updateFrom(detail: PartyDetail, patch: Partial<Parameters<typeof updateParty>[1]> = {}) {
  return updateParty(detail.id, {
    kind: detail.kind,
    document: detail.document,
    legalName: detail.legalName,
    tradeName: detail.tradeName,
    roles: detail.roles,
    stateRegistration: detail.stateRegistration,
    municipalRegistration: detail.municipalRegistration,
    taxRegime: detail.taxRegime,
    email: detail.email,
    phone: detail.phone,
    notes: detail.notes,
    defaultPaymentTermId: detail.defaultPaymentTerm?.id ?? null,
    ...patch,
  })
}

/** "Adicionar papel" (CA 4): o documento já existe; acrescenta os papéis que faltam ao cadastro existente. */
export async function addRoles(id: string, roles: PartyRoleKind[]) {
  const detail = await getParty(id)
  return updateFrom(detail, { roles: [...new Set([...detail.roles, ...roles])] })
}
