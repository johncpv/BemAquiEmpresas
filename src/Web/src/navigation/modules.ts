import {
  ArrowDownLeft, ArrowLeftRight, ArrowUpRight, Award, BadgeCheck, Barcode, BookText, Boxes, Briefcase, Building, Building2, Calculator,
  CalendarClock, CalendarDays, CalendarX, ChartGantt, ChartLine, ChartPie, CheckCheck, ClipboardCheck, ClipboardList, Clock, Cog, Coins,
  Contact, CreditCard, DraftingCompass, Eye, EyeOff, Factory, FileChartColumn, FileCog, FileInput, FileOutput, FilePlus, FileSpreadsheet,
  FileText, Filter, FolderKanban, FolderTree, Gauge, Hammer, HandCoins, HardHat, Hash, Home, Hourglass, Import, Landmark, LayoutDashboard,
  List, ListTree, Lock, type LucideIcon, MapPin, MessagesSquare, MonitorPlay, MonitorSmartphone, Network, OctagonAlert,
  PackageCheck, PackageMinus, PackageSearch, PackageX, Plug, Receipt, Repeat, Rocket, Route, Ruler, Scale, ScanSearch, Settings, Settings2, ShieldCheck,
  ShieldPlus, ShoppingCart, SlidersHorizontal, Sparkles, Store, SquareKanban, SquarePen, Table, Tag, Tags, Tally5, Target, Timer, TreePalm, Truck,
  UserCog, Users, UsersRound, Wallet, Warehouse, Wrench,
} from 'lucide-react'

/** As áreas do dock, na ordem do CLAUDE.md (ADR 0005 D8) e do ModuleAreas (a "Loja" entrou com a PERF-03). */
export interface Area {
  id: string
  /** Mesmo texto de ModuleAreas no backend (campo area de /me/permissions): liga o módulo à área e nunca muda por perfil. */
  key: string
  /** Rótulo na tela: o padrão, ou o de areaLabels do tenant ("Retaguarda" no lugar de "Fábrica", PERF-03). */
  name: string
  short: string
  icon: LucideIcon
}

export const AREAS: readonly Area[] = [
  { id: 'overview', key: 'Visão geral', name: 'Visão geral', short: 'Visão geral', icon: Home },
  { id: 'commercial', key: 'Comercial', name: 'Comercial', short: 'Comercial', icon: Briefcase },
  // PERF-03: módulos do PDV (Catálogo, Vendas, Caixa). Sem módulo visível, a área some do dock, como as demais.
  { id: 'store', key: 'Loja', name: 'Loja', short: 'Loja', icon: Store },
  { id: 'factory', key: 'Fábrica', name: 'Fábrica', short: 'Fábrica', icon: Factory },
  { id: 'financial', key: 'Financeiro', name: 'Financeiro', short: 'Financeiro', icon: Landmark },
  { id: 'people', key: 'Pessoas', name: 'Pessoas', short: 'Pessoas', icon: Users },
  { id: 'dedicated', key: 'Telas dedicadas', name: 'Telas dedicadas', short: 'Telas', icon: MonitorSmartphone },
  { id: 'system', key: 'Sistema', name: 'Sistema', short: 'Sistema', icon: Settings2 },
]

export interface Tab {
  slug: string
  label: string
  /** Ícone decorativo antes do texto (mesmo vocabulário das abas das páginas, navigation/tab-icons.ts). */
  icon?: LucideIcon
  /** Grupo da aba quando o módulo agrupa as sub-telas (Financeiro, FND-09e CA 12). */
  group?: string
  /** Ação do módulo exigida para a aba aparecer no menu e na busca (ex.: "view_cost" em "Orçado × realizado", DSH-02 CA 19). */
  action?: string
  /** A aba só aparece com uma destas permissões (ex.: Integrações em Configurações, FND-16 CA 16: sem ver o módulo dono, a aba some). */
  requiresAny?: readonly string[]
  /** Só membro do Proprietário vê a aba (Privacidade, LGPD-01a CA 2). */
  ownerOnly?: boolean
  /** Recurso por perfil (adendo da ADR 0046): a aba só aparece para tenant com algum destes perfis de negócio. */
  profiles?: readonly string[]
  /** Ações do módulo que trazem a aba para a primeira posição (o Painel do supervisor com production.approve ou approve_quality, DSH-03). */
  firstFor?: readonly string[]
}

