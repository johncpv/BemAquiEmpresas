import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import {
  acceptHighReconciliations, closeReconciliation, getGetReconciliationQueryKey, getReconciliationClosingPreview, getStatementLineHistory, reconcile, useGetReconciliation,
  useListBankAccounts,
} from '@/api/generated/bemaqui'
import type { ClosingPreview, ReconDone, ReconHistory, ReconPair, ReconciliationView, ReconUnpairedLine, ReconUnpairedTransaction } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { FinanceTabs } from '../FinanceTabs'
import { today } from '../receivables/receivables'
import { CONFIDENCE, fullDate, KIND_LABEL, nextPeriod } from './reconciliation'
import {
  LineEntryDialog, LineSettleDialog, LinesForTransactionDialog, LineSummary, ManualDialog, NOT_VERIFIED, RuleDialog, SEGREGATION, TransactionSummary, UndoDialog,
} from './ReconciliationDialogs'

const primary = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const secondary = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'
const link = 'inline-flex min-h-11 items-center px-2 text-sm font-semibold text-primary hover:underline'
const monthStart = (iso: string) => iso.slice(0, 8) + '01'
const monthEnd = (iso: string) => { const [y, m] = iso.split('-').map(Number); return new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10) }
const CONFIDENCE_HINT = 'Alta: boleto do Inter com o mesmo identificador do banco, ou mesmo valor e mesma data sem outra transação igual. Média: mesmo valor com até 3 dias de diferença e o CPF/CNPJ do terceiro ou o número do título ou da OP na descrição. Linha não verificada pelo banco nunca tem Alta.'
const CLOSE_HINT = 'Com tudo conciliado ou ignorado e o saldo do banco igual ao do sistema, quem aprova em Caixa e pagamentos fecha o período. Depois, desfazer, ignorar linha ou lançar com data no período pede aprovação e motivo, e o Proprietário é avisado.'

function Flags({ line }: { line: ReconPair['line'] }) {
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      {line.bankVerified ? null : <span className="inline-flex items-center"><StatusChip tone="warn">Não verificada pelo banco</StatusChip><Dica nome="Não verificada pelo banco" texto={NOT_VERIFIED} /></span>}
      {line.blocked ? <span className="inline-flex items-center text-xs text-warn-ink">{line.blockedReason}<Dica nome="Quem importou não confirma" texto={SEGREGATION} /></span> : null}
    </span>
  )
}

/** Uma linha por par (§4.4): banco à esquerda, motivo e confiança no meio, sistema à direita; Enter na linha concilia (atalho). */
function PairRow({ pair, canEdit, onAccept, manual }: { pair: ReconPair; canEdit: boolean; onAccept: (p: ReconPair) => Promise<void>; manual: React.ReactNode }) {
  const confidence = CONFIDENCE[pair.confidence] ?? { label: pair.confidence, tone: 'neutral' as const }
  const disabled = !canEdit || pair.line.blocked
  return (
    <li tabIndex={0} aria-keyshortcuts="Enter" aria-label={`Par de ${formatDate(pair.line.date)}: ${pair.reason}`}
      onKeyDown={(e) => { if (e.key === 'Enter' && e.target === e.currentTarget && !disabled) void onAccept(pair) }}
      className="grid gap-2 rounded-md border border-border bg-card p-3 focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary sm:grid-cols-[1fr_minmax(10rem,14rem)_1fr] sm:items-center">
      <div><LineSummary line={pair.line} /><Flags line={pair.line} /></div>
      <div className="space-y-1 text-sm sm:text-center">
        <StatusChip tone={confidence.tone}>Confiança {confidence.label}</StatusChip>
        <p className="text-xs text-muted-foreground">{pair.reason}</p>
        {pair.transaction.pendingReview ? <p className="text-xs"><Link to={'/app/financeiro/receber' as never} search={{ filtro: 'a-conferir' } as never} className="font-semibold text-primary hover:underline">Pagamento a conferir</Link></p> : null}
      </div>
      <div className="space-y-2">
        <TransactionSummary tx={pair.transaction} />
        {canEdit ? (
          <span className="flex flex-wrap items-center justify-end gap-1">
            {manual}
            <button type="button" disabled={disabled} title={pair.line.blocked ? pair.line.blockedReason ?? undefined : undefined} onClick={() => void onAccept(pair)} className={primary}>Conciliar</button>
          </span>
        ) : null}
      </div>
    </li>
  )
}

