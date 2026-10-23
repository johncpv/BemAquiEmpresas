import { createRootRoute, createRoute, createRouter, Outlet, redirect } from '@tanstack/react-router'
import { lazy } from 'react'
import { InvitePage } from '@/auth/InvitePage'
import { LoginPage } from '@/auth/LoginPage'
import { ForgotPasswordPage, ResetPasswordPage } from '@/auth/PasswordPages'
import { ensureSession } from '@/auth/session-guard'
import { RequiredTwoFactorSetupPage, TwoFactorVerifyPage } from '@/auth/TwoFactorPages'
import { AccountPage } from '@/pages/account/AccountPage'
import { AccountSecurityPage } from '@/pages/account/AccountSecurityPage'
import { GroupsPage } from '@/pages/access/GroupsPage'
import { UsersPage } from '@/pages/access/UsersPage'
import { ModuleGate } from '@/pages/ModuleGate'
import { SecurityPage } from '@/pages/settings/SecurityPage'
import { CompanyPage } from '@/pages/settings/company/CompanyPage'
import { NumberingPage } from '@/pages/settings/numbering/NumberingPage'
import { ParametersPage } from '@/pages/settings/parameters/ParametersPage'
import { AiSettingsPage } from '@/pages/settings/AiSettingsPage'
import { ImportRunPage } from '@/pages/settings/import/ImportRunPage'
import { ImportRunsPage } from '@/pages/settings/import/ImportRunsPage'
import { ShopfloorLayout, TvLayout } from '@/pages/DedicatedLayouts'
import { FabEntryPage } from '@/pages/shopfloor/FabEntryPage'
import { Inicio } from '@/pages/Inicio'
import { ModulePage, NotFound } from '@/pages/ModulePage'
import { getPlatformCurrentAdmin } from '@/api/generated/bemaqui'
import { PlatformLayout } from '@/platform/PlatformLayout'
import { PlatformLoginPage } from '@/platform/PlatformLoginPage'
import { PlatformSetPasswordPage } from '@/platform/PlatformSetPasswordPage'
import { TenantDetailPage } from '@/platform/TenantDetailPage'
import { TenantsPage } from '@/platform/TenantsPage'
import { ComponentsMold, CreateMold, DetailMold, PanelMold } from '@/dev/molds/lazy'
import { BomsPage } from '@/pages/engineering/boms/BomsPage'
import { NewRoutingPage } from '@/pages/engineering/routings/NewRoutingPage'
import { RoutingPage } from '@/pages/engineering/routings/RoutingPage'
import { RoutingsPage } from '@/pages/engineering/routings/RoutingsPage'
import { BankAccountsPage } from '@/pages/finance/BankAccountsPage'
import { NewPaymentTermPage, PaymentTermPage } from '@/pages/finance/PaymentTermPage'
import { PaymentTermsPage } from '@/pages/finance/PaymentTermsPage'
import { ChartOfAccountsPage } from '@/pages/finance/ChartOfAccountsPage'
import { NewPayablePage } from '@/pages/finance/payables/NewPayablePage'
import { PayablePage } from '@/pages/finance/payables/PayablePage'
import { PayablesPage } from '@/pages/finance/payables/PayablesPage'
import { RecurringExpensesPage } from '@/pages/finance/payables/RecurringExpensesPage'
import { OpeningPage } from '@/pages/finance/opening/OpeningPage'
import { CashFlowPage } from '@/pages/finance/cash-flow/CashFlowPage'
import { AgingPage } from '@/pages/finance/aging/AgingPage'
import { StatementPage } from '@/pages/finance/statement/StatementPage'
import { ComparePage } from '@/pages/quotes/ComparePage'
import { QuotePage } from '@/pages/quotes/QuotePage'
import { ApprovalRulesPage } from '@/pages/finance/approvals/ApprovalRulesPage'
import { ContractPage } from '@/pages/contracts/ContractPage'
import { ContractsPage } from '@/pages/contracts/ContractsPage'
import { AmendmentPage } from '@/pages/contracts/amendments/AmendmentPage'
import { ProjectPage } from '@/pages/projects/ProjectPage'
import { ProjectsPage } from '@/pages/projects/ProjectsPage'
import { InstallationPage } from '@/pages/production/installations/InstallationPage'
import { FabricationOrderPage } from '@/pages/production/orders/FabricationOrderPage'
import { FabricationOrdersPage } from '@/pages/production/orders/FabricationOrdersPage'
import { KanbanPage } from '@/pages/production/kanban/KanbanPage'
import { OpenLaborPage } from '@/pages/production/labor/OpenLaborPage'
import { FabQueuePage } from '@/pages/shopfloor/FabQueuePage'
import { FabStepPage } from '@/pages/shopfloor/FabStepPage'
import { FabQualityQueuePage, FabQualityStepPage } from '@/pages/shopfloor/FabQuality'
import { QualityQueuePage, QualityStepPage } from '@/pages/production/quality/QualityPages'
import { NonConformitiesPage } from '@/pages/production/quality/NonConformitiesPage'
import { NonConformityPage } from '@/pages/production/quality/NonConformityPage'
import { InstallationsPage } from '@/pages/production/installations/InstallationsPage'
import { InstallationTemplatePage } from '@/pages/production/installations/InstallationTemplatePage'
import { QuotesPage } from '@/pages/quotes/QuotesPage'
import { FunnelPage } from '@/pages/quotes/funnel/FunnelPage'
import { NewPaymentOrderPage } from '@/pages/finance/payment-orders/NewPaymentOrderPage'
import { PaymentOrderPage } from '@/pages/finance/payment-orders/PaymentOrderPage'
import { PaymentOrdersPage } from '@/pages/finance/payment-orders/PaymentOrdersPage'
import { DiscountsReportPage } from '@/pages/finance/receivables/DiscountsReportPage'
import { NewReceivablePage } from '@/pages/finance/receivables/NewReceivablePage'
import { ReceivablePage } from '@/pages/finance/receivables/ReceivablePage'
import { ReceivablesPage } from '@/pages/finance/receivables/ReceivablesPage'
import { CostCentersPage } from '@/pages/finance/CostCentersPage'
import { EmployeePage } from '@/pages/hr/EmployeePage'
import { EmployeesPage } from '@/pages/hr/EmployeesPage'
import { NewEmployeePage } from '@/pages/hr/NewEmployeePage'
import { PositionsPage } from '@/pages/hr/PositionsPage'
import { SchedulesPage } from '@/pages/hr/SchedulesPage'
import { UnionsPage } from '@/pages/hr/UnionsPage'
import { CategoriesPage } from '@/pages/inventory/CategoriesPage'
import { ItemPage } from '@/pages/inventory/ItemPage'
import { ItemsPage } from '@/pages/inventory/ItemsPage'
import { LocationsPage } from '@/pages/inventory/LocationsPage'
import { NewItemPage } from '@/pages/inventory/NewItemPage'
import { UnitsPage } from '@/pages/inventory/UnitsPage'
import { WarrantyPage } from '@/pages/inventory/WarrantyPage'
import { BalancesPage } from '@/pages/inventory/stock/BalancesPage'
import { ManualPostingPage } from '@/pages/inventory/stock/ManualPostingPage'
import { ManualPostingsPage } from '@/pages/inventory/stock/ManualPostingsPage'
import { RequisitionPage } from '@/pages/inventory/stock/RequisitionPage'
import { RequisitionsPage } from '@/pages/inventory/stock/RequisitionsPage'
import { ShortagesPage } from '@/pages/inventory/stock/ShortagesPage'
import { MovementPage } from '@/pages/inventory/stock/MovementPage'
import { MovementsPage } from '@/pages/inventory/stock/MovementsPage'
import { CalendarPage } from '@/pages/production/CalendarPage'
import { WorkCentersPage } from '@/pages/production/WorkCentersPage'
import { NewPartyPage } from '@/pages/parties/NewPartyPage'
import { PartiesPage } from '@/pages/parties/PartiesPage'
import { PartyPage } from '@/pages/parties/PartyPage'
import { AppShell } from '@/shell/AppShell'

