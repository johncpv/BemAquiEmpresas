/** O que a regra da página inicial pode olhar: as permissões efetivas e se é do grupo Proprietário. */
export interface HomeUser {
  isOwner?: boolean
  permissions?: readonly string[]
}

export const HOME_ROUTE = '/app'

/**
 * Página inicial depois de entrar, sem rota de volta (DSH-04 CA 8, ADR 0028 §4): o Início, com a Minha pauta, é a página inicial de
 * todos. Ponto único da regra: a DSH-01a acrescenta aqui o Proprietário com `dashboard.view`, que vai a `/app/painel`.
 */
export function homeRouteFor(user?: HomeUser | null): string {
  void user
  return HOME_ROUTE
}
