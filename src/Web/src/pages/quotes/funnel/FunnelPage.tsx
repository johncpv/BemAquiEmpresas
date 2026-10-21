import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useGetQuoteFunnel } from '@/api/generated/bemaqui'
import type { QuoteFunnel } from '@/api/generated/model'
import { DateInput } from '@/components/form/inputs'
import { inputClass } from '@/components/form/form-utils'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { today } from '../../finance/receivables/receivables'
import { QUOTE_STATUS } from '../quotes'
import { addDays, barWidth, FUNNEL_DICAS, LOST_REASONS, pctLabel } from './funnel'

type Search = { de?: string; ate?: string; autor?: string }

/** Comercial → Funil (COM-08): situação da revisão corrente, conversão, motivos de perda e "Vencendo"; filtros na URL. */
export function FunnelPage() {
  const search = useSearch({ strict: false }) as Search
  const navigate = useNavigate()
  const set = (patch: Search) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, ...patch }), replace: true } as never)
  const funnel = useGetQuoteFunnel({ from: search.de || undefined, to: search.ate || undefined, authorId: search.autor || undefined })
  const from = funnel.data?.from ?? search.de ?? addDays(today(), -90)
  const to = funnel.data?.to ?? search.ate ?? today()

  let body
  if (funnel.isPending) body = <div className="space-y-3"><Skeleton className="h-24" /><Skeleton className="h-64" /></div>
  else if (funnel.isError) body = <ErrorState title="Não foi possível montar o funil." message="Confira o período escolhido e tente de novo." onRetry={() => funnel.refetch()} />
  else if (funnel.data.stages.every((s) => s.count === 0)) {
    body = (
      <EmptyState title="Sem orçamentos no período." description="Amplie o período ou escolha outro autor."
        action={<button type="button" onClick={() => set({ de: addDays(to, -365), ate: to })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver últimos 12 meses</button>} />
    )
  } else body = <FunnelBody funnel={funnel.data} drill={{ de: from, ate: to, autor: search.autor }} />

  return (
    <>
      <PageHeading title="Comercial: Funil">
        <p className="mt-2 text-muted-foreground">Quantos orçamentos estão em cada etapa, quantos viram contrato e por que perdemos os outros.</p>
      </PageHeading>
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="space-y-1 text-sm font-medium"><span className="inline-flex items-center">De<Dica nome="Período do funil" texto={FUNNEL_DICAS.periodo} /></span>
          <DateInput value={from} onChange={(v) => { if (v) set({ de: v }) }} className="w-36" />
        </label>
        <label className="space-y-1 text-sm font-medium"><span className="block">Até</span>
          <DateInput value={to} onChange={(v) => { if (v) set({ ate: v }) }} className="w-36" />
        </label>
        <label className="space-y-1 text-sm font-medium"><span className="block">Autor</span>
          <select value={search.autor ?? ''} onChange={(e) => set({ autor: e.target.value || undefined })} className={inputClass}>
            <option value="">Todos</option>
            {(funnel.data?.authors ?? []).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        </label>
      </div>
      {body}
    </>
  )
}

function FunnelBody({ funnel, drill }: { funnel: QuoteFunnel; drill: Search }) {
  const canEdit = useCan('quotes.edit')
  const max = Math.max(...funnel.stages.map((s) => s.count))
  const lostTotal = funnel.lossReasons.reduce((sum, r) => sum + r.count, 0)
  return (
    <div className="space-y-6">
      <section aria-label="Taxa de conversão" className="grid gap-3 sm:grid-cols-3">
        <p className="rounded-md border border-border bg-card p-3 text-sm">
          <span className="flex items-center text-xs text-muted-foreground">Taxa de conversão (quantidade)<Dica nome="Taxa de conversão" texto={FUNNEL_DICAS.conversao} /></span>
          <span className="text-lg font-semibold tabular-nums">{pctLabel(funnel.conversion.byCount)}</span>
        </p>
        <p className="rounded-md border border-border bg-card p-3 text-sm">
          <span className="block text-xs text-muted-foreground">Taxa de conversão (valor)</span>
          <span className="text-lg font-semibold tabular-nums">{pctLabel(funnel.conversion.byAmount)}</span>
        </p>
        <p className="rounded-md border border-border bg-card p-3 text-sm">
          <span className="block text-xs text-muted-foreground">Decididos no período</span>
          <span className="text-lg font-semibold tabular-nums">{funnel.conversion.decided}</span>
        </p>
      </section>

      <section aria-labelledby="funil-situacoes" className="space-y-2">
        <h2 id="funil-situacoes" className="text-base font-semibold">Orçamentos por situação</h2>
        <ul className="space-y-2">
          {funnel.stages.map((s, i) => (
            <li key={s.status}>
              <Link to={'/app/orcamentos' as never} search={{ situacao: s.situation, de: drill.de, ate: drill.ate, autor: drill.autor } as never}
                className="block rounded-md p-1 hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring">
                <span className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
                  <span className="font-medium">{QUOTE_STATUS[s.status]?.label ?? s.status}</span>
                  <span className="tabular-nums"><strong>{s.count}</strong> · <Money value={s.amount} /></span>
                </span>
                <span aria-hidden="true" className="mt-1 block h-3 rounded-sm bg-muted">
                  <span className="block h-3 rounded-sm" style={{ width: `${barWidth(s.count, max)}%`, background: `var(--chart-${(i % 6) + 1})` }} />
                </span>
              </Link>
            </li>
          ))}
        </ul>
        <details className="text-sm">
          <summary className="cursor-pointer font-semibold text-primary">Ver os valores em tabela</summary>
          <table className="mt-2 w-full text-sm">
            <caption className="sr-only">Orçamentos por situação</caption>
            <thead><tr className="text-left text-muted-foreground"><th scope="col">Situação</th><th scope="col" className="text-right">Quantidade</th><th scope="col" className="text-right">Valor</th></tr></thead>
            <tbody>{funnel.stages.map((s) => <tr key={s.status} className="border-t border-border"><td>{QUOTE_STATUS[s.status]?.label ?? s.status}</td><td className="text-right tabular-nums">{s.count}</td><td className="text-right"><Money value={s.amount} /></td></tr>)}</tbody>
          </table>
        </details>
      </section>

      <section aria-labelledby="funil-perdas" className="space-y-2">
        <h2 id="funil-perdas" className="text-base font-semibold">Motivos de perda</h2>
        {lostTotal === 0 ? <p className="text-sm text-muted-foreground">Nenhum orçamento perdido no período.</p> : (
          <table className="w-full text-sm">
            <thead><tr className="text-left text-muted-foreground"><th scope="col">Motivo</th><th scope="col" className="text-right">Quantidade</th><th scope="col" className="text-right">Valor</th><th scope="col" className="text-right">%</th></tr></thead>
            <tbody>{funnel.lossReasons.map((r) => (
              <tr key={r.reason ?? ""} className="border-t border-border"><td>{(r.reason ? LOST_REASONS[r.reason] : undefined) ?? r.reason}</td><td className="text-right tabular-nums">{r.count}</td><td className="text-right"><Money value={r.amount} /></td><td className="text-right tabular-nums">{pctLabel(r.pct)}</td></tr>
            ))}</tbody>
          </table>
        )}
        {funnel.otherLosses.length > 0 ? (
          <details className="text-sm">
            <summary className="cursor-pointer font-semibold text-primary">Detalhe dos "Outro" ({funnel.otherLosses.length})</summary>
            <ul className="mt-2 space-y-1">
              {funnel.otherLosses.map((o) => (
                <li key={o.id}><Link to={'/app/orcamentos/$id' as never} params={{ id: o.id } as never} className="font-semibold text-primary hover:underline">{o.number}{o.revision > 0 ? ` rev. ${o.revision}` : ''}</Link> — {o.detail || 'sem detalhe'} · <Money value={o.totalPrice} /></li>
              ))}
            </ul>
          </details>
        ) : null}
      </section>

      <section aria-labelledby="funil-vencendo" className="space-y-2">
        <h2 id="funil-vencendo" className="text-base font-semibold">Vencendo nos próximos 7 dias</h2>
        {funnel.expiring.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum orçamento enviado vence nos próximos 7 dias.</p> : (
          <ul className="divide-y divide-border rounded-md border border-border">
            {funnel.expiring.map((q) => (
              <li key={q.id} className="flex flex-wrap items-center justify-between gap-2 p-3 text-sm">
                <span>
                  <Link to={'/app/orcamentos/$id' as never} params={{ id: q.id } as never} className="font-semibold text-primary hover:underline">{q.number}{q.revision > 0 ? ` rev. ${q.revision}` : ''}</Link> · {q.partyName}
                  <span className="block text-xs text-muted-foreground">Vence em {formatDate(q.validUntil)} ({q.daysLeft === 0 ? 'hoje' : q.daysLeft === 1 ? 'amanhã' : `em ${q.daysLeft} dias`}) · <Money value={q.totalPrice} /></span>
                </span>
                {canEdit ? (
                  <span className="flex flex-wrap gap-2">
                    <Link to={'/app/orcamentos/$id' as never} params={{ id: q.id } as never} search={{ acao: 'prorrogar' } as never} className="rounded-md border border-border px-3 py-2 font-semibold">Prorrogar validade</Link>
                    <Link to={'/app/orcamentos/$id' as never} params={{ id: q.id } as never} search={{ acao: 'revisar' } as never} className="rounded-md border border-border px-3 py-2 font-semibold">Nova revisão</Link>
                  </span>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}
