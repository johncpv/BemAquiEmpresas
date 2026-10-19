import { useNavigation } from '@/navigation/navigation-context'

/** Mesmo critério do <Can>, como hook: ação "modulo.acao" liberada para o usuário (FND-06c CA 14). */
export function useCan(perm: string) {
  const { all } = useNavigation()
  const [code, action] = perm.split('.')
  const module = all.find((m) => m.code === code)
  return !!module && !module.locked && module.actions.includes(action)
}
