import type { PartyRoleKind } from '@/api/generated/model'
import { COMMERCE_PROFILES } from '@/navigation/modules'
import { useHasProfile } from '@/navigation/navigation-context'
import { ROLE_OPTIONS } from './labels'

/**
 * Papéis oferecidos na tela (PAG-01, decisão do PO de 17/11/2026): "Adquirente" só para tenant com perfil Comércio, Serviços ou
 * E-commerce; na fábrica ele some, salvo no terceiro que já tem o papel.
 */
export function useRoleOptions(current: readonly PartyRoleKind[] = []) {
  const commerce = useHasProfile(COMMERCE_PROFILES)
  return ROLE_OPTIONS.filter((o) => o.value !== 'Acquirer' || commerce || current.includes('Acquirer'))
}