function BankOnlyRow({ item, view, canEdit, canPay, canApprove, onChanged }: { item: ReconUnpairedLine; view: ReconciliationView; canEdit: boolean; canPay: boolean; canApprove: boolean; onChanged: (label?: string) => Promise<void> }) {
  const line = item.line
  const action = item.action
  const newOrder = `/app/financeiro/pagar/novo`
  return (
    <li className="space-y-2 rounded-md border border-border bg-card p-3">
      <LineSummary line={line} />
      <Flags line={line} />
      {action ? <p className="text-sm font-medium">{action.text}</p> : <p className="text-sm text-muted-foreground">Sem sugestão. Escolha à mão a transação do sistema.</p>}
      {canEdit ? (
        <span className="flex flex-wrap items-center gap-1">
          {action?.kind === 'BankEntry' && canPay ? <LineEntryDialog accountId={view.accountId} item={item} cap={view.ofxBankEntryCap} canApprove={canApprove} onDone={() => onChanged()} /> : null}
          {action?.kind === 'Settle' ? <LineSettleDialog accountId={view.accountId} item={item} onDone={() => onChanged()} /> : null}
          {action?.kind === 'PaymentOrder' ? (
            <>
              <ManualDialog accountId={view.accountId} line={line} paymentsOnly onDone={() => onChanged()} trigger={<button type="button" disabled={line.blocked} className={secondary}>Vincular a uma OP paga</button>} />
              <Link to={newOrder as never} search={{ valor: Math.abs(line.amount), vencimento: line.date, descricao: line.description } as never} className={link}>Abrir OP para regularizar</Link>
            </>
          ) : null}
          {!action && item.canBankEntry && line.amount > 0 && canPay ? <LineEntryDialog accountId={view.accountId} item={item} cap={view.ofxBankEntryCap} canApprove={canApprove} onDone={() => onChanged()} /> : null}
          <ManualDialog accountId={view.accountId} line={line} onDone={() => onChanged()} trigger={<button type="button" disabled={line.blocked} className={link}>Escolher à mão</button>} />
          {item.suggestRule ? <RuleDialog line={line} onDone={() => onChanged()} /> : null}
        </span>
      ) : null}
    </li>
  )
}

function SystemOnlyRow({ item, view, canEdit, onChanged }: { item: ReconUnpairedTransaction; view: ReconciliationView; canEdit: boolean; onChanged: () => Promise<void> }) {
  const tx = item.transaction
  return (
    <li className={`space-y-2 rounded-md border bg-card p-3 ${item.stale ? 'border-l-4 border-warn-ink' : 'border-border'}`}>
      <TransactionSummary tx={tx} />
      <span className="flex flex-wrap items-center gap-2">
        {item.stale ? <StatusChip tone="warn">Sem o banco há {item.daysWithoutBank} dias</StatusChip> : <StatusChip tone="info">Aguardando o banco</StatusChip>}
        {tx.link ? <Link to={tx.link as never} className={link}>Ver origem</Link> : null}
        {canEdit ? <LinesForTransactionDialog accountId={view.accountId} tx={tx} onDone={() => onChanged()} /> : null}
      </span>
    </li>
  )
}

function History({ accountId, lineId }: { accountId: string; lineId: string }) {
  const [data, setData] = useState<ReconHistory | null>(null)
  const [failed, setFailed] = useState(false)
  useEffect(() => { void getStatementLineHistory(accountId, lineId).then(setData, () => setFailed(true)) }, [accountId, lineId])
  if (failed) return <p role="alert" className="text-sm text-crit-ink">Não foi possível carregar o histórico.</p>
  if (!data) return <p role="status" className="text-sm text-muted-foreground">Carregando o histórico…</p>
  return (
    <div className="space-y-1 text-sm">
      <p className="text-muted-foreground">Importada {data.source === 'Ofx' ? 'de arquivo OFX' : 'pela API'} por {data.importedByName ?? 'o sistema'}{data.importedAt ? ` em ${formatDateTime(data.importedAt)}` : ''}.</p>
      <ul className="space-y-1">
        {data.entries.map((e) => (
          <li key={e.reconciliationId}>
            {KIND_LABEL[e.kind] ?? e.kind}{e.confidence ? ` (confiança ${CONFIDENCE[e.confidence]?.label ?? e.confidence})` : ''}: {e.reason} · {e.createdByName ?? 'o sistema'} em {formatDateTime(e.createdAt)}
            {e.closedPeriodReason ? ` · período fechado: ${e.closedPeriodReason}` : ''}
            {e.undoneAt ? ` · desfeita por ${e.undoneByName ?? 'o sistema'} em ${formatDateTime(e.undoneAt)}: ${e.undoReason}` : ''}
          </li>
        ))}
      </ul>
    </div>
  )
}

