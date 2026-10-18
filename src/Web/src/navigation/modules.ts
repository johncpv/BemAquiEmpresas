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
  parties: { slug: 'cadastros', icon: Contact },
  engineering: { slug: 'engenharia', icon: DraftingCompass },
  production: { slug: 'producao', icon: Factory },
  inventory: { slug: 'estoque', icon: Boxes },
  purchasing: { slug: 'compras', icon: ShoppingCart },
  assets: { slug: 'patrimonio', icon: Tag },
  finance: { slug: 'financeiro', icon: Wallet },
  fiscal: { slug: 'fiscal', icon: Receipt },
  hr: { slug: 'rh', icon: Users },
  kiosk: { slug: 'painel-tv', icon: MonitorPlay },
  shopfloor: { slug: 'operador', icon: HardHat },
  identity: { slug: 'acessos', icon: ShieldCheck, tabs: [{ slug: 'usuarios', label: 'Usuários' }, { slug: 'grupos', label: 'Grupos' }] },
  settings: { slug: 'configuracoes', icon: Settings, tabs: [{ slug: 'seguranca', label: 'Segurança' }] },
}

/** Mesmo formato de MyModule em GET /api/v1/me/permissions (FND-06c). */
export interface PermissionModule {
  code: string
  name: string
  area: string
  released: boolean
  enabled: boolean
  actions: readonly string[]
}

export interface NavModule extends PermissionModule {
  slug: string
  path: string
  icon: LucideIcon
  tabs: readonly Tab[]
  count?: number
}

export interface NavArea extends Area {
  modules: NavModule[]
}

export function buildNavigation(modules: readonly PermissionModule[]): NavArea[] {
  return AREAS.map((area) => ({
    ...area,
    modules: modules
      .filter((m) => m.area === area.name && MODULE_UI[m.code])
      .map((m) => {
        const ui = MODULE_UI[m.code]
        return { ...m, slug: ui.slug, path: `/app/${ui.slug}`, icon: ui.icon, tabs: ui.tabs ?? [] }
      }),
  }))
}

export function findModuleBySlug(areas: readonly NavArea[], slug: string): { area: NavArea; module: NavModule } | undefined {
  for (const area of areas) {
    const module = area.modules.find((m) => m.slug === slug)
    if (module) return { area, module }
  }
  return undefined
}
