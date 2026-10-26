import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { markDreViewed, useGetDre } from '@/api/generated/bemaqui'
import type { DreChangeSummary, DreReport } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { Chart } from '@/components/ui/chart'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { MOBILE, useMediaQuery } from '@/hooks/useMediaQuery'
import { formatDateTime, formatMoney } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { addMonths, changesText, currentMonth, DRE_DICAS, monthLabel, monthLong, PERIODS } from './dre'
import { DreCellPanel } from './DreCellPanel'
import { DreChangesPanel } from './DreChangesPanel'
import { type DreCellTarget, DreTable } from './DreTable'

const linkButton = 'inline-flex min-h-11 items-center rounded-md px-1 font-semibold underline sm:min-h-0'

function Notices({ report, changes, onChanges, onClassify }: { report: DreReport; changes: DreChangeSummary | null; onChanges: () => void; onClassify: (accountId: string) => void }) {
  const classifyTotal = report.toClassify.reduce((s, c) => s + c.total, 0)
  return (
    <>
      {changes ? (
        <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">
          <span>{changesText(changes.since, changes.count, changes.months, formatDateTime)}</span>
          <button type="button" onClick={onChanges} className={linkButton}>Ver o que mudou</button>
        </div>
      ) : null}
      {report.toClassify.length > 0 ? (
        <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <span className="inline-flex items-center">{formatMoney(classifyTotal)} em contas a classificar neste período.<Dica texto={DRE_DICAS.aClassificar} nome="A classificar" /></span>
          <span className="flex flex-wrap gap-3">
            {report.toClassify.map((c) => <button key={c.accountId} type="button" onClick={() => onClassify(c.accountId)} className={linkButton}>Ver os títulos de {c.code} {c.name}</button>)}
          </span>
        </div>
      ) : null}
      {report.pendingParameters > 0 ? (
        <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <span className="inline-flex flex-wrap items-center gap-2">
            <StatusChip tone="warn">A confirmar com a contadora</StatusChip>
            {report.pendingParameters} {report.pendingParameters === 1 ? 'parâmetro do DRE ainda não foi conferido' : 'parâmetros do DRE ainda não foram conferidos'} (contas de juros e descontos e a regra do sinal).
          </span>
          <Link to={'/app/configuracoes/parametros' as never} className={linkButton + ' text-primary'}>Ver em Configurações</Link>
        </div>
      ) : null}
    </>
  )
}

/**
 * Financeiro → DRE (FIN-08, R-FIN-13): resultado gerencial mês a mês por competência, com a receita "Provisório" até as notas,
 * os avisos acionáveis (mudou desde, a classificar, parâmetros a confirmar), a `TreeTable` analítica e o drill-down por célula.
 */