/**
 * Parte de tela de cada módulo do ModuleCatalog: rota, ícone e sub-telas (abas na topbar, CA 8). As abas daqui são a
 * única navegação do módulo: a página não repete essas abas (sub-abas de uma tela, como as do Ponto, ficam na página).
 */
export interface ModuleUi {
  slug: string
  /** Outros caminhos do mesmo módulo (ex.: /app/projetos/$id em "Contratos e projetos"), para a topbar achar o módulo. */
  aliases?: readonly string[]
  icon: LucideIcon
  tabs?: readonly Tab[]
  /** Tela dedicada fora do /app (ex.: o app do operador em /fab, PRD-03a CA 27): o menu e a busca levam direto a ela. */
  href?: string
  /** Fora do menu, da busca e da ajuda do menu (ex.: Conecta, INT-01: a entrada fica em Configurações → Integrações). */
  menu?: false
  /**
   * Abas abertas pelo recurso do perfil com o módulo não liberado (EXE-RH: Funcionários e Cargos do RH no perfil Serviços). Só elas aparecem
   * no menu e na busca; as outras rotas do módulo mostram a tela de não liberado.
   */
  partialTabs?: readonly string[]
}

/**
 * Perfis do recurso de pagamentos da venda (PAG-01, decisão do PO de 17/11/2026), o mesmo do BusinessProfileCatalog.Features no
 * servidor: a fábrica (Konebras) não vê formas de pagamento nem o papel Adquirente.
 */
export const COMMERCE_PROFILES = ['retail', 'services', 'ecommerce'] as const

const CASH = 'Caixa e pagamentos'
const SETUP = 'Configuração financeira'
const STOCK_OPERATION = 'Operação'
const STOCK_SETUP = 'Cadastros'

