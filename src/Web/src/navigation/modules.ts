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
}

/** Parte de tela de cada módulo do ModuleCatalog: rota, ícone e sub-telas (abas na topbar, CA 8). */
export interface ModuleUi {
  slug: string
  icon: LucideIcon
  tabs?: readonly Tab[]
}

export const MODULE_UI: Readonly<Record<string, ModuleUi>> = {
  dashboard: { slug: 'painel', icon: LayoutDashboard },
  quotes: { slug: 'orcamentos', icon: FileText },
  projects: { slug: 'projetos', icon: FolderKanban },
  parties: { slug: 'cadastros', icon: Contact, tabs: [{ slug: 'terceiros', label: 'Terceiros' }] },
  engineering: { slug: 'engenharia', icon: DraftingCompass },
  production: { slug: 'producao', icon: Factory, tabs: [{ slug: 'centros-de-trabalho', label: 'Centros de trabalho' }, { slug: 'calendario', label: 'Calendário' }] },
  inventory: { slug: 'estoque', icon: Boxes, tabs: [{ slug: 'itens', label: 'Itens' }, { slug: 'locais', label: 'Locais' }, { slug: 'unidades', label: 'Unidades' }, { slug: 'categorias', label: 'Categorias' }, { slug: 'garantia', label: 'Garantia padrão' }] },
  purchasing: { slug: 'compras', icon: ShoppingCart },
  assets: { slug: 'patrimonio', icon: Tag },
  finance: { slug: 'financeiro', icon: Wallet, tabs: [{ slug: 'contas', label: 'Contas e caixas' }, { slug: 'condicoes-de-pagamento', label: 'Condições de pagamento' }, { slug: 'plano-de-contas', label: 'Plano de contas' }, { slug: 'centros-de-custo', label: 'Centros de custo' }] },
  fiscal: { slug: 'fiscal', icon: Receipt },
  hr: { slug: 'rh', icon: Users, tabs: [{ slug: 'funcionarios', label: 'Funcionários' }, { slug: 'cargos', label: 'Cargos' }, { slug: 'jornadas', label: 'Jornadas' }, { slug: 'sindicatos', label: 'Sindicatos' }] },
  kiosk: { slug: 'painel-tv', icon: MonitorPlay },
  shopfloor: { slug: 'operador', icon: HardHat },
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
  return ui ? { ...m, slug: ui.slug, path: `/app/${ui.slug}`, icon: ui.icon, tabs: ui.tabs ?? [], locked: !m.released } : null
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
  return modules.find((m) => m.slug === slug)
}

export function areaOf(areas: readonly NavArea[], module: NavModule): NavArea | undefined {
  return areas.find((a) => a.name === module.area)
}
