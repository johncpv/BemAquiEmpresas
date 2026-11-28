import { useNavigation } from '@/navigation/navigation-context'
import type { NavModule } from '@/navigation/modules'

/** Critério do <Can> sem hook, para listas: ação "modulo.acao" liberada para o usuário (FND-06c CA 14). */
export function canIn(all: readonly Pick<NavModule, 'code' | 'locked' | 'actions'>[], perm: string) {
  const [code, action] = perm.split('.')
  const module = all.find((m) => m.code === code)
  return !!module && !module.locked && module.actions.includes(action)
}

/**
 * Módulo aberto só em parte pelo recurso do perfil (EXE-RH: o RH do perfil Serviços sem o módulo no plano): as telas escondem os campos,
 * abas e ações das partes que continuam não liberadas (jornada, sindicato, afastamentos, folha, ponto), e não chamam a API delas.
 */
export function usePartial(code: string) {
  const { all } = useNavigation()
  return !!all.find((m) => m.code === code)?.partial
}

/** Mesmo critério do <Can>, como hook: ação "modulo.acao" liberada para o usuário (FND-06c CA 14). */
export function useCan(perm: string) {
  const { all } = useNavigation()
  return canIn(all, perm)
}
