import { useNavigate, useParams } from '@tanstack/react-router'
import { updatePurchaseOrder, useGetPurchaseOrder } from '@/api/generated/bemaqui'
import type { PurchaseOrderDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { PageHeading } from '@/shell/PageHeading'
import { type OrderDraft, PurchaseOrderForm } from './PurchaseOrderForm'
import { ORDER_STATUS } from './purchase-orders'

const draftOf = (o: PurchaseOrderDetail): OrderDraft => ({
  party: { id: o.partyId, label: o.partyName }, partySuggested: false, paymentTermId: o.paymentTermId, expectedDate: o.expectedDate, general: !o.projectId,
  project: o.projectId ? { id: o.projectId, label: `${o.projectNumber ?? ''} — ${o.projectName ?? ''}` } : null, freight: String(o.freight), ipiPct: String(o.ipiPct), notes: o.notes ?? '',
  lines: o.lines.map((l) => ({
    item: { id: l.itemId, label: `${l.itemCode} — ${l.itemName}` }, quantity: String(l.quantity), unitId: '', unitPrice: String(l.unitPrice), priceSuggested: false, priceSource: null,
    requestLineIds: l.requestLineIds, requestNumbers: l.requests.map((r) => r.number),
  })),
})

/**
 * Editar pedido (CA 3, 15): no rascunho só grava; em aprovação, aprovado ou enviado (sem recebimento), o diálogo de impacto avisa que o
 * pedido volta para a aprovação e, se já foi enviado, que precisa ser reenviado ao fornecedor.
 */
export function EditPurchaseOrderPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const navigate = useNavigate()
  const query = useGetPurchaseOrder(id)
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Pedido não encontrado.' : 'Não foi possível carregar o pedido.'} message={notFound ? 'Ele pode ter sido aberto em outra empresa. Volte à lista.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const o = query.data
  const back = () => void navigate({ to: '/app/compras/pedidos/$id', params: { id } } as never)
  if (!o.canEdit) {
    return <ErrorState title={`O ${o.number} não pode ser editado.`} message={`Situação: ${ORDER_STATUS[o.status].label}. Pedido cancelado, recebido ou com recebimento não muda.`} />
  }

  const reapproval = o.status !== 'Draft'
  return (
    <>
      <PageHeading title={`Editar ${o.number}`}>
        <p className="mt-2 text-muted-foreground">{reapproval ? 'Mudar valor, fornecedor, condição, data ou itens volta o pedido para a aprovação.' : 'Rascunho: as mudanças só valem depois do envio para aprovação.'}</p>
      </PageHeading>
      <PurchaseOrderForm initial={draftOf(o)} options={[]} fromRequests={o.lines.some((l) => l.requestLineIds.length > 0)} reapproval={reapproval ? { wasSent: o.status === 'Sent' } : undefined}
        saveLabel={reapproval ? 'Salvar e enviar para aprovação' : 'Salvar pedido'} sendingLabel="Salvando…" onBack={back}
        onSubmit={async (payload) => {
          await updatePurchaseOrder(id, { ...payload, version: o.version })
          if (!reapproval) notifySuccess('Pedido salvo')
          back()
        }} />
    </>
  )
}
