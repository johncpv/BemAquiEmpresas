import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deleteItem, getGetItemQueryKey, inactivateItem, reactivateItem, useGetItem } from '@/api/generated/bemaqui'
import type { ItemDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { TYPE_LABELS } from './item-labels'
import { ItemDataForm } from './ItemDataForm'

/** Item (CAD-02a, molde "detalhe com abas"): Dados e Histórico de alterações; Conversões e Fornecedores entram com a CAD-02b. */
export function ItemPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetItem(id)
  const canEdit = useCan('inventory.edit')
  const [steps, setSteps] = useState(search.novo === '1')

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Item não encontrado.' : 'Não foi possível carregar o item.'} message={notFound ? 'Ele pode ter sido excluído. Volte à lista de itens.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const item = query.data
  const setItem = (i: ItemDetail) => client.setQueryData(getGetItemQueryKey(id), i)
  const setActive = async (active: boolean) => setItem(active ? await reactivateItem(id) : await inactivateItem(id))
  const duplicate = () => void navigate({ to: '/app/estoque/itens/novo', search: { duplicar: id } as never })

  return (
    <>
      <PageHeading title={`${item.code} — ${item.name}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <StatusChip tone={item.isActive ? 'ok' : 'neutral'}>{item.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
            <Tag>{TYPE_LABELS[item.type]}</Tag>
            <span className="text-sm text-muted-foreground">Custo médio: <Money value={item.averageCost} /></span>
            <Link to="/app/estoque/itens" className="ml-2 text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          <div className="flex flex-wrap gap-2">
            <Can perm="inventory.create">
              <button type="button" onClick={duplicate} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Duplicar item</button>
            </Can>
            <Can perm="inventory.edit">
              {item.isActive ? (
                <ConfirmDialog verb="Inativar" done="Item inativado" title="Inativar este item?" description="Ele some dos seletores e continua nos documentos que já o usam. Dá para reativar."
                  onConfirm={() => setActive(false)} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar item</button>} />
              ) : (
                <ConfirmDialog verb="Reativar" done="Item reativado" title="Reativar este item?" onConfirm={() => setActive(true)}
                  trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>} />
              )}
            </Can>
            <Can perm="inventory.delete">
              <ConfirmDialog verb="Excluir" done="Item excluído" destructive title="Excluir este item?" description="Só dá para excluir item sem movimento, saldo, estrutura de produto ou documento."
                onConfirm={async () => { await deleteItem(id); await navigate({ to: '/app/estoque/itens' }) }}
                recovery={item.isActive ? { verb: 'Inativar', done: 'Item inativado', when: (code) => code === 'item_in_use', onSelect: () => setActive(false) } : undefined}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir item</button>} />
            </Can>
          </div>
        </div>
      </PageHeading>
      {steps ? (
        <div className="mb-4">
          <NextSteps onDismiss={() => setSteps(false)} steps={[
            { label: 'Cadastrar outro item', onSelect: () => void navigate({ to: '/app/estoque/itens/novo' }) },
            { label: 'Duplicar este item', onSelect: duplicate },
          ]} />
        </div>
      ) : null}
      <UrlTabs label="Seções do item" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', keepMounted: true, content: <ItemDataForm key={item.id} item={item} canEdit={canEdit} onSaved={setItem} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Item" id={item.id} /> },
      ]} />
    </>
  )
}
