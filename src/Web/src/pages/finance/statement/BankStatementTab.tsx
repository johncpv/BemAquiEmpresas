import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { CheckCircle2, ChevronDown, Loader2, XCircle } from 'lucide-react'
import { Fragment, useEffect, useRef, useState } from 'react'
import {
  fetchBankStatement, getGetBankStatementLinesQueryKey, grantOfxContingency, ignoreBankStatementLine, importBankStatementOfx, undoBankStatementBatch, useGetBankStatementLines,
} from '@/api/generated/bemaqui'
import type { BankStatementBatchView, BankStatementImportResult, BankStatementLineView, BankStatementView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { DateInput } from '@/components/form/inputs'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifySuccess } from '@/components/ui/messages'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today } from '../receivables/receivables'
import {
  contingencyMax, dayMonth, FETCH_PHASE_TEXT, type FetchPhase, importSummary, isBusy, LINE_KIND, LINE_STATUS, reconcileLabel, SOURCE_LABEL, TRIGGER_LABEL,
} from './bank-statement'

const button = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md px-3 py-2 text-sm font-semibold disabled:opacity-50'
const primary = `${button} bg-primary text-primary-foreground`
const secondary = `${button} border border-border bg-card`
const WAITING_AFTER_MS = 3_000
const SLOW_AFTER_MS = 30_000
const NOT_VERIFIED = 'Linha de um arquivo OFX importado como contingência numa conta com API: não veio do banco pela API e precisa de conferência na conciliação.'

/** "Buscar agora" no padrão 6: Enviado, Aguardando, Demorando (30 s), Confirmado ou Recusado com motivo e ação, sem reenvio. */
function useFetch(accountId: string, onDone: (r: BankStatementImportResult) => Promise<void>) {
  const [phase, setPhase] = useState<FetchPhase>('idle')
  const [refusal, setRefusal] = useState<string | null>(null)
  const [result, setResult] = useState<BankStatementImportResult | null>(null)
  const timers = useRef<number[]>([])
  useEffect(() => () => timers.current.forEach((t) => window.clearTimeout(t)), [])
  const run = async () => {
    if (isBusy(phase)) return
    setPhase('sent')
    setRefusal(null)
    timers.current = [window.setTimeout(() => setPhase((p) => (p === 'sent' ? 'waiting' : p)), WAITING_AFTER_MS), window.setTimeout(() => setPhase((p) => (isBusy(p) ? 'slow' : p)), SLOW_AFTER_MS)]
    try {
      const r = await fetchBankStatement(accountId)
      setResult(r)
      setPhase('confirmed')
      notifySuccess('Extrato atualizado')
      await onDone(r)
    } catch (e) {
      setRefusal(e instanceof ApiError ? e.message : 'Não foi possível buscar o extrato. Tente de novo em alguns minutos.')
      setPhase('refused')
    } finally {
      timers.current.forEach((t) => window.clearTimeout(t))
    }
  }

  return { phase, refusal, result, run }
}

function FetchStatus({ phase, refusal, result }: { phase: FetchPhase; refusal: string | null; result: BankStatementImportResult | null }) {
  if (phase === 'idle') return null
  if (isBusy(phase)) return <p role="status" className="flex items-center gap-2 text-sm"><Loader2 aria-hidden="true" className="size-4 animate-spin" />{FETCH_PHASE_TEXT[phase as 'sent']}</p>
  if (phase === 'confirmed' && result) return <p role="status" className="flex items-center gap-2 text-sm font-semibold text-ok-ink"><CheckCircle2 aria-hidden="true" className="size-4" />Extrato atualizado: {importSummary(result)}.</p>
  return <p role="alert" className="flex items-start gap-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink"><XCircle aria-hidden="true" className="mt-0.5 size-4 shrink-0" />Recusado: {refusal}</p>
}