function DoneRow({ done, view, canEdit, canApprove, onChanged }: { done: ReconDone; view: ReconciliationView; canEdit: boolean; canApprove: boolean; onChanged: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  return (
    <li className="space-y-2 rounded-md border border-border bg-card p-3">
      <div className="grid gap-2 sm:grid-cols-2">
        <div className="space-y-1">{done.lines.map((l) => <LineSummary key={l.id} line={l} />)}</div>
        <div className="space-y-1">{done.transactions.map((t) => <TransactionSummary key={t.id} tx={t} />)}</div>
      </div>
      <p className="text-xs text-muted-foreground">{KIND_LABEL[done.kind] ?? done.kind}{done.reason ? `: ${done.reason}` : ''} · {done.createdByName ?? 'o sistema'} em {formatDateTime(done.createdAt)}</p>
      <span className="flex flex-wrap items-center gap-1">
        <button type="button" aria-expanded={open} onClick={() => setOpen(!open)} className={link}>Histórico</button>
        {canEdit ? <UndoDialog accountId={view.accountId} done={done} canApprove={canApprove} onDone={onChanged} /> : null}
      </span>
      {open ? <History accountId={view.accountId} lineId={done.lines[0].id} /> : null}
    </li>
  )
}

function Section({ title, count, hint, children }: { title: string; count: number; hint?: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="space-y-2">
      <h2 className="flex items-center gap-2 text-base font-semibold">{title}<span className="tabular-nums text-muted-foreground">({count})</span>{hint ? <Dica nome={title} texto={hint} /> : null}</h2>
      {children}
    </section>
  )
}

function CloseDialog({ view, onDone }: { view: ReconciliationView; onDone: (until: string) => Promise<void> }) {
  const until = view.reconciledUntil!
  const [preview, setPreview] = useState<ClosingPreview | null>(null)
  return (
    <ConfirmDialog size="lg" verb="Fechar conciliação" done="Conciliação fechada" title={`Fechar conciliação até ${fullDate(until)}?`}
      description="Depois de fechado, desfazer, ignorar linha ou lançar com data no período exige quem aprova em Caixa e pagamentos e motivo."
      onOpen={() => { setPreview(null); void getReconciliationClosingPreview(view.accountId, { until }).then(setPreview) }}
      preconditions={[{ ok: !!preview?.allowed, text: preview?.refusal ?? (preview ? 'Pronto para fechar.' : 'Conferindo o período…') }]}
      impactList={preview ? {
        label: 'O fechamento', items: [
          { key: 'linhas', primary: `${preview.lines} linhas`, secondary: `${preview.reconciled} conciliadas, ${preview.ignored} ignoradas` },
          { key: 'entradas', primary: <>Entradas <Money value={preview.credits} /></>, secondary: <>Saídas <Money value={preview.debits} /></> },
          { key: 'saldo', primary: <>Saldo do banco <Money value={preview.bankBalance ?? 0} /></>, secondary: <>Sistema <Money value={preview.systemBalance} /></> },
        ], total: <>Diferença <Money value={preview.difference ?? 0} /></>,
      } : undefined}
      onConfirm={async () => { await closeReconciliation(view.accountId, { until }); await onDone(until) }}
      trigger={<button type="button" className={primary}>Fechar conciliação até {fullDate(until).slice(0, 5)}</button>} />
  )
}

function AcceptHighDialog({ view, onDone }: { view: ReconciliationView; onDone: (n: number) => Promise<void> }) {
  const high = view.pairs.filter((p) => p.confidence === 'High' && !p.line.blocked)
  if (high.length === 0) return null
  return (
    <ConfirmDialog size="lg" verb="Aceitar todas de confiança alta" done="Conciliado" title={`Conciliar ${high.length} pares de confiança alta?`}
      impactList={{
        label: 'Vão ser conciliados',
        items: high.map((p) => ({ key: p.line.id, primary: <>{formatDate(p.line.date)} · <Money value={p.line.amount} /></>, secondary: `${p.line.description} ↔ ${p.transaction.description}` })),
        total: <>Total <Money value={high.reduce((a, p) => a + p.line.amount, 0)} /></>,
      }}
      onConfirm={async () => {
        const r = await acceptHighReconciliations(view.accountId, {
          from: view.from, to: view.to, pairs: high.map((p) => ({ lineId: p.line.id, lineVersion: p.line.version, transactionId: p.transaction.id, transactionVersion: p.transaction.version })),
        })
        await onDone(r.reconciled)
      }}
      trigger={<button type="button" className={secondary}>Aceitar todas de confiança alta ({high.length})</button>} />
  )
}

/**
 * Financeiro → Conciliação (BNK-03b): conta e período na URL; uma linha por par, "Sem par no banco", "No sistema, não no banco" e as
 * conciliadas; barra de totais; "Conciliada até" e o fechamento. A 400 px, os pares viram cartões empilhados com os botões de 44 px.
 */
export function ReconciliationPage() {
  const search = useSearch({ strict: false }) as { conta?: string; de?: string; ate?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canEdit = useCan('finance.edit')
  const canPay = useCan('finance.pay')
  const canApprove = useCan('finance.approve')
  const accounts = useListBankAccounts({ pageSize: 100 })
  const active = (accounts.data?.items ?? []).filter((a) => a.isActive)
  const accountId = search.conta ?? active.find((a) => a.isDefault)?.id ?? active[0]?.id
  const from = search.de ?? monthStart(today())
  const to = search.ate ?? monthEnd(from)
  const query = useGetReconciliation(accountId ?? '', { from, to }, { query: { enabled: !!accountId } })
  const [next, setNext] = useState<{ label: string; onSelect: () => void }[]>([])
  const [error, setError] = useState<string | null>(null)
  const set = (patch: Record<string, unknown>) => void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, ...patch }), replace: true } as never)
  const refresh = async (label?: string) => {
    if (label) notifySuccess(label)
    await client.invalidateQueries({ queryKey: getGetReconciliationQueryKey(accountId ?? '') })
  }

  const accept = async (p: ReconPair) => {
    setError(null)
    try {
      if (!accountId) return
      await reconcile(accountId, { lines: [{ id: p.line.id, version: p.line.version }], transactions: [{ id: p.transaction.id, version: p.transaction.version }], accepted: true })
      await refresh('Conciliado')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível conciliar. Recarregue e tente de novo.')
    }
  }

  const header = (
    <PageHeading title="Conciliação">
      <p className="mt-2 text-muted-foreground">O extrato do banco lado a lado com o que o sistema registrou, com o par já sugerido.</p>
    </PageHeading>
  )
  const filters = (
    <div className="mb-3 flex flex-wrap items-end gap-3">
      <label className="space-y-1 text-sm font-medium">Conta
        <select value={accountId ?? ''} onChange={(e) => set({ conta: e.target.value || undefined })} className={inputClass}>
          {active.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
        </select>
      </label>
      <label className="space-y-1 text-sm font-medium">De<DateInput value={from} onChange={(v) => { if (v) set({ de: v }) }} /></label>
      <label className="space-y-1 text-sm font-medium">Até<DateInput value={to} onChange={(v) => { if (v) set({ ate: v }) }} /></label>
    </div>
  )

  let body
  if (accounts.isPending || (accountId && query.isPending)) body = <Skeleton className="h-64" />
  else if (accounts.isError || query.isError) body = <ErrorState title="Não foi possível carregar a conciliação." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => { void accounts.refetch(); void query.refetch() }} />
  else if (!accountId) body = <EmptyState title="Cadastre uma conta bancária para conciliar." action={<Link to={'/app/financeiro/contas' as never} className={primary}>Ir para Contas e caixas</Link>} />
  else {
    const v = query.data!
    const t = v.totals
    const statementLink = `/app/financeiro/contas/${v.accountId}/extrato`
    const nothing = v.pairs.length === 0 && v.bankOnly.length === 0 && v.systemOnly.length === 0
    body = !v.hasStatement
      ? <EmptyState title="Importe o extrato para conciliar." action={<Link to={statementLink as never} search={{ aba: 'extrato-banco' } as never} className={primary}>Abrir o extrato do banco</Link>} />
      : (
        <div className="space-y-5">
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-md border border-border bg-card px-3 py-2 text-sm tabular-nums" aria-label="Totais">
            <span>Pendentes <b>{t.pending}</b></span><span>Conciliadas <b>{t.reconciled}</b></span><span>Ignoradas <b>{t.ignored}</b></span>
            {t.balanceDate ? (
              <span className={t.difference === 0 ? '' : 'font-semibold text-warn-ink'}>Banco <Money value={t.bankBalance ?? 0} /> · sistema <Money value={t.systemBalance ?? 0} /> · diferença <Money value={t.difference ?? 0} /> em {formatDate(t.balanceDate)}</span>
            ) : <span className="text-muted-foreground">Sem saldo do banco no período</span>}
          </div>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="flex flex-wrap items-center gap-2">
              {v.reconciledUntil ? <StatusChip tone="ok">Conciliada até {fullDate(v.reconciledUntil)}</StatusChip> : null}
              {v.closedUntil ? <StatusChip tone="info">Fechada até {fullDate(v.closedUntil)}</StatusChip> : null}
            </span>
            <span className="flex flex-wrap gap-2">
              {canEdit ? <AcceptHighDialog view={v} onDone={(n) => refresh(`${n} ${n === 1 ? 'linha conciliada' : 'linhas conciliadas'}`)} /> : null}
              {canApprove && v.reconciledUntil && (!v.closedUntil || v.reconciledUntil > v.closedUntil) ? (
                <CloseDialog view={v} onDone={async (until) => {
                  await refresh()
                  const p = nextPeriod(until)
                  setNext([{ label: `Conciliar a partir de ${formatDate(p.from)}`, onSelect: () => set({ de: p.from, ate: p.to }) }])
                }} />
              ) : null}
              {canApprove && v.reconciledUntil ? <Dica nome="Fechar conciliação" texto={CLOSE_HINT} /> : null}
            </span>
          </div>
          {next.length ? <NextSteps onDismiss={() => setNext([])} steps={next} /> : null}
          {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{error}</p> : null}
          {v.counterpartyMasked ? <p className="text-xs text-muted-foreground">A contraparte aparece mascarada: o nome e o documento inteiros ficam com quem edita o financeiro.</p> : null}
          {nothing ? <EmptyState title="Nada a conciliar neste período." action={<button type="button" onClick={() => { const d = new Date(Date.parse(from + 'T00:00:00Z') - 86_400_000).toISOString().slice(0, 10); set({ de: monthStart(d), ate: monthEnd(d) }) }} className={secondary}>Ver mês anterior</button>} /> : null}
          {v.pairs.length ? (
            <Section title="Pares sugeridos" count={v.pairs.length} hint={CONFIDENCE_HINT}>
              <ul className="space-y-2">{v.pairs.map((p) => <PairRow key={p.line.id} pair={p} canEdit={canEdit} onAccept={accept}
                manual={<ManualDialog accountId={v.accountId} line={p.line} onDone={() => refresh()} trigger={<button type="button" disabled={p.line.blocked} className={link}>Escolher outra</button>} />} />)}</ul>
            </Section>
          ) : null}
          {v.bankOnly.length ? (
            <Section title="Sem par no banco" count={v.bankOnly.length}>
              <ul className="grid gap-2 lg:grid-cols-2">{v.bankOnly.map((u) => <BankOnlyRow key={u.line.id} item={u} view={v} canEdit={canEdit} canPay={canPay} canApprove={canApprove} onChanged={refresh} />)}</ul>
            </Section>
          ) : null}
          {v.systemOnly.length ? (
            <Section title="No sistema, não no banco" count={v.systemOnly.length}>
              <ul className="grid gap-2 lg:grid-cols-2">{v.systemOnly.map((u) => <SystemOnlyRow key={u.transaction.id} item={u} view={v} canEdit={canEdit} onChanged={() => refresh()} />)}</ul>
            </Section>
          ) : null}
          {v.done.length ? (
            <Section title="Conciliadas" count={v.done.length}>
              <ul className="space-y-2">{v.done.map((d) => <DoneRow key={d.id} done={d} view={v} canEdit={canEdit} canApprove={canApprove} onChanged={() => refresh()} />)}</ul>
            </Section>
          ) : null}
        </div>
      )
  }

  return (
    <>
      {header}
      <FinanceTabs />
      {filters}
      {body}
    </>
  )
}