export function DrePage() {
  const search = useSearch({ strict: false }) as { meses?: string; ate?: string }
  const navigate = useNavigate()
  const mobile = useMediaQuery(MOBILE)
  const months = PERIODS.find((p) => String(p) === search.meses) ?? 12
  const to = search.ate && /^\d{4}-\d{2}$/.test(search.ate) ? `${search.ate}-01` : currentMonth()
  const from = addMonths(to, -(months - 1))
  const query = useGetDre({ from, to })
  const [target, setTarget] = useState<DreCellTarget | null>(null)
  const [showChanges, setShowChanges] = useState(false)
  const [monthIndex, setMonthIndex] = useState<number | null>(null)
  // CA 9: o aviso é da consulta anterior; a marca nova é gravada ao abrir, e o aviso fica até sair da tela.
  const [changes, setChanges] = useState<DreChangeSummary | null>(null)
  const marked = useRef(false)
  const set = (patch: Record<string, string | undefined>) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, ...patch }), replace: true } as never)

  useEffect(() => {
    if (!query.data || marked.current) return
    marked.current = true
    setChanges(query.data.changes ?? null)
    void markDreViewed().catch(() => { marked.current = false })
  }, [query.data])

  let body
  if (query.isPending) body = <div className="space-y-3" aria-busy="true"><Skeleton className="h-10" />{Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="h-8" />)}</div>
  else if (query.isError) body = <ErrorState title="Não foi possível montar o DRE." onRetry={() => void query.refetch()} />
  else if (!query.data.hasEntries) {
    body = (
      <EmptyState title="Sem lançamentos no período." description="Os títulos e lançamentos entram aqui pela data de competência."
        action={<Link to={'/app/financeiro/receber' as never} className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ver contas a receber</Link>} />
    )
  } else {
    const r = query.data
    const index = Math.min(monthIndex ?? r.months.length - 1, r.months.length - 1)
    const net = r.rows.find((x) => x.key === 'subtotal:NetRevenue')
    const result = r.rows.find((x) => x.key === 'subtotal:PeriodResult')
    const points = r.months.map((m, i) => ({ mes: monthLabel(m.month), receita: net?.values[i] ?? 0, resultado: result?.values[i] ?? 0 }))
    body = (
      <div className="space-y-4">
        <Notices report={r} changes={changes} onChanges={() => setShowChanges(true)}
          onClassify={(accountId) => { const row = r.rows.find((x) => x.accountId === accountId); if (row) setTarget({ row, month: null }) }} />
        {mobile ? (
          <label className="block space-y-1 text-sm font-medium">Mês
            <select value={index} onChange={(e) => setMonthIndex(Number(e.target.value))} className={inputClass + ' min-h-11'}>
              {r.months.map((m, i) => <option key={m.month} value={i}>{monthLong(m.month)}{m.isProvisional ? ' (provisório)' : ''}</option>)}
            </select>
          </label>
        ) : null}
        <DreTable report={r} mobile={mobile} monthIndex={index} onOpen={setTarget} />
        <p className="text-xs text-muted-foreground">Clique numa célula de conta ou de grupo, ou use as setas e Enter, para ver os documentos que somam o valor.</p>
        {mobile ? null : (
          <Chart title="Receita líquida e resultado do período" data={points} xKey="mes" xLabel="Mês" format={(v) => formatMoney(v, false)}
            series={[{ key: 'receita', label: 'Receita líquida' }, { key: 'resultado', label: 'Resultado do período' }]} />
        )}
      </div>
    )
  }

  return (
    <>
      <PageHeading title="Quanto a empresa vendeu, gastou e lucrou em cada mês.">
        <p className="mt-2 flex flex-wrap items-center gap-2 text-muted-foreground">
          <span className="inline-flex items-center">DRE gerencial por competência<Dica texto={DRE_DICAS.competencia} nome="Competência" /></span>
          {query.data?.isProvisional ? <span className="inline-flex items-center"><StatusChip tone="info">Provisório</StatusChip><Dica texto={DRE_DICAS.provisorio} nome="Provisório" /></span> : null}
        </p>
      </PageHeading>
      <FinanceTabs />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="space-y-1 text-sm font-medium">Período
          <select value={months} onChange={(e) => set({ meses: e.target.value === '12' ? undefined : e.target.value })} className={inputClass + ' min-h-11 sm:min-h-0'}>
            {PERIODS.map((p) => <option key={p} value={p}>Últimos {p} meses</option>)}
          </select>
        </label>
        <label className="space-y-1 text-sm font-medium">Até o mês
          <input type="month" value={to.slice(0, 7)} max={currentMonth().slice(0, 7)} onChange={(e) => set({ ate: e.target.value && e.target.value !== currentMonth().slice(0, 7) ? e.target.value : undefined })} className={inputClass + ' min-h-11 sm:min-h-0'} />
        </label>
      </div>
      {body}
      {target ? <DreCellPanel target={target} from={from} to={to} onClose={() => setTarget(null)} /> : null}
      {showChanges && changes ? <DreChangesPanel since={changes.since} from={from} to={to} onClose={() => setShowChanges(false)} /> : null}
    </>
  )
}