function ImportOfx({ accountId, contingency, onDone, trigger }: { accountId: string; contingency: boolean; onDone: (r: BankStatementImportResult) => Promise<void>; trigger: React.ReactNode }) {
  const [file, setFile] = useState<File | null>(null)
  return (
    <ConfirmDialog verb="Importar extrato" done="Extrato importado" title="Importar extrato OFX" size="lg" onOpen={() => setFile(null)}
      description={contingency ? 'Contingência: as linhas entram marcadas "Não verificada pelo banco". As que já vieram pela API são ignoradas.' : 'O arquivo passa pelo antivírus, a conta do arquivo é conferida com esta e os lançamentos já importados são ignorados.'}
      preconditions={[{ ok: !!file, text: 'Arquivo .ofx escolhido (até 5 MB).' }]}
      onConfirm={async () => {
        if (!file) throw new DialogMessage('Escolha o arquivo OFX.')
        await onDone(await importBankStatementOfx(accountId, { file }))
      }}
      trigger={trigger}>
      <label className="mt-3 block space-y-1 text-sm font-medium">
        <span className="inline-flex items-center">Arquivo OFX<Dica nome="OFX" texto="Formato de extrato que todo internet banking exporta (às vezes chamado Money ou Microsoft Money). Escolha a conta e o período e baixe o arquivo .ofx." /></span>
        <input type="file" accept=".ofx" aria-label="Arquivo OFX" onChange={(e) => setFile(e.target.files?.[0] ?? null)} className={inputClass} />
      </label>
    </ConfirmDialog>
  )
}

