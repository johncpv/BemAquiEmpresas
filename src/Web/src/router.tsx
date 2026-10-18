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
import { ShopfloorLayout, TvLayout } from '@/pages/DedicatedLayouts'
import { Inicio } from '@/pages/Inicio'
import { ModulePage, NotFound } from '@/pages/ModulePage'
import { AppShell } from '@/shell/AppShell'

const Gallery = import.meta.env.DEV ? lazy(() => import('./dev/Gallery')) : null

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
const moduleRoute = createRoute({ getParentRoute: () => appRoute, path: '$module', component: ModulePage })
const moduleTabRoute = createRoute({ getParentRoute: () => appRoute, path: '$module/$tab', component: ModulePage })

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
  appRoute.addChildren([appIndexRoute, accountRoute, accountSecurityRoute, accessRoute, usersRoute, groupsRoute, settingsRoute, securityRoute, moduleRoute, moduleTabRoute]),
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
