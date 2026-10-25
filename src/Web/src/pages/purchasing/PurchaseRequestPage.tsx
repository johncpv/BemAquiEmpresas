import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { cancelPurchaseRequest, cancelPurchaseRequestLine, getGetPurchaseRequestQueryKey, updatePurchaseRequestLine, useGetPurchaseRequest } from '@/api/generated/bemaqui'
import type { PurchaseRequestDetail, PurchaseRequestLineView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { formatQuantity } from '@/pages/inventory/stock/stock'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { PurchaseRequestHistory } from './PurchaseRequestHistory'
import { destination, ORIGIN, PURCHASE_DICAS, STATUS, todayIso, urgency } from './purchasing'

const button = (text: string, primary = false) => (
  <button type="button" className={primary ? 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>
)
const small = (text: string) => <button type="button" className="rounded-md border border-border px-2.5 py-1 text-sm font-semibold">{text}</button>

const requester = (r: PurchaseRequestDetail) =>
  r.origin === 'Manual' ? (r.requestedByName ?? '—') : r.origin === 'MinStock' ? 'Sistema · Estoque mínimo' : `Sistema · Falta da BOM ${r.orderNumber ?? ''}`.trim()

/**
 * Solicitação de compra (CA 12 a 14, 17): as linhas com a necessidade e a situação; com purchasing.edit, "Editar linha" (quantidade e
 * necessidade; linha automática ganha "Editada à mão") e "Cancelar linha"/"Cancelar solicitação" com motivo. A 400 px cada linha vira
 * cartão. Na aba Histórico, antes e depois com o usuário ou a origem do sistema.
 */
export function PurchaseRequestPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { criada } = useSearch({ strict: false }) as { criada?: number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetPurchaseRequest(id)
  const canOrder = useCan('purchasing.create')
  const order = (lineIds: string[]) => void navigate({ to: '/app/compras/pedidos/novo', search: { sc: lineIds.join(',') } } as never)
  // CMP-02: "Cotar estes itens" abre a nova cotação com as linhas em aberto (os últimos fornecedores dos itens já vêm escolhidos).
  const quote = (lineIds: string[]) => void navigate({ to: '/app/compras/cotacoes/nova', search: { sc: lineIds.join(',') } } as never)
  const openLines = () => query.data?.lines.filter((l) => l.status === 'Open').map((l) => l.id) ?? []
  const [steps, setSteps] = useState<Step[]>(() => (criada ? [
    ...(canOrder ? [{ label: 'Cotar estes itens', onSelect: () => quote(openLines()) }, { label: 'Pedir direto ao último fornecedor', onSelect: () => order(openLines()) }] : []),
    { label: 'Nova solicitação', onSelect: () => void navigate({ to: '/app/compras/solicitacoes/nova' } as never) },
    { label: 'Ver solicitações', onSelect: () => void navigate({ to: '/app/compras/solicitacoes' } as never) },
  ] : []))
  const [warning, setWarning] = useState<string | null>(null)
  const [reason, setReason] = useState('')

  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Solicitação não encontrada.' : 'Não foi possível carregar a solicitação.'} message={notFound ? 'Ela pode ter sido aberta em outra empresa. Volte à lista.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const r = query.data
  const refresh = async () => { await client.invalidateQueries({ queryKey: getGetPurchaseRequestQueryKey(id) }) }
  const set = (saved: PurchaseRequestDetail) => client.setQueryData(getGetPurchaseRequestQueryKey(id), saved)
  const nearest = urgency(r.nearestNeededBy)

  return (
    <>
      <PageHeading title={`${r.number} — ${ORIGIN[r.origin]}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={STATUS[r.status].tone}>{STATUS[r.status].label}</StatusChip>
            <span className="inline-flex items-center"><Tag>{ORIGIN[r.origin]}</Tag>{r.origin === 'BomShortage' ? <Dica nome="Falta da BOM" texto={PURCHASE_DICAS.faltaBom} /> : r.origin === 'MinStock' ? <Dica nome="Estoque mínimo" texto={PURCHASE_DICAS.minimo} /> : null}</span>
            {nearest && r.status !== 'Cancelled' ? <StatusChip tone={nearest.tone}>{nearest.label}</StatusChip> : null}
            <Link to={'/app/compras/solicitacoes' as never} className="font-semibold text-primary">Voltar às solicitações</Link>
          </p>
          <span className="flex flex-wrap gap-2">
          {canOrder && r.lines.some((l) => l.status === 'Open') ? (
            <>
              <button type="button" onClick={() => quote(r.lines.filter((l) => l.status === 'Open').map((l) => l.id))} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
                Cotar estes itens
              </button>
              <button type="button" onClick={() => order(r.lines.filter((l) => l.status === 'Open').map((l) => l.id))} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">
                Pedir direto ao último fornecedor
              </button>
            </>
          ) : null}
          {r.canCancel ? (
            <ConfirmDialog destructive verb="Cancelar solicitação" done="Solicitação cancelada" title={`Cancelar a ${r.number}?`}
              description="As linhas em aberto são canceladas. Nada muda no estoque nem no financeiro. Linha automática cancelada não volta sozinha."
              onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
              onConfirm={async () => { set(await cancelPurchaseRequest(id, { reason: reason.trim(), version: r.version })) }} onReload={refresh} trigger={button('Cancelar solicitação')}>
              <label htmlFor="motivo-sc" className="text-sm font-medium">Motivo</label>
              <textarea id="motivo-sc" value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />
            </ConfirmDialog>
          ) : null}
          </span>
        </div>
      </PageHeading>
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      {warning ? <p role="status" className="mb-3 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">{warning}</p> : null}
      <FactsBar label="Fatos da solicitação" facts={[
        { label: 'Destino', value: destination(r.projectNumber, r.projectName) },
        {
          label: 'OS', value: r.productionOrderId
            ? <Link to={'/app/producao/os/$id' as never} params={{ id: r.productionOrderId } as never} search={{ aba: 'material' } as never} className="font-semibold text-primary">{r.orderNumber ?? 'OS'}</Link>
            : '—',
        },
        { label: 'Necessidade mais próxima', value: <span className="tabular-nums">{r.nearestNeededBy ? formatDate(r.nearestNeededBy) : '—'}</span>, dica: PURCHASE_DICAS.necessidade },
        { label: 'Pedida por', value: `${requester(r)} · ${formatDateTime(r.createdAt)}` },
      ]} />
      {r.notes ? <p className="mb-2 text-sm"><span className="text-muted-foreground">Observação:</span> {r.notes}</p> : null}
      {r.cancelReason ? <p className="mb-2 text-sm"><span className="text-muted-foreground">Cancelada{r.cancelledByName ? ` por ${r.cancelledByName}` : ''}{r.cancelledAt ? ` em ${formatDateTime(r.cancelledAt)}` : ''}:</span> {r.cancelReason}</p> : null}
      <UrlTabs label="Seções da solicitação" fallback="itens" tabs={[
        {
          id: 'itens', label: 'Itens', content: (
            <ul className="mt-3 divide-y divide-border rounded-md border border-border text-sm" aria-label="Itens da solicitação">
              {r.lines.map((l) => (
                <LineRow key={l.id} request={r} line={l} onSaved={(saved, w) => { set(saved); setWarning(w) }} onReload={refresh} />
              ))}
            </ul>
          ),
        },
        { id: 'historico', label: 'Histórico de alterações', content: <PurchaseRequestHistory id={r.id} /> },
      ]} />
    </>
  )
}

function LineRow({ request, line: l, onSaved, onReload }: { request: PurchaseRequestDetail; line: PurchaseRequestLineView; onSaved: (saved: PurchaseRequestDetail, warning: string | null) => void; onReload: () => Promise<unknown> }) {
  const [quantity, setQuantity] = useState<string | null>(String(l.quantity))
  const [neededBy, setNeededBy] = useState(l.neededBy)
  const [reason, setReason] = useState('')
  const open = l.status === 'Open'
  const u = open ? urgency(l.neededBy) : null
  const editable = request.canEdit && open
  return (
    <li className={`flex flex-wrap items-center justify-between gap-x-4 gap-y-2 p-3 ${u?.tone === 'crit' ? 'border-l-4 border-l-crit' : u ? 'border-l-4 border-l-warn' : ''}`}>
      <span className="min-w-0 flex-1 basis-56">
        <span className="font-mono">{l.itemCode}</span> {l.itemName}
        <span className="mt-1 flex flex-wrap items-center gap-1">
          {l.calculatedOnOnboarding ? <span className="inline-flex items-center"><Tag>Calculado na implantação</Tag><Dica nome="Calculado na implantação" texto={PURCHASE_DICAS.implantacao} /></span> : null}
          {l.manuallyEdited && request.origin !== 'Manual' ? <span className="inline-flex items-center"><Tag>Editada à mão</Tag><Dica nome="Editada à mão" texto={PURCHASE_DICAS.editada} /></span> : null}
          {l.orderNumber && l.productionOrderId ? <Link to={'/app/producao/os/$id' as never} params={{ id: l.productionOrderId } as never} search={{ aba: 'material' } as never} className="text-xs font-semibold text-primary">{l.orderNumber}</Link> : null}
        </span>
      </span>
      <span className="tabular-nums"><b>{formatQuantity(l.quantity)} {l.unitCode}</b>{l.enteredUnitCode && l.enteredQuantity !== null ? <span className="text-muted-foreground"> ({formatQuantity(l.enteredQuantity)} {l.enteredUnitCode})</span> : null}</span>
      <span className="flex flex-wrap items-center gap-1 tabular-nums">{formatDate(l.neededBy)}{u ? <StatusChip tone={u.tone}>{u.label}</StatusChip> : null}</span>
      <StatusChip tone={STATUS[l.status].tone}>{STATUS[l.status].label}</StatusChip>
      {l.cancelReason ? <span className="basis-full text-xs text-muted-foreground">Motivo: {l.cancelReason}</span> : null}
      {editable ? (
        <span className="flex gap-2">
          <ConfirmDialog verb="Salvar linha" done="Linha salva" title={`Editar ${l.itemCode}`}
            description={request.origin === 'Manual' ? 'Quantidade na unidade de estoque e a data de necessidade.' : 'Linha automática alterada à mão não muda mais sozinha com a falta ou com o mínimo.'}
            onOpen={() => { setQuantity(String(l.quantity)); setNeededBy(l.neededBy) }}
            preconditions={[{ ok: Number(quantity) > 0, text: 'Quantidade maior que zero.' }, { ok: !!neededBy && neededBy >= todayIso(), text: 'Necessidade a partir de hoje.' }]}
            onConfirm={async () => {
              const saved = await updatePurchaseRequestLine(request.id, l.id, { quantity: Number(quantity), neededBy, version: l.version })
              onSaved(saved.request, saved.warning ?? null)
            }}
            onReload={onReload} trigger={small('Editar linha')}>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1">
                <label htmlFor={`qtd-${l.id}`} className="text-sm font-medium">Quantidade ({l.unitCode})</label>
                <QuantityInput id={`qtd-${l.id}`} value={quantity} onChange={setQuantity} />
              </div>
              <div className="space-y-1">
                <label htmlFor={`data-${l.id}`} className="text-sm font-medium">Necessidade</label>
                <DateInput id={`data-${l.id}`} value={neededBy} onChange={setNeededBy} />
              </div>
            </div>
          </ConfirmDialog>
          <ConfirmDialog destructive verb="Cancelar linha" done="Linha cancelada" title={`Cancelar ${l.itemCode} da ${request.number}?`}
            description="Só esta linha é cancelada. Nada muda no estoque nem no financeiro."
            onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
            onConfirm={async () => { onSaved(await cancelPurchaseRequestLine(request.id, l.id, { reason: reason.trim(), version: l.version }), null) }}
            onReload={onReload} trigger={small('Cancelar linha')}>
            <label htmlFor={`motivo-${l.id}`} className="text-sm font-medium">Motivo</label>
            <textarea id={`motivo-${l.id}`} value={reason} maxLength={500} rows={2} onChange={(e) => setReason(e.target.value)} className={inputClass} />
          </ConfirmDialog>
        </span>
      ) : null}
    </li>
  )
}
