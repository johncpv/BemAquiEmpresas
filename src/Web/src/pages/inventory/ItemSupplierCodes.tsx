import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { createItemSupplierCode, deleteItemSupplierCode, getListItemSupplierCodesQueryKey, listSupplierOptions, updateItemSupplierCode, useListItemSupplierCodes } from '@/api/generated/bemaqui'
import type { ItemDetail, SupplierCodeDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DecimalInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'

const factorText = (n: number) => new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 6 }).format(n)
const searchSuppliers = async (term: string) => (await listSupplierOptions({ q: term })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))

/** Aba Fornecedores do item (CAD-02b CA 8 a 14): o código e a unidade que cada fornecedor usa, para a nota de entrada. */
export function ItemSupplierCodes({ item, canEdit, adding, onAddingChange }: { item: ItemDetail; canEdit: boolean; adding: boolean; onAddingChange: (v: boolean) => void }) {
  const query = useListItemSupplierCodes(item.id)
  const client = useQueryClient()
  const [editing, setEditing] = useState<SupplierCodeDetail | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListItemSupplierCodesQueryKey(item.id) })
  const close = () => { setEditing(null); onAddingChange(false) }
  const newButton = canEdit ? <button type="button" onClick={() => onAddingChange(true)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Vincular fornecedor</button> : undefined

  if (query.isPending) return <Skeleton className="h-40" />
  if (query.isError) return <ErrorState onRetry={() => query.refetch()} />
  return (
    <div className="max-w-3xl space-y-3">
      {query.data.length === 0 ? (
        <EmptyState title="Nenhum código de fornecedor." description="Ele é preenchido sozinho no primeiro recebimento por nota, ou cadastre agora." action={newButton} />
      ) : (
        <>
          <div className="flex justify-end">{newButton}</div>
          <ul className="divide-y divide-border rounded-md border border-border">
            {query.data.map((c) => (
              <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
                <span className="flex flex-wrap items-center gap-2">
                  <button type="button" onClick={() => setEditing(c)} className="font-semibold text-primary hover:underline">{c.partyName}</button>
                  {c.partyDeleted ? <StatusChip tone="neutral">Fornecedor excluído</StatusChip> : null}
                </span>
                <span className="tabular-nums">Código <b className="font-mono">{c.supplierCode}</b>{c.supplierUnit ? ` · 1 ${c.supplierUnit} = ${factorText(c.conversionFactor)} ${item.unitCode}` : ''}</span>
                {canEdit ? (
                  <ConfirmDialog verb="Remover" done="Removido" destructive title={`Remover o código ${c.supplierCode} de ${c.partyName}?`} description="A próxima nota desse fornecedor vai pedir o item de novo."
                    onConfirm={async () => { await deleteItemSupplierCode(item.id, c.id); refresh() }}
                    trigger={<button type="button" className="text-sm font-semibold text-crit-ink">Remover código do fornecedor<span className="sr-only"> {c.supplierCode}</span></button>} />
                ) : null}
              </li>
            ))}
          </ul>
        </>
      )}
      {adding || editing ? <SupplierCodePanel item={item} code={editing} canEdit={canEdit} onClose={close} onSaved={() => { refresh(); close() }} /> : null}
    </div>
  )
}

function SupplierCodePanel({ item, code, canEdit, onClose, onSaved }: { item: ItemDetail; code: SupplierCodeDetail | null; canEdit: boolean; onClose: () => void; onSaved: () => void }) {
  const initial = { party: code ? { id: code.partyId, label: code.partyName } : null, supplierCode: code?.supplierCode ?? '', unit: code?.supplierUnit ?? item.unitCode, factor: String(code?.conversionFactor ?? 1) as string | null }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<{ message: string; code?: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const local = !draft.party ? 'Escolha o fornecedor.' : !draft.supplierCode.trim() ? 'Informe o código que o fornecedor usa.' : !(Number(draft.factor ?? 0) > 0) ? 'Informe um fator maior que zero.' : undefined
  const [tried, setTried] = useState(false)

  const save = async () => {
    setTried(true)
    if (local || !draft.party) return false
    const input = { partyId: draft.party.id, supplierCode: draft.supplierCode.trim(), supplierUnit: draft.unit.trim() || null, conversionFactor: Number(draft.factor) }
    try {
      if (code) await updateItemSupplierCode(item.id, code.id, input)
      else await createItemSupplierCode(item.id, input)
      notifySuccess('Código do fornecedor salvo')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, code: e.code, fields: e.fields } : { message: 'Não foi possível salvar o código do fornecedor.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={code ? `Código de ${code.partyName}` : 'Vincular fornecedor'} dirty={dirty && canEdit} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar código do fornecedor</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormError message={error?.message ?? (tried ? local : undefined)} />
        {error?.code === 'party_not_supplier' && draft.party ? (
          <Link to="/app/cadastros/terceiros/$id" params={{ id: draft.party.id }} className="inline-block text-sm font-semibold text-primary">Abrir o cadastro do terceiro</Link>
        ) : null}
        <Field label="Fornecedor" required error={error?.fields.partyId?.[0] && error.code !== 'party_not_supplier' ? error.fields.partyId[0] : undefined}>
          {(p) => <Combobox {...p} queryKey={['supplier-options']} search={searchSuppliers} value={draft.party?.id ?? null} selected={draft.party} placeholder="Busque pelo nome ou CNPJ" onChange={(party) => { setError(null); setDraft({ ...draft, party }) }} />}
        </Field>
        <Field label="Código do fornecedor" required error={error?.fields.supplierCode?.[0]} hint="Como sai na nota fiscal dele.">
          {(p) => <input {...p} value={draft.supplierCode} maxLength={60} onChange={(e) => setDraft({ ...draft, supplierCode: e.target.value })} className={inputClass + ' font-mono'} />}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Unidade do fornecedor" error={error?.fields.supplierUnit?.[0]} hint="Como vem na nota (ex.: CX).">
            {(p) => <input {...p} value={draft.unit} maxLength={10} onChange={(e) => setDraft({ ...draft, unit: e.target.value.toUpperCase() })} className={inputClass + ' uppercase'} />}
          </Field>
          <Field label={`Fator para ${item.unitCode}`} required error={error?.fields.conversionFactor?.[0]} hint={`1 ${draft.unit || 'unidade'} = ${factorText(Number(draft.factor ?? 0))} ${item.unitCode}`}>
            {(p) => <DecimalInput {...p} scale={6} value={draft.factor} onChange={(factor) => setDraft({ ...draft, factor })} />}
          </Field>
        </div>
      </fieldset>
      {code ? (
        <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="ItemSupplierCode" id={code.id} /></div>
        </details>
      ) : null}
    </SidePanel>
  )
}
