import { Link, useParams } from '@tanstack/react-router'
import { findModuleBySlug } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'

/** Tela de módulo ainda sem conteúdo: diz o que vem e oferece voltar ao início (estado vazio com ação). */
export function ModulePage() {
  const { module: slug, tab: tabSlug } = useParams({ strict: false }) as { module?: string; tab?: string }
  const { areas } = useNavigation()
  const found = slug ? findModuleBySlug(areas, slug) : undefined
  if (!found) return <NotFound />

  const tab = found.module.tabs.find((t) => t.slug === tabSlug)
  return (
    <>
      <PageHeading title={tab ? `${found.module.name}: ${tab.label}` : found.module.name} />
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
