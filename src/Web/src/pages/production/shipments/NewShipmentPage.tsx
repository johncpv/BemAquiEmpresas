import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { createShipment, getListReadyToShipQueryKey, getListShipmentsQueryKey, useGetShipmentForm, useListShipmentGeneralStock } from '@/api/generated/bemaqui'
import type { ReadyToShipLine, ShipmentForm } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { choicesToLines, defaultChoices, formProblems, type LineChoice, lineKey, quantity, SHIPMENT_DICAS, SHIPMENT_TEXTS } from './shipments'

const primary = 'inline-flex min-h-10 items-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-10 items-center rounded-md border border-border px-3 text-sm font-semibold'

/**
 * Novo romaneio (/app/producao/expedicao/novo?projeto=&os=, PRD-06b CA 1, 2 e 9): vem preenchido com as séries e quantidades do projeto
 * (padrão: todas; vindo da OS, as séries da OS), o endereço da obra, a transportadora sugerida (tag "Sugerido"), placa, motorista,
 * volumes, peso e data prevista. "Incluir produto do estoque geral" acrescenta produto acabado sem projeto, marcado. A 400 px, itens
 * e séries em cartões.
 */
export function NewShipmentPage() {
  const { projeto, os } = useSearch({ strict: false }) as { projeto?: string; os?: string }
  const form = useGetShipmentForm({ projectId: projeto ?? '', productionOrderId: os }, { query: { enabled: !!projeto } })
  if (!projeto) {
    return (
      <>
        <PageHeading title="Novo romaneio" />
        <ProductionTabs />
        <EmptyState title="Escolha o projeto." description="O romaneio é de um projeto: abra pela lista Prontos para expedir ou pelo projeto."
          action={<Link to={'/app/producao/expedicao' as never} className={secondary}>Ver prontos para expedir</Link>} />
      </>
    )
  }

  if (form.isPending) return <Skeleton className="h-96" />
  if (form.isError) return <ErrorState title="Não foi possível abrir o romaneio." message="O projeto pode não existir. Volte à expedição e tente de novo." onRetry={() => form.refetch()} />
  return <ShipmentEditor form={form.data} />
}

