import type { PartyDetail } from '@/api/generated/model'

/** Campos do contato que vieram do Conecta e ninguém confirmou no ERP (INT-04 CA 11). */
export function appContactFields(party: Pick<PartyDetail, 'emailSetByApp' | 'phoneSetByApp'>): string[] {
  return [party.emailSetByApp ? 'o e-mail' : null, party.phoneSetByApp ? 'o telefone' : null].filter((f): f is string => f !== null)
}