// Galerias (FND-09a e FND-09d): em desenvolvimento e no build da homologação (VITE_GALERIA=true); nunca em produção.
const galleryEnabled = import.meta.env.DEV || import.meta.env.VITE_GALERIA === 'true'
const Gallery = galleryEnabled ? lazy(() => import('./dev/Gallery')) : null


const text = (v: unknown) => (typeof v === 'string' && v ? v : undefined)

const rootRoute = createRootRoute({ component: Outlet, notFoundComponent: NotFound })
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  beforeLoad: () => {
    throw redirect({ to: '/app' })
  },
})

// Telas públicas (rotas anônimas do CLAUDE.md): sem dock, com a marca do tenant.
// "aparelho=1": login completo feito no tablet da fábrica, sem sessão persistente (PRD-03b CA 6).
const loginRoute = createRoute({ getParentRoute: () => rootRoute, path: '/entrar', component: LoginPage, validateSearch: (s: Record<string, unknown>): { volta?: string; email?: string; aparelho?: string } => ({ volta: text(s.volta), email: text(s.email), aparelho: s.aparelho === 1 || s.aparelho === '1' ? '1' : undefined }) })
const verifyRoute = createRoute({ getParentRoute: () => rootRoute, path: '/entrar/verificacao', component: TwoFactorVerifyPage })
const requiredSetupRoute = createRoute({ getParentRoute: () => rootRoute, path: '/entrar/configurar-2fa', component: RequiredTwoFactorSetupPage })
const forgotRoute = createRoute({ getParentRoute: () => rootRoute, path: '/esqueci-senha', component: ForgotPasswordPage, validateSearch: (s: Record<string, unknown>): { email?: string } => ({ email: text(s.email) }) })
const resetRoute = createRoute({ getParentRoute: () => rootRoute, path: '/redefinir-senha', component: ResetPasswordPage, validateSearch: (s: Record<string, unknown>): { token?: string } => ({ token: text(s.token) }) })
const inviteRoute = createRoute({ getParentRoute: () => rootRoute, path: '/convite', component: InvitePage, validateSearch: (s: Record<string, unknown>): { token?: string } => ({ token: text(s.token) }) })

