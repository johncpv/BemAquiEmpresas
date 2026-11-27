import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import {
  cancelSalesOrder, chargeSalesOrder, confirmSalesOrder, refundSalesOrder, registerSalesOrderPayment, reopenSalesOrder, useGetSalesOrderChargeDraft, useListSalesOrderPaymentOptions,
} from '@/api/generated/bemaqui'
import type { SalesOrderDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog, type Precondition } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'

type Done = (order: SalesOrderDetail) => void

interface DialogProps {
  order: SalesOrderDetail
  open: boolean
  onOpenChange: (open: boolean) => void
  onDone: Done
}

/** "Confirmar pedido" (CA 9): reserva tudo ou nada; preço mudado desde a criação mostra o novo e pede para confirmar de novo. */
export function ConfirmOrderDialog({ order, open, onOpenChange, onDone }: DialogProps) {
  const [priceNotice, setPriceNotice] = useState<string>()
  return (
    <ConfirmDialog open={open} onOpenChange={onOpenChange} verb={priceNotice ? 'Confirmar com o preço novo' : 'Confirmar pedido'} done="Pedido confirmado" title={`Confirmar o pedido ${order.number}`}
      onOpen={() => setPriceNotice(undefined)}
      impact={<p>O estoque de todos os itens fica reservado no local do pedido até o pagamento. Faltando saldo de algum item, nada é reservado.</p>}
      onConfirm={async () => {
        try {
          onDone(await confirmSalesOrder(order.id, { version: order.version, acceptNewPrices: !!priceNotice }))
        } catch (e) {
          if (e instanceof ApiError && e.code === 'price_changed') {
            setPriceNotice(e.message)
          }

          throw e
        }
      }}>
      {priceNotice ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{priceNotice}</p> : null}
    </ConfirmDialog>
  )
}

/** "Gerar cobrança" (CA 15 a 17): já preenchido (total, vencimento, e-mail); sem documento ou endereço, mostra o que falta e "Completar cadastro". */
export function ChargeDialog({ order, open, onOpenChange, onDone }: DialogProps) {
  const draft = useGetSalesOrderChargeDraft(order.id, { query: { enabled: open } })
  const idempotency = useIdempotencyKey()
  const [due, setDue] = useState<string | null>(null)
  const [send, setSend] = useState(true)
  if (open && draft.isPending) {
    return <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Gerar cobrança" done="" title="Gerar cobrança" onConfirm={async () => undefined}><Skeleton className="h-32" /></ConfirmDialog>
  }

  if (open && draft.isError) {
    return (
      <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Gerar cobrança" done="" title="Gerar cobrança" onConfirm={async () => undefined}>
        <ErrorState title="Não foi possível abrir a cobrança." message="Tente de novo em instantes." onRetry={() => void draft.refetch()} />
      </ConfirmDialog>
    )
  }

  const d = draft.data
  const preconditions: Precondition[] = d ? [
    { ok: d.integrationReady, text: 'Conecte a conta do Inter para emitir boletos.', fix: { label: 'Configurar o Banco Inter', onSelect: () => window.location.assign('/app/configuracoes/integracoes/inter') } },
    { ok: d.paymentMethodConfigured, text: 'Configure a forma de pagamento do boleto em Configurações → Parâmetros.', fix: { label: 'Abrir Parâmetros', onSelect: () => window.location.assign('/app/configuracoes/parametros') } },
    ...d.missing.map((m) => ({ ok: false, text: `Falta no cadastro do cliente: ${m}.`, fix: { label: 'Completar cadastro', onSelect: () => window.location.assign(`/app/cadastros/terceiros/${d.customerId}`) } })),
  ] : []
  const dueDate = due ?? d?.dueDate ?? null
  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Gerar cobrança" done="Cobrança gerada" title={`Gerar cobrança do pedido ${order.number}`}
      description="Boleto com Pix do Inter, pelo título do pedido no Financeiro. Multa e juros vêm de Configurações → Cobrança."
      onOpen={() => { setDue(null); setSend(true) }}
      preconditions={preconditions}
      impact={d ? (
        <dl className="grid grid-cols-2 gap-1">
          <dt>Valor</dt><dd className="text-right font-semibold"><Money value={Number(d.amount)} /></dd>
          <dt>Vencimento</dt><dd className="text-right">{dueDate ? formatDate(dueDate) : '—'}</dd>
          <dt>E-mail</dt><dd className="break-all text-right">{send && d.email ? d.email : 'Sem envio por e-mail'}</dd>
        </dl>
      ) : null}
      onConfirm={async () => {
        const result = await chargeSalesOrder(order.id, { dueDate: dueDate ?? '', sendEmail: send && !!d?.email, version: order.version }, idempotency.options)
        idempotency.renew()
        onDone(result)
      }}>
      {d ? (
        <>
          <Field label="Vencimento" required hint="Padrão: 1 dia útil. A reserva vale até o dia útil seguinte ao vencimento.">{(p) => <DateInput {...p} autoFocus value={dueDate} onChange={setDue} />}</Field>
          <label className="flex min-h-11 items-center gap-2 text-sm">
            <input type="checkbox" checked={send && !!d.email} disabled={!d.email} onChange={(e) => setSend(e.target.checked)} className="size-4 accent-primary" />
            {d.email ? `Enviar o boleto para ${d.email}` : 'O cliente não tem e-mail de cobrança.'}
          </label>
        </>
      ) : null}
    </ConfirmDialog>
  )
}

