import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetShopfloorStepMaterialQueryKey, requestShopfloorMaterial, useGetShopfloorStepMaterial } from '@/api/generated/bemaqui'
import type { ShopfloorMaterialItem } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { IDEMPOTENCY_HEADER } from '@/api/idempotency'
import { FloorButton, FloorDica, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { notifySuccess } from '@/components/ui/messages'
import { StatusChip } from '@/components/ui/status-chip'
import { requestStateLabel } from '@/pages/inventory/stock/requisitions'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { MATERIAL_DICA, parseFloorQuantity, stepQuantity } from './shopfloor'

const time = (iso: string) => new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' })

/**
 * Material da etapa no app do operador (EST-03 CA 5 a 7): "Pedir material" abre a folha de tela cheia com os itens da BOM e as
 * quantidades; o pedido vai ao almoxarifado e **nunca** baixa estoque. Os pedidos da etapa aparecem com "Pedido enviado ao
 * almoxarifado", "Material separado" ou "Recusado: {motivo}". Depois de pausar por falta de material, "Pedir material" vem em
 * destaque como próximo passo.
 */
export function FabMaterial({ stepId, highlight }: { stepId: string; highlight: boolean }) {
  const client = useQueryClient()
  const query = useGetShopfloorStepMaterial(stepId)
  const [open, setOpen] = useState(false)
  if (query.isPending) return <FloorSkeleton rows={1} />
  if (query.isError) {
    // Sem o Estoque liberado (403 module_not_released) a seção some; o resto da etapa continua.
    if (query.error instanceof ApiError && query.error.status === 403) return null
    return <FloorState tone="error" title="Não foi possível carregar o material da etapa." action={<FloorButton tone="secondary" onClick={() => void query.refetch()}>Tentar de novo</FloorButton>} />
  }

  const view = { ...query.data, requests: query.data.requests ?? [], items: query.data.items ?? [] }
  return (
    <section aria-labelledby="material-etapa" className={highlight ? 'space-y-3 rounded-md border-2 border-warn bg-warn-soft p-4' : 'space-y-3'}>
      <h2 id="material-etapa" className="flex items-center text-[20px] font-bold">Material<FloorDica nome="Pedir material" texto={MATERIAL_DICA} /></h2>
      {highlight ? <p className="text-[18px] text-warn-ink">Pausou por falta de material? Peça ao almoxarifado o que falta.</p> : null}
      <FloorButton className="w-full" tone={highlight ? 'primary' : 'secondary'} onClick={() => setOpen(true)}>Pedir material</FloorButton>
      {view.requests.length > 0 ? (
        <ul className="space-y-[8px]" aria-label="Pedidos de material desta etapa">
          {view.requests.map((r) => {
            const state = requestStateLabel(r.state, r.declineReason)
            return (
              <li key={r.id} className="rounded-md border border-border bg-card p-3 text-[18px]">
                <p className="flex flex-wrap items-center gap-2"><b>{r.number}</b><StatusChip tone={state.tone}>{state.label}</StatusChip><span className="text-base text-muted-foreground">{time(r.requestedAt)}</span></p>
                <p className="text-base text-muted-foreground tabular-nums">{r.lines.map((l) => `${l.itemName} ${formatQuantity(l.quantity)} ${l.unitCode}`).join(' · ')}</p>
              </li>
            )
          })}
        </ul>
      ) : null}
      {open ? (
        <RequestSheet stepId={stepId} orderNumber={view.orderNumber} bomReleased={view.bomReleased} items={view.items}
          onClose={() => setOpen(false)} onSent={async () => { setOpen(false); await client.invalidateQueries({ queryKey: getGetShopfloorStepMaterialQueryKey(stepId) }) }} />
      ) : null}
    </section>
  )
}

function RequestSheet({ stepId, orderNumber, bomReleased, items, onClose, onSent }: {
  stepId: string; orderNumber: string; bomReleased: boolean; items: readonly ShopfloorMaterialItem[]; onClose: () => void; onSent: () => Promise<void>
}) {
  const [quantities, setQuantities] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [key] = useState(() => crypto.randomUUID())
  const lines = items.map((i) => ({ itemId: i.itemId, quantity: parseFloorQuantity(quantities[i.itemId]) })).filter((l) => l.quantity > 0)
  const send = async () => {
    setBusy(true)
    setError(null)
    try {
      await requestShopfloorMaterial(stepId, { lines }, { headers: { [IDEMPOTENCY_HEADER]: key } })
      notifySuccess('Pedido enviado ao almoxarifado')
      await onSent()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar o pedido. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <FloorSheet open onOpenChange={(o) => { if (!o) onClose() }} title={`Pedir material · ${orderNumber}`}
      description="Escolha os itens e as quantidades. O almoxarifado separa e baixa; o pedido não tira nada do estoque sozinho."
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={onClose} disabled={busy}>Voltar</FloorButton>
          {bomReleased && items.length > 0 ? <FloorButton className="flex-1" onClick={() => void send()} disabled={busy || lines.length === 0}>{busy ? 'Enviando…' : 'Pedir material'}</FloorButton> : null}
        </div>
      )}>
      {!bomReleased || items.length === 0 ? <FloorState title="Esta OS ainda não tem BOM liberada. Avise o supervisor." /> : (
        <ul className="space-y-[8px]" aria-label="Itens da BOM">
          {items.map((i) => {
            const value = quantities[i.itemId] ?? ''
            const set = (v: string) => setQuantities((q) => ({ ...q, [i.itemId]: v }))
            return (
              <li key={i.itemId} className="space-y-2 rounded-md border-2 border-input bg-card p-3">
                <p className="font-semibold">{i.itemName} <span className="font-normal text-muted-foreground">· {i.itemCode}</span></p>
                <p className="text-base text-muted-foreground tabular-nums">BOM {formatQuantity(i.required)} · reservado {formatQuantity(i.reserved)} · baixado {formatQuantity(i.issued)} {i.unitCode}</p>
                <div className="flex items-center gap-[8px]">
                  <FloorButton tone="secondary" aria-label={`Menos ${i.itemName}`} onClick={() => set(stepQuantity(value, -1))}>−</FloorButton>
                  <input inputMode="decimal" value={value} onChange={(e) => set(e.target.value)} aria-label={`Quantidade de ${i.itemName} (${i.unitCode})`} placeholder="0"
                    className="min-h-[56px] w-full flex-1 rounded-md border-2 border-input bg-card px-3 text-center text-[20px] tabular-nums" />
                  <FloorButton tone="secondary" aria-label={`Mais ${i.itemName}`} onClick={() => set(stepQuantity(value, 1))}>+</FloorButton>
                  <span className="w-12 text-base text-muted-foreground">{i.unitCode}</span>
                </div>
              </li>
            )
          })}
        </ul>
      )}
      {error ? <div role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] text-crit-ink">{error}</div> : null}
    </FloorSheet>
  )
}
