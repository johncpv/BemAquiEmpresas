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
import { BillingSettingsPage } from '@/pages/settings/billing/BillingSettingsPage'
import { AiSettingsPage } from '@/pages/settings/AiSettingsPage'
import { ImportRunPage } from '@/pages/settings/import/ImportRunPage'
import { IntegrationEventsPage } from '@/pages/settings/integrations/IntegrationEventsPage'
import { InterAccountPage } from '@/pages/settings/integrations/InterAccountPage'
import { InterAccountsPage } from '@/pages/settings/integrations/InterAccountsPage'
import { FiscalIntegrationPage } from '@/pages/settings/integrations/FiscalIntegrationPage'
import { FiscalCertificatePage } from '@/pages/settings/fiscal/FiscalCertificatePage'
import { FiscalSettingsPage } from '@/pages/settings/fiscal/FiscalSettingsPage'
import { PrivacyRequestPage } from '@/pages/settings/privacy/PrivacyRequestPage'
import { PrivacyRequestsPage } from '@/pages/settings/privacy/PrivacyRequestsPage'
import { RetentionPage } from '@/pages/settings/privacy/RetentionPage'
import { TaxRulesPage } from '@/pages/fiscal/TaxRulesPage'
import { NewTaxRulePage, TaxRulePage } from '@/pages/fiscal/TaxRulePage'
import { ImportIncomingPage } from '@/pages/fiscal/incoming/ImportIncomingPage'
import { IncomingInvoicePage } from '@/pages/fiscal/incoming/IncomingInvoicePage'
import { InvoiceReceiptPage } from '@/pages/fiscal/incoming/InvoiceReceiptPage'
import { IncomingInvoicesPage } from '@/pages/fiscal/incoming/IncomingInvoicesPage'
import { ImportRunsPage } from '@/pages/settings/import/ImportRunsPage'
import { ShopfloorLayout, TvLayout } from '@/pages/DedicatedLayouts'
import { TvLegacyPage, TvPage } from '@/pages/tv/TvPage'
import { KioskPreviewPage } from '@/pages/kiosk/KioskPreviewPage'
import { KioskScreensPage } from '@/pages/kiosk/KioskScreensPage'
import { FabEntryPage } from '@/pages/shopfloor/FabEntryPage'
import { Inicio } from '@/pages/Inicio'
import { DashboardPage } from '@/pages/dashboard/DashboardPage'
import { ProjectResultPage } from '@/pages/dashboard/results/ProjectResultPage'
import { ProjectResultsPage } from '@/pages/dashboard/results/ProjectResultsPage'
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
import { DrePage } from '@/pages/finance/dre/DrePage'
import { AgingPage } from '@/pages/finance/aging/AgingPage'
import { StatementPage } from '@/pages/finance/statement/StatementPage'
import { ReconciliationPage } from '@/pages/finance/reconciliation/ReconciliationPage'
import { ComparePage } from '@/pages/quotes/ComparePage'
import { QuotePage } from '@/pages/quotes/QuotePage'
import { ApprovalRulesPage } from '@/pages/finance/approvals/ApprovalRulesPage'
import { ContractPage } from '@/pages/contracts/ContractPage'
import { ContractsPage } from '@/pages/contracts/ContractsPage'
import { AmendmentPage } from '@/pages/contracts/amendments/AmendmentPage'
import { ProjectPage } from '@/pages/projects/ProjectPage'
import { ProjectsPage } from '@/pages/projects/ProjectsPage'
import { ProjectMarginsPage } from '@/pages/projects/margins/ProjectMarginsPage'
import { InstallationPage } from '@/pages/production/installations/InstallationPage'
import { FabricationOrderPage } from '@/pages/production/orders/FabricationOrderPage'
import { FabricationOrdersPage } from '@/pages/production/orders/FabricationOrdersPage'
import { KanbanPage } from '@/pages/production/kanban/KanbanPage'
import { SupervisorPage } from '@/pages/production/supervisor/SupervisorPage'
import { PlanningPage } from '@/pages/production/planning/PlanningPage'
import { OpenLaborPage } from '@/pages/production/labor/OpenLaborPage'
import { FabQueuePage } from '@/pages/shopfloor/FabQueuePage'
import { FabStepPage } from '@/pages/shopfloor/FabStepPage'
import { FabQualityQueuePage, FabQualityStepPage } from '@/pages/shopfloor/FabQuality'
import { QualityQueuePage, QualityStepPage } from '@/pages/production/quality/QualityPages'
import { NonConformitiesPage } from '@/pages/production/quality/NonConformitiesPage'
import { NonConformityPage } from '@/pages/production/quality/NonConformityPage'
import { NewShipmentPage } from '@/pages/production/shipments/NewShipmentPage'
import { ShipmentPage } from '@/pages/production/shipments/ShipmentPage'
import { ShipmentsPage } from '@/pages/production/shipments/ShipmentsPage'
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
import { AssetPage } from '@/pages/assets/AssetPage'
import { AssetSitesPage } from '@/pages/assets/AssetSitesPage'
import { AssetsPage } from '@/pages/assets/AssetsPage'
import { NewAssetPage } from '@/pages/assets/NewAssetPage'
import { EmployeePage } from '@/pages/hr/EmployeePage'
import { EmployeesPage } from '@/pages/hr/EmployeesPage'
import { NewEmployeePage } from '@/pages/hr/NewEmployeePage'
import { PositionsPage } from '@/pages/hr/PositionsPage'
import { SchedulesPage } from '@/pages/hr/SchedulesPage'
import { UnionsPage } from '@/pages/hr/UnionsPage'
import { MyPayslipsPage } from '@/pages/hr/MyPayslipsPage'
import { FabTimeClockPage } from '@/pages/hr/timeclock/FabTimeClockPage'
import { MyTimeClockPage } from '@/pages/hr/timeclock/MyTimeClockPage'
import { TimeEntriesPage } from '@/pages/hr/timeclock/TimeEntriesPage'
import { TimeAdjustmentsPage } from '@/pages/hr/timeclock/TimeAdjustmentsPage'
import { PayrollItemsPage } from '@/pages/hr/payroll/PayrollItemsPage'
import { PayrollRunPage } from '@/pages/hr/payroll/PayrollRunPage'
import { AccountantExportPage } from '@/pages/hr/accountant/AccountantExportPage'
import { PayrollRunsPage } from '@/pages/hr/payroll/PayrollRunsPage'
import { CategoriesPage } from '@/pages/inventory/CategoriesPage'
import { ItemPage } from '@/pages/inventory/ItemPage'
import { ItemsPage } from '@/pages/inventory/ItemsPage'
import { LocationsPage } from '@/pages/inventory/LocationsPage'
import { NewItemPage } from '@/pages/inventory/NewItemPage'
import { UnitsPage } from '@/pages/inventory/UnitsPage'
import { WarrantyPage } from '@/pages/inventory/WarrantyPage'
import { AdjustmentPage } from '@/pages/inventory/stock/AdjustmentPage'
import { AdjustmentsPage } from '@/pages/inventory/stock/AdjustmentsPage'
import { CountingLayout, InventoryCountingPage } from '@/pages/inventory/stock/InventoryCountingPage'
import { InventoryCountPage } from '@/pages/inventory/stock/InventoryCountPage'
import { InventoryCountsPage } from '@/pages/inventory/stock/InventoryCountsPage'
import { BalancesPage } from '@/pages/inventory/stock/BalancesPage'
import { ManualPostingPage } from '@/pages/inventory/stock/ManualPostingPage'
import { ManualPostingsPage } from '@/pages/inventory/stock/ManualPostingsPage'
import { RequisitionPage } from '@/pages/inventory/stock/RequisitionPage'
import { RequisitionsPage } from '@/pages/inventory/stock/RequisitionsPage'
import { ShortagesPage } from '@/pages/inventory/stock/ShortagesPage'
import { CapacityPage } from '@/pages/inventory/stock/CapacityPage'
import { NewPurchaseRequestPage } from '@/pages/purchasing/NewPurchaseRequestPage'
import { PurchaseRequestPage } from '@/pages/purchasing/PurchaseRequestPage'
import { PurchaseRequestsPage } from '@/pages/purchasing/PurchaseRequestsPage'
import { EditPurchaseOrderPage } from '@/pages/purchasing/orders/EditPurchaseOrderPage'
import { NewPurchaseOrderPage } from '@/pages/purchasing/orders/NewPurchaseOrderPage'
import { PurchaseOrderPage } from '@/pages/purchasing/orders/PurchaseOrderPage'
import { PurchaseOrdersPage } from '@/pages/purchasing/orders/PurchaseOrdersPage'
import { TraceabilityPage } from '@/pages/inventory/stock/TraceabilityPage'
import { GoodsReceiptPage } from '@/pages/purchasing/receipts/GoodsReceiptPage'
import { GoodsReceiptsPage } from '@/pages/purchasing/receipts/GoodsReceiptsPage'
import { NewGoodsReceiptPage } from '@/pages/purchasing/receipts/NewGoodsReceiptPage'
import { NewRfqPage } from '@/pages/purchasing/rfqs/NewRfqPage'
import { RfqPage } from '@/pages/purchasing/rfqs/RfqPage'
import { RfqsPage } from '@/pages/purchasing/rfqs/RfqsPage'
import { MovementPage } from '@/pages/inventory/stock/MovementPage'
import { MovementsPage } from '@/pages/inventory/stock/MovementsPage'
import { CalendarPage } from '@/pages/production/CalendarPage'
import { WorkCentersPage } from '@/pages/production/WorkCentersPage'
import { NewPartyPage } from '@/pages/parties/NewPartyPage'
import { PartiesPage } from '@/pages/parties/PartiesPage'
import { PartyPage } from '@/pages/parties/PartyPage'
import { AppShell } from '@/shell/AppShell'
import { FloorHelp } from '@/help/FloorHelp'
import { FabAssetPage, TagPage } from '@/pages/assets/AssetCardPages'

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
// Painel do proprietário (DSH-01a): cenário do caixa na URL (?cenario=base|ajustado).
const dashboardRoute = createRoute({ getParentRoute: () => appRoute, path: 'painel', component: () => <ModuleGate code="dashboard"><DashboardPage /></ModuleGate>, validateSearch: (s: Record<string, unknown>): { cenario?: string } => ({ cenario: text(s.cenario) }) })
// Resultado gerencial por projeto (DSH-01b CA 6 a 8): filtros na URL (?situacao, ?cliente, ?de=aaaa-mm, ?ate=aaaa-mm); projects.view_cost e finance.view na tela e na API.
const projectResultsRoute = createRoute({ getParentRoute: () => appRoute, path: 'painel/resultado-por-projeto', component: () => <ModuleGate code="dashboard"><ProjectResultsPage /></ModuleGate> })
const projectResultRoute = createRoute({ getParentRoute: () => appRoute, path: 'painel/resultado-por-projeto/$id', component: () => <ModuleGate code="dashboard"><ProjectResultPage /></ModuleGate> })
const accountRoute = createRoute({ getParentRoute: () => appRoute, path: 'conta', component: AccountPage })
const accountSecurityRoute = createRoute({ getParentRoute: () => appRoute, path: 'conta/seguranca', component: AccountSecurityPage })
// "Meus holerites" (RH-02b, ADR 0035): do funcionário, sem ação no grupo; a página confere o módulo liberado e o vínculo vem da API.
const myPayslipsRoute = createRoute({ getParentRoute: () => appRoute, path: 'meus-holerites', component: MyPayslipsPage })
// "Meu ponto" (RH-01a, ADR 0035): do funcionário, sem ação no grupo; o mesmo painel do /fab/ponto, mais "Minhas marcações".
const myTimeClockRoute = createRoute({ getParentRoute: () => appRoute, path: 'meu-ponto', component: MyTimeClockPage, validateSearch: (s: Record<string, unknown>): { aba?: string } => ({ aba: text(s.aba) }) })
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
// Integrações (FND-16): Eventos recebidos; Banco Inter (BNK-01a) e WhatsApp (WPP-01) entram como sub-telas.
const integrationsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/integracoes', beforeLoad: () => { throw redirect({ to: '/app/configuracoes/integracoes/eventos' }) } })
const integrationEventsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/integracoes/eventos', component: gated('settings', IntegrationEventsPage) })
// Banco Inter (BNK-01a): contas 077 e o detalhe da integração; ?acao=trocar abre direto em "Trocar credenciais" (alerta de vencimento).
const interAccountsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/integracoes/inter', component: gated('settings', InterAccountsPage) })
const interAccountRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/integracoes/inter/$contaId', component: gated('settings', InterAccountPage), validateSearch: (s: Record<string, unknown>): { acao?: string } => ({ acao: text(s.acao) }) })
// Configurações → Cobrança (BNK-01b): multa, juros, desconto, envio do boleto e a regra do vencido.
const billingSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/cobranca', component: gated('settings', BillingSettingsPage) })
const importRunsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/importacao', component: gated('settings', ImportRunsPage) })
const importRunRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/importacao/$id', component: gated('settings', ImportRunPage) })
// Emissor fiscal (FIS-01): conexão com o EmissorFiscal; a tela confere também o módulo fiscal liberado.
const fiscalIntegrationRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/integracoes/emissor-fiscal', component: gated('settings', FiscalIntegrationPage) })
// Configurações → Fiscal e Certificado digital (FIS-02a): a tela confere o módulo fiscal liberado; ver com settings.view ou fiscal.view.
const fiscalSettingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/fiscal', component: FiscalSettingsPage })
const fiscalCertificateRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/fiscal/certificado', component: FiscalCertificatePage })
// Configurações → Privacidade (LGPD-01a): pedidos do titular, só do Proprietário (a tela mostra o aviso para os demais).
const privacyRequestsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/privacidade', component: gated('settings', PrivacyRequestsPage) })
const privacyRequestRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/privacidade/$id', component: gated('settings', PrivacyRequestPage) })
// Configurações → Privacidade → Retenção (LGPD-01b CA 1): a tabela de retenção, só do Proprietário.
const privacyRetentionRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes/privacidade/retencao', component: gated('settings', RetentionPage) })
// Fiscal → Regras fiscais (FIS-02b): /app/fiscal leva às regras; filtro, versão e o painel Simular (?simular=1) na URL.
const fiscalRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal', beforeLoad: () => { throw redirect({ to: '/app/fiscal/regras' }) } })
const taxRulesRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/regras', component: gated('fiscal', TaxRulesPage) })
const newTaxRuleRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/regras/nova', component: gated('fiscal', NewTaxRulePage) })
const taxRuleRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/regras/$id', component: gated('fiscal', TaxRulePage) })
// Fiscal → Notas de entrada (FIS-04a): lista com o filtro de situação na URL, Importar XML e a nota com as abas na URL.
const incomingInvoicesRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/entrada', component: gated('fiscal', IncomingInvoicesPage) })
const importIncomingRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/entrada/importar', component: gated('fiscal', ImportIncomingPage) })
const incomingInvoiceRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/entrada/$id', component: gated('fiscal', IncomingInvoicePage) })
// Receber pela nota (FIS-04b): o recebimento da CMP-04 preenchido pela nota conferida.
const invoiceReceiptRoute = createRoute({ getParentRoute: () => appRoute, path: 'fiscal/entrada/$id/recebimento', component: gated('fiscal', InvoiceReceiptPage) })
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
// Capacidade de produzir (EST-06), na "Operação" do Estoque: produto, OS e quantidade na URL; nada é gravado.
const capacityRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/capacidade', component: gated('inventory', CapacityPage) })
// Requisições de material das OS (EST-03), na "Operação" do Estoque: "Aguardando você" é o filtro padrão; o detalhe tem as ações.
const requisitionsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/requisicoes', component: gated('inventory', RequisitionsPage) })
const requisitionRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/requisicoes/$id', component: gated('inventory', RequisitionPage) })
// Compras (CMP-01): Solicitações é a primeira aba; Cotações, Pedidos e Recebimentos entram com a CMP-02, a CMP-03a e a CMP-04.
const purchasingRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras', beforeLoad: () => { throw redirect({ to: '/app/compras/solicitacoes' }) } })
const purchaseRequestsRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/solicitacoes', component: gated('purchasing', PurchaseRequestsPage) })
const newPurchaseRequestRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/solicitacoes/nova', component: gated('purchasing', NewPurchaseRequestPage) })
const purchaseRequestRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/solicitacoes/$id', component: gated('purchasing', PurchaseRequestPage) })
// Cotações (CMP-02): lista (abertas por padrão), nova (das SC por ?sc= ou com itens digitados) e detalhe com abas na URL.
const rfqsRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/cotacoes', component: gated('purchasing', RfqsPage) })
const newRfqRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/cotacoes/nova', component: gated('purchasing', NewRfqPage) })
const rfqRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/cotacoes/$id', component: gated('purchasing', RfqPage) })
// Pedidos de compra (CMP-03a): lista, novo (à mão ou das SC), detalhe com abas na URL e edição.
const purchaseOrdersRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/pedidos', component: gated('purchasing', PurchaseOrdersPage) })
const newPurchaseOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/pedidos/novo', component: gated('purchasing', NewPurchaseOrderPage) })
const purchaseOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/pedidos/$id', component: gated('purchasing', PurchaseOrderPage) })
const editPurchaseOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/pedidos/$id/editar', component: gated('purchasing', EditPurchaseOrderPage) })
// Rastreabilidade (EST-04), na "Operação" do Estoque: busca "Série ou lote"; termo, item e série ou lote na URL.
const traceabilityRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/rastreabilidade', component: gated('inventory', TraceabilityPage) })
// Ajustes de estoque (EST-01b), na "Operação" do Estoque: "Aguardando você" é o filtro padrão de quem aprova; abas do detalhe na URL.
const adjustmentsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/ajustes', component: gated('inventory', AdjustmentsPage) })
const adjustmentRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/ajustes/$id', component: gated('inventory', AdjustmentPage) })
// Recebimentos (CMP-04): abre em "A receber"; o detalhe do recebimento e o registro pela página do pedido.
const goodsReceiptsRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/recebimentos', component: gated('purchasing', GoodsReceiptsPage) })
const goodsReceiptRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/recebimentos/$id', component: gated('purchasing', GoodsReceiptPage) })
const newGoodsReceiptRoute = createRoute({ getParentRoute: () => appRoute, path: 'compras/pedidos/$id/recebimento', component: gated('purchasing', NewGoodsReceiptPage) })
// Inventários (EST-05), na "Operação" do Estoque: lista e detalhe com abas (Contagem, Divergências, Histórico) na URL.
const inventoryCountsRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/inventarios', component: gated('inventory', InventoryCountsPage) })
const inventoryCountRoute = createRoute({ getParentRoute: () => appRoute, path: 'estoque/inventarios/$id', component: gated('inventory', InventoryCountPage) })
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
// Painel do supervisor (DSH-03): blocos do quadro, qualidade, faltas, capacidade e indicadores; linha na URL (?linha=).
const supervisorRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/supervisor', component: gated('production', SupervisorPage), validateSearch: (s: Record<string, unknown>): { linha?: string } => ({ linha: text(s.linha) }) })
// Planejamento (PLN-01a): última execução do plano, "Recalcular agora" e OS sem previsão; as abas chegam na PLN-02 a PLN-04.
const planningRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/planejamento', component: gated('production', PlanningPage) })
// Apontamentos abertos há muito tempo (PRD-03a CA 22).
const openLaborRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/apontamentos-abertos', component: gated('production', OpenLaborPage) })
// Qualidade e não conformidade (PRD-05): fila e inspeção, lista e detalhe da NC (filtros na URL).
const qualityRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/qualidade', component: gated('production', QualityQueuePage) })
const qualityStepRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/qualidade/$stepId', component: gated('production', QualityStepPage) })
const nonConformitiesRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/nao-conformidades', component: gated('production', NonConformitiesPage) })
const nonConformityRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/nao-conformidades/$id', component: gated('production', NonConformityPage) })
// Expedição (PRD-06b): prontos para expedir e romaneios, o novo romaneio (do projeto ou da OS) e o romaneio.
const shipmentsRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/expedicao', component: gated('production', ShipmentsPage) })
const newShipmentRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/expedicao/novo', component: gated('production', NewShipmentPage), validateSearch: (s: Record<string, unknown>): { projeto?: string; os?: string } => ({ projeto: text(s.projeto), os: text(s.os) }) })
const shipmentRoute = createRoute({ getParentRoute: () => appRoute, path: 'producao/expedicao/$id', component: gated('production', ShipmentPage) })
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
// Patrimônio (PAT-01a): bens (lista, novo com ?de= para duplicar, ficha com abas) e locais.
const assetsRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'patrimonio', beforeLoad: () => { throw redirect({ to: '/app/patrimonio/bens' }) } })
const assetsRoute = createRoute({ getParentRoute: () => appRoute, path: 'patrimonio/bens', component: gated('assets', AssetsPage) })
const newAssetRoute = createRoute({ getParentRoute: () => appRoute, path: 'patrimonio/bens/novo', component: gated('assets', NewAssetPage), validateSearch: (s: Record<string, unknown>): { de?: string } => ({ de: text(s.de) }) })
const assetRoute = createRoute({ getParentRoute: () => appRoute, path: 'patrimonio/bens/$id', component: gated('assets', AssetPage) })
const assetSitesRoute = createRoute({ getParentRoute: () => appRoute, path: 'patrimonio/locais', component: gated('assets', AssetSitesPage) })
// URL do QR da etiqueta (PAT-01b CA 2, 5 e 8): exige sessão (sem ela, o login volta só para caminho relativo); resolve a etiqueta e abre a
// ficha resumida no celular e no tablet ou a completa no computador. Não é rota anônima: a API confere módulo e assets.view.
const tagRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/b/$tag',
  component: () => <FloorHelp><ModuleGate code="assets"><TagPage /></ModuleGate></FloorHelp>,
  beforeLoad: async ({ location }) => {
    if (!(await ensureSession())) throw redirect({ to: '/entrar', search: { volta: location.href } })
  },
})
// RH (CAD-04a e CAD-04b): funcionários, cargos, jornadas e sindicatos.
const hrRootRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh', beforeLoad: () => { throw redirect({ to: '/app/rh/funcionarios' }) } })
const employeesRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/funcionarios', component: gated('hr', EmployeesPage) })
const newEmployeeRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/funcionarios/novo', component: gated('hr', NewEmployeePage) })
const employeeRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/funcionarios/$id', component: gated('hr', EmployeePage) })
const positionsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/cargos', component: gated('hr', PositionsPage) })
const schedulesRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/jornadas', component: gated('hr', SchedulesPage) })
const unionsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/sindicatos', component: gated('hr', UnionsPage) })
// Ponto (RH-01a): marcações por dia, com o período, o funcionário e o centro na URL (?de, ?ate, ?funcionario, ?centro).
const hrTimeRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/ponto', beforeLoad: () => { throw redirect({ to: '/app/rh/ponto/marcacoes' }) } })
const timeEntriesRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/ponto/marcacoes', component: gated('hr', TimeEntriesPage), validateSearch: (s: Record<string, unknown>): { de?: string; ate?: string; funcionario?: string; centro?: string } => ({ de: text(s.de), ate: text(s.ate), funcionario: text(s.funcionario), centro: text(s.centro) }) })
// RH → Ponto → Ajustes para aprovar (RH-01b): situação, funcionário e o pedido aberto no painel, na URL.
const timeAdjustmentsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/ponto/ajustes', component: gated('hr', TimeAdjustmentsPage), validateSearch: (s: Record<string, unknown>): { situacao?: string; funcionario?: string; pedido?: string } => ({ situacao: text(s.situacao), funcionario: text(s.funcionario), pedido: text(s.pedido) }) })
// Folha lançada e rubricas (RH-02a).
const payrollRunsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/folha', component: gated('hr', PayrollRunsPage) })
const payrollRunRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/folha/$id', component: gated('hr', PayrollRunPage) })
const payrollItemsRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/rubricas', component: gated('hr', PayrollItemsPage) })
// Exportação para a contadora (RH-03).
const accountantExportRoute = createRoute({ getParentRoute: () => appRoute, path: 'rh/exportar-contadora', component: gated('hr', AccountantExportPage) })
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
// Orçado × realizado (DSH-02): só com projects.view_cost (a tela confere; a API devolve 403).
const projectMarginsRoute = createRoute({ getParentRoute: () => appRoute, path: 'projetos/margens', component: gated('projects', ProjectMarginsPage) })
const projectRoute = createRoute({ getParentRoute: () => appRoute, path: 'projetos/$id', component: gated('projects', ProjectPage) })
// Extrato da conta (FIN-04).
const statementRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/contas/$id/extrato', component: gated('finance', StatementPage) })
// Conciliação bancária (BNK-03b): ?conta=, ?de= e ?ate=.
const reconciliationRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/conciliacao', component: gated('finance', ReconciliationPage) })
// Ordens de pagamento (FIN-03a).
const paymentOrdersRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/ordens-de-pagamento', component: gated('finance', PaymentOrdersPage) })
const newPaymentOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/ordens-de-pagamento/nova', component: gated('finance', NewPaymentOrderPage) })
const paymentOrderRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/ordens-de-pagamento/$id', component: gated('finance', PaymentOrderPage) })
// Inadimplência (FIN-06a).
const agingRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/inadimplencia', component: gated('finance', AgingPage) })
// Fluxo de caixa (FIN-05).
const cashFlowRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/fluxo-de-caixa', component: gated('finance', CashFlowPage) })
// DRE gerencial (FIN-08): ?meses=3|6|12|24 e ?ate=aaaa-mm.
const dreRoute = createRoute({ getParentRoute: () => appRoute, path: 'financeiro/dre', component: gated('finance', DrePage) })
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
// Telas dedicadas → Painel da TV (PNL-01a): cadastro das telas, link de uso único, novo link e revogação.
const kioskScreensRoute = createRoute({ getParentRoute: () => appRoute, path: 'painel-tv', component: gated('kiosk', KioskScreensPage) })
// PNL-01b: prévia da tela no backoffice (kiosk.view e production.view), as visões como a TV mostra.
const kioskPreviewRoute = createRoute({ getParentRoute: () => appRoute, path: 'painel-tv/$id/previa', component: gated('kiosk', KioskPreviewPage) })
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
// "Meu ponto" no tablet (RH-01a): o layout confere só o módulo hr liberado (autoatendimento, ADR 0035).
const fabTimeClockRoute = createRoute({ getParentRoute: () => fabRoute, path: 'ponto', component: FabTimeClockPage })
// Ficha resumida do bem pela leitura da etiqueta (PAT-01b CA 5 e 6): o layout confere o módulo assets.
const fabAssetRoute = createRoute({ getParentRoute: () => fabRoute, path: 'bens/$id', component: FabAssetPage })
// Contagem do inventário no celular (EST-05 CA 4): tela dedicada com o kit chão de fábrica, sem o dock, com a sessão do backoffice.
const countingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/app/estoque/inventarios/$id/contagem',
  component: CountingLayout,
  beforeLoad: async ({ location }) => {
    if (!(await ensureSession())) throw redirect({ to: '/entrar', search: { volta: location.href } })
  },
})
const countingIndexRoute = createRoute({ getParentRoute: () => countingRoute, path: '/', component: InventoryCountingPage })
// Painel da TV (PNL-01a, ADR 0030): /tv#token troca o token pelo cookie e a rota fica só /tv; qualquer /tv/* é o link antigo, desligado.
const tvRoute = createRoute({ getParentRoute: () => rootRoute, path: '/tv', component: TvLayout })
const tvIndexRoute = createRoute({ getParentRoute: () => tvRoute, path: '/', component: TvPage })
const tvLegacyRoute = createRoute({ getParentRoute: () => tvRoute, path: '$', component: TvLegacyPage })
const galleryRoute = createRoute({ getParentRoute: () => rootRoute, path: '/dev/galeria', component: Gallery ?? NotFound })

