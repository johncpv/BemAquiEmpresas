import { useNavigate } from '@tanstack/react-router'
import { useContext } from 'react'
import { NavigationContext } from '@/navigation/navigation-context'

/** Código da recusa da etapa sem BOM liberada (D12, ENG-02 CA 16; PRD-03a CA 9). */
export const BOM_NOT_RELEASED = 'bom_not_released'

/**
 * Próximo passo da recusa por BOM (ENG-02 CA 16): no diálogo de "Concluir etapa" e de "Apontar por funcionário", a recusa
 * <c>bom_not_released</c> oferece "Abrir BOM da OS" (só com engineering.view). Fora do menu carregado (cartão isolado), não oferece.
 */
export function useBomRecovery(orderId: string) {
  const navigate = useNavigate()
  const engineering = useContext(NavigationContext)?.all.find((m) => m.code === 'engineering')
  if (!engineering || engineering.locked || !engineering.actions.includes('view')) return undefined
  return {
    verb: 'Abrir BOM da OS',
    when: (code: string | undefined) => code === BOM_NOT_RELEASED,
    // Navega depois de o diálogo fechar: quem abriu o diálogo pela URL (?concluir=) limpa a URL ao fechar.
    onSelect: () => {
      setTimeout(() => void navigate({ to: '/app/producao/os/$id', params: { id: orderId }, search: { aba: 'bom' } } as never), 0)
      return Promise.resolve()
    },
  }
}
