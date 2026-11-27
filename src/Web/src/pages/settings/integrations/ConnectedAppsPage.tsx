import { Link } from '@tanstack/react-router'
import { useListConnectedApps } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { PageHeading } from '@/shell/PageHeading'
import { CONECTA_MODULE, connectionState } from './conecta'
import { IntegrationsNav } from './IntegrationsNav'

/**
 * Configurações → Integrações → Aplicativos conectados (INT-01 CA 20): aplicativo, conta, quem conectou, o que pode fazer, situação e último uso.
 * A 400 px ficam aplicativo, situação e último uso. Desconectar é só na página do aplicativo.
 */
export function ConnectedAppsPage() {
  const query = useListConnectedApps({ query: { retry: false } })
  if (query.error instanceof ApiError && query.error.code === 'module_not_released') return <ModuleNotReleased module={CONECTA_MODULE} />
  const items = query.data ?? []
  return (
    <>
      <PageHeading title="Aplicativos conectados" description="Os aplicativos que usam o ERP em nome da empresa, quem autorizou e o que cada um pode fazer." />
      <IntegrationsNav />
      {query.isPending ? (
        <div className="space-y-2" aria-busy="true">{[0, 1].map((i) => <Skeleton key={i} className="h-12" />)}</div>
      ) : query.isError ? (
        <ErrorState title="Não foi possível carregar os aplicativos." onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState
          title="Nenhum aplicativo conectado."
          description="Conecte o Conecta para vender e cobrar pelo WhatsApp."
          action={<Link to="/app/configuracoes/integracoes/conecta" className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground">Conectar o Conecta</Link>}
        />
      ) : (
        <table className="w-full text-sm">
          <thead className="text-left text-muted-foreground">
            <tr>
              <th className="py-2 pr-3 font-semibold">Aplicativo</th>
              <th className="hidden py-2 pr-3 font-semibold md:table-cell">Conta</th>
              <th className="hidden py-2 pr-3 font-semibold md:table-cell">Quem conectou</th>
              <th className="hidden py-2 pr-3 font-semibold lg:table-cell">O que pode fazer</th>
              <th className="py-2 pr-3 font-semibold">Situação</th>
              <th className="py-2 font-semibold">Último uso</th>
            </tr>
          </thead>
          <tbody>
            {items.map((app) => {
              const c = app.connection
              return (
                <tr key={app.clientId} className="border-t border-border align-top">
                  <td className="py-2 pr-3"><Link to="/app/configuracoes/integracoes/conecta" className="font-semibold text-primary">{app.name}</Link></td>
                  <td className="hidden py-2 pr-3 md:table-cell">{c?.accountLabel ?? '—'}</td>
                  <td className="hidden py-2 pr-3 md:table-cell">{c?.grantorName ?? '—'}</td>
                  <td className="hidden py-2 pr-3 lg:table-cell">{c?.scopes.map((s) => s.text).join(' · ') ?? '—'}</td>
                  <td className="py-2 pr-3"><StatusChip tone={connectionState(c).tone}>{c?.statusText ?? 'Não conectado'}</StatusChip></td>
                  <td className="py-2 tabular-nums">{c?.lastUsedAt ? formatDateTime(c.lastUsedAt) : '—'}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </>
  )
}