function GrantContingency({ accountId, onDone }: { accountId: string; onDone: () => Promise<void> }) {
  const limit = contingencyMax(today())
  const [reason, setReason] = useState('')
  const [until, setUntil] = useState(limit)
  const [done, setDone] = useState('OFX liberado')
  return (
    <ConfirmDialog verb="Liberar OFX nesta conta" done={done} title="Liberar OFX nesta conta?" size="lg" onOpen={() => { setReason(''); setUntil(limit) }}
      description="Use só quando o extrato do Inter não chegar pela API. As linhas do arquivo entram como não verificadas pelo banco e o Proprietário é avisado."
      preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo escrito.' }, { ok: !!until && until >= today() && until <= limit, text: `Prazo até ${dayMonth(limit)} (30 dias).` }]}
      onConfirm={async () => {
        const granted = await grantOfxContingency(accountId, { reason: reason.trim(), validUntil: until })
        setDone(`OFX liberado até ${dayMonth(granted.validUntil)}`)
        await onDone()
      }}
      trigger={<button type="button" className={secondary}>Liberar OFX nesta conta</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
      <label className="mt-3 block space-y-1 text-sm font-medium">Liberado até<DateInput value={until} onChange={(v) => setUntil(v ?? '')} /></label>
    </ConfirmDialog>
  )
}

function IgnoreLine({ accountId, line, canApprove, onDone }: { accountId: string; line: BankStatementLineView; canApprove: boolean; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  const debit = line.amount < 0
  return (
    <ConfirmDialog verb="Ignorar linha" done="Linha ignorada" title="Ignorar esta linha?" onOpen={() => setReason('')}
      description="A linha não é apagada: fica como Ignorada, com o motivo, e sai da conciliação."
      preconditions={[{ ok: !debit || canApprove, text: 'Linha de saída: só quem aprova em Caixa e pagamentos pode ignorar.' }, { ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
      onConfirm={async () => { await ignoreBankStatementLine(accountId, line.id, { reason: reason.trim(), version: line.version }); await onDone() }}
      trigger={<button type="button" className="min-h-11 px-2 text-xs font-semibold text-primary hover:underline">Ignorar</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}

function UndoBatch({ accountId, batch, onDone }: { accountId: string; batch: BankStatementBatchView; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  const period = batch.periodFrom && batch.periodTo ? `${formatDate(batch.periodFrom)} a ${formatDate(batch.periodTo)}` : '—'
  return (
    <ConfirmDialog destructive verb="Desfazer importação" done="Importação desfeita" title="Desfazer esta importação?" size="lg" onOpen={() => setReason('')}
      description="As linhas do lote saem do extrato do banco (continuam no histórico) e o arquivo pode ser importado de novo."
      impact={<ul className="list-disc space-y-1 pl-5"><li>{batch.newCount} {batch.newCount === 1 ? 'linha sai' : 'linhas saem'} do extrato</li><li>Período {period}</li><li>{TRIGGER_LABEL[batch.trigger] ?? batch.trigger}{batch.fileName ? `: ${batch.fileName}` : ''}</li></ul>}
      preconditions={[{ ok: batch.reconciledCount === 0, text: 'Nenhuma linha do lote conciliada.' }, { ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
      onConfirm={async () => { await undoBankStatementBatch(accountId, batch.id, { reason: reason.trim(), version: batch.version }); await onDone() }}
      trigger={<button type="button" className="min-h-11 px-2 text-xs font-semibold text-crit-ink hover:underline">Desfazer importação</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}

function Lines({ view, canEdit, canApprove, onChanged }: { view: BankStatementView; canEdit: boolean; canApprove: boolean; onChanged: () => Promise<void> }) {
  const [open, setOpen] = useState<string | null>(null)
  return (
    <div className="rounded-md border border-border bg-card">
      <table className="w-full text-sm" aria-label="Extrato do banco">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr><th className="px-3 py-2 text-left">Data</th><th className="hidden px-3 py-2 text-left sm:table-cell">Descrição</th><th className="hidden px-3 py-2 text-left md:table-cell">Contraparte</th><th className="hidden px-3 py-2 text-left sm:table-cell">Origem</th><th className="px-3 py-2 text-right">Valor</th><th className="px-3 py-2 text-left">Situação</th><th className="px-1 py-2"><span className="sr-only">Ações</span></th></tr>
        </thead>
        <tbody>
          {view.lines.map((l) => {
            const status = LINE_STATUS[l.status] ?? { label: l.status, tone: 'neutral' as const }
            const counterparty = [l.counterpartyName, l.counterpartyDocument].filter(Boolean).join(' · ')
            return (
              <Fragment key={l.id}>
                <tr className="border-t border-border">
                  <td className="px-3 py-2 tabular-nums">{formatDate(l.date)}</td>
                  <td className="hidden px-3 py-2 sm:table-cell"><span className="inline-flex flex-wrap items-center gap-1.5">{l.description}<Tag>{LINE_KIND[l.kind] ?? l.kind}</Tag></span></td>
                  <td className="hidden px-3 py-2 md:table-cell">{counterparty || '—'}</td>
                  <td className="hidden px-3 py-2 sm:table-cell"><span className="inline-flex flex-wrap items-center gap-1.5">{SOURCE_LABEL[l.source] ?? l.source}{l.bankVerified ? null : <StatusChip tone="warn">Não verificada pelo banco</StatusChip>}</span></td>
                  <td className="px-3 py-2 text-right"><Money value={l.amount} className={l.amount < 0 ? 'text-crit-ink' : 'text-ok-ink'} /></td>
                  <td className="px-3 py-2"><StatusChip tone={status.tone}>{status.label}</StatusChip></td>
                  <td className="px-1 py-1 text-right whitespace-nowrap">
                    {canEdit && l.status === 'Pending' ? <IgnoreLine accountId={view.accountId} line={l} canApprove={canApprove} onDone={onChanged} /> : null}
                    <button type="button" aria-expanded={open === l.id} aria-label={`Detalhes da linha de ${formatDate(l.date)}`} onClick={() => setOpen(open === l.id ? null : l.id)} className="inline-flex size-11 items-center justify-center sm:hidden"><ChevronDown aria-hidden="true" className="size-4" /></button>
                  </td>
                </tr>
                {open === l.id ? (
                  <tr className="sm:hidden"><td colSpan={7} className="space-y-1 bg-muted/50 px-3 py-2 text-sm">
                    <p>{l.description} · {LINE_KIND[l.kind] ?? l.kind}</p>
                    {counterparty ? <p className="text-muted-foreground">{counterparty}</p> : null}
                    <p className="flex flex-wrap items-center gap-1.5">{SOURCE_LABEL[l.source] ?? l.source}{l.bankVerified ? null : <StatusChip tone="warn">Não verificada pelo banco</StatusChip>}</p>
                    {l.ignoreReason ? <p>Motivo: {l.ignoreReason}</p> : null}
                  </td></tr>
                ) : null}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/**
 * Aba "Extrato do banco" da conta (BNK-03a CA 8): linhas do banco por data, origem e situação, saldo do banco contra o do sistema,
 * "Buscar agora" (Inter), "Importar OFX" (sem API, ou com a contingência), "Liberar OFX nesta conta" (com API, finance.approve),
 * ignorar linha e desfazer importação. Período e situação ficam na URL.
 */
export function BankStatementTab({ accountId, from, to }: { accountId: string; from: string; to: string }) {
  const search = useSearch({ strict: false }) as { situacao?: string; page?: number }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canEdit = useCan('finance.edit')
  const canApprove = useCan('finance.approve')
  const status = search.situacao
  const query = useGetBankStatementLines(accountId, { from, to, status, pageSize: 200 })
  const [next, setNext] = useState<{ label: string; onSelect: () => void }[]>([])
  const set = (patch: Record<string, unknown>) => void navigate({ to: '.', search: (p: Record<string, unknown>) => ({ ...p, ...patch }), replace: true } as never)
  const refresh = () => client.invalidateQueries({ queryKey: getGetBankStatementLinesQueryKey(accountId) })
  const imported = async (r: BankStatementImportResult) => {
    await refresh()
    const label = reconcileLabel(r.pendingCount)
    setNext(label ? [{ label, onSelect: () => set({ situacao: 'Pending' }) }] : [])
  }

  const fetcher = useFetch(accountId, imported)

  if (query.isPending) return <Skeleton className="h-64" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o extrato do banco." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} />
  const v = query.data
  const importButton = <button type="button" className={v.apiConnected ? secondary : primary}>Importar OFX</button>
  const actions = (
    <span className="flex flex-wrap gap-2">
      {canEdit && v.apiConnected ? <button type="button" onClick={() => void fetcher.run()} disabled={isBusy(fetcher.phase) || !!v.nextFetchAt} className={primary}>Buscar agora</button> : null}
      {canEdit && v.ofxAllowed ? <ImportOfx accountId={accountId} contingency={v.apiConnected} onDone={imported} trigger={importButton} /> : null}
      {canApprove && v.apiConnected && !v.contingency ? <GrantContingency accountId={accountId} onDone={refresh} /> : null}
    </span>
  )

  return (
    <div className="space-y-3">
      <EnvironmentBanner service="bank" test={v.environment === 'Sandbox'} message="Ambiente de teste do banco: o extrato vem do sandbox do Inter." />
      {v.contingency ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm font-semibold text-warn-ink">OFX liberado nesta conta até {formatDate(v.contingency.validUntil)}. As linhas não são verificadas pelo banco.</p> : null}
      {v.latest ? (
        <p className={`flex flex-wrap items-center gap-x-2 rounded-md border px-3 py-2 text-sm tabular-nums ${v.latest.difference === 0 ? 'border-border bg-card' : 'border-warn-soft bg-warn-soft text-warn-ink'}`}>
          <span>Saldo no banco <Money value={v.latest.bankBalance} className="font-semibold" /></span><span aria-hidden="true">·</span>
          <span>no sistema <Money value={v.latest.systemBalance} className="font-semibold" /></span><span aria-hidden="true">·</span>
          <span className="inline-flex items-center">diferença <Money value={v.latest.difference} className="ml-1 font-semibold" /><Dica nome="Diferença de saldo" texto="Saldo que o banco informou no fim do dia menos o saldo do sistema no mesmo dia. Diferente de zero há mais de 2 dias, quem edita o financeiro é avisado: concilie as linhas pendentes." /></span>
          <span className="text-muted-foreground">em {formatDate(v.latest.date)}</span>
        </p>
      ) : null}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <label className="space-y-1 text-sm font-medium">Situação
          <select value={status ?? ''} onChange={(e) => set({ situacao: e.target.value || undefined })} className={inputClass}>
            <option value="">Todas</option><option value="Pending">Pendentes</option><option value="Reconciled">Conciliadas</option><option value="Ignored">Ignoradas</option>
          </select>
        </label>
        {actions}
      </div>
      <FetchStatus phase={fetcher.phase} refusal={fetcher.refusal} result={fetcher.result} />
      {v.nextFetchAt && canEdit && v.apiConnected ? <p className="text-xs text-muted-foreground">Próxima busca a partir de {formatDateTime(v.nextFetchAt)}.</p> : null}
      {next.length ? <NextSteps onDismiss={() => setNext([])} steps={next} /> : null}
      {v.counterpartyMasked ? <p className="text-xs text-muted-foreground">A contraparte aparece mascarada: o nome e o documento inteiros ficam com quem edita o financeiro.</p> : null}
      {v.total === 0
        ? <EmptyState title={status ? 'Nenhuma linha nessa situação no período.' : 'Nenhuma linha do banco ainda. Conecte o Inter ou importe um arquivo OFX.'} action={status ? <button type="button" onClick={() => set({ situacao: undefined })} className={secondary}>Ver todas</button> : (
            <span className="flex flex-wrap justify-center gap-2">
              {actions}
              {!v.apiConnected && canEdit ? <Link to={'/app/configuracoes/integracoes/inter' as never} className={secondary}>Conectar o Inter</Link> : null}
            </span>
          )} />
        : <Lines view={v} canEdit={canEdit} canApprove={canApprove} onChanged={refresh} />}
      {v.total > v.lines.length ? <p className="text-xs text-muted-foreground">Mostrando {v.lines.length} de {v.total} linhas. Diminua o período para ver o resto.</p> : null}
      {v.lines.some((l) => !l.bankVerified) ? <p className="inline-flex items-center text-xs text-muted-foreground">Não verificada pelo banco<Dica nome="Não verificada pelo banco" texto={NOT_VERIFIED} /></p> : null}
      {v.balances.length ? (
        <section aria-labelledby="saldo-banco" className="space-y-2">
          <h2 id="saldo-banco" className="text-sm font-semibold">Saldo do banco por dia</h2>
          <div className="overflow-x-auto rounded-md border border-border bg-card"><table className="w-full text-sm tabular-nums">
            <thead className="text-xs text-muted-foreground"><tr><th className="px-3 py-2 text-left">Dia</th><th className="px-3 py-2 text-right">Banco</th><th className="px-3 py-2 text-right">Sistema</th><th className="px-3 py-2 text-right">Diferença</th></tr></thead>
            <tbody>{v.balances.map((b) => <tr key={b.date} className="border-t border-border"><td className="px-3 py-2">{formatDate(b.date)}</td><td className="px-3 py-2 text-right"><Money value={b.bankBalance} /></td><td className="px-3 py-2 text-right"><Money value={b.systemBalance} /></td><td className="px-3 py-2 text-right"><Money value={b.difference} highlightNegative /></td></tr>)}</tbody>
          </table></div>
        </section>
      ) : null}
      {v.batches.length ? (
        <section aria-labelledby="importacoes" className="space-y-2">
          <h2 id="importacoes" className="text-sm font-semibold">Importações</h2>
          <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
            {v.batches.map((b) => (
              <li key={b.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
                <div className="min-w-0 space-y-0.5">
                  <p className="font-medium">{TRIGGER_LABEL[b.trigger] ?? b.trigger}{b.fileName ? ` · ${b.fileName}` : ''}{b.undoneAt ? <> · <StatusChip tone="neutral">Desfeita</StatusChip></> : null}</p>
                  <p className="text-xs text-muted-foreground">{formatDateTime(b.importedAt)} por {b.importedByName} · {importSummary(b)}{b.periodFrom && b.periodTo ? ` · ${formatDate(b.periodFrom)} a ${formatDate(b.periodTo)}` : ''}</p>
                  {b.undoReason ? <p className="text-xs">Desfeita por {b.undoneByName}: {b.undoReason}</p> : null}
                  {b.notes ? <p className="text-xs text-muted-foreground">{b.notes}</p> : null}
                </div>
                {canEdit && !b.undoneAt && b.reconciledCount === 0 && b.newCount > 0 ? <UndoBatch accountId={accountId} batch={b} onDone={refresh} /> : null}
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  )
}
