import { useEffect, useState } from 'react'
import { issueBillingSlipBatch, usePreviewBillingSlipBatch } from '@/api/generated/bemaqui'
import type { BillingBatchPreview } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { formatDate } from '@/lib/format'

/**
 * "Emitir boletos" em lote (BNK-01b CA 19, padrão 7): impacto em lista com os elegíveis (título, cliente, valor, vencimento e e-mail),
 * os recusados com o motivo e o total; cada título vai com o seu Idempotency-Key, pelos padrões de Configurações → Cobrança.
 */
export function BatchBillingDialog({ ids, open, onOpenChange, onDone }: { ids: string[]; open: boolean; onOpenChange: (o: boolean) => void; onDone: () => void }) {
  const preview = usePreviewBillingSlipBatch()
  const [data, setData] = useState<BillingBatchPreview | null>(null)
  const [keys] = useState(() => new Map<string, string>())
  const [send, setSend] = useState(true)
  const { mutate } = preview
  const idsKey = ids.join(',')
  useEffect(() => {
    if (!open) return
    mutate({ data: { receivableIds: idsKey.split(',') } }, { onSuccess: setData })
  }, [open, idsKey, mutate])

  const n = data?.eligible.length ?? 0
  const keyOf = (id: string) => {
    if (!keys.has(id)) keys.set(id, crypto.randomUUID())
    return keys.get(id)!
  }

  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={onOpenChange} verb={n === 1 ? 'Emitir 1 boleto' : `Emitir ${n} boletos`} done="" title="Emitir boletos"
      description="Cada título vira um boleto com Pix pelo Inter, com o saldo, o vencimento e a multa e os juros de Configurações → Cobrança."
      preconditions={[
        { ok: !!data, text: preview.isError ? 'Não foi possível conferir os títulos. Feche e tente de novo.' : 'Conferindo os títulos…' },
        { ok: !data || n > 0, text: 'Nenhum dos títulos selecionados pode receber boleto.' },
      ]}
      impactList={data ? {
        label: 'Vão receber boleto',
        items: data.eligible.map((e) => ({ key: e.receivableId, primary: <>{e.number} · {e.partyName}</>, secondary: <><Money value={e.amount} /> · vence {formatDate(e.dueDate)} · {e.email ?? 'sem e-mail de cobrança'}</> })),
        refused: data.refused.map((r) => ({ key: r.receivableId, primary: <>{r.number} · {r.partyName}</>, reason: r.reason })),
        total: <Money value={data.total} />,
      } : undefined}
      onConfirm={async () => {
        const result = await issueBillingSlipBatch({ items: (data?.eligible ?? []).map((e) => ({ receivableId: e.receivableId, clientRequestId: keyOf(e.receivableId) })), sendEmail: send })
        notifySuccess(result.message)
        onDone()
      }}>
      <EnvironmentBanner service="bank" test={!!data?.sandbox} className="mb-0" />
      <label className="flex min-h-11 items-start gap-2 text-sm">
        <input type="checkbox" checked={send} onChange={(e) => setSend(e.target.checked)} className="mt-0.5 size-4 accent-primary" />
        <span><b>Enviar por e-mail ao emitir</b><span className="block text-xs text-muted-foreground">Para o contato de cobrança de cada cliente, quando o banco confirmar.</span></span>
      </label>
    </ConfirmDialog>
  )
}
