import { useNavigate, useSearch } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import { createPurchaseRequest, listPurchaseItemOptions, listPurchaseProjectOptions, useGetPurchaseRequestDefaults } from '@/api/generated/bemaqui'
import type { PurchaseItemOption, PurchaseRequestDefaults } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, QuantityInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, InlineError, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PURCHASE_DICAS, todayIso } from './purchasing'

interface Line {
  item: ComboOption | null
  quantity: string | null
  unitId: string
  neededBy: string
  /** A data ainda é a sugerida (tag "Sugerido"). */
  suggested: boolean
}

const label = (i: PurchaseItemOption) => `${i.code} — ${i.name}`

/**
 * Nova solicitação (CA 3, 4): linhas com item (só material ativo), quantidade na unidade de estoque ou noutra com conversão e a
 * necessidade, já preenchida com hoje + prazo padrão e a tag "Sugerido"; destino = projeto em andamento ou "Estoque geral". Vinda da
 * falta de uma OS (?os=&item=), o destino, o item, a quantidade que falta e a necessidade chegam preenchidos (propositivo).
 */
export function NewPurchaseRequestPage() {
  const { os, item } = useSearch({ strict: false }) as { os?: string; item?: string }
  const defaults = useGetPurchaseRequestDefaults({ productionOrderId: os, itemId: item })
  return (
    <>
      <PageHeading title="Nova solicitação de compra">
        <p className="mt-2 text-muted-foreground">Peça o que precisa ser comprado e para quando. Não tem aprovação: o comprador recebe na lista.</p>
      </PageHeading>
      {defaults.isPending ? <Skeleton className="h-96 max-w-4xl" /> : defaults.isError || !defaults.data
        ? <ErrorState title="Não foi possível abrir a solicitação." message="Verifique a conexão e tente de novo. Se veio de uma OS, ela pode ter sido removida." onRetry={() => defaults.refetch()} />
        : <PurchaseRequestForm defaults={defaults.data} />}
    </>
  )
}