/** "Registrar pagamento" (CA 20, sales.approve): dinheiro (com troco), maquininha ou Pix recebido fora; com boleto, o sistema confere no banco antes. */
export function PaymentDialog({ order, open, onOpenChange, onDone }: DialogProps) {
  const options = useListSalesOrderPaymentOptions({ query: { enabled: open } })
  const idempotency = useIdempotencyKey()
  const [method, setMethod] = useState('')
  const [received, setReceived] = useState<string | null>(null)
  const chosen = options.data?.find((m) => m.id === method)
  const amount = received ? Number(received) : Number(order.total)
  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb="Registrar pagamento" done="Pagamento registrado" title={`Registrar pagamento do pedido ${order.number}`}
      onOpen={() => { setMethod(''); setReceived(null) }}
      preconditions={[{ ok: !!method, text: 'Escolha a forma em que o valor foi recebido.' }]}
      impact={(
        <dl className="grid grid-cols-2 gap-1">
          <dt>Total do pedido</dt><dd className="text-right font-semibold"><Money value={Number(order.total)} /></dd>
          {chosen?.allowsChange && amount > Number(order.total) ? <><dt>Troco</dt><dd className="text-right"><Money value={amount - Number(order.total)} /></dd></> : null}
          <dt>Depois</dt><dd className="text-right">O pedido vira venda e o boleto aberto é cancelado.</dd>
        </dl>
      )}
      onConfirm={async () => {
        const result = await registerSalesOrderPayment(order.id, { paymentMethodId: method, version: order.version, amountReceived: received ? Number(received) : null }, idempotency.options)
        idempotency.renew()
        onDone(result)
      }}>
      {options.isError ? <ErrorState title="Não foi possível carregar as formas de pagamento." onRetry={() => void options.refetch()} /> : (
        <Field label="Forma de pagamento" required>
          {(p) => (
            <select {...p} autoFocus value={method} onChange={(e) => setMethod(e.target.value)} className={inputClass}>
              <option value="">Escolha</option>
              {(options.data ?? []).map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
            </select>
          )}
        </Field>
      )}
      {chosen?.allowsChange ? <Field label="Valor recebido" hint="Para calcular o troco.">{(p) => <MoneyInput {...p} value={received} onChange={setReceived} />}</Field> : null}
    </ConfirmDialog>
  )
}

/**
 * "Cancelar pedido" (CA 25) e "Cancelar pedido e estornar venda" (CA 26): motivo obrigatório; no pago, a caixa "Já devolvi o valor ao cliente". O impacto
 * diz o que acontece: reserva liberada, boleto cancelado, venda estornada.
 */
export function CancelOrderDialog({ order, open, onOpenChange, onDone }: DialogProps) {
  const paid = order.status === 'Paid'
  const [reason, setReason] = useState('')
  const [refunded, setRefunded] = useState(false)
  const verb = paid ? 'Cancelar pedido e estornar venda' : 'Cancelar pedido'
  return (
    <ConfirmDialog destructive open={open} onOpenChange={onOpenChange} verb={verb} done={paid ? 'Pedido cancelado e venda estornada' : 'Pedido cancelado'}
      title={`${verb} ${order.number}`} onOpen={() => { setReason(''); setRefunded(false) }}
      preconditions={[
        { ok: reason.trim().length >= 3, text: 'Informe o motivo (ao menos 3 letras).' },
        ...(paid ? [{ ok: refunded, text: 'Marque que o valor já foi devolvido ao cliente.' }] : []),
      ]}
      impact={(
        <ul className="list-disc space-y-1 pl-5">
          <li>A reserva do estoque é liberada.</li>
          {order.charge ? <li>{paid ? 'A devolução fica registrada no título do pedido, no Financeiro.' : 'O boleto e o título do pedido são cancelados.'}</li> : null}
          {paid ? <li>A venda é estornada e o estoque volta.</li> : null}
        </ul>
      )}
      onConfirm={async () => onDone(await cancelSalesOrder(order.id, { reason: reason.trim(), version: order.version, refundedOutside: refunded }))}>
      <Field label="Motivo" required>{(p) => <input {...p} autoFocus maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
      {paid ? (
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" checked={refunded} onChange={(e) => setRefunded(e.target.checked)} className="size-4 accent-primary" />
          Já devolvi o valor ao cliente
        </label>
      ) : null}
    </ConfirmDialog>
  )
}

/** "Reabrir pedido" do pago depois de cancelado (CA 19): reserva de novo; sem saldo, recusa. */
export function ReopenDialog({ order, open, onOpenChange, onDone }: DialogProps) {
  return (
    <ConfirmDialog open={open} onOpenChange={onOpenChange} verb="Reabrir pedido" done="Pedido reaberto" title={`Reabrir o pedido ${order.number}`}
      impact={<p>O estoque é reservado de novo e o pedido vira venda pelo boleto que o cliente pagou.</p>}
      onConfirm={async () => onDone(await reopenSalesOrder(order.id, { version: order.version }))} />
  )
}

/** "Devolver o valor" do pago depois de cancelado (CA 19): registra a devolução feita fora; nada é estornado sozinho. */
export function RefundDialog({ order, open, onOpenChange, onDone }: DialogProps) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog open={open} onOpenChange={onOpenChange} verb="Registrar devolução" done="Devolução registrada" title={`Devolver o valor do pedido ${order.number}`}
      onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length >= 3, text: 'Diga como o valor foi devolvido.' }]}
      impact={<p>A saída fica registrada na conta do recebimento e o título do pedido é encerrado como devolvido.</p>}
      onConfirm={async () => onDone(await refundSalesOrder(order.id, { version: order.version, reason: reason.trim() }))}>
      <Field label="Como o valor foi devolvido" required>{(p) => <input {...p} autoFocus maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
    </ConfirmDialog>
  )
}

export function SaleLink({ saleId }: { saleId: string }) {
  return <Link to={'/app/loja/vendas/$id' as never} params={{ id: saleId } as never} className="font-semibold text-primary hover:underline">Ver venda</Link>
}
