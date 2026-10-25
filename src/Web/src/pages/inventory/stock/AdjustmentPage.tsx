import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import {
  approveStockAdjustment, cancelStockAdjustment, getGetStockAdjustmentQueryKey, rejectStockAdjustment, reverseStockAdjustment, useGetStockAdjustment,
} from '@/api/generated/bemaqui'
import type { StockAdjustmentDetail, StockAdjustmentLineView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { RecordHistory } from '@/components/history/RecordHistory'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { ADJUSTMENT_DICAS, ADJUSTMENT_MODE, itemsLabel } from './adjustments'
import { AdjustmentStatusChip } from './AdjustmentsPage'
import { formatQuantity, formatUnitCost } from './stock'

const button = (text: string, primary = false) => (
  <button type="button" className={primary ? 'min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'min-h-[44px] rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>
)

/** O que o ajuste faz no saldo de cada item (padrão 4): antes → depois e o valor; na aprovação, calculado sobre o saldo de agora. */
function Impact({ a, reverse = false }: { a: StockAdjustmentDetail; reverse?: boolean }) {
  const changed = !reverse && a.impact?.changedSinceRequest
  return (
    <div className="space-y-1">
      {!reverse && a.impact?.reservationNote ? <p role="status" className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">{a.impact.reservationNote} Quem aprova no Estoque recebe o aviso.</p> : null}
      {changed ? <p role="status" className="rounded border border-warn-soft bg-warn-soft px-2 py-1 text-xs text-warn-ink">O saldo mudou desde o envio. Os valores abaixo já são os de agora{a.mode === 'Counted' ? ': a diferença foi recalculada sobre o saldo atual' : ': a diferença enviada se soma ao saldo atual'}.</p> : null}
      <ul className="space-y-1">
        {a.lines.map((l) => {
          const delta = reverse ? -(l.appliedDeltaQty ?? 0) : l.deltaNow
          const before = l.systemQtyNow
          return (
            <li key={l.id} className="tabular-nums">
              <b>{l.itemCode}</b>{l.lotNumber ? ` · lote ${l.lotNumber}` : ''}: {formatQuantity(before)} → <b>{formatQuantity(before + delta)}</b> {l.unitCode} ({formatQuantity(delta, true)}) · <Money value={reverse ? Math.abs((l.appliedDeltaQty ?? 0) * (l.unitCost ?? 0)) : l.valueNow} />
            </li>
          )
        })}
      </ul>
      {reverse ? null : <p>Sobras <Money value={a.impact?.surplus ?? a.surplusValue} className="font-semibold" /> · Faltas <Money value={a.impact?.shortage ?? a.shortageValue} className="font-semibold" /></p>}
    </div>
  )
}

function LineCard({ a, l }: { a: StockAdjustmentDetail; l: StockAdjustmentLineView }) {
  const pending = a.status === 'Pending'
  return (
    <li className={l.refusal ? 'space-y-1 rounded-md border border-l-4 border-border border-l-crit bg-card p-3 text-sm' : 'space-y-1 rounded-md border border-border bg-card p-3 text-sm'}>
      <p><b><span className="font-mono">{l.itemCode}</span> {l.itemName}</b>{l.lotNumber ? <span className="text-muted-foreground"> · lote {l.lotNumber}</span> : null}</p>
      <p className="tabular-nums">
        Sistema no envio {formatQuantity(l.systemQtyAtRequest)} {l.unitCode}
        {a.mode === 'Counted' ? <> · contado <b>{formatQuantity(l.countedQty ?? 0)}</b></> : null}
        {' '}· diferença <b>{formatQuantity(l.deltaQty, true)}</b> · <Money value={l.estimatedValue} />
        {l.enteredUnitCode && l.enteredUnitCode !== l.unitCode ? <span className="text-muted-foreground"> (digitado {formatQuantity(l.enteredQuantity)} {l.enteredUnitCode})</span> : null}
      </p>
      {pending ? (
        <details>
          <summary className="cursor-pointer text-muted-foreground">Agora: disponível {formatQuantity(l.available)} {l.unitCode}{l.changed ? ' · o saldo mudou desde o envio' : ''}</summary>
          <p className="tabular-nums text-muted-foreground">Físico {formatQuantity(l.onHand)} · Reservado {formatQuantity(l.reserved)} · Sistema {formatQuantity(l.systemQtyNow)} · Diferença na aprovação {formatQuantity(l.deltaNow, true)} · Depois {formatQuantity(l.onHandAfter)}</p>
        </details>
      ) : null}
      {l.appliedDeltaQty !== null ? (
        <p className="tabular-nums">
          Aplicado {formatQuantity(l.appliedDeltaQty, true)} {l.unitCode}{l.unitCost !== null ? <> pelo custo médio {formatUnitCost(l.unitCost)}</> : null}
          {l.movementId ? <> · <Link to={'/app/estoque/movimentos/$id' as never} params={{ id: l.movementId } as never} className="font-semibold text-primary">{l.movementNumber ?? 'Ver movimento'}</Link></> : null}
        </p>
      ) : null}
      {l.refusal ? (
        <p role="alert" className="text-crit-ink">{l.refusal} <Link to={'/app/estoque/itens/$id' as never} params={{ id: l.itemId } as never} search={{ aba: 'reservas' } as never} className="font-semibold underline">Ver reservas do item</Link></p>
      ) : null}
    </li>
  )
}

/**
 * Ajuste de estoque (CA 5 a 12 e 17): cabeçalho com número, situação, local, modo e autor; fatos (itens, sobras, faltas, valor); abas
 * Itens, Decisão e Histórico de alterações. "Aprovar ajuste" e "Reprovar ajuste" (inventory.approve, outra pessoa) decidem sobre o que
 * foi enviado; "Cancelar ajuste" pelo autor; "Estornar ajuste" pelo documento. Aprovado, fica em modo leitura com a faixa do caminho.
 */
export function AdjustmentPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetStockAdjustment(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const [text, setText] = useState('')
  const [steps, setSteps] = useState<Step[]>([])
  const idempotency = useIdempotencyKey()
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Ajuste não encontrado.' : 'Não foi possível carregar o ajuste.'} message={notFound ? 'Ele pode ter sido digitado errado. Volte à lista de ajustes.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const a = query.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetStockAdjustmentQueryKey(id) }) }
  const after = (detail: StockAdjustmentDetail) => {
    setText('')
    const below = detail.status === 'Approved' && !detail.reversedAt && detail.lines.some((l) => l.belowMinimum)
    setSteps([
      { label: 'Próximo aguardando você', onSelect: () => void navigate({ to: '/app/estoque/ajustes' as never, search: { filtro: 'aguardando-voce' } as never }) },
      ...(below ? [{ label: 'Gerar solicitação de compra', onSelect: () => void navigate({ to: '/app/compras' as never }) }] : []),
      { label: 'Ver saldos do local', onSelect: () => void navigate({ to: '/app/estoque/saldos' as never, search: { local: detail.locationId } as never }) },
    ])
  }
  const refusalItem = a.impact?.refusalItemId
  const pending = a.status === 'Pending'
  return (
    <>
      <PageHeading title={`${a.number} — ajuste de ${a.locationName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <AdjustmentStatusChip status={a.status} reversed={!!a.reversedAt} />
            {a.selfApproved || a.reversalSelfApproved ? <span className="inline-flex items-center"><Tag>Autoaprovado</Tag><Dica nome="Autoaprovado" texto={ADJUSTMENT_DICAS.autoaprovado} /></span> : null}
            <span className="inline-flex items-center">{ADJUSTMENT_MODE[a.mode]}<Dica nome="Quantidade contada × diferença" texto={ADJUSTMENT_DICAS.modo} /></span>
            <span>{a.requestedByName ?? '—'}</span>
            <Link to={'/app/estoque/ajustes' as never} className="font-semibold text-primary">Voltar aos ajustes</Link>
          </p>
          <div className="flex flex-wrap gap-2">
            {a.canDecide ? (
              <>
                <ConfirmDialog size="lg" verb="Aprovar ajuste" done="Ajuste aprovado" title={`Aprovar o ${a.number}?`}
                  description="Você decide sobre o que foi enviado: o saldo muda agora, pelo custo médio do momento. Para mudar algo, reprove com o motivo."
                  preconditions={a.impact?.refusal ? [{ ok: false, text: a.impact.refusal, fix: refusalItem ? { label: 'Ver reservas do item', onSelect: () => void navigate({ to: '/app/estoque/itens/$id' as never, params: { id: refusalItem } as never, search: { aba: 'reservas' } as never }) } : undefined }] : []}
                  impact={<Impact a={a} />}
                  onConfirm={async () => { const d = await approveStockAdjustment(id, { comment: text.trim() || null, version: a.version }, idempotency.options); idempotency.renew(); await refresh(); after(d) }}
                  onReload={refresh} trigger={button('Aprovar ajuste', true)}>
                  <label htmlFor="comentario-aprovar" className="text-sm font-medium">Comentário (opcional)</label>
                  <textarea id="comentario-aprovar" value={text} maxLength={500} rows={2} onChange={(e) => setText(e.target.value)} className={inputClass} />
                </ConfirmDialog>
                <ConfirmDialog verb="Reprovar ajuste" done="Ajuste reprovado" title={`Reprovar o ${a.number}?`} description="Nada muda no saldo e quem lançou recebe o comentário para lançar outro."
                  preconditions={[{ ok: text.trim().length > 0, text: 'Motivo da reprovação escrito.' }]}
                  onConfirm={async () => { const d = await rejectStockAdjustment(id, { comment: text.trim(), version: a.version }); await refresh(); after(d) }} onReload={refresh} trigger={button('Reprovar ajuste')}>
                  <label htmlFor="comentario-reprovar" className="text-sm font-medium">Motivo da reprovação</label>
                  <textarea id="comentario-reprovar" value={text} maxLength={500} rows={3} onChange={(e) => setText(e.target.value)} className={inputClass} />
                </ConfirmDialog>
              </>
            ) : null}
            {a.canCancel ? (
              <ConfirmDialog destructive verb="Cancelar ajuste" done="Ajuste cancelado" title={`Cancelar o ${a.number}?`} description="O ajuste sai da fila de aprovação e nada muda no saldo."
                preconditions={[{ ok: text.trim().length > 0, text: 'Motivo do cancelamento escrito.' }]}
                onConfirm={async () => { await cancelStockAdjustment(id, { reason: text.trim(), version: a.version }); setText(''); await refresh() }} onReload={refresh} trigger={button('Cancelar ajuste')}>
                <label htmlFor="motivo-cancelar" className="text-sm font-medium">Motivo do cancelamento</label>
                <textarea id="motivo-cancelar" value={text} maxLength={200} rows={2} onChange={(e) => setText(e.target.value)} className={inputClass} />
              </ConfirmDialog>
            ) : null}
            {a.canReverse ? (
              <ConfirmDialog destructive size="lg" verb="Estornar ajuste" done="Ajuste estornado" title={`Estornar o ${a.number}?`}
                description="Nascem os movimentos inversos, pelo custo de cada movimento do ajuste. Se a sobra já foi usada ou reservada, o estorno é recusado."
                impact={<Impact a={a} reverse />} preconditions={[{ ok: text.trim().length > 0, text: 'Motivo do estorno escrito.' }]}
                onConfirm={async () => { const d = await reverseStockAdjustment(id, { reason: text.trim(), version: a.version }, idempotency.options); idempotency.renew(); await refresh(); after(d) }}
                onReload={refresh} trigger={button('Estornar ajuste')}>
                <label htmlFor="motivo-estorno" className="text-sm font-medium">Motivo do estorno</label>
                <textarea id="motivo-estorno" value={text} maxLength={200} rows={2} onChange={(e) => setText(e.target.value)} className={inputClass} />
              </ConfirmDialog>
            ) : null}
          </div>
        </div>
      </PageHeading>
      <InventoryTabs />
      {pending && a.decideBlockedReason ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">{a.decideBlockedReason}</p> : null}
      {a.status === 'Approved' && a.decidedAt && !a.reversedAt ? (
        <p role="note" className="mb-3 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
          Aprovado em {formatDateTime(a.decidedAt)} por {a.decidedByName ?? 'usuário removido'}. Para corrigir, lance outro ajuste ou estorne este.
        </p>
      ) : null}
      {a.reversedAt ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">Estornado em {formatDateTime(a.reversedAt)} por {a.reversedByName ?? '—'}: {a.reversalReason}</p> : null}
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      <FactsBar label="Fatos do ajuste" facts={[
        { label: 'Itens', value: itemsLabel(a.itemCount) },
        { label: 'Sobras', value: <Money value={a.surplusValue} /> },
        { label: 'Faltas', value: <Money value={a.shortageValue} /> },
        { label: 'Valor', value: <Money value={a.differenceValue} /> },
      ]} />
      <p className="mb-2 text-sm"><span className="text-muted-foreground">Motivo:</span> {a.reasonLabel}{a.reasonDetail ? ` — ${a.reasonDetail}` : ''}</p>
      <UrlTabs label="Seções do ajuste" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: <ul className="mt-1 space-y-2" aria-label="Itens do ajuste">{a.lines.map((l) => <LineCard key={l.id} a={a} l={l} />)}</ul> },
        {
          id: 'decisao', label: 'Decisão', content: (
            <dl className="grid gap-2 text-sm sm:grid-cols-[12rem_1fr]">
              <dt className="text-muted-foreground">Quem lançou</dt><dd>{a.requestedByName ?? '—'} · {formatDateTime(a.requestedAt)}</dd>
              {a.originNumber ? <><dt className="text-muted-foreground">Origem</dt><dd>{a.originLink ? <Link to={a.originLink as never} className="font-semibold text-primary">Inventário {a.originNumber}</Link> : `Inventário ${a.originNumber}`}</dd></> : null}
              <dt className="text-muted-foreground">{a.status === 'Cancelled' ? 'Cancelado' : 'Quem decidiu'}</dt>
              <dd>{a.decidedAt ? `${a.decidedByName ?? '—'} · ${formatDateTime(a.decidedAt)}` : 'Aguardando a decisão de quem aprova no Estoque.'}{a.selfApproved ? <> <Tag>Autoaprovado</Tag><Dica nome="Autoaprovado" texto={ADJUSTMENT_DICAS.autoaprovado} /></> : null}</dd>
              {a.decisionComment ? <><dt className="text-muted-foreground">Comentário</dt><dd>{a.decisionComment}</dd></> : null}
              {a.cancelReason ? <><dt className="text-muted-foreground">Motivo do cancelamento</dt><dd>{a.cancelReason}</dd></> : null}
              {a.reversedAt ? <><dt className="text-muted-foreground">Estorno</dt><dd>{a.reversedByName ?? '—'} · {formatDateTime(a.reversedAt)} · {a.reversalReason}{a.reversalSelfApproved ? <> <Tag>Autoaprovado</Tag></> : null}</dd></> : null}
              {a.status === 'Approved' && !a.canReverse && a.reverseBlockedReason ? <><dt className="text-muted-foreground">Estornar</dt><dd>{a.reverseBlockedReason}</dd></> : null}
            </dl>
          ),
        },
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="StockAdjustment" id={a.id} /> },
      ]} />
    </>
  )
}
