import { useNavigate, useSearch } from '@tanstack/react-router'
import { createPurchaseOrder, useGetPurchaseOrderDefaults } from '@/api/generated/bemaqui'
import type { PurchaseOrderDefaults } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { PageHeading } from '@/shell/PageHeading'
import { type OrderDraft, PurchaseOrderForm } from './PurchaseOrderForm'

/** Formulário preenchido com o mais provável (CA 1, R-PRP-1): o último fornecedor do item, a condição dele, o último preço e o destino das SC. */
function draftFromDefaults(d: PurchaseOrderDefaults): OrderDraft {
  return {
    party: d.partyId ? { id: d.partyId, label: d.partyName ?? '' } : null, partySuggested: d.partySuggested, paymentTermId: d.paymentTermId ?? '', expectedDate: d.expectedDate,
    general: !d.projectId, project: d.projectId ? { id: d.projectId, label: `${d.projectNumber ?? ''} — ${d.projectName ?? ''}` } : null, freight: null, ipiPct: null, notes: '',
    lines: d.lines.length
      ? d.lines.map((l) => ({
        item: { id: l.item.id, label: `${l.item.code} — ${l.item.name}` }, quantity: String(l.quantity), unitId: l.item.unitId, unitPrice: l.unitPrice != null ? String(l.unitPrice) : null,
        priceSuggested: l.priceSuggested, priceSource: l.priceSource ?? null, requestLineIds: l.requestLineIds, requestNumbers: l.requestNumbers,
      }))
      : [{ item: null, quantity: null, unitId: '', unitPrice: null, priceSuggested: false, priceSource: null, requestLineIds: [], requestNumbers: [] }],
  }
}

/**
 * Fábrica → Compras → Pedidos → Novo pedido (CA 1): à mão ou a partir das linhas de SC em aberto (<c>?sc=</c>, "Pedir direto ao último
 * fornecedor"), já preenchido. Nasce em rascunho com o número PC; o envio para aprovação é o próximo passo.
 */
export function NewPurchaseOrderPage() {
  const { sc, item } = useSearch({ strict: false }) as { sc?: string; item?: string }
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const requestLineIds = sc ? sc.split(',').filter(Boolean) : undefined
  const defaults = useGetPurchaseOrderDefaults({ requestLineIds, itemId: item })
  return (
    <>
      <PageHeading title="Novo pedido de compra">
        <p className="mt-2 text-muted-foreground">O pedido nasce em rascunho. Depois de salvar, envie para aprovação: aprovado, o pagamento previsto entra no fluxo de caixa.</p>
      </PageHeading>
      {defaults.isPending ? <Skeleton className="h-96 max-w-5xl" /> : defaults.isError || !defaults.data
        ? <ErrorState title="Não foi possível abrir o pedido." message="As solicitações escolhidas podem ter virado pedido ou sido canceladas. Volte às solicitações e tente de novo." onRetry={() => defaults.refetch()} />
        : (
          <PurchaseOrderForm initial={draftFromDefaults(defaults.data)} options={defaults.data.lines.map((l) => l.item)} fromRequests={!!requestLineIds?.length}
            saveLabel="Criar pedido" sendingLabel="Criando…"
            onBack={() => void navigate({ to: requestLineIds?.length ? '/app/compras/solicitacoes' : '/app/compras/pedidos' } as never)}
            onSubmit={async (payload) => {
              const saved = await createPurchaseOrder({ ...payload, rfqId: null, quotesCount: null, leadDays: null }, idempotency.options)
              idempotency.renew()
              notifySuccess('Pedido criado')
              void navigate({ to: '/app/compras/pedidos/$id', params: { id: saved.id }, search: { criado: 1 } } as never)
            }} />
        )}
    </>
  )
}