// Backoffice (/app): exige sessão; sem ela, vai ao login guardando a rota (FND-04).
const appRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app',
  component: AppShell,
  beforeLoad: async ({ location }) => {
    if (!(await ensureSession())) throw redirect({ to: '/entrar', search: { volta: location.href } })
  },
})
const appIndexRoute = createRoute({ getParentRoute: () => appRoute, path: '/', component: Inicio })
const accountRoute = createRoute({ getParentRoute: () => appRoute, path: 'conta', component: AccountPage })
const accountSecurityRoute = createRoute({ getParentRoute: () => appRoute, path: 'conta/seguranca', component: AccountSecurityPage })
// Telas reais de módulo: mesmas regras do menu (ModuleGate) e rota própria por aba (FND-09b CA 8).
const gated = (code: string, Page: () => React.ReactNode) => () => <ModuleGate code={code}><Page /></ModuleGate>
const accessRoute = createRoute({ getParentRoute: () => appRoute, path: 'acessos', beforeLoad: () => { throw redirect({ to: '/app/acessos/usuarios' }) } })
const usersRoute = createRoute({ getParentRoute: () => appRoute, path: 'acessos/usuarios', component: gated('identity', UsersPage) })
const groupsRoute = createRoute({ getParentRoute: () => appRoute, path: 'acessos/grupos', component: gated('identity', GroupsPage), validateSearch: (s: Record<string, unknown>): { grupo?: string } => ({ grupo: text(s.grupo) }) })
const settingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes', beforeLoad: () => { throw redirect({ to: '/app/configuracoes/empresa' }) } })
// Configurações da empresa (CAD-06): Empresa, Numeração e Parâmetros vêm antes de Segurança.
const companyRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/empresa', component: gated('settings', CompanyPage) })
const numberingRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/numeracao', component: gated('settings', NumberingPage) })
const parametersRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/parametros', component: gated('settings', ParametersPage) })
const securityRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/seguranca', component: gated('settings', SecurityPage) })
const aiSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/ia', component: gated('settings', AiSettingsPage) })
// Importação do Dolibarr (MIG-01a): só leitura; a carga é pela CLI.
const importRunsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/importacao', component: gated('settings', ImportRunsPage) })
const importRunRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/importacao/$id', component: gated('settings', ImportRunPage) })
// Cadastros (CAD-01a): /app/cadastros leva à primeira aba do módulo.
const partiesRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'cadastros', beforeLoad: () => { throw redirect({ to: '/app/cadastros/terceiros' }) } })
const partiesRoute = createRoute({ getParentRoute: () => appRoute, path: 'cadastros/terceiros', component: gated('parties', PartiesPage) })
const newPartyRoute = createRoute({ getParentRoute: () => appRoute, path: 'cadastros/terceiros/novo', component: gated('parties', NewPartyPage) })
const partyRoute = createRoute({ getParentRoute: () => appRoute, path: 'cadastros/terceiros/$id', component: gated('parties', PartyPage) })
// Financeiro (CAD-05b): contas e caixas e condições de pagamento.
// Estoque e Produção (CAD-03): locais, centros de trabalho e calendário.
// Saldos é a aba padrão do Estoque (EST-01a); Movimentos e Lançamentos pendentes completam a "Operação".
const inventoryRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque', beforeLoad: () => { throw redirect({ to: '/app/estoque/saldos' }) } })
const balancesRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/saldos', component: gated('inventory', BalancesPage) })
const movementsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/movimentos', component: gated('inventory', MovementsPage) })
const movementRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/movimentos/$id', component: gated('inventory', MovementPage) })
const manualPostingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/lancamentos', component: gated('inventory', ManualPostingsPage) })
const manualPostingRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/lancamentos/$id', component: gated('inventory', ManualPostingPage) })
// Faltas das OS abertas (EST-02 CA 15), na "Operação" do Estoque; filtros na URL.
const shortagesRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/faltas', component: gated('inventory', ShortagesPage) })
// Requisições de material das OS (EST-03), na "Operação" do Estoque: "Aguardando você" é o filtro padrão; o detalhe tem as ações.
const requisitionsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/requisicoes', component: gated('inventory', RequisitionsPage) })
const requisitionRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/requisicoes/$id', component: gated('inventory', RequisitionPage) })
const locationsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/locais', component: gated('inventory', LocationsPage) })
// Itens, unidades, categorias e garantia padrão (CAD-02a).
const itemsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/itens', component: gated('inventory', ItemsPage) })
const newItemRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/itens/novo', component: gated('inventory', NewItemPage) })
const itemRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/itens/$id', component: gated('inventory', ItemPage) })
const unitsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/unidades', component: gated('inventory', UnitsPage) })
const categoriesRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/categorias', component: gated('inventory', CategoriesPage) })
const warrantyRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/garantia', component: gated('inventory', WarrantyPage) })
// Engenharia (ENG-01): roteiros de fabricação; /app/engenharia leva à primeira aba.
const engineeringRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'engenharia', beforeLoad: () => { throw redirect({ to: '/app/engenharia/roteiros' }) } })
const routingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'engenharia/roteiros', component: gated('engineering', RoutingsPage) })
const newRoutingRoute = createRoute({ getParentRoute: () => appRoute, path: 'engenharia/roteiros/novo', component: gated('engineering', NewRoutingPage), validateSearch: (s: Record<string, unknown>): { de?: string } => ({ de: text(s.de) }) })
const routingRoute = createRoute({ getParentRoute: () => appRoute, path: 'engenharia/roteiros/$id', component: gated('engineering', RoutingPage) })
// BOMs das OS (ENG-02 CA 17): abre no filtro das OS que vão parar sem BOM; a BOM em si fica na aba BOM da OS.
const bomsRoute = createRoute({ getParentRoute: () => appRoute, path: 'engenharia/boms', component: gated('engineering', BomsPage) })
const productionRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao', beforeLoad: () => { throw redirect({ to: '/app/producao/os' }) } })
// OS de fabricação (PRD-01): primeira aba da Produção.
const fabricationOrdersRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/os', component: gated('production', FabricationOrdersPage) })
const fabricationOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/os/$id', component: gated('production', FabricationOrderPage) })
// Kanban (PRD-02): colunas pelo código da etapa, filtros na URL e tempo real.
const kanbanRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/kanban', component: gated('production', KanbanPage) })
// Apontamentos abertos há muito tempo (PRD-03a CA 22).
const openLaborRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/apontamentos-abertos', component: gated('production', OpenLaborPage) })
// Qualidade e não conformidade (PRD-05): fila e inspeção, lista e detalhe da NC (filtros na URL).
const qualityRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/qualidade', component: gated('production', QualityQueuePage) })
const qualityStepRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/qualidade/$stepId', component: gated('production', QualityStepPage) })
const nonConformitiesRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/nao-conformidades', component: gated('production', NonConformitiesPage) })
const nonConformityRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/nao-conformidades/$id', component: gated('production', NonConformityPage) })
// "Operador" no dock leva ao app do operador (PRD-03a CA 27).
const operatorRoute = createRoute({ getParentRoute: () => appRoute, path: 'operador', beforeLoad: () => { throw redirect({ to: '/fab' }) } })
const workCentersRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/centros-de-trabalho', component: gated('production', WorkCentersPage) })
const installationsRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/instalacoes', component: gated('production', InstallationsPage) })
const installationRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/instalacoes/$id', component: gated('production', InstallationPage) })
const installationTemplateRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/modelo-instalacao', component: gated('production', InstallationTemplatePage) })
const calendarRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/calendario', component: gated('production', CalendarPage) })
const financeRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro', beforeLoad: () => { throw redirect({ to: '/app/financeiro/contas' }) } })
const bankAccountsRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/contas', component: gated('finance', BankAccountsPage), validateSearch: (s: Record<string, unknown>): { conta?: string } => ({ conta: text(s.conta) }) })
const paymentTermsRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/condicoes-de-pagamento', component: gated('finance', PaymentTermsPage) })
const newPaymentTermRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/condicoes-de-pagamento/nova', component: gated('finance', NewPaymentTermPage) })
// RH (CAD-04a e CAD-04b): funcionários, cargos, jornadas e sindicatos.
const hrRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh', beforeLoad: () => { throw redirect({ to: '/app/rh/funcionarios' }) } })
const employeesRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/funcionarios', component: gated('hr', EmployeesPage) })
const newEmployeeRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/funcionarios/novo', component: gated('hr', NewEmployeePage) })
const employeeRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/funcionarios/$id', component: gated('hr', EmployeePage) })
const positionsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/cargos', component: gated('hr', PositionsPage) })
const schedulesRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/jornadas', component: gated('hr', SchedulesPage) })
const unionsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/sindicatos', component: gated('hr', UnionsPage) })
// Plano de contas e centros de custo (CAD-05a).
const chartRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/plano-de-contas', component: gated('finance', ChartOfAccountsPage) })
const costCentersRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/centros-de-custo', component: gated('finance', CostCentersPage) })
// Contas a receber (FIN-01).
const receivablesRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/receber', component: gated('finance', ReceivablesPage) })
const newReceivableRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/receber/novo', component: gated('finance', NewReceivablePage) })
const discountsReportRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/receber/descontos-e-cancelamentos', component: gated('finance', DiscountsReportPage) })
const receivableRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/receber/$id', component: gated('finance', ReceivablePage) })
// Contas a pagar e recorrentes (FIN-02).
const payablesRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/pagar', component: gated('finance', PayablesPage) })
const newPayableRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/pagar/novo', component: gated('finance', NewPayablePage) })
const payableRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/pagar/$id', component: gated('finance', PayablePage) })
const approvalRulesRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/alcadas', component: gated('finance', ApprovalRulesPage) })
const recurringRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/recorrentes', component: gated('finance', RecurringExpensesPage) })
// Orçamentos (COM-01a).
const quotesRoute = createRoute({ getParentRoute: () => appRoute, path: 'orcamentos', component: gated('quotes', QuotesPage) })
const quoteFunnelRoute = createRoute({ getParentRoute: () => appRoute, path: 'orcamentos/funil', component: gated('quotes', FunnelPage) })
const quoteCompareRoute = createRoute({ getParentRoute: () => appRoute, path: 'orcamentos/$id/comparar', component: gated('quotes', ComparePage) })
const quoteRoute = createRoute({ getParentRoute: () => appRoute, path: 'orcamentos/$id', component: gated('quotes', QuotePage) })
// Contratos e projetos (COM-04a): o menu "Contratos e projetos" abre a lista de contratos até a COM-05.
const contractsRoute = createRoute({ getParentRoute: () => appRoute, path: 'contratos', component: gated('projects', ContractsPage) })
const amendmentRoute = createRoute({ getParentRoute: () => appRoute, path: 'contratos/$id/aditivos/$aditivoId', component: gated('projects', AmendmentPage) })
const contractRoute = createRoute({ getParentRoute: () => appRoute, path: 'contratos/$id', component: gated('projects', ContractPage) })
const projectsRoute = createRoute({ getParentRoute: () => appRoute, path: 'projetos', component: gated('projects', ProjectsPage) })
const projectRoute = createRoute({ getParentRoute: () => appRoute, path: 'projetos/$id', component: gated('projects', ProjectPage) })
// Extrato da conta (FIN-04).
const statementRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/contas/$id/extrato', component: gated('finance', StatementPage) })
// Ordens de pagamento (FIN-03a).
const paymentOrdersRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/ordens-de-pagamento', component: gated('finance', PaymentOrdersPage) })
const newPaymentOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/ordens-de-pagamento/nova', component: gated('finance', NewPaymentOrderPage) })
const paymentOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/ordens-de-pagamento/$id', component: gated('finance', PaymentOrderPage) })
// Inadimplência (FIN-06a).
const agingRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/inadimplencia', component: gated('finance', AgingPage) })
// Fluxo de caixa (FIN-05).
const cashFlowRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/fluxo-de-caixa', component: gated('finance', CashFlowPage) })
// Implantação financeira (FIN-00).
const openingRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/implantacao', component: gated('finance', OpeningPage) })
const paymentTermRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/condicoes-de-pagamento/$id', component: gated('finance', PaymentTermPage) })
// Moldes de tela da FND-09d, dentro do shell, para o PO aprovar no contexto real.
const moldsRoute = createRoute({ getParentRoute: () => appRoute, path: 'dev/moldes', beforeLoad: () => { throw redirect({ to: '/app/dev/moldes/cadastro' }) } })
const createMoldRoute = createRoute({ getParentRoute: () => appRoute, path: 'dev/moldes/cadastro', component: CreateMold })
const detailMoldRoute = createRoute({ getParentRoute: () => appRoute, path: 'dev/moldes/detalhe', component: DetailMold })
const panelMoldRoute = createRoute({ getParentRoute: () => appRoute, path: 'dev/moldes/painel', component: PanelMold })
const componentsMoldRoute = createRoute({ getParentRoute: () => appRoute, path: 'dev/componentes', component: ComponentsMold })
const devRoutes = galleryEnabled ? [moldsRoute, createMoldRoute, detailMoldRoute, panelMoldRoute, componentsMoldRoute] : []
const moduleRoute = createRoute({ getParentRoute: () => appRoute, path: '$module', component: ModulePage })
const moduleTabRoute = createRoute({ getParentRoute: () => appRoute, path: '$module/$tab', component: ModulePage })

