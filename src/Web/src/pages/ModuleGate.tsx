import type { ReactNode } from 'react'
import { useNavigation } from '@/navigation/navigation-context'
import { AccessDenied, ModuleNotReleased, NotFound } from './ModulePage'

/**
 * Tela real de um módulo: as mesmas regras do menu (FND-06d). Não liberado mostra o cadeado; sem acesso mostra acesso
 * negado; a autorização de verdade continua no backend.
 */
export function ModuleGate({ code, children }: { code: string; children: ReactNode }) {
  const { all, status } = useNavigation()
  if (status === 'loading') return null
  const module = all.find((m) => m.code === code)
  if (!module) return <NotFound />
  if (module.locked) return <ModuleNotReleased module={module} />
  if (!module.enabled) return <AccessDenied module={module} />
  return <>{children}</>
}

/** Ação permitida no módulo (o <Can> do front, FND-06c CA 14): esconde o que o usuário não pode fazer. */
export function Can({ perm, children }: { perm: string; children: ReactNode }) {
  const { all } = useNavigation()
  const [code, action] = perm.split('.')
  const module = all.find((m) => m.code === code)
  return module && !module.locked && module.actions.includes(action) ? <>{children}</> : null
}

/**
 * Autoatendimento do funcionário (ADR 0035, "Meu ponto" e "Meus holerites"): só a camada 1 (módulo liberado no tenant), sem grupo; o
 * vínculo com funcionário ativo vem da API (403 employee_not_active).
 */
export function ReleasedGate({ code, children }: { code: string; children: ReactNode }) {
  const { all, status } = useNavigation()
  if (status === 'loading') return null
  const module = all.find((m) => m.code === code)
  if (!module) return <NotFound />
  if (module.locked) return <ModuleNotReleased module={module} />
  return <>{children}</>
}
