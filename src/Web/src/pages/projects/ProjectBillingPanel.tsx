import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createOutgoingInvoice, useGetProjectBilling, useListInstallationsToInvoice } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useStartNfse } from '../fiscal/use-start-nfse'
import { INVOICE_DICAS, quantity } from '../fiscal/outgoing-invoices'

const primary = 'inline-flex min-h-11 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-11 items-center rounded-md border border-border bg-card px-3 text-sm font-semibold'

/**
 * "Faturar" no projeto (FIS-03a CA 2 e 15): os romaneios do projeto com a nota (link) ou "Emitir NF-e" para o expedido sem nota, e o que
 * ainda não saiu com "Novo romaneio". Toda NF-e de mercadoria nasce de um romaneio, porque só o romaneio baixa o estoque (D4).
 */
export function ProjectBillingPanel({ projectId }: { projectId: string }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" onClick={() => setOpen(true)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Faturar</button>
      <SidePanel open={open} onOpenChange={setOpen} title="Faturar o projeto" description="A NF-e do equipamento sai do romaneio expedido; a instalação sai em NFS-e no aceite técnico.">
        {open ? <BillingBody projectId={projectId} /> : null}
      </SidePanel>
    </>
  )
}

function BillingBody({ projectId }: { projectId: string }) {
  const navigate = useNavigate()
  const query = useGetProjectBilling({ projectId })
  const installations = useListInstallationsToInvoice()
  const nfse = useStartNfse()
  const idempotency = useIdempotencyKey()
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-14" /><Skeleton className="h-14" /></div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o faturamento do projeto." onRetry={() => void query.refetch()} />
  const b = query.data
  const issue = async (shipmentId: string) => {
    setBusy(shipmentId)
    setError(null)
    try {
      const created = await createOutgoingInvoice({ shipmentId }, idempotency.options)
      idempotency.renew()
      void navigate({ to: '/app/fiscal/saida/$id', params: { id: created.id } } as never)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível criar a nota. Tente de novo.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className="space-y-4 text-sm">
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-crit-ink">{error}</p> : null}
      <section aria-labelledby="romaneios-faturar">
        <h3 id="romaneios-faturar" className="mb-2 font-semibold">Romaneios</h3>
        {b.shipments.length === 0 ? <EmptyState title="Nenhum romaneio no projeto." description="Monte o romaneio do que já está pronto para expedir." /> : (
          <ul className="space-y-2">
            {b.shipments.map((s) => (
              <li key={s.shipmentId} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-2">
                <span>
                  <Link to={'/app/producao/expedicao/$id' as never} params={{ id: s.shipmentId } as never} className="font-semibold text-primary tabular-nums hover:underline">{s.number}</Link>
                  <span className="text-muted-foreground">{s.shippedAt ? ` · expedido em ${formatDateTime(s.shippedAt)}` : ' · ainda não expedido'}</span>
                </span>
                {s.invoiceId ? <Link to={'/app/fiscal/saida/$id' as never} params={{ id: s.invoiceId } as never} className="font-semibold text-primary hover:underline">{s.invoiceLabel}</Link>
                  : s.canIssue ? <button type="button" disabled={busy !== null} onClick={() => void issue(s.shipmentId)} className={primary}>{busy === s.shipmentId ? 'Criando…' : 'Emitir NF-e'}</button> : null}
              </li>
            ))}
          </ul>
        )}
      </section>
      <section aria-labelledby="pendentes-faturar">
        <h3 id="pendentes-faturar" className="mb-2 font-semibold">Ainda não saiu</h3>
        {b.pendingItems.length === 0 ? <p className="text-muted-foreground">Todo o equipamento e as peças do contrato já saíram em romaneio.</p> : (
          <>
            <ul className="mb-2 space-y-1">
              {b.pendingItems.map((i) => <li key={i.projectItemId} className="tabular-nums">{i.description}: {quantity(i.shipped)} de {quantity(i.quantity)} expedido</li>)}
            </ul>
            <Link to={'/app/producao/expedicao/novo' as never} search={{ projeto: projectId } as never} className={secondary}>Novo romaneio</Link>
          </>
        )}
      </section>
      {nfse.error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-crit-ink">{nfse.error}</p> : null}
      {(installations.data ?? []).filter((i) => i.projectId === projectId).map((i) => (
        <div key={i.installationOrderId} className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-2">
          <span><b className="tabular-nums">{i.orderNumber}</b><span className="text-muted-foreground"> · aceite técnico registrado, sem NFS-e</span></span>
          <button type="button" disabled={nfse.busy !== null} onClick={() => void nfse.start(i.installationOrderId, projectId)} className={primary}>{nfse.busy === i.installationOrderId ? 'Criando…' : 'Emitir NFS-e'}</button>
        </div>
      ))}
      {b.serviceItems > 0 ? <p className="flex items-center text-muted-foreground">{b.serviceItems} {b.serviceItems === 1 ? 'serviço fica' : 'serviços ficam'} para a NFS-e no aceite técnico.<Dica nome="Venda mista" texto={INVOICE_DICAS.vendaMista} /></p> : null}
    </div>
  )
}
