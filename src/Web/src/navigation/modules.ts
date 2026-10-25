import {
  Boxes, Briefcase, Contact, DraftingCompass, Factory, FileText, FolderKanban, HardHat, Home, Landmark, LayoutDashboard,
  type LucideIcon, MonitorPlay, MonitorSmartphone, Receipt, Settings, Settings2, ShieldCheck, ShoppingCart, Tag, Users, Wallet,
} from 'lucide-react'

/** As 7 áreas do dock, na ordem do CLAUDE.md (ADR 0005 D8). */
export interface Area {
  id: string
  /** Mesmo texto de ModuleAreas no backend (campo area de /me/permissions). */
  name: string
  short: string
  icon: LucideIcon
}

export const AREAS: readonly Area[] = [
  { id: 'overview', name: 'Visão geral', short: 'Visão geral', icon: Home },
  { id: 'commercial', name: 'Comercial', short: 'Comercial', icon: Briefcase },
  { id: 'factory', name: 'Fábrica', short: 'Fábrica', icon: Factory },
  { id: 'financial', name: 'Financeiro', short: 'Financeiro', icon: Landmark },
  { id: 'people', name: 'Pessoas', short: 'Pessoas', icon: Users },
  { id: 'dedicated', name: 'Telas dedicadas', short: 'Telas', icon: MonitorSmartphone },
  { id: 'system', name: 'Sistema', short: 'Sistema', icon: Settings2 },
]

export interface Tab {
  slug: string
  label: string
  /** Grupo da aba quando o módulo agrupa as sub-telas (Financeiro, FND-09e CA 12). */
  group?: string
}

/** Parte de tela de cada módulo do ModuleCatalog: rota, ícone e sub-telas (abas na topbar, CA 8). */
export interface ModuleUi {
  slug: string
  /** Outros caminhos do mesmo módulo (ex.: /app/projetos/$id em "Contratos e projetos"), para a topbar achar o módulo. */
  aliases?: readonly string[]
  icon: LucideIcon
  tabs?: readonly Tab[]
  /** Tela dedicada fora do /app (ex.: o app do operador em /fab, PRD-03a CA 27): o menu e a busca levam direto a ela. */
  href?: string
}

const CASH = 'Caixa e pagamentos'
const SETUP = 'Configuração financeira'
const STOCK_OPERATION = 'Operação'
const STOCK_SETUP = 'Cadastros'

export const MODULE_UI: Readonly<Record<string, ModuleUi>> = {
  dashboard: { slug: 'painel', icon: LayoutDashboard },
  quotes: { slug: 'orcamentos', icon: FileText, tabs: [{ slug: 'funil', label: 'Funil' }] },
  projects: { slug: 'projetos', aliases: ['contratos'], icon: FolderKanban },
  parties: { slug: 'cadastros', icon: Contact, tabs: [{ slug: 'terceiros', label: 'Terceiros' }] },
  engineering: { slug: 'engenharia', icon: DraftingCompass, tabs: [{ slug: 'roteiros', label: 'Roteiros' }, { slug: 'boms', label: 'BOMs' }] },
  production: { slug: 'producao', icon: Factory, tabs: [{ slug: 'os', label: 'OS de fabricação' }, { slug: 'kanban', label: 'Kanban' }, { slug: 'apontamentos-abertos', label: 'Apontamentos abertos' }, { slug: 'expedicao', label: 'Expedição' }, { slug: 'instalacoes', label: 'Instalações' }, { slug: 'centros-de-trabalho', label: 'Centros de trabalho' }, { slug: 'calendario', label: 'Calendário' }, { slug: 'modelo-instalacao', label: 'Modelo da OS de instalação' }] },
  inventory: { slug: 'estoque', icon: Boxes, tabs: [
    { slug: 'saldos', label: 'Saldos', group: STOCK_OPERATION },
    { slug: 'movimentos', label: 'Movimentos', group: STOCK_OPERATION },
    { slug: 'lancamentos', label: 'Lançamentos pendentes', group: STOCK_OPERATION },
    { slug: 'ajustes', label: 'Ajustes', group: STOCK_OPERATION },
    { slug: 'inventarios', label: 'Inventários', group: STOCK_OPERATION },
    { slug: 'faltas', label: 'Faltas', group: STOCK_OPERATION },
    { slug: 'capacidade', label: 'Capacidade de produzir', group: STOCK_OPERATION },
    { slug: 'requisicoes', label: 'Requisições', group: STOCK_OPERATION },
    { slug: 'rastreabilidade', label: 'Rastreabilidade', group: STOCK_OPERATION },
    { slug: 'itens', label: 'Itens', group: STOCK_SETUP },
    { slug: 'locais', label: 'Locais', group: STOCK_SETUP },
    { slug: 'unidades', label: 'Unidades', group: STOCK_SETUP },
    { slug: 'categorias', label: 'Categorias', group: STOCK_SETUP },
    { slug: 'garantia', label: 'Garantia padrão', group: STOCK_SETUP },
  ] },
  purchasing: { slug: 'compras', icon: ShoppingCart, tabs: [{ slug: 'solicitacoes', label: 'Solicitações' }, { slug: 'cotacoes', label: 'Cotações' }, { slug: 'pedidos', label: 'Pedidos' }, { slug: 'recebimentos', label: 'Recebimentos' }] },
  assets: { slug: 'patrimonio', icon: Tag },
  finance: { slug: 'financeiro', icon: Wallet, tabs: [
    { slug: 'receber', label: 'A receber', group: CASH },
    { slug: 'pagar', label: 'A pagar', group: CASH },
    { slug: 'fluxo-de-caixa', label: 'Fluxo de caixa', group: CASH },
    { slug: 'inadimplencia', label: 'Inadimplência', group: CASH },
    { slug: 'ordens-de-pagamento', label: 'Ordens de pagamento', group: CASH },
    { slug: 'contas', label: 'Contas e caixas', group: CASH },
    { slug: 'plano-de-contas', label: 'Plano de contas', group: SETUP },
    { slug: 'centros-de-custo', label: 'Centros de custo', group: SETUP },
    { slug: 'condicoes-de-pagamento', label: 'Condições de pagamento', group: SETUP },
    { slug: 'recorrentes', label: 'Recorrentes', group: SETUP },
    { slug: 'alcadas', label: 'Alçadas', group: SETUP },
    { slug: 'implantacao', label: 'Implantação financeira', group: SETUP },
  ] },
  fiscal: { slug: 'fiscal', icon: Receipt },
  hr: { slug: 'rh', icon: Users, tabs: [{ slug: 'funcionarios', label: 'Funcionários' }, { slug: 'cargos', label: 'Cargos' }, { slug: 'jornadas', label: 'Jornadas' }, { slug: 'sindicatos', label: 'Sindicatos' }] },
  kiosk: { slug: 'painel-tv', icon: MonitorPlay },
  shopfloor: { slug: 'operador', icon: HardHat, href: '/fab' },
  identity: { slug: 'acessos', icon: ShieldCheck, tabs: [{ slug: 'usuarios', label: 'Usuários' }, { slug: 'grupos', label: 'Grupos' }] },
  settings: { slug: 'configuracoes', icon: Settings, tabs: [{ slug: 'empresa', label: 'Empresa' }, { slug: 'numeracao', label: 'Numeração' }, { slug: 'parametros', label: 'Parâmetros' }, { slug: 'seguranca', label: 'Segurança' }, { slug: 'ia', label: 'Inteligência artificial' }, { slug: 'importacao', label: 'Importação do Dolibarr' }] },
}

