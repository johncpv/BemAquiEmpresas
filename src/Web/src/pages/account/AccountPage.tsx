import { Link } from '@tanstack/react-router'
import { useGetCurrentSession } from '@/api/generated/bemaqui'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { PageHeading } from '@/shell/PageHeading'

/** Meu perfil: quem está logado e o atalho para a segurança da conta. */
export function AccountPage() {
  const me = useGetCurrentSession()
  return (
    <>
      <PageHeading title="Meu perfil" />
      {me.isPending ? <Skeleton className="h-20 max-w-md" /> : me.isError ? <ErrorState onRetry={() => me.refetch()} /> : (
        <section className="max-w-md space-y-2 rounded-md border border-border bg-card p-5 text-sm">
          <p><span className="text-muted-foreground">Nome: </span>{me.data.name || '—'}</p>
          <p><span className="text-muted-foreground">E-mail: </span>{me.data.email}</p>
          <Link to="/app/conta/seguranca" className="inline-block pt-2 font-semibold text-primary">Verificação em duas etapas</Link>
        </section>
      )}
    </>
  )
}
