import type { PermissionModule } from './modules'

/**
 * Dados de exemplo no formato de /me/permissions (FND-09b): tudo liberado e habilitado. A FND-06d troca pela API real,
 * com cadeado no não liberado e sem os módulos que o usuário não tem.
 */
export const SAMPLE_MODULES: readonly PermissionModule[] = [
  ['dashboard', 'Painel do proprietário', 'Visão geral'],
  ['quotes', 'Orçamentos', 'Comercial'],
  ['projects', 'Projetos', 'Comercial'],
  ['parties', 'Cadastros', 'Comercial'],
  ['engineering', 'Engenharia', 'Fábrica'],
  ['production', 'Produção', 'Fábrica'],
  ['inventory', 'Estoque', 'Fábrica'],
  ['purchasing', 'Compras', 'Fábrica'],
  ['assets', 'Patrimônio', 'Fábrica'],
  ['finance', 'Caixa e pagamentos', 'Financeiro'],
  ['fiscal', 'Fiscal', 'Financeiro'],
  ['hr', 'RH', 'Pessoas'],
  ['kiosk', 'Painel da TV', 'Telas dedicadas'],
  ['shopfloor', 'App do operador', 'Telas dedicadas'],
  ['identity', 'Usuários e acessos', 'Sistema'],
  ['settings', 'Configurações', 'Sistema'],
].map(([code, name, area]) => ({ code, name, area, released: true, enabled: true, actions: ['view'] }))
