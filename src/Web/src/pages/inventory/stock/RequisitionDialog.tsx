import { useState } from 'react'
import { confirmMaterialRequest, createMaterialRequisition, getRequisitionItemStock, searchItems, useGetRequisitionDraft } from '@/api/generated/bemaqui'
import type { RequisitionDetail, RequisitionDraft } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { formatQuantity } from './stock'
import {
  excessOf, extraRow, impactOf, needsApproval, overValue, REQUISITION_DICAS, type RequisitionRow, rowProblems, rowsFromDraft, rowsFromRequest, splitRow, toInput, totalValue,
} from './requisitions'

const search = async (term: string): Promise<ComboOption[]> =>
  (await searchItems({ q: term || undefined })).filter((i) => i.type !== 'Service').map((i) => ({ id: i.id, label: `${i.code} — ${i.name}` }))

interface Props {
  orderId: string
  onClose: () => void
  onDone: (detail: RequisitionDetail) => void
  /** Confirmar o pedido do operador (CA 7): o id, a versão e o que foi pedido. */
  request?: { id: string; number: string; version: number; lines: readonly { itemId: string; quantity: number }[] }
}

/**
 * "Requisitar material" (CA 1 a 4) e a confirmação do pedido do operador (CA 7): as reservas ativas da OS com todo o reservado a
 * baixar, o lote sugerido ("Sugerido") e a divisão em lotes, o "Material extra" e o motivo do consumo acima da BOM; o impacto por
 * item (quantidade, lote, disponível antes → depois) e o total pelo custo médio. Acima do limite, "Enviar para aprovação". Diálogo
 * em tela cheia a 400 px, com as linhas em cartões.
 */
export function RequisitionDialog({ orderId, onClose, onDone, request }: Props) {
  const draft = useGetRequisitionDraft(orderId)
  if (draft.isPending) {
    return <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb="Baixar material" done="Material baixado" title="Requisitar material" preconditions={[{ ok: false, text: 'Carregando as reservas da OS.' }]} onConfirm={async () => undefined}><Skeleton className="h-32" /></ConfirmDialog>
  }

  if (draft.isError) {
    return (
      <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb="Baixar material" done="Material baixado" title="Requisitar material" preconditions={[{ ok: false, text: 'Reservas da OS carregadas.' }]} onConfirm={async () => undefined}>
        <ErrorState title="Não foi possível carregar as reservas da OS." message="Verifique a conexão e tente de novo." onRetry={() => draft.refetch()} />
      </ConfirmDialog>
    )
  }

  return <RequisitionForm draft={draft.data} request={request} onClose={onClose} onDone={onDone} />
}

