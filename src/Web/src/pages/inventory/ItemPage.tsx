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
import { ImportedTag } from '@/components/ui/imported-tag'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { TYPE_LABELS } from './item-labels'
import { ItemConversions } from './ItemConversions'
import { ItemDataForm } from './ItemDataForm'
import { ItemSupplierCodes } from './ItemSupplierCodes'
import { ItemKardex } from './stock/ItemKardex'
import { ItemLots } from './stock/ItemLots'
import { ItemSerials } from './stock/ItemSerials'
import { ItemReservations } from './stock/ReservationHistory'

/**
 * Item (CAD-02a, molde "detalhe com abas"): Dados, Conversões e Fornecedores (CAD-02b), Movimentos (kardex, EST-01a), Reservas (EST-02),
 * Lotes (EST-04), Séries (PRD-06a2) e Histórico de alterações.
 */
export function ItemPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetItem(id)
  const canEdit = useCan('inventory.edit')
  const [steps, setSteps] = useState(search.novo === '1')
  const [, setTab] = useUrlTab('dados')
  const [addingConversion, setAddingConversion] = useState(false)
  const [addingCode, setAddingCode] = useState(false)

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
            <ImportedTag source={item.importSource} />
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
      {steps && canEdit ? (
        <div className="mb-4">
          <NextSteps onDismiss={() => setSteps(false)} steps={[
            { label: 'Cadastrar conversão', onSelect: () => { setTab('conversoes'); setAddingConversion(true) } },
            { label: 'Vincular código do fornecedor', onSelect: () => { setTab('fornecedores'); setAddingCode(true) } },
            { label: 'Duplicar este item', onSelect: duplicate },
          ]} />
        </div>
      ) : null}
      <UrlTabs label="Seções do item" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', keepMounted: true, content: <ItemDataForm key={item.id} item={item} canEdit={canEdit} onSaved={setItem} /> },
        { id: 'conversoes', label: 'Conversões', content: <ItemConversions item={item} canEdit={canEdit} adding={addingConversion} onAddingChange={setAddingConversion} /> },
        { id: 'fornecedores', label: 'Fornecedores', content: <ItemSupplierCodes item={item} canEdit={canEdit} adding={addingCode} onAddingChange={setAddingCode} /> },
        { id: 'movimentos', label: 'Movimentos', content: <ItemKardex itemId={item.id} /> },
        // EST-02 CA 13 e 18: físico, reservado e disponível e o histórico da reserva em todas as OS.
        { id: 'reservas', label: 'Reservas', content: <ItemReservations itemId={item.id} /> },
        // EST-04 CA 5 e 6: lotes com fornecedor, entrada, validade, saldo e local; correção do lote digitado errado.
        { id: 'lotes', label: 'Lotes', content: <ItemLots itemId={item.id} tracksLot={item.tracksLot} /> },
        // PRD-06a2 CA 11: séries do produto acabado (série, OS, projeto, situação e local); EST-04: "Rastrear" abre a origem.
        { id: 'series', label: 'Séries', content: <ItemSerials itemId={item.id} tracksSerial={item.tracksSerial} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Item" id={item.id} /> },
      ]} />
    </>
  )
}
