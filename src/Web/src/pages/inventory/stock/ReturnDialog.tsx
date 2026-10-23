import { useState } from 'react'
import { recordMaterialLoss, returnMaterialToStock, useListLocations } from '@/api/generated/bemaqui'
import type { RequisitionDetail, ReturnableLine } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { lineTotal, formatQuantity } from './stock'
import { num, REQUISITION_DICAS } from './requisitions'

interface Props {
  orderId: string
  projectNumber?: string | null
  lines: readonly ReturnableLine[]
  /** "Devolver ao estoque" (CA 12) ou "Manter no projeto como perda" (CA 13, só na OS cancelada). */
  mode: 'return' | 'loss'
  onClose: () => void
  onDone: (detail: RequisitionDetail) => void
}

/**
 * Devolução ao estoque (CA 12) e perda da OS cancelada (CA 13): as linhas baixadas com o que ainda pode voltar (padrão: tudo), o
 * local que recebe (o da baixa, sugerido) e o impacto pelo custo da baixa original. Diálogo em tela cheia a 400 px.
 */
export function ReturnDialog({ orderId, projectNumber, lines, mode, onClose, onDone }: Props) {
  const [picked, setPicked] = useState<Record<string, string | null>>(() => Object.fromEntries(lines.map((l) => [l.issueLineId, String(l.returnable)])))
  const [location, setLocation] = useState('')
  const [reason, setReason] = useState('')
  const locations = useListLocations({ pageSize: 100 })
  const active = (locations.data?.items ?? []).filter((l) => l.isActive)
  const idempotency = useIdempotencyKey()
  const canSeeCost = useCan('projects.view_cost')
  const suggested = lines[0]?.locationId ?? ''
  const locationId = location || suggested
  const chosen = lines.filter((l) => num(picked[l.issueLineId]) > 0)
  const value = chosen.reduce((sum, l) => sum + lineTotal(num(picked[l.issueLineId]), l.unitCost), 0)
  const loss = mode === 'loss'
  const preconditions = [
    { ok: chosen.length > 0, text: 'Ao menos uma linha com quantidade.' },
    ...chosen.filter((l) => num(picked[l.issueLineId]) > l.returnable).map((l) => ({ ok: false, text: `${l.itemCode}: até ${formatQuantity(l.returnable)} ${l.unitCode} (o baixado e ainda não devolvido).` })),
    ...(loss ? [{ ok: reason.trim().length > 0, text: 'Motivo da perda escrito.' }] : [{ ok: !!locationId, text: 'Local que recebe escolhido.' }]),
  ]
  const submit = async () => {
    const body = chosen.map((l) => ({ issueLineId: l.issueLineId, quantity: num(picked[l.issueLineId]) }))
    const result = loss
      ? await recordMaterialLoss(orderId, { reason: reason.trim(), lines: body }, idempotency.options)
      : await returnMaterialToStock(orderId, { locationId, lines: body }, idempotency.options)
    idempotency.renew()
    onDone(result)
  }

  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb={loss ? 'Manter no projeto como perda' : 'Devolver ao estoque'} done={loss ? 'Perda registrada' : 'Material devolvido'}
      title={loss ? 'Manter o material no projeto como perda' : 'Devolver ao estoque'}
      description={<span className="inline-flex items-center">{loss ? 'O material não volta ao estoque e o custo continua no projeto.' : 'A devolução sai pelo custo da baixa original.'}<Dica nome={loss ? 'Perda' : 'Devolver ao estoque'} texto={loss ? REQUISITION_DICAS.perda : REQUISITION_DICAS.devolver} /></span>}
      preconditions={preconditions} onConfirm={submit}
      impact={chosen.length > 0 ? (
        <div aria-live="polite" className="space-y-1 text-sm">
          {chosen.map((l) => <p key={l.issueLineId} className="tabular-nums"><b>{l.itemCode}</b> · {formatQuantity(num(picked[l.issueLineId]))} {l.unitCode}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''} · da {l.requisitionNumber}</p>)}
          <p>Valor pelo custo da baixa: <Money value={value} className="font-semibold" /></p>
          {canSeeCost && projectNumber ? <p>{loss ? `Continua no custo do projeto ${projectNumber}.` : `Sai do custo do projeto ${projectNumber}.`}</p> : null}
        </div>
      ) : null}>
      <ul className="space-y-2" aria-label="Linhas baixadas">
        {lines.map((l) => (
          <li key={l.issueLineId} className="grid gap-2 rounded-md border border-border bg-card p-3 sm:grid-cols-[1fr_10rem]">
            <span className="text-sm">
              <b><span className="font-mono">{l.itemCode}</span> {l.itemName}</b>
              <span className="block text-xs text-muted-foreground tabular-nums">{l.requisitionNumber} · {l.locationCode}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''} · baixado {formatQuantity(l.issued)}, pode voltar {formatQuantity(l.returnable)} {l.unitCode}</span>
            </span>
            <QuantityInput value={picked[l.issueLineId] ?? null} onChange={(v) => setPicked((p) => ({ ...p, [l.issueLineId]: v }))} aria-label={`Quantidade de ${l.itemCode}`} />
          </li>
        ))}
      </ul>
      {loss ? (
        <label className="block text-sm">
          <span className="font-medium">Motivo da perda</span>
          <textarea value={reason} maxLength={200} rows={2} onChange={(e) => setReason(e.target.value)} className={inputClass} />
        </label>
      ) : (
        <label className="block text-sm">
          <span className="font-medium">Local que recebe</span>
          <span className="flex items-center gap-2">
            <select value={locationId} onChange={(e) => setLocation(e.target.value)} className={inputClass} aria-label="Local que recebe">
              <option value="">Escolha</option>
              {active.map((l) => <option key={l.id} value={l.id}>{l.code} — {l.name}</option>)}
            </select>
            {!location && suggested ? <Tag>Sugerido</Tag> : null}
          </span>
        </label>
      )}
    </ConfirmDialog>
  )
}
