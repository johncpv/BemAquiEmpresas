import { Link, useParams } from '@tanstack/react-router'
import { ArrowLeft, Eye } from 'lucide-react'
import { useState } from 'react'
import { getSaleCustomerDocument, useGetSaleDetail } from '@/api/generated/bemaqui'
import type { SaleDetail, SaleTrailEntry } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { notifyError } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { CashSessionFinance } from './CashSessionFinance'
import { DICAS, saleChips, TRAIL_KIND, trailTone } from './sales'

const dateTime = (iso: string) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' })
const qty = (n: number) => n.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/** CA 6: o CPF/CNPJ inteiro só com um toque, e cada leitura fica na auditoria (a tela avisa). */
function CustomerDocument({ sale }: { sale: SaleDetail }) {
  const [full, setFull] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const canReveal = useCan('sales.view_customer_document')
  if (!sale.customerDocument) return <span className="text-muted-foreground">Consumidor não identificado</span>
  const reveal = async () => {
    setBusy(true)
    try {
      setFull((await getSaleCustomerDocument(sale.id)).document)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível mostrar o documento. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <span className="tabular-nums">{full ?? sale.customerDocument}</span>
      {full || !canReveal ? null : (
        <button type="button" disabled={busy} onClick={() => void reveal()} className="inline-flex min-h-9 items-center gap-1 rounded-md border border-border px-2 text-sm font-semibold disabled:opacity-60">
          <Eye aria-hidden="true" className="size-4" />{busy ? 'Abrindo…' : 'Mostrar'}
        </button>
      )}
      <Dica nome="CPF do consumidor" texto="A lista e a planilha mostram o documento mascarado. Ver o documento inteiro fica registrado no histórico: quem viu e quando." />
    </span>
  )
}

function Items({ sale }: { sale: SaleDetail }) {
  return (
    <div className="overflow-x-auto rounded-md border border-border">
      <table className="w-full text-sm">
        <caption className="sr-only">Itens da venda {sale.number}</caption>
        <thead className="bg-muted text-left">
          <tr>
            <th className="p-2">Item</th><th className="p-2 text-right">Qtd.</th>
            <th className="p-2 text-right">Tabela<Dica nome="Preço da tabela" texto="O preço da tabela da loja. Quando o operador trocou o preço no caixa, o praticado fica diferente e o motivo aparece na linha." /></th>
            <th className="p-2 text-right">Praticado</th><th className="p-2 text-right">Desconto</th><th className="p-2 text-right">Total</th>
            {sale.lines.some((l) => l.unitCost != null) ? <th className="p-2 text-right">Custo un.</th> : null}
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {sale.lines.map((l) => (
            <tr key={l.id}>
              <td className="p-2">
                {l.description}
                {l.priceReason ? <span className="block text-xs text-muted-foreground">Troca de preço: {l.priceReason}</span> : null}
                {l.discountReason ? <span className="block text-xs text-muted-foreground">Desconto: {l.discountReason}</span> : null}
              </td>
              <td className="p-2 text-right tabular-nums">{qty(Number(l.quantity))} {l.unitCode}</td>
              <td className="p-2 text-right"><Money value={Number(l.listUnitPrice)} /></td>
              <td className={l.listUnitPrice !== l.unitPrice ? 'p-2 text-right font-semibold text-warn-ink' : 'p-2 text-right'}><Money value={Number(l.unitPrice)} /></td>
              <td className="p-2 text-right">{Number(l.discount) ? <Money value={Number(l.discount)} /> : '—'}</td>
              <td className="p-2 text-right"><Money value={Number(l.total)} /></td>
              {sale.lines.some((x) => x.unitCost != null) ? <td className="p-2 text-right">{l.unitCost == null ? '—' : <Money value={Number(l.unitCost)} />}</td> : null}
            </tr>
          ))}
        </tbody>
        <tfoot className="border-t-2 border-border">
          {Number(sale.discountTotal) ? <tr><td className="p-2" colSpan={5}>Descontos</td><td className="p-2 text-right"><Money value={-Number(sale.discountTotal)} /></td></tr> : null}
          <tr className="font-semibold"><td className="p-2" colSpan={5}>Total</td><td className="p-2 text-right"><Money value={Number(sale.total)} /></td></tr>
        </tfoot>
      </table>
    </div>
  )
}

function Payments({ sale }: { sale: SaleDetail }) {
  if (sale.payments.length === 0) return <p className="text-sm text-muted-foreground">Esta venda não tem pagamento registrado.</p>
  return (
    <ul className="divide-y divide-border rounded-md border border-border text-sm">
      {sale.payments.map((p) => (
        <li key={p.id} className="flex flex-wrap items-baseline justify-between gap-2 p-3">
          <span>
            <b>{p.methodName}</b>
            {p.installments > 1 ? ` em ${p.installments}x` : ''}
            {p.cardBrand ? ` · ${p.cardBrand}` : ''}
            {p.nsu ? <span className="text-muted-foreground"> · NSU {p.nsu}</span> : null}
            {p.authorizationCode ? <span className="text-muted-foreground"> · aut. {p.authorizationCode}</span> : null}
            {p.confirmedByName ? <span className="block text-xs text-muted-foreground">Confirmado por {p.confirmedByName}{p.confirmedAt ? ` em ${dateTime(p.confirmedAt)}` : ''}</span> : null}
            {' '}{p.status !== 'Captured' ? <StatusChip tone={p.status === 'Refunded' || p.status === 'Voided' ? 'info' : 'warn'}>{p.status === 'Refunded' ? 'Estornado' : p.status === 'Voided' ? 'Cancelado' : p.status}</StatusChip> : null}
          </span>
          <span className="tabular-nums">
            <Money value={Number(p.amount)} />
            {Number(p.changeAmount) ? <span className="block text-xs text-muted-foreground">troco <Money value={Number(p.changeAmount)} /></span> : null}
          </span>
        </li>
      ))}
    </ul>
  )
}

/** A trilha (CA 4): os passos em ordem de tempo, cada um com o grupo (caixa, supervisor, sincronização, estoque, financeiro, impressão). */
export function Trail({ entries, cashSessionId, canSeeFinance = true }: { entries: readonly SaleTrailEntry[]; cashSessionId?: string | null; canSeeFinance?: boolean }) {
  return (
    <div className="space-y-4">
      <ol className="space-y-2" aria-label="Trilha da venda">
        {entries.map((e, i) => (
          <li key={`${e.at}-${i}`} className="flex gap-3 rounded-md border border-border bg-card p-3 text-sm" data-kind={e.kind}>
            <span className="w-20 flex-none text-xs text-muted-foreground tabular-nums">{dateTime(e.at).slice(0, 10)}<br />{dateTime(e.at).slice(12)}</span>
            <span className="min-w-0 flex-1">
              <span className="flex flex-wrap items-center gap-2">
                <StatusChip tone={trailTone(e.tone)}>{TRAIL_KIND[e.kind] ?? e.kind}</StatusChip>
                <b className="font-semibold">{e.title}</b>
              </span>
              {e.detail ? <span className="mt-1 block text-muted-foreground">{e.detail}</span> : null}
            </span>
          </li>
        ))}
      </ol>
      {cashSessionId ? (
        <div className="space-y-2">
          <Link to={'/app/loja/caixas' as never} search={{ sessionId: cashSessionId } as never} className="text-sm font-semibold text-primary hover:underline">Ver a sessão de caixa</Link>
          {canSeeFinance ? <CashSessionFinance sessionId={cashSessionId} /> : null}
        </div>
      ) : null}
    </div>
  )
}

/**
 * Venda (PDV-07 CA 4 a 6): o cabeçalho com loja, caixa, operador, chips e o documento do consumidor mascarado (o inteiro com um toque, auditado), e
 * as abas Itens (preço da tabela e praticado), Pagamentos e Trilha. Carregando, não encontrada e erro.
 */
export function SalePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetSaleDetail(id)
  const canSeeFinance = useCan('finance.view')
  const back = <Link to="/app/loja/vendas" className="inline-flex items-center gap-1 text-sm font-semibold text-primary"><ArrowLeft aria-hidden="true" className="size-4" />Vendas</Link>
  if (query.isPending) return <><PageHeading title="Venda" />{back}<Skeleton className="mt-4 h-64" /></>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return (
      <>
        <PageHeading title="Venda" />
        {back}
        <ErrorState title={notFound ? 'Venda não encontrada.' : 'Não foi possível abrir a venda.'}
          message={notFound ? 'Confira o link ou procure a venda na lista.' : 'Tente de novo em instantes.'} onRetry={notFound ? undefined : () => void query.refetch()} />
      </>
    )
  }

  const s = query.data
  return (
    <>
      <PageHeading title={`Venda ${s.number}`} description={[s.storeName, s.registerLabel, s.operatorName].filter(Boolean).join(' · ')} />
      {back}
      <section className="my-4 grid gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <div><span className="block text-muted-foreground">Quando</span><span className="tabular-nums">{dateTime(s.at)}</span></div>
        <div><span className="block text-muted-foreground">Total</span><span className="text-lg font-semibold"><Money value={Number(s.total)} /></span>
          {s.margin != null ? <span className="block text-xs text-muted-foreground">margem <Money value={Number(s.margin)} highlightNegative /></span> : null}</div>
        <div><span className="block text-muted-foreground">Estado</span><span className="flex flex-wrap gap-1">{saleChips(s).map((c) => <StatusChip key={c.label} tone={c.tone}>{c.label}</StatusChip>)}</span>
          {s.hasDivergence && s.paymentDifference != null ? <span className="block text-xs text-muted-foreground">diferença no pagamento <Money value={Number(s.paymentDifference)} highlightNegative /></span> : null}</div>
        <div><span className="block text-muted-foreground">Consumidor</span><CustomerDocument sale={s} /></div>
        {s.cancelledAt ? <div className="sm:col-span-2 lg:col-span-4"><StatusChip tone="crit">Cancelada</StatusChip> em {dateTime(s.cancelledAt)}{s.cancelledByName ? ` por ${s.cancelledByName}` : ''}{s.cancelReason ? `: ${s.cancelReason}` : ''}</div> : null}
        {s.hasDivergence || s.stockStatus === 'Shortfall' ? (
          <div className="sm:col-span-2 lg:col-span-4">
            <Link to="/app/loja/pendencias" search={{ registerId: s.registerId ?? undefined }} className="font-semibold text-primary hover:underline">Resolver nas Pendências do PDV</Link>
          </div>
        ) : null}
      </section>
      <UrlTabs label="Seções da venda" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: <Items sale={s} /> },
        { id: 'pagamentos', label: 'Pagamentos', content: <Payments sale={s} /> },
        {
          id: 'trilha', label: 'Trilha',
          content: <><p className="mb-3 text-sm text-muted-foreground">Tudo o que aconteceu com esta venda, em ordem.<Dica nome="Trilha" texto={DICAS.trilha} /></p><Trail entries={s.trail} cashSessionId={s.cashSessionId} canSeeFinance={canSeeFinance} /></>,
        },
      ]} />
    </>
  )
}
