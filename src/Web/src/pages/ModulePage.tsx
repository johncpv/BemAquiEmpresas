import { Link, useParams } from '@tanstack/react-router'
import { Lock, ShieldOff } from 'lucide-react'
import { findModuleBySlug, type NavModule } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { useHelpTab } from '@/help/help-context'
import { PageHeading } from '@/shell/PageHeading'

export const NOT_RELEASED_MESSAGE = 'Funcionalidade não liberada nessa versão do sistema.'
export const ACCESS_DENIED_MESSAGE = 'Você não tem acesso a esta funcionalidade. Fale com o administrador.'

/** Tela padrão de módulo não liberado (CLAUDE.md, FND-06d CA 2): nome, a mensagem exata e o que o módulo fará. */
export function ModuleNotReleased({ module }: { module: Pick<NavModule, 'name' | 'description'> }) {
  useHelpTab('nao-liberado')
  return (
    <>
      <PageHeading title={module.name} />
      <div className="max-w-xl space-y-2 rounded-md border border-border bg-card p-6">
        <Lock aria-hidden="true" className="size-6 text-muted-foreground" />
        <p className="font-semibold">{NOT_RELEASED_MESSAGE}</p>
        {module.description ? <p className="text-sm text-muted-foreground">{module.description}</p> : null}
        <p className="text-sm text-muted-foreground">Quando o módulo for liberado, o administrador habilita o acesso no grupo de usuários.</p>
        <Link to="/app" className="inline-block pt-2 text-sm font-semibold text-primary">Voltar ao início</Link>
      </div>
    </>
  )
}

/** Liberado, mas sem grupo que dê acesso (FND-06d CA 3; mensagem da ADR 0010 item 8). */
export function AccessDenied({ module }: { module: Pick<NavModule, 'name'> }) {
  useHelpTab('sem-acesso')
  return (
    <>
      <PageHeading title="Sem acesso a esta tela" />
      <div className="max-w-xl space-y-2 rounded-md border border-border bg-card p-6">
        <ShieldOff aria-hidden="true" className="size-6 text-muted-foreground" />
        <p className="font-semibold">{ACCESS_DENIED_MESSAGE}</p>
        <p className="text-sm text-muted-foreground">Seu usuário não tem permissão para {module.name}. Peça ao administrador para incluir você num grupo com acesso.</p>
        <Link to="/app" className="inline-block pt-2 text-sm font-semibold text-primary">Voltar ao início</Link>
      </div>
    </>
  )
}

/** Rota de um módulo: tela, cadeado ou acesso negado, conforme /me/permissions; nunca uma tela em branco. */
export function ModulePage() {
  const { module: slug, tab: tabSlug } = useParams({ strict: false }) as { module?: string; tab?: string }
  const { all, status } = useNavigation()
  if (status === 'loading') return null
  const module = slug ? findModuleBySlug(all, slug) : undefined
  if (!module) return <NotFound />
  if (module.locked) return <ModuleNotReleased module={module} />
  if (!module.enabled) return <AccessDenied module={module} />

  const tab = module.tabs.find((t) => t.slug === tabSlug)
  return (
    <>
      <PageHeading title={tab ? `${module.name}: ${tab.label}` : module.name} />
      <div className="rounded-md border border-dashed border-border bg-card p-6">
        <p className="font-semibold">Esta tela está em construção.</p>
        <p className="mt-1 text-sm text-muted-foreground">Ela chega numa próxima entrega do BemAqui Empresas.</p>
        <Link to="/app" className="mt-4 inline-block text-sm font-semibold text-primary">Voltar ao início</Link>
      </div>
    </>
  )
}

export function NotFound() {
  return (
    <>
      <PageHeading title="Página não encontrada" />
      <p className="text-muted-foreground">O endereço não existe ou mudou.</p>
      <Link to="/app" className="mt-4 inline-block text-sm font-semibold text-primary">Ir para o início</Link>
    </>
  )
}