export const MODULE_UI: Readonly<Record<string, ModuleUi>> = {
  dashboard: { slug: 'painel', icon: LayoutDashboard },
  quotes: { slug: 'orcamentos', icon: FileText, tabs: [{ slug: 'funil', label: 'Funil', icon: Filter }] },
  // DSH-02: "Orçado × realizado" (/app/projetos/margens), só com projects.view_cost.
  projects: { slug: 'projetos', aliases: ['contratos'], icon: FolderKanban, tabs: [{ slug: 'margens', label: 'Orçado × realizado', icon: Target, action: 'view_cost' }] },
  parties: { slug: 'cadastros', icon: Contact, tabs: [{ slug: 'terceiros', label: 'Terceiros', icon: Contact }] },
  // CAT-01: catálogo do PDV, na área Loja (oculto para a fábrica pelo perfil, ADR 0046 e 0047).
  catalog: { slug: 'catalogo', icon: Barcode, tabs: [{ slug: 'produtos', label: 'Produtos', icon: Barcode }, { slug: 'categorias', label: 'Categorias', icon: FolderTree }] },
  // PDV-01: retaguarda da loja (sales.*): lojas, caixas e o aparelho de cada um. A lista de vendas e os cards chegam na PDV-07.
  // VND-02a: Loja → Pedidos (pedido de venda até o pagamento).
  sales: { slug: 'loja', icon: Store, tabs: [{ slug: 'lojas', label: 'Lojas e caixas', icon: Store }, { slug: 'pedidos', label: 'Pedidos', icon: ShoppingCart }] },
  // PDV-01: o caixa é tela dedicada (/pdv), no aparelho registrado da loja; por enquanto, só o registro do aparelho.
  pos: { slug: 'pdv', icon: Calculator, href: '/pdv' },
  engineering: { slug: 'engenharia', icon: DraftingCompass, tabs: [{ slug: 'roteiros', label: 'Roteiros', icon: Route }, { slug: 'boms', label: 'BOMs', icon: Network }] },
  production: { slug: 'producao', icon: Factory, tabs: [{ slug: 'os', label: 'OS de fabricação', icon: ClipboardList }, { slug: 'kanban', label: 'Kanban', icon: SquareKanban }, { slug: 'planejamento', label: 'Planejamento', icon: ChartGantt }, { slug: 'qualidade', label: 'Qualidade', icon: BadgeCheck }, { slug: 'nao-conformidades', label: 'Não conformidades', icon: OctagonAlert }, { slug: 'apontamentos-abertos', label: 'Apontamentos abertos', icon: Timer }, { slug: 'expedicao', label: 'Expedição', icon: Truck }, { slug: 'instalacoes', label: 'Instalações', icon: Hammer }, { slug: 'centros-de-trabalho', label: 'Centros de trabalho', icon: Cog }, { slug: 'calendario', label: 'Calendário', icon: CalendarDays }, { slug: 'modelo-instalacao', label: 'Modelo da OS de instalação', icon: FileCog }, { slug: 'supervisor', label: 'Painel do supervisor', icon: Eye, firstFor: ['approve', 'approve_quality'] }] },
  inventory: { slug: 'estoque', icon: Boxes, tabs: [
    { slug: 'saldos', label: 'Saldos', icon: Warehouse, group: STOCK_OPERATION },
    { slug: 'movimentos', label: 'Movimentos', icon: ArrowLeftRight, group: STOCK_OPERATION },
    { slug: 'lancamentos', label: 'Lançamentos pendentes', icon: Hourglass, group: STOCK_OPERATION },
    { slug: 'ajustes', label: 'Ajustes', icon: SquarePen, group: STOCK_OPERATION },
    { slug: 'inventarios', label: 'Inventários', icon: Tally5, group: STOCK_OPERATION },
    { slug: 'faltas', label: 'Faltas', icon: PackageX, group: STOCK_OPERATION },
    // PDV-05: vendas do caixa que saíram sem saldo (só comércio).
    { slug: 'divergencias', label: 'Divergências', icon: PackageSearch, group: STOCK_OPERATION, profiles: COMMERCE_PROFILES },
    { slug: 'capacidade', label: 'Capacidade de produzir', icon: Gauge, group: STOCK_OPERATION },
    { slug: 'requisicoes', label: 'Requisições', icon: PackageMinus, group: STOCK_OPERATION },
    { slug: 'rastreabilidade', label: 'Rastreabilidade', icon: ScanSearch, group: STOCK_OPERATION },
    { slug: 'itens', label: 'Itens', icon: List, group: STOCK_SETUP },
    { slug: 'locais', label: 'Locais', icon: MapPin, group: STOCK_SETUP },
    { slug: 'unidades', label: 'Unidades', icon: Ruler, group: STOCK_SETUP },
    { slug: 'categorias', label: 'Categorias', icon: FolderTree, group: STOCK_SETUP },
    { slug: 'garantia', label: 'Garantia padrão', icon: ShieldPlus, group: STOCK_SETUP },
  ] },
  purchasing: { slug: 'compras', icon: ShoppingCart, tabs: [{ slug: 'solicitacoes', label: 'Solicitações', icon: FilePlus }, { slug: 'cotacoes', label: 'Cotações', icon: MessagesSquare }, { slug: 'pedidos', label: 'Pedidos', icon: ShoppingCart }, { slug: 'recebimentos', label: 'Recebimentos', icon: PackageCheck }] },
  // PAT-01a: bens e locais do patrimônio (área Fábrica); PAT-03a: manutenção e o custo dela.
  assets: { slug: 'patrimonio', icon: Tag, tabs: [{ slug: 'bens', label: 'Bens', icon: Tag }, { slug: 'manutencao', label: 'Manutenção', icon: Wrench }, { slug: 'custo', label: 'Custo de manutenção', icon: Calculator }, { slug: 'locais', label: 'Locais', icon: MapPin }] },
  finance: { slug: 'financeiro', icon: Wallet, tabs: [
    { slug: 'receber', label: 'A receber', icon: ArrowDownLeft, group: CASH },
    { slug: 'pagar', label: 'A pagar', icon: ArrowUpRight, group: CASH },
    { slug: 'fluxo-de-caixa', label: 'Fluxo de caixa', icon: ChartLine, group: CASH },
    { slug: 'dre', label: 'DRE', icon: FileChartColumn, group: CASH },
    { slug: 'inadimplencia', label: 'Inadimplência', icon: CalendarX, group: CASH },
    { slug: 'ordens-de-pagamento', label: 'Ordens de pagamento', icon: HandCoins, group: CASH },
    { slug: 'contas', label: 'Contas e caixas', icon: Landmark, group: CASH },
    { slug: 'conciliacao', label: 'Conciliação', icon: CheckCheck, group: CASH },
    { slug: 'plano-de-contas', label: 'Plano de contas', icon: ListTree, group: SETUP },
    { slug: 'centros-de-custo', label: 'Centros de custo', icon: ChartPie, group: SETUP },
    { slug: 'condicoes-de-pagamento', label: 'Condições de pagamento', icon: CreditCard, group: SETUP },
    // PAG-01: formas de pagamento da venda (dinheiro, Pix, cartões) com a taxa e o prazo da maquininha.
    { slug: 'formas-de-pagamento', label: 'Formas de pagamento', icon: Wallet, group: SETUP, profiles: COMMERCE_PROFILES },
    { slug: 'recorrentes', label: 'Recorrentes', icon: Repeat, group: SETUP },
    { slug: 'alcadas', label: 'Alçadas', icon: Scale, group: SETUP },
    { slug: 'implantacao', label: 'Implantação financeira', icon: Rocket, group: SETUP },
  ] },
  // FIS-02b: Regras fiscais no grupo "Configuração fiscal" (a NF-e de saída entra com a FIS-03a). FIS-04a: Notas de entrada.
  fiscal: { slug: 'fiscal', icon: Receipt, tabs: [{ slug: 'entrada', label: 'Notas de entrada', icon: FileInput }, { slug: 'regras', label: 'Regras fiscais', icon: BookText, group: 'Configuração fiscal' }] },
  // EXE-RH: no perfil Serviços sem o RH no plano, só Funcionários e Cargos (recurso hr.people_registry).
  hr: { slug: 'rh', icon: Users, partialTabs: ['funcionarios', 'cargos'], tabs: [{ slug: 'funcionarios', label: 'Funcionários', icon: Users }, { slug: 'cargos', label: 'Cargos', icon: Award }, { slug: 'jornadas', label: 'Jornadas', icon: CalendarClock }, { slug: 'sindicatos', label: 'Sindicatos', icon: Building2 }, { slug: 'ponto', label: 'Ponto', icon: Clock }, { slug: 'ferias', label: 'Férias', icon: TreePalm }, { slug: 'folha', label: 'Folha', icon: FileSpreadsheet }, { slug: 'rubricas', label: 'Rubricas', icon: Tags }, { slug: 'tabelas-folha', label: 'Tabelas da folha', icon: Table }, { slug: 'conferencia-folha', label: 'Conferência da folha', icon: ClipboardCheck }, { slug: 'exportar-contadora', label: 'Exportar para a contadora', icon: FileOutput, action: 'view_sensitive' }] },
  kiosk: { slug: 'painel-tv', icon: MonitorPlay },
  shopfloor: { slug: 'operador', icon: HardHat, href: '/fab' },
  // INT-01: o Conecta não entra no menu; a página fica em Configurações → Integrações → Conecta.
  conecta: { slug: 'conecta', icon: Plug, href: '/app/configuracoes/integracoes/conecta', menu: false },
  identity: { slug: 'acessos', icon: ShieldCheck, tabs: [{ slug: 'usuarios', label: 'Usuários', icon: UserCog }, { slug: 'grupos', label: 'Grupos', icon: UsersRound }] },
  settings: { slug: 'configuracoes', icon: Settings, tabs: [{ slug: 'empresa', label: 'Empresa', icon: Building }, { slug: 'numeracao', label: 'Numeração', icon: Hash }, { slug: 'parametros', label: 'Parâmetros', icon: SlidersHorizontal }, { slug: 'seguranca', label: 'Segurança', icon: Lock }, { slug: 'ia', label: 'Inteligência artificial', icon: Sparkles }, { slug: 'importacao', label: 'Importação do Dolibarr', icon: Import }, { slug: 'integracoes', label: 'Integrações', icon: Plug, requiresAny: ['finance.view', 'fiscal.view'] }, { slug: 'cobranca', label: 'Cobrança', icon: Coins, requiresAny: ['finance.view', 'settings.view'] }, { slug: 'fiscal', label: 'Fiscal', icon: Receipt, requiresAny: ['fiscal.view', 'settings.view'] }, { slug: 'privacidade', label: 'Privacidade (LGPD)', icon: EyeOff, ownerOnly: true }] },
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
  /** Fora de todo perfil de negócio do tenant e não liberado (PERF-01, ADR 0046): some do dock e da busca; a URL direta mostra ModuleNotReleased. */
  hidden?: boolean
  /** Não liberado, com parte aberta pelo recurso do perfil (EXE-RH): o menu mostra só as abas de partialTabs. */
  partial?: boolean
}

