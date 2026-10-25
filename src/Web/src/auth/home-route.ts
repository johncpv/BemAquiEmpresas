import { getMyPermissions } from '@/api/generated/bemaqui'

/** O que a regra da página inicial pode olhar: as permissões efetivas, os módulos e se é do grupo Proprietário. */
export interface HomeUser {
  isOwner?: boolean
  permissions?: readonly string[]
  modules?: readonly { code: string; released: boolean; enabled: boolean; actions: readonly string[] }[]
}

export const HOME_ROUTE = '/app'
export const OWNER_HOME_ROUTE = '/app/painel'

/** Painel liberado e habilitado para o usuário: pela permissão efetiva (já limitada aos módulos liberados) ou pelo módulo. */
function seesDashboard(user: HomeUser) {
  if (user.permissions?.includes('dashboard.view')) return true
  const dashboard = user.modules?.find((m) => m.code === 'dashboard')
  return !!dashboard && dashboard.released && dashboard.enabled && dashboard.actions.includes('view')
}

/**
 * Página inicial depois de entrar, sem rota de volta (DSH-04 CA 8, DSH-01a CA 3, ADR 0028 §4): o Início, com a Minha pauta, para
 * todos; o membro do grupo Proprietário com o painel liberado vai ao Painel do proprietário. Ponto único da regra.
 */
export function homeRouteFor(user?: HomeUser | null): string {
  return user?.isOwner && seesDashboard(user) ? OWNER_HOME_ROUTE : HOME_ROUTE
}

/** Rota pedida de verdade: o /app que o login recebe quando a sessão cai no Início (ou por padrão) não conta. */
export const hasReturn = (volta: string | undefined) => !!volta && volta !== HOME_ROUTE && volta !== HOME_ROUTE + '/'

/** Depois do login, do 2FA ou do convite: a rota pedida, se houver; senão a página inicial pelo /me/permissions (Início se falhar). */
export async function landingAfterLogin(volta: string | undefined): Promise<string> {
  if (volta && hasReturn(volta)) return volta
  try {
    return homeRouteFor(await getMyPermissions())
  } catch {
    return HOME_ROUTE
  }
}
