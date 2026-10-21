import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { getListRoutingTemplateItemsQueryKey, linkRoutingTemplateItem, listRoutingProductOptions, unlinkRoutingTemplateItem, useListRoutingTemplateItems } from '@/api/generated/bemaqui'
import type { RoutingTemplateDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { useCan } from '@/pages/use-can'

const TYPE: Record<string, string> = { Finished: 'Produto acabado', Component: 'Componente' }

/**
 * Aba Produtos (CA 7): produtos acabados e componentes ativos que usam o roteiro; a OS de fabricação (PRD-01) sugere o roteiro
 * pelo produto. Um produto tem um roteiro só: o vínculo recusado oferece "Abrir roteiro {nome}".
 */
export function RoutingProducts({ template, onChange }: { template: RoutingTemplateDetail; onChange: () => Promise<void> }) {
  const canEdit = useCan('engineering.edit')
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useListRoutingTemplateItems(template.id)
  const [picking, setPicking] = useState(false)
  const [refused, setRefused] = useState<{ message: string; id?: string; name?: string } | null>(null)
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getListRoutingTemplateItemsQueryKey(template.id) })
    await onChange()
  }

  const link = async (itemId: string) => {
    setRefused(null)
    try {
      await linkRoutingTemplateItem(template.id, { itemId })
      notifySuccess('Produto vinculado')
      setPicking(false)
      await refresh()
    } catch (e) {
      setRefused(e instanceof ApiError
        ? { message: e.message, id: typeof e.data.routingTemplateId === 'string' ? e.data.routingTemplateId : undefined, name: typeof e.data.routingTemplateName === 'string' ? e.data.routingTemplateName : undefined }
        : { message: 'Não foi possível vincular o produto. Tente de novo.' })
    }
  }

  const search = async (term: string) => (await listRoutingProductOptions({ q: term || undefined })).map((p) => ({
    id: p.id, label: `${p.code} — ${p.name}`, hint: p.routingTemplateName ? `Já usa ${p.routingTemplateName}` : TYPE[p.type] ?? p.type,
  }))

  const picker = canEdit ? (
    picking ? (
      <div className="w-full max-w-md space-y-1">
        <Combobox aria-label="Produto para vincular" queryKey={['routing-products']} search={search} placeholder="Buscar produto pelo código ou nome" onChange={(o) => { if (o) void link(o.id) }} />
        <p className="text-xs text-muted-foreground">Produtos acabados e componentes ativos do Estoque.</p>
      </div>
    ) : <button type="button" onClick={() => setPicking(true)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Vincular produto</button>
  ) : null

  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os produtos." onRetry={() => query.refetch()} />
  const items = query.data
  return (
    <div className="space-y-3">
      {refused ? (
        <div role="alert" className="flex flex-wrap items-center gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <span>{refused.message}</span>
          {refused.id ? <button type="button" onClick={() => void navigate({ to: '/app/engenharia/roteiros/$id', params: { id: refused.id }, search: { aba: 'produtos' } } as never)} className="rounded-md border border-border bg-card px-2 py-1 font-semibold text-foreground">Abrir roteiro {refused.name}</button> : null}
        </div>
      ) : null}
      {items.length === 0 ? (
        <EmptyState title="Nenhum produto usa este roteiro. Vincule para a OS nascer com ele." action={picker} />
      ) : (
        <>
          <div className="flex flex-wrap items-start justify-between gap-2">{picker}</div>
          <ul aria-label="Produtos vinculados" className="divide-y divide-border rounded-md border border-border bg-card text-sm">
            {items.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span className="flex flex-wrap items-center gap-2"><b className="tabular-nums">{i.code}</b> {i.name} <Tag>{TYPE[i.type] ?? i.type}</Tag>{i.isActive ? null : <Tag>Inativo</Tag>}</span>
                {canEdit ? (
                  <ConfirmDialog verb="Desvincular" done="Produto desvinculado" title={`Desvincular ${i.code}?`} description="A OS deste produto deixa de sugerir este roteiro. As OS já criadas não mudam."
                    onConfirm={async () => { await unlinkRoutingTemplateItem(template.id, i.id); await refresh() }}
                    trigger={<button type="button" className="text-xs font-semibold text-crit-ink">Desvincular</button>} />
                ) : null}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}