function PurchaseRequestForm({ defaults }: { defaults: PurchaseRequestDefaults }) {
  const navigate = useNavigate()
  const canCreate = useCan('purchasing.create')
  const idempotency = useIdempotencyKey()
  const options = useRef(new Map<string, PurchaseItemOption>(defaults.item ? [[defaults.item.id, defaults.item]] : []))
  const first: Line = {
    item: defaults.item ? { id: defaults.item.id, label: label(defaults.item) } : null, quantity: defaults.quantity ? String(defaults.quantity) : null,
    unitId: defaults.item?.unitId ?? '', neededBy: defaults.neededBy, suggested: true,
  }
  const [lines, setLines] = useState<Line[]>([first])
  const [general, setGeneral] = useState(!defaults.projectId)
  const [project, setProject] = useState<ComboOption | null>(defaults.projectId ? { id: defaults.projectId, label: `${defaults.projectNumber ?? ''} — ${defaults.projectName ?? ''}` } : null)
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [failure, setFailure] = useState<string>()
  const [sending, setSending] = useState(false)
  const fromOrder = !!defaults.productionOrderId

  const searchItems = async (term: string): Promise<ComboOption[]> => {
    const found = await listPurchaseItemOptions({ q: term || undefined })
    for (const o of found) options.current.set(o.id, o)
    return found.map((o) => ({ id: o.id, label: label(o), hint: o.unitCode }))
  }
  const searchProjects = async (term: string): Promise<ComboOption[]> =>
    (await listPurchaseProjectOptions({ q: term || undefined })).map((p) => ({ id: p.id, label: `${p.number} — ${p.name}` }))

  const submit = async () => {
    setErrors({})
    setFailure(undefined)
    const missing: Record<string, string> = {}
    lines.forEach((l, i) => {
      if (!l.item) missing[`lines[${i}].itemId`] = 'Escolha o item.'
      if (!l.quantity || Number(l.quantity) <= 0) missing[`lines[${i}].quantity`] = 'Informe a quantidade, maior que zero.'
      if (!l.neededBy || l.neededBy < todayIso()) missing[`lines[${i}].neededBy`] = 'A necessidade não pode ser antes de hoje.'
    })
    if (!general && !project) missing.projectId = 'Escolha o projeto ou marque Estoque geral.'
    if (Object.keys(missing).length) {
      setErrors(missing)
      return
    }

    setSending(true)
    try {
      const saved = await createPurchaseRequest({
        projectId: general ? null : project!.id, productionOrderId: defaults.productionOrderId ?? null, notes: notes.trim() || null,
        lines: lines.map((l) => {
          const option = options.current.get(l.item!.id)
          return { itemId: l.item!.id, quantity: Number(l.quantity), unitId: !l.unitId || l.unitId === option?.unitId ? null : l.unitId, neededBy: l.neededBy }
        }),
      }, idempotency.options)
      idempotency.renew()
      notifySuccess('Solicitação enviada')
      void navigate({ to: '/app/compras/solicitacoes/$id', params: { id: saved.id }, search: { criada: 1 } } as never)
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length) setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]])))
      setFailure(e instanceof ApiError ? e.message : 'Não foi possível enviar a solicitação. Tente de novo.')
    } finally {
      setSending(false)
    }
  }

  return (
    <div className="max-w-4xl space-y-6">
      <FormSection title="Itens" description="Quantidade na unidade de estoque ou noutra com conversão cadastrada. Serviço não entra: lance um título a pagar.">
        <RowsEditor<Line> noun="item" rows={lines} onChange={setLines} min={1} max={50} disabled={!canCreate}
          newRow={() => ({ item: null, quantity: null, unitId: '', neededBy: defaults.neededBy, suggested: true })}
          rowError={(i) => errors[`lines[${i}]`]}
          columns={[
            {
              label: 'Item', width: 'minmax(0, 2fr)', render: (l, i, update) => {
                const error = errors[`lines[${i}].itemId`]
                return (
                  <div>
                    <Combobox aria-label={`Item da linha ${i + 1}`} aria-required aria-invalid={!!error} aria-describedby={error ? `linha-${i}-item` : undefined}
                      queryKey={['purchase-items']} search={searchItems} value={l.item?.id ?? null} selected={l.item} placeholder="Código ou nome" disabled={!canCreate}
                      onChange={(o) => update({ item: o, unitId: o ? options.current.get(o.id)?.unitId ?? '' : '' })} />
                    <InlineError id={`linha-${i}-item`} message={error} />
                  </div>
                )
              },
            },
            {
              label: 'Quantidade', width: '12rem', render: (l, i, update) => {
                const option = l.item ? options.current.get(l.item.id) : undefined
                const unit = option?.units.find((u) => u.unitId === l.unitId)
                const converted = unit && !unit.isStockUnit && l.quantity ? Number(l.quantity) * unit.toStock : null
                const error = errors[`lines[${i}].quantity`] ?? errors[`lines[${i}].unitId`]
                return (
                  <div>
                    <span className="flex items-center gap-2">
                      <QuantityInput aria-label={`Quantidade da linha ${i + 1}`} aria-invalid={!!error} aria-describedby={error ? `linha-${i}-qtd` : undefined} value={l.quantity} onChange={(v) => update({ quantity: v })} />
                      {option && option.units.length > 1 ? (
                        <select aria-label={`Unidade da linha ${i + 1}`} value={l.unitId} onChange={(e) => update({ unitId: e.target.value })} className={`${inputClass} w-20`}>
                          {option.units.map((u) => <option key={u.unitId} value={u.unitId}>{u.code}</option>)}
                        </select>
                      ) : <span className="text-sm text-muted-foreground">{option?.unitCode}</span>}
                    </span>
                    {converted !== null ? <p aria-live="polite" className="text-xs text-muted-foreground">= {formatQuantity(converted)} {option?.unitCode}</p> : null}
                    <InlineError id={`linha-${i}-qtd`} message={error} />
                  </div>
                )
              },
            },
            {
              label: 'Necessidade', width: '13rem', render: (l, i, update) => {
                const error = errors[`lines[${i}].neededBy`]
                return (
                  <div>
                    <span className="flex items-center gap-2">
                      <DateInput aria-label={`Necessidade da linha ${i + 1}`} aria-invalid={!!error} aria-describedby={error ? `linha-${i}-data` : undefined} value={l.neededBy}
                        onChange={(v) => update({ neededBy: v, suggested: false })} />
                      {l.suggested ? <Tag title={fromOrder ? 'Data prometida do projeto menos o prazo padrão de compra' : `Hoje + ${defaults.leadDays} dias (prazo padrão)`}>Sugerido</Tag> : null}
                    </span>
                    <InlineError id={`linha-${i}-data`} message={error} />
                  </div>
                )
              },
            },
          ]} />
        <p className="inline-flex items-center text-xs text-muted-foreground">Necessidade<Dica nome="Necessidade" texto={PURCHASE_DICAS.necessidade} /> é quando o material precisa estar na fábrica.</p>
      </FormSection>

      <FormSection title="Destino" description="Para um projeto em andamento ou para o estoque geral.">
        {fromOrder ? <p className="text-sm">Falta da <b>{defaults.orderNumber}</b>: o destino é o projeto da OS.</p> : null}
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={general} disabled={fromOrder || !canCreate} onChange={(e) => setGeneral(e.target.checked)} className="size-4 accent-primary" />
          Estoque geral (sem projeto)
        </label>
        {!general ? (
          <Field label="Projeto" required error={errors.projectId}>
            {(p) => <Combobox {...p} queryKey={['purchase-projects']} search={searchProjects} value={project?.id ?? null} selected={project} placeholder="Projeto em andamento" onChange={setProject} disabled={fromOrder || !canCreate} />}
          </Field>
        ) : null}
        <Field label="Observação (opcional)" error={errors.notes}>
          {(p) => <textarea {...p} value={notes} maxLength={500} rows={2} onChange={(e) => setNotes(e.target.value)} className={inputClass} />}
        </Field>
      </FormSection>

      <FormError message={failure} />
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={() => void navigate({ to: '/app/compras/solicitacoes' } as never)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
        {canCreate ? <button type="button" disabled={sending} onClick={() => void submit()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">{sending ? 'Enviando…' : 'Enviar solicitação'}</button> : null}
      </div>
    </div>
  )
}