/** Mesmo formato de MyModule em GET /api/v1/me/permissions (FND-06c/06d). */
export interface PermissionModule {
  code: string
  name: string
  area: string
  order?: number
  description?: string
  released: boolean
  enabled: boolean
  actions: readonly string[]
}

export interface NavModule extends PermissionModule {
  slug: string
  aliases: readonly string[]
  path: string
  icon: LucideIcon
  tabs: readonly Tab[]
  /** Não liberado no tenant: aparece com cadeado e leva à tela ModuleNotReleased (FND-06d CA 1, 2). */
  locked: boolean
  count?: number
}

export interface NavArea extends Area {
  modules: NavModule[]
}

export interface Navigation {
  /** Só o que o usuário vê: liberado com acesso, ou não liberado (com cadeado). Área vazia some (CA 4). */
  areas: NavArea[]
  /** Todos os módulos, para a rota saber se mostra a tela, o cadeado ou o acesso negado (CA 2, 3). */
  all: NavModule[]
}

const toNav = (m: PermissionModule): NavModule | null => {
  const ui = MODULE_UI[m.code]
  return ui ? { ...m, slug: ui.slug, aliases: ui.aliases ?? [], path: ui.href ?? `/app/${ui.slug}`, icon: ui.icon, tabs: ui.tabs ?? [], locked: !m.released } : null
}

/** Visível no menu: liberado com acesso, ou não liberado (com cadeado). Liberado sem acesso fica oculto (CA 3). */
export const isVisible = (m: NavModule) => m.locked || m.enabled

export function buildNavigation(modules: readonly PermissionModule[]): Navigation {
  const all = modules.map(toNav).filter((m): m is NavModule => m !== null)
  const areas = AREAS.map((area) => ({
    ...area,
    modules: all.filter((m) => m.area === area.name && isVisible(m)).sort((x, y) => (x.order ?? 0) - (y.order ?? 0)),
  })).filter((area) => area.modules.length > 0)
  return { areas, all }
}

export function findModuleBySlug(modules: readonly NavModule[], slug: string): NavModule | undefined {
  return modules.find((m) => m.slug === slug || m.aliases.includes(slug))
}

export function areaOf(areas: readonly NavArea[], module: NavModule): NavArea | undefined {
  return areas.find((a) => a.name === module.area)
}

/** Grupos das abas do Financeiro, na ordem: operação primeiro, configuração depois (D12). */
export const FINANCE_GROUPS = [CASH, SETUP].map((label) => ({ label, tabs: (MODULE_UI.finance.tabs ?? []).filter((t) => t.group === label) }))

/** Grupos das abas do Estoque (padrão 9 da Fase 3): operação primeiro, cadastros depois; Saldos é a aba padrão. */
export const INVENTORY_GROUPS = [STOCK_OPERATION, STOCK_SETUP].map((label) => ({ label, tabs: (MODULE_UI.inventory.tabs ?? []).filter((t) => t.group === label) }))
