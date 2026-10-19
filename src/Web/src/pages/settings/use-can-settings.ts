import { useNavigation } from '@/navigation/navigation-context'

/** Ação do módulo settings liberada para o usuário (o mesmo critério do <Can>). */
export function useCanSettings(action: string) {
  const { all } = useNavigation()
  const module = all.find((m) => m.code === 'settings')
  return !!module && !module.locked && module.actions.includes(action)
}