function ShipmentEditor({ form }: { form: ShipmentForm }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const idempotency = useIdempotencyKey()
  const [choices, setChoices] = useState<LineChoice[]>(() => defaultChoices(form.available, form.productionOrderId))
  const [address, setAddress] = useState(form.deliveryAddress ?? '')
  const [carrier, setCarrier] = useState(form.suggestedCarrierId ?? '')
  const [plate, setPlate] = useState('')
  const [driver, setDriver] = useState('')
  const [volumes, setVolumes] = useState('')
  const [weight, setWeight] = useState('')
  const [planned, setPlanned] = useState('')
  const [general, setGeneral] = useState(false)
  const [error, setError] = useState<string | undefined>()
  const [busy, setBusy] = useState(false)
  const problems = formProblems(address, choices)
  const update = (key: string, change: Partial<LineChoice>) => setChoices((all) => all.map((c) => (c.key === key ? { ...c, ...change } : c)))
  const addGeneral = (line: ReadyToShipLine) => {
    if (choices.some((c) => c.key === lineKey(line))) return
    setChoices((all) => [...all, { key: lineKey(line), line, serialIds: [], quantity: line.tracksSerial ? '' : '1', general: true }])
  }

  const save = async () => {
    setBusy(true)
    setError(undefined)
    try {
      const saved = await createShipment({
        projectId: form.projectId, deliveryAddress: address.trim(), carrierPartyId: carrier || null, vehiclePlate: plate.trim() || null, driverName: driver.trim() || null,
        volumes: volumes.trim() ? Number(volumes) : null, grossWeight: weight.trim() ? Number(weight.replace(/\./g, '').replace(',', '.')) : null, plannedDate: planned || null,
        lines: choicesToLines(choices),
      }, idempotency.options)
      idempotency.renew()
      notifySuccess(SHIPMENT_TEXTS.salvo)
      await client.invalidateQueries({ queryKey: getListShipmentsQueryKey() })
      await client.invalidateQueries({ queryKey: getListReadyToShipQueryKey() })
      await navigate({ to: '/app/producao/expedicao/$id', params: { id: saved.id }, search: { salvo: 1 } } as never)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o romaneio. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeading title={`Novo romaneio — ${form.projectNumber}`}>
        <p className="mt-1 flex items-center text-sm text-muted-foreground">{form.partyName} · {form.projectName}<Dica nome="Romaneio" texto={SHIPMENT_DICAS.romaneio} /></p>
      </PageHeading>
      <ProductionTabs />
      <div className="space-y-4">
        <FormError message={error} />
        <fieldset className="grid gap-4 rounded-md border border-border bg-card p-4 sm:grid-cols-2">
          <legend className="px-1 text-sm font-semibold">Entrega e transporte</legend>
          <label className="space-y-1 text-sm font-medium sm:col-span-2">Endereço de entrega
            <textarea rows={2} maxLength={500} value={address} onChange={(e) => setAddress(e.target.value)} className={inputClass} />
            <span className="block text-xs font-normal text-muted-foreground">O da obra do projeto, já preenchido. Edite se a entrega for em outro lugar.</span>
          </label>
          <label className="space-y-1 text-sm font-medium">
            <span className="flex items-center gap-2">Transportadora (opcional){carrier && carrier === form.suggestedCarrierId ? <Tag>Sugerido</Tag> : null}</span>
            <select value={carrier} onChange={(e) => setCarrier(e.target.value)} className={inputClass}>
              <option value="">Sem transportadora</option>
              {form.carriers.map((c) => <option key={c.id} value={c.id}>{c.name}{c.suggested ? ' (última usada com o cliente)' : ''}</option>)}
            </select>
          </label>
          <label className="space-y-1 text-sm font-medium">Data prevista<input type="date" value={planned} onChange={(e) => setPlanned(e.target.value)} className={inputClass} /></label>
          <label className="space-y-1 text-sm font-medium">Placa (opcional)<input value={plate} maxLength={10} onChange={(e) => setPlate(e.target.value)} className={`${inputClass} uppercase`} /></label>
          <label className="space-y-1 text-sm font-medium">Motorista (opcional)<input value={driver} maxLength={120} onChange={(e) => setDriver(e.target.value)} className={inputClass} /></label>
          <label className="space-y-1 text-sm font-medium">Volumes (opcional)<input inputMode="numeric" value={volumes} onChange={(e) => setVolumes(e.target.value.replace(/\D/g, ''))} className={`${inputClass} tabular-nums`} /></label>
          <label className="space-y-1 text-sm font-medium">Peso bruto em kg (opcional)<input inputMode="decimal" value={weight} onChange={(e) => setWeight(e.target.value)} className={`${inputClass} tabular-nums`} /></label>
        </fieldset>

        <section aria-labelledby="itens-romaneio" className="space-y-2">
          <h2 id="itens-romaneio" className="text-base font-semibold">Itens</h2>
          {choices.length === 0 ? (
            <EmptyState title="O projeto não tem produto acabado no estoque." description="Conclua a OS com o produto no estoque ou inclua produto do estoque geral."
              action={<Link to={'/app/producao/os' as never} search={{ projeto: form.projectId } as never} className={secondary}>Ver OS do projeto</Link>} />
          ) : (
            <ul className="space-y-2">{choices.map((c) => <ChoiceCard key={c.key} choice={c} onChange={(change) => update(c.key, change)} onRemove={c.general ? () => setChoices((all) => all.filter((x) => x.key !== c.key)) : undefined} />)}</ul>
          )}
          <button type="button" className={secondary} onClick={() => setGeneral((v) => !v)} aria-expanded={general}>Incluir produto do estoque geral</button>
          {general ? <GeneralStockPicker onPick={addGeneral} /> : null}
        </section>

        {problems.length > 0 ? <ul className="list-disc pl-5 text-sm text-muted-foreground">{problems.map((p) => <li key={p}>{p}</li>)}</ul> : null}
        <div className="flex flex-wrap gap-2">
          <button type="button" className={primary} disabled={busy || problems.length > 0} onClick={() => void save()}>{busy ? 'Salvando…' : 'Salvar romaneio'}</button>
          <Link to={'/app/producao/expedicao' as never} className={secondary}>Voltar</Link>
        </div>
      </div>
    </>
  )
}

/** Cartão do item (400 px): séries para marcar (as que estão em outro romaneio ficam desabilitadas) ou a quantidade. */
function ChoiceCard({ choice: c, onChange, onRemove }: { choice: LineChoice; onChange: (change: Partial<LineChoice>) => void; onRemove?: () => void }) {
  const l = c.line
  return (
    <li className="rounded-md border border-border bg-card p-3 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span><b className="font-semibold">{l.itemCode}</b> {l.itemName} <span className="text-muted-foreground">· {l.locationName}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''}</span></span>
        <span className="flex items-center gap-2">
          {c.general ? <span className="inline-flex items-center"><Tag>Estoque geral</Tag><Dica nome="Estoque geral" texto={SHIPMENT_DICAS.estoqueGeral} /></span> : null}
          <span className="tabular-nums text-xs text-muted-foreground">Disponível {quantity(l.shippable)} {l.unitCode}</span>
          {onRemove ? <button type="button" onClick={onRemove} className="text-xs font-semibold text-primary underline">Tirar</button> : null}
        </span>
      </div>
      {l.tracksSerial ? (
        <fieldset className="mt-2">
          <legend className="text-xs font-medium text-muted-foreground">Séries ({c.serialIds.length} de {l.serials.length})</legend>
          <ul className="mt-1 grid gap-1 sm:grid-cols-2">
            {l.serials.map((s) => (
              <li key={s.id}>
                <label className="flex min-h-9 items-center gap-2 tabular-nums">
                  <input type="checkbox" disabled={!!s.openShipmentNumber} checked={c.serialIds.includes(s.id)}
                    onChange={(e) => onChange({ serialIds: e.target.checked ? [...c.serialIds, s.id] : c.serialIds.filter((x) => x !== s.id) })} />
                  {s.serialNumber}{s.openShipmentNumber ? <span className="text-xs text-muted-foreground">no {s.openShipmentNumber}</span> : null}
                </label>
              </li>
            ))}
          </ul>
        </fieldset>
      ) : (
        <label className="mt-2 flex items-center gap-2 text-sm font-medium">Quantidade
          <input inputMode="decimal" value={c.quantity} onChange={(e) => onChange({ quantity: e.target.value })} className={`${inputClass} w-32 tabular-nums`} />
          <span className="text-xs font-normal text-muted-foreground">{l.unitCode}{l.inOpenShipments > 0 ? ` · ${quantity(l.inOpenShipments)} já em romaneio aberto` : ''}</span>
        </label>
      )}
    </li>
  )
}

/** "Incluir produto do estoque geral" (CA 2): busca o produto acabado sem projeto; a linha entra marcada e pede outra pessoa do estoque. */
function GeneralStockPicker({ onPick }: { onPick: (line: ReadyToShipLine) => void }) {
  const [term, setTerm] = useState('')
  const found = useListShipmentGeneralStock({ q: term || undefined })
  return (
    <div className="space-y-2 rounded-md border border-border bg-muted p-3 text-sm">
      <p className="flex items-center">Produto acabado sem projeto. Para confirmar a saída, outra pessoa com aprovação no Estoque confirma.<Dica nome="Estoque geral" texto={SHIPMENT_DICAS.estoqueGeral} /></p>
      <label className="block space-y-1 font-medium">Buscar produto<input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Código ou nome" className={inputClass} /></label>
      {found.isPending ? <Skeleton className="h-12" /> : found.isError ? <p className="text-crit-ink">Não foi possível buscar o estoque geral.</p> : found.data.length === 0 ? (
        <p className="text-muted-foreground">Nenhum produto acabado sem projeto com esse nome.</p>
      ) : (
        <ul className="space-y-1">
          {found.data.map((l) => (
            <li key={lineKey(l)} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-2">
              <span><b className="font-semibold">{l.itemCode}</b> {l.itemName} <span className="text-muted-foreground">· {l.locationName} · disponível {quantity(l.shippable)} {l.unitCode}</span></span>
              <button type="button" onClick={() => onPick(l)} className="rounded-md border border-border px-2 py-1 text-xs font-semibold text-primary">Incluir</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