export interface NavModule extends PermissionModule {
  slug: string
  aliases: readonly string[]
  path: string
  icon: LucideIcon
  tabs: readonly Tab[]
  /** Fora do menu e da busca (ModuleUi.menu = false). */
  inMenu?: false
  /** Não liberado no tenant: aparece com cadeado e leva à tela ModuleNotReleased (FND-06d CA 1, 2). */
  locked: boolean
  count?: number
}

export interface NavArea extends Area {
  modules: NavModule[]
}

export interface Navigation {
  /** Só o que o usuário vê: liberado com acesso, ou não liberado e no perfil (com cadeado). Área vazia some (CA 4). */
  areas: NavArea[]
  /** Todos os módulos, para a rota saber se mostra a tela, o cadeado ou o acesso negado (CA 2, 3). */
  all: NavModule[]
}

/** Permissão `modulo.acao` liberada e marcada para o usuário (o mesmo critério do <Can>). */
const grants = (modules: readonly PermissionModule[], permission: string) => {
  const [code, action] = permission.split('.')
  const module = modules.find((m) => m.code === code)
  return !!module && module.released && module.actions.includes(action)
}

const toNav = (m: PermissionModule, modules: readonly PermissionModule[], isOwner: boolean, profiles: readonly string[]): NavModule | null => {
  const ui = MODULE_UI[m.code]
  // EXE-RH: módulo aberto em parte pelo recurso do perfil aparece sem cadeado, só com as abas dessa parte.
  const partial = !m.released && !!m.partial && !!ui?.partialTabs
  const allowed = (ui?.tabs ?? []).filter(
    (t) => (!t.action || m.actions.includes(t.action)) && (!t.requiresAny || t.requiresAny.some((p) => grants(modules, p))) && (!t.ownerOnly || isOwner)
      && (!t.profiles || t.profiles.some((p) => profiles.includes(p))) && (!partial || ui!.partialTabs!.includes(t.slug)),
  )
  const first = (t: Tab) => !!t.firstFor?.some((a) => m.actions.includes(a))
  const tabs = [...allowed.filter(first), ...allowed.filter((t) => !first(t))]
  return ui ? { ...m, partial, slug: ui.slug, aliases: ui.aliases ?? [], path: ui.href ?? `/app/${ui.slug}`, icon: ui.icon, tabs, locked: !m.released && !partial, ...(ui.menu === false ? { inMenu: false as const } : {}) } : null
}