// Plataforma (FND-13/03b): no host da plataforma, com a sessão do super admin (cookie). Sem sessão, vai ao login dela.
const platformLoginRoute = createRoute({ getParentRoute: () => rootRoute, path: '/plataforma/entrar', component: PlatformLoginPage, validateSearch: (s: Record<string, unknown>): { volta?: string } => ({ volta: text(s.volta) }) })
const platformPasswordRoute = createRoute({ getParentRoute: () => rootRoute, path: '/plataforma/definir-senha', component: PlatformSetPasswordPage, validateSearch: (s: Record<string, unknown>): { token?: string } => ({ token: text(s.token) }) })
const platformRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/plataforma',
  component: PlatformLayout,
  beforeLoad: async ({ location }) => {
    try {
      await getPlatformCurrentAdmin()
    } catch {
      throw redirect({ to: '/plataforma/entrar', search: { volta: location.href } })
    }
  },
})
const platformIndexRoute = createRoute({ getParentRoute: () => platformRoute, path: '/', beforeLoad: () => { throw redirect({ to: '/plataforma/tenants' }) } })
const tenantsRoute = createRoute({ getParentRoute: () => platformRoute, path: 'tenants', component: TenantsPage })
const tenantRoute = createRoute({ getParentRoute: () => platformRoute, path: 'tenants/$key', component: TenantDetailPage, validateSearch: (s: Record<string, unknown>): { aba?: string } => ({ aba: text(s.aba) }) })

