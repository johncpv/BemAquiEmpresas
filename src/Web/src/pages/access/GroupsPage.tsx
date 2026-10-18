import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { getListGroupsQueryKey, useGetGroup, useListGroups } from '@/api/generated/bemaqui'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { cn } from '@/lib/utils'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { GroupEditor } from './GroupEditor'
import { NewGroupDialog } from './NewGroupDialog'

/** Usuários e acessos → Grupos (FND-06b, protótipo): lista à esquerda, matriz à direita; grupo escolhido fica na URL. */
export function GroupsPage() {
  const search = useSearch({ strict: false }) as { grupo?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const groups = useListGroups()
  const selectedId = search.grupo ?? groups.data?.[0]?.id
  const detail = useGetGroup(selectedId ?? '', { query: { enabled: !!selectedId } })
  const select = (id: string) => void navigate({ to: '.', search: { grupo: id }, replace: true } as never)

  return (
    <>
      <PageHeading title="Usuários e acessos: Grupos">
        <p className="mt-1 text-muted-foreground">O usuário recebe permissões pelos grupos em que está. Em mais de um grupo, vale a soma das permissões.</p>
      </PageHeading>
      <div className="grid gap-4 lg:grid-cols-[280px_1fr]">
        <div className="min-w-0 space-y-3">
          {groups.isPending ? <Skeleton className="h-64" /> : groups.isError ? <ErrorState onRetry={() => groups.refetch()} /> : (
            <ul aria-label="Grupos" className="divide-y divide-border rounded-md border border-border bg-card">
              {groups.data.map((g) => (
                <li key={g.id}>
                  <button type="button" onClick={() => select(g.id)} aria-current={g.id === selectedId ? 'true' : undefined} className={cn('w-full px-3 py-2 text-left hover:bg-muted', g.id === selectedId && 'bg-primary-soft')}>
                    <b className="block text-sm font-semibold">{g.name}</b>
                    <span className="flex items-center gap-2 text-xs text-muted-foreground">
                      {g.memberCount} {g.memberCount === 1 ? 'usuário' : 'usuários'}
                      {!g.isActive ? <StatusChip tone="warn">Inativo</StatusChip> : null}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <Can perm="identity.create">
            <NewGroupDialog groups={groups.data ?? []} onCreated={async (id) => { await client.invalidateQueries({ queryKey: getListGroupsQueryKey() }); select(id) }} />
          </Can>
          <div className="space-y-1 rounded-md border border-border bg-card p-3 text-xs text-muted-foreground">
            <b className="block text-sm text-foreground">Como a permissão é calculada</b>
            <p>1. O módulo precisa estar liberado na versão contratada.</p>
            <p>2. O módulo precisa estar habilitado em algum grupo do usuário.</p>
            <p>3. Cada ação (ver, criar, editar, aprovar, excluir) é marcada no grupo.</p>
            <p>Ninguém concede o que não tem; aprovar, pagar e dados sensíveis só o Proprietário concede.</p>
          </div>
        </div>
        <div className="min-w-0">
          {!selectedId ? null : detail.isPending ? <Skeleton className="h-96" /> : detail.isError ? <ErrorState onRetry={() => detail.refetch()} /> : (
            <GroupEditor key={detail.data.group.id + detail.dataUpdatedAt} detail={detail.data} onSelect={select} />
          )}
        </div>
      </div>
    </>
  )
}
