import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { createItemConversion, deleteItemConversion, getListItemConversionsQueryKey, updateItemConversion, useListItemConversions, useListUnits } from '@/api/generated/bemaqui'
import type { ConversionDetail, ItemDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DecimalInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'

const qty = (n: number) => new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 6 }).format(n)
const round4 = (n: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 4, maximumFractionDigits: 4 }).format(Math.round(n * 10000) / 10000)
const FACTOR_DICA = 'Quanto 1 unidade de um lado vale do outro. Exemplo: a chapa de aço 2 mm pesa 15,7 kg por metro quadrado, então 1 M2 = 15,7 KG.'

/**
 * Aba Conversões do item (CAD-02b CA 1, 2, 7 e 14): "1 {de} = {fator} {para}", com a prévia de 100 na unidade de estoque.
 * Uma das unidades é sempre a de estoque, que já vem preenchida.
 */
export function ItemConversions({ item, canEdit, adding, onAddingChange }: { item: ItemDetail; canEdit: boolean; adding: boolean; onAddingChange: (v: boolean) => void }) {
  const query = useListItemConversions(item.id)
  const client = useQueryClient()
  const [editing, setEditing] = useState<ConversionDetail | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListItemConversionsQueryKey(item.id) })
  const open = adding || editing !== null
  const close = () => { setEditing(null); onAddingChange(false) }
  const newButton = canEdit ? <button type="button" onClick={() => onAddingChange(true)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova conversão</button> : undefined

  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  return (
    <div className="max-w-3xl space-y-3">
      {query.data.length === 0 ? (
        <EmptyState title="Este item não tem conversões." description="Cadastre se ele é comprado ou consumido em outra unidade." action={newButton} />
      ) : (
        <>
          <div className="flex justify-end">{newButton}</div>
          <ul className="divide-y divide-border rounded-md border border-border">
            {query.data.map((c) => (
              <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
                <button type="button" onClick={() => setEditing(c)} className="font-semibold tabular-nums text-primary hover:underline">1 {c.fromUnitCode} = {qty(c.factor)} {c.toUnitCode}</button>
                <span className="text-muted-foreground tabular-nums">{preview(c, item.unitCode)}</span>
                {canEdit ? (
                  <ConfirmDialog verb="Remover" done="Removido" destructive title={`Remover a conversão entre ${c.fromUnitCode} e ${c.toUnitCode}?`} description="Os movimentos já lançados não mudam."
                    onConfirm={async () => { await deleteItemConversion(item.id, c.id); refresh() }}
                    trigger={<button type="button" className="text-sm font-semibold text-crit-ink">Remover conversão<span className="sr-only"> entre {c.fromUnitCode} e {c.toUnitCode}</span></button>} />
                ) : null}
              </li>
            ))}
          </ul>
        </>
      )}
      {open ? <ConversionPanel item={item} conversion={editing} canEdit={canEdit} onClose={close} onSaved={() => { refresh(); close() }} /> : null}
    </div>
  )
}

/** "Converter 100 KG em M2 → 6,3694": parte da unidade de estoque (CA 1). */
function preview(c: { fromUnitCode: string; toUnitCode: string; factor: number }, stock: string) {
  if (!c.factor) return ''
  const other = c.fromUnitCode === stock ? c.toUnitCode : c.fromUnitCode
  const perStock = c.fromUnitCode === stock ? c.factor : 1 / c.factor
  return `Converter 100 ${stock} em ${other} → ${round4(100 * perStock)}`
}

function ConversionPanel({ item, conversion, canEdit, onClose, onSaved }: { item: ItemDetail; conversion: ConversionDetail | null; canEdit: boolean; onClose: () => void; onSaved: () => void }) {
  const units = useListUnits({ includeInactive: true })
  const initial = { from: conversion?.fromUnitId ?? '', to: conversion?.toUnitId ?? item.unitId, factor: conversion ? String(conversion.factor) : null as string | null }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const code = (id: string) => units.data?.find((u) => u.id === id)?.code ?? ''
  const factor = Number(draft.factor ?? 0)
  const local = !draft.from || !draft.to ? 'Escolha as duas unidades.' : draft.from === draft.to ? 'Escolha duas unidades diferentes.'
    : draft.from !== item.unitId && draft.to !== item.unitId ? `Uma das unidades precisa ser ${item.unitCode}, a unidade de estoque.` : !(factor > 0) ? 'Informe um fator maior que zero.' : undefined

  const save = async () => {
    if (local) return false
    const input = { fromUnitId: draft.from, toUnitId: draft.to, factor }
    try {
      if (conversion) await updateItemConversion(item.id, conversion.id, input)
      else await createItemConversion(item.id, input)
      toastDone('Conversão salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a conversão.', fields: {} })
      return false
    }
  }

  const select = (label: string, value: string, onChange: (v: string) => void) => (
    <Field label={label} required>
      {(p) => (
        <select {...p} value={value} onChange={(e) => onChange(e.target.value)} className={inputClass}>
          <option value="">Escolha</option>
          {(units.data ?? []).filter((u) => u.isActive || u.id === value).map((u) => <option key={u.id} value={u.id}>{u.code} — {u.name}</option>)}
        </select>
      )}
    </Field>
  )

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={conversion ? 'Conversão de unidade' : 'Nova conversão'} description={`Unidade de estoque: ${item.unitCode}.`} dirty={dirty && canEdit} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar conversão</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormError message={error?.message ?? (dirty && draft.from && draft.factor ? local : undefined)} />
        {conversion ? <p role="status" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Os movimentos já lançados não mudam.</p> : null}
        {select('De (1 unidade de)', draft.from, (from) => setDraft({ ...draft, from }))}
        <Field label="Fator" required dica={FACTOR_DICA} error={error?.fields.factor?.[0]}>
          {(p) => <DecimalInput {...p} scale={6} value={draft.factor} onChange={(f) => setDraft({ ...draft, factor: f })} />}
        </Field>
        {select('Para (vale tantas)', draft.to, (to) => setDraft({ ...draft, to }))}
        {!local ? (
          <p aria-live="polite" className="rounded-md border border-border bg-card px-3 py-2 text-sm tabular-nums">
            1 {code(draft.from)} = {qty(factor)} {code(draft.to)} · {preview({ fromUnitCode: code(draft.from), toUnitCode: code(draft.to), factor }, item.unitCode)}
          </p>
        ) : null}
      </fieldset>
      {conversion ? (
        <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="ItemUnitConversion" id={conversion.id} /></div>
        </details>
      ) : null}
    </SidePanel>
  )
}