// Superfícies com layout próprio (02-arquitetura §4): /fab operador, /tv painel. /portal reservado.
// App do operador (PRD-03a): fila em /fab e a etapa em /fab/etapa/$stepId. Sem sessão, a entrada do tablet (/fab/entrar, PRD-03b):
// nomes de quem tem PIN no aparelho registrado, ou "Entrar com e-mail e senha".
const fabRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/fab',
  component: ShopfloorLayout,
  beforeLoad: async () => {
    if (!(await ensureSession())) throw redirect({ to: '/fab/entrar' })
  },
})
const fabEntryRoute = createRoute({ getParentRoute: () => rootRoute, path: '/fab/entrar', component: FabEntryPage })
const fabQueueRoute = createRoute({ getParentRoute: () => fabRoute, path: '/', component: FabQueuePage })
const fabStepRoute = createRoute({ getParentRoute: () => fabRoute, path: 'etapa/$stepId', component: FabStepPage })
// Qualidade no tablet (PRD-05): fila e inspeção com production.approve_quality (o layout confere o módulo production).
const fabQualityRoute = createRoute({ getParentRoute: () => fabRoute, path: 'qualidade', component: FabQualityQueuePage })
const fabQualityStepRoute = createRoute({ getParentRoute: () => fabRoute, path: 'qualidade/$stepId', component: FabQualityStepPage })
const tvRoute = createRoute({ getParentRoute: () => rootRoute, path: '/tv/$token', component: TvLayout })
const galleryRoute = createRoute({ getParentRoute: () => rootRoute, path: '/dev/galeria', component: Gallery ?? NotFound })

