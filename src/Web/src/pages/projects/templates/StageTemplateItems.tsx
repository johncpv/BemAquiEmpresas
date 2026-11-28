import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { getListStageTemplateItemsQueryKey, linkStageTemplateItem, listStageTemplateItemOptions, unlinkStageTemplateItem, useListStageTemplateItems } from '@/api/generated/bemaqui'
import type { StageTemplateDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { useCan } from '@/pages/use-can'

/**
 * Aba Itens (EXE-02 CA 10): itens de serviço ativos que sugerem este modelo no orçamento. Só item de serviço; um item tem um modelo só (o
 * vínculo recusado oferece "Abrir modelo {nome}"). Vincular exige projects.edit.
 */
export function StageTemplateItems({ template, onChange }: { template: StageTemplateDetail; onChange: () => Promise<void> }) {
  const canEdit = useCan('projects.edit')
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useListStageTemplateItems(template.id)
  const [picking, setPicking] = useState(false)
  const [refused, setRefused] = useState<{ message: string; id?: string; name?: string } | null>(null)
  const refresh = async () => {
    await client.invalidateQueries({ queryKey: getListStageTemplateItemsQueryKey(template.id) })
    await onChange()
  }

  const link = async (itemId: string) => {
    setRefused(null)
    try {
      await linkStageTemplateItem(template.id, { itemId })
      notifySuccess('Item vinculado')
      setPicking(false)
      await refresh()
    } catch (e) {
      setRefused(e instanceof ApiError
        ? { message: e.message, id: typeof e.data.stageTemplateId === 'string' ? e.data.stageTemplateId : undefined, name: typeof e.data.stageTemplateName === 'string' ? e.data.stageTemplateName : undefined }
        : { message: 'Não foi possível vincular o item. Tente de novo.' })
    }
  }

  const search = async (term: string) => (await listStageTemplateItemOptions({ q: term || undefined })).map((i) => ({
    id: i.id, label: `${i.code} — ${i.name}`, hint: i.stageTemplateName ? `Já usa ${i.stageTemplateName}` : 'Serviço',
  }))

  const picker = canEdit ? (
    picking ? (
      <div className="w-full max-w-md space-y-1">
        <Combobox aria-label="Item de serviço para vincular" queryKey={['stage-template-items']} search={search} placeholder="Buscar item de serviço pelo código ou nome" onChange={(o) => { if (o) void link(o.id) }} />
        <p className="text-xs text-muted-foreground">Só itens de serviço ativos do Estoque.</p>
      </div>
    ) : <button type="button" onClick={() => setPicking(true)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Vincular item de serviço</button>
  ) : null

  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os itens." onRetry={() => query.refetch()} />
  const items = query.data
  return (
    <div className="space-y-3">
      {refused ? (
        <div role="alert" className="flex flex-wrap items-center gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <span>{refused.message}</span>
          {refused.id ? <button type="button" onClick={() => void navigate({ to: '/app/projetos/modelos/$id', params: { id: refused.id }, search: { aba: 'itens' } } as never)} className="rounded-md border border-border bg-card px-2 py-1 font-semibold text-foreground">Abrir modelo {refused.name}</button> : null}
        </div>
      ) : null}
      {items.length === 0 ? (
        <EmptyState title="Nenhum item de serviço usa este modelo. Vincule para o orçamento sugerir este modelo pelo item." action={picker} />
      ) : (
        <>
          <div className="flex flex-wrap items-start justify-between gap-2">{picker}</div>
          <ul aria-label="Itens vinculados" className="divide-y divide-border rounded-md border border-border bg-card text-sm">
            {items.map((i) => (
              <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span className="flex flex-wrap items-center gap-2"><b className="tabular-nums">{i.code}</b> {i.name}{i.isActive ? null : <Tag>Inativo</Tag>}</span>
                {canEdit ? (
                  <ConfirmDialog verb="Desvincular" done="Item desvinculado" title={`Desvincular ${i.code}?`} description="O orçamento deixa de sugerir este modelo pelo item. Os projetos já criados não mudam."
                    onConfirm={async () => { await unlinkStageTemplateItem(template.id, i.id); await refresh() }}
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