function RequisitionForm({ draft, request, onClose, onDone }: { draft: RequisitionDraft } & Omit<Props, 'orderId'>) {
  const [rows, setRows] = useState<RequisitionRow[]>(() => (request ? rowsFromRequest(draft, request.lines) : rowsFromDraft(draft)))
  const [extra, setExtra] = useState<ComboOption | null>(null)
  const [errors, setErrors] = useState<Record<number, string>>({})
  const idempotency = useIdempotencyKey()
  const canSeeCost = useCan('projects.view_cost')
  const excess = excessOf(rows)
  const over = overValue(rows, excess)
  const approval = needsApproval(over, draft.approvalLimit)
  const impact = impactOf(rows.filter((r) => Number(r.quantity) > 0))
  const update = (key: string, change: Partial<RequisitionRow>) => setRows((all) => all.map((r) => (r.key === key ? { ...r, ...change } : r)))
  const verb = approval ? 'Enviar para aprovação' : 'Baixar material'

  const addExtra = async (option: ComboOption | null) => {
    setExtra(option)
    if (!option) return
    try {
      const stock = await getRequisitionItemStock(option.id)
      const location = stock.locations.find((l) => l.available > 0) ?? stock.locations.find((l) => l.isDefault) ?? stock.locations[0]
      if (!location) {
        notifyError('Este item não tem saldo em nenhum local.')
        return
      }

      setRows((all) => [...all, extraRow(stock, location)])
      setExtra(null)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível carregar o saldo do item.')
    }
  }

  const submit = async () => {
    setErrors({})
    const lines = toInput(rows)
    const result = request
      ? await confirmMaterialRequest(request.id, { lines, version: request.version }, idempotency.options)
      : await createMaterialRequisition(draft.productionOrderId, { lines }, idempotency.options)
    idempotency.renew()
    onDone(result)
  }

  const title = request ? `Confirmar o pedido ${request.number}` : `Requisitar material da ${draft.orderNumber ?? 'OS'}`
  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb={verb} done={approval ? 'Requisição enviada para aprovação' : 'Material baixado'} title={title}
      description={request ? 'Ajuste o lote e a quantidade ao que foi separado. O operador vê "Material separado" quando você baixar.' : 'Vem todo o reservado para a OS. Ajuste o lote e a quantidade ao que vai sair do estoque.'}
      preconditions={rowProblems(rows).map((text) => ({ ok: false, text }))} onConfirm={submit}
      onFieldErrors={(e) => {
        const byLine: Record<number, string> = {}
        for (const [field, messages] of Object.entries(e.fields)) {
          const match = /^lines\[(\d+)\]/.exec(field)
          if (match) byLine[Number(match[1])] = messages[0]
        }
        setErrors(byLine)
        return Object.keys(byLine).length > 0
      }}
      impact={impact.length > 0 ? (
        <div aria-live="polite" className="space-y-1 text-sm">
          {impact.map((g) => (
            <p key={`${g.itemCode}-${g.locationCode}`} className="tabular-nums">
              <b>{g.itemCode}</b> · {formatQuantity(g.quantity)} {g.unitCode}{g.lots.length ? ` · lote ${g.lots.join(', ')}` : ''} · {g.locationCode}: físico {formatQuantity(g.onHand)} → <b>{formatQuantity(g.onHandAfter)}</b>, disponível {formatQuantity(g.available)} → <b>{formatQuantity(g.availableAfter)}</b>
            </p>
          ))}
          <p>Total pelo custo médio de agora: <Money value={totalValue(rows)} className="font-semibold" />{over > 0 ? <> · acima da BOM: <Money value={over} /></> : null}</p>
          {canSeeCost && draft.projectNumber ? <p>Entra no custo do projeto {draft.projectNumber}.</p> : null}
          {approval ? <p role="status" className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">O consumo acima da BOM passa de <Money value={draft.approvalLimit} />: a requisição vai para a aprovação de outra pessoa e nada sai do estoque até lá.</p> : null}
        </div>
      ) : null}>
      {draft.orderClosed ? <p role="alert" className="text-sm text-crit-ink">A OS está concluída ou cancelada e não recebe material.</p> : null}
      {rows.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum material reservado para baixar. Acrescente um material extra, se precisar.</p> : null}
      <ul className="space-y-3" aria-label="Linhas da requisição">
        {rows.map((r, i) => {
          const lot = r.lots.find((l) => l.lotId === r.lotId)
          return (
            <li key={r.key} className="space-y-2 rounded-md border border-border bg-card p-3">
              <p className="flex flex-wrap items-center gap-2 text-sm">
                <b><span className="font-mono">{r.itemCode}</span> {r.itemName}</b>
                <span className="text-muted-foreground">{r.locationCode}</span>
                {r.isExtra ? <span className="inline-flex items-center"><StatusChip tone="warn">Material extra</StatusChip><Dica nome="Consumo acima da BOM" texto={REQUISITION_DICAS.consumoAcima} /></span> : null}
              </p>
              <p className="text-xs text-muted-foreground tabular-nums">Reservado {formatQuantity(r.reserved)} · disponível livre {formatQuantity(r.available)} {r.unitCode}</p>
              <div className="grid gap-2 sm:grid-cols-2">
                {r.tracksLot ? (
                  <label className="block text-sm">
                    <span className="font-medium">Lote</span>
                    <span className="flex items-center gap-2">
                      <select value={r.lotId ?? ''} onChange={(e) => update(r.key, { lotId: e.target.value || null, suggestedLot: false })} className={inputClass} aria-label={`Lote de ${r.itemCode}`}>
                        <option value="">Escolha</option>
                        {r.lots.map((l) => <option key={l.lotId} value={l.lotId}>{l.lotNumber} — {formatQuantity(l.onHand)} {r.unitCode}{l.expiry ? ` · validade ${l.expiry.split('-').reverse().join('/')}` : ''}</option>)}
                      </select>
                      {r.suggestedLot && lot?.suggested ? <span className="inline-flex items-center"><Tag>Sugerido</Tag><Dica nome="Sugerido" texto={REQUISITION_DICAS.sugerido} /></span> : null}
                    </span>
                  </label>
                ) : null}
                <label className="block text-sm">
                  <span className="font-medium">Quantidade ({r.unitCode})</span>
                  <QuantityInput value={r.quantity} onChange={(v) => update(r.key, { quantity: v })} aria-label={`Quantidade de ${r.itemCode}`} />
                </label>
              </div>
              {excess[i] > 0 ? (
                <label className="block text-sm">
                  <span className="inline-flex items-center font-medium">Motivo do consumo acima da BOM ({formatQuantity(excess[i])} {r.unitCode})<Dica nome="Consumo acima da BOM" texto={REQUISITION_DICAS.consumoAcima} /></span>
                  <textarea value={r.overReason} maxLength={200} rows={2} onChange={(e) => update(r.key, { overReason: e.target.value })} className={inputClass} aria-label={`Motivo do consumo acima da BOM de ${r.itemCode}`} />
                </label>
              ) : null}
              {errors[i] ? <p role="alert" className="text-sm text-crit-ink">{errors[i]}</p> : null}
              <p className="flex flex-wrap gap-3 text-sm">
                {r.tracksLot && r.lots.length > 1 ? <button type="button" className="font-semibold text-primary" onClick={() => setRows((all) => { const at = all.findIndex((x) => x.key === r.key); return [...all.slice(0, at + 1), splitRow(r), ...all.slice(at + 1)] })}>Dividir em outro lote</button> : null}
                <button type="button" className="font-semibold text-primary" onClick={() => setRows((all) => all.filter((x) => x.key !== r.key))}>Tirar da requisição</button>
              </p>
            </li>
          )
        })}
      </ul>
      <div className="space-y-1">
        <p className="inline-flex items-center text-sm font-medium">Material extra (fora da BOM)<Dica nome="Consumo acima da BOM" texto={REQUISITION_DICAS.consumoAcima} /></p>
        <Combobox queryKey={['requisition-items']} search={search} value={extra?.id ?? null} selected={extra} placeholder="Busque o item pelo código ou nome" onChange={(o) => void addExtra(o)} aria-label="Material extra" />
      </div>
    </ConfirmDialog>
  )
}