export const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  verifyRoute,
  requiredSetupRoute,
  forgotRoute,
  resetRoute,
  inviteRoute,
  tagRoute,
  appRoute.addChildren([appIndexRoute, assetsRootRoute, assetsRoute, newAssetRoute, assetRoute, assetSitesRoute, dashboardRoute, accountRoute, accountSecurityRoute, accessRoute, usersRoute, groupsRoute, settingsRoute, companyRoute, numberingRoute, parametersRoute, securityRoute, projectResultsRoute, projectResultRoute, myPayslipsRoute, myTimeClockRoute,
    aiSettingsRoute, importRunsRoute, importRunRoute, integrationsRoute, integrationEventsRoute, interAccountsRoute, interAccountRoute, partiesRootRoute, partiesRoute, newPartyRoute, partyRoute, billingSettingsRoute,
    inventoryRootRoute, balancesRoute, movementsRoute, movementRoute, manualPostingsRoute, manualPostingRoute, shortagesRoute, requisitionsRoute, requisitionRoute, locationsRoute, itemsRoute, newItemRoute, itemRoute, unitsRoute, categoriesRoute, warrantyRoute, engineeringRootRoute, routingsRoute, newRoutingRoute, routingRoute, bomsRoute, productionRootRoute, fabricationOrdersRoute, fabricationOrderRoute, kanbanRoute, openLaborRoute, operatorRoute, workCentersRoute, calendarRoute, installationsRoute, installationRoute, installationTemplateRoute, qualityRoute, qualityStepRoute, nonConformitiesRoute, nonConformityRoute, purchasingRootRoute, purchaseRequestsRoute, newPurchaseRequestRoute, purchaseRequestRoute, purchaseOrdersRoute, newPurchaseOrderRoute, purchaseOrderRoute, editPurchaseOrderRoute, shipmentsRoute, newShipmentRoute, shipmentRoute, traceabilityRoute, adjustmentsRoute, adjustmentRoute, goodsReceiptsRoute, goodsReceiptRoute, newGoodsReceiptRoute, rfqsRoute, newRfqRoute, rfqRoute, inventoryCountsRoute, inventoryCountRoute, capacityRoute, planningRoute, supervisorRoute,
    financeRootRoute, bankAccountsRoute, paymentTermsRoute, newPaymentTermRoute, paymentTermRoute, chartRoute, costCentersRoute, receivablesRoute, newReceivableRoute, discountsReportRoute, receivableRoute, payablesRoute, newPayableRoute, payableRoute, recurringRoute, approvalRulesRoute, openingRoute, cashFlowRoute, dreRoute, agingRoute, paymentOrdersRoute, newPaymentOrderRoute, paymentOrderRoute, statementRoute, reconciliationRoute, quotesRoute, quoteFunnelRoute, quoteRoute, quoteCompareRoute, contractsRoute, contractRoute, amendmentRoute, projectsRoute, projectMarginsRoute, projectRoute, hrRootRoute, employeesRoute, newEmployeeRoute, employeeRoute, positionsRoute, schedulesRoute, unionsRoute, hrTimeRoute, timeEntriesRoute, timeAdjustmentsRoute, kioskScreensRoute, kioskPreviewRoute, payrollRunsRoute, payrollRunRoute, payrollItemsRoute, fiscalIntegrationRoute, fiscalSettingsRoute, fiscalCertificateRoute, privacyRequestsRoute, privacyRequestRoute, privacyRetentionRoute, fiscalRootRoute, taxRulesRoute, newTaxRuleRoute, taxRuleRoute, incomingInvoicesRoute, importIncomingRoute, incomingInvoiceRoute, invoiceReceiptRoute, accountantExportRoute, ...devRoutes, moduleRoute, moduleTabRoute]),
  platformLoginRoute,
  platformPasswordRoute,
  platformRoute.addChildren([platformIndexRoute, tenantsRoute, tenantRoute]),
  fabEntryRoute,
  fabRoute.addChildren([fabQueueRoute, fabStepRoute, fabQualityRoute, fabQualityStepRoute, fabTimeClockRoute, fabAssetRoute]),
  countingRoute.addChildren([countingIndexRoute]),
  tvRoute.addChildren([tvIndexRoute, tvLegacyRoute]),
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