export const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  verifyRoute,
  requiredSetupRoute,
  forgotRoute,
  resetRoute,
  inviteRoute,
  appRoute.addChildren([appIndexRoute, accountRoute, accountSecurityRoute, accessRoute, usersRoute, groupsRoute, settingsRoute, companyRoute, numberingRoute, parametersRoute, securityRoute,
    aiSettingsRoute, importRunsRoute, importRunRoute, partiesRootRoute, partiesRoute, newPartyRoute, partyRoute,
    inventoryRootRoute, balancesRoute, movementsRoute, movementRoute, manualPostingsRoute, manualPostingRoute, shortagesRoute, requisitionsRoute, requisitionRoute, locationsRoute, itemsRoute, newItemRoute, itemRoute, unitsRoute, categoriesRoute, warrantyRoute, engineeringRootRoute, routingsRoute, newRoutingRoute, routingRoute, bomsRoute, productionRootRoute, fabricationOrdersRoute, fabricationOrderRoute, kanbanRoute, openLaborRoute, operatorRoute, workCentersRoute, calendarRoute, installationsRoute, installationRoute, installationTemplateRoute, qualityRoute, qualityStepRoute, nonConformitiesRoute, nonConformityRoute,
    financeRootRoute, bankAccountsRoute, paymentTermsRoute, newPaymentTermRoute, paymentTermRoute, chartRoute, costCentersRoute, receivablesRoute, newReceivableRoute, discountsReportRoute, receivableRoute, payablesRoute, newPayableRoute, payableRoute, recurringRoute, approvalRulesRoute, openingRoute, cashFlowRoute, agingRoute, paymentOrdersRoute, newPaymentOrderRoute, paymentOrderRoute, statementRoute, quotesRoute, quoteFunnelRoute, quoteRoute, quoteCompareRoute, contractsRoute, contractRoute, amendmentRoute, projectsRoute, projectRoute, hrRootRoute, employeesRoute, newEmployeeRoute, employeeRoute, positionsRoute, schedulesRoute, unionsRoute, ...devRoutes, moduleRoute, moduleTabRoute]),
  platformLoginRoute,
  platformPasswordRoute,
  platformRoute.addChildren([platformIndexRoute, tenantsRoute, tenantRoute]),
  fabEntryRoute,
  fabRoute.addChildren([fabQueueRoute, fabStepRoute, fabQualityRoute, fabQualityStepRoute]),
  tvRoute,
  galleryRoute,
])

export function createAppRouter(history?: Parameters<typeof createRouter>[0]['history']) {
  return createRouter({ routeTree, history, defaultPreload: 'intent' })
}

export const router = createAppRouter()

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
