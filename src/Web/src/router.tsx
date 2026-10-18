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
import { AiSettingsPage } from '@/pages/settings/AiSettingsPage'
import { ImportRunPage } from '@/pages/settings/import/ImportRunPage'
import { ImportRunsPage } from '@/pages/settings/import/ImportRunsPage'
import { ShopfloorLayout, TvLayout } from '@/pages/DedicatedLayouts'
import { Inicio } from '@/pages/Inicio'
import { ModulePage, NotFound } from '@/pages/ModulePage'
import { getPlatformCurrentAdmin } from '@/api/generated/bemaqui'
import { PlatformLayout } from '@/platform/PlatformLayout'
import { PlatformLoginPage } from '@/platform/PlatformLoginPage'
import { PlatformSetPasswordPage } from '@/platform/PlatformSetPasswordPage'
import { TenantDetailPage } from '@/platform/TenantDetailPage'
import { TenantsPage } from '@/platform/TenantsPage'
import { ComponentsMold, CreateMold, DetailMold, PanelMold } from '@/dev/molds/lazy'
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
const loginRoute = createRoute({ getParentRoute: () => rootRoute, path: '/entrar', component: LoginPage, validateSearch: (s: Record<string, unknown>): { volta?: string; email?: string } => ({ volta: text(s.volta), email: text(s.email) }) })
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
const settingsRoute = createRoute({ getParentRoute: () => appRoute, path: 'configuracoes', beforeLoad: () => { throw redirect({ to: '/app/configuracoes/seguranca' }) } })
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
const fabRoute = createRoute({ getParentRoute: () => rootRoute, path: '/fab', component: ShopfloorLayout })
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
  appRoute.addChildren([appIndexRoute, accountRoute, accountSecurityRoute, accessRoute, usersRoute, groupsRoute, settingsRoute, securityRoute,
    aiSettingsRoute, importRunsRoute, importRunRoute, partiesRootRoute, partiesRoute, newPartyRoute, partyRoute, ...devRoutes, moduleRoute, moduleTabRoute]),
  platformLoginRoute,
  platformPasswordRoute,
  platformRoute.addChildren([platformIndexRoute, tenantsRoute, tenantRoute]),
  fabRoute,
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