/**
 * Visível no menu: liberado com acesso, ou não liberado e no perfil (com cadeado). Liberado sem acesso fica oculto (CA 3); fora do
 * perfil também (PERF-01 CA 7, ADR 0046). A exceção manual que libera um módulo fora do perfil chega como liberado e aparece (CA 10).
 */
export const isVisible = (m: NavModule) => m.inMenu !== false && !m.hidden && (m.locked || m.enabled)

/**
 * profiles: perfis ativos do tenant, para os recursos por perfil (adendo da PAG-01). areaLabels: área (ModuleAreas) → rótulo do
 * tenant, de /me/permissions (PERF-03); vazio para a fábrica, com dock idêntico.
 */
export function buildNavigation(
  modules: readonly PermissionModule[], isOwner = false, profiles: readonly string[] = [], areaLabels: Readonly<Record<string, string>> = {},
): Navigation {
  const all = modules.map((m) => toNav(m, modules, isOwner, profiles)).filter((m): m is NavModule => m !== null)
  const areas = AREAS.map((area) => ({
    ...area,
    ...(areaLabels[area.key] ? { name: areaLabels[area.key], short: areaLabels[area.key] } : {}),
    modules: all.filter((m) => m.area === area.key && isVisible(m)).sort((x, y) => (x.order ?? 0) - (y.order ?? 0)),
  })).filter((area) => area.modules.length > 0)
  return { areas, all }
}

export function findModuleBySlug(modules: readonly NavModule[], slug: string): NavModule | undefined {
  return modules.find((m) => m.slug === slug || m.aliases.includes(slug))
}

export function areaOf(areas: readonly NavArea[], module: NavModule): NavArea | undefined {
  return areas.find((a) => a.key === module.area)
}
