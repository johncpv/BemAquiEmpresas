import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import {
  calculateTermination, cancelTermination, getDownloadTerminationTermUrl, getGetEmployeeQueryKey, getGetEmployeeTerminationQueryKey, simulateTermination, useGetEmployeeTermination,
} from '@/api/generated/bemaqui'
import type { EmployeeDetail, TerminationItem, TerminationResultView, TerminationView } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { type Step, NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatMoney } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { NOTICE_LABELS, STATUS_TONES, TERMINATION_DICAS, type TerminationDraft, terminationBody, terminationDraftError } from './termination'

const button = 'min-h-11 rounded-md border border-border px-3 text-sm font-semibold'

const downloadTerm = (t: { id: string; terminationDate: string }) =>
  apiDownload(getDownloadTerminationTermUrl(t.id), `termo-rescisao-conferencia-${t.terminationDate}.pdf`).catch((e: unknown) => notifyError(e instanceof Error ? e.message : 'Não foi possível gerar o termo.'))

/** O resultado (CA 1 e 3): o líquido, o FGTS, a multa e o custo total em cartões; as verbas e os descontos com a memória. */
function ResultView({ r }: { r: TerminationResultView }) {
  const cards = [
    { label: 'Líquido do funcionário', value: r.net, strong: true },
    { label: 'Proventos', value: r.gross },
    { label: 'Descontos', value: r.deductions },
    { label: 'FGTS do mês e rescisório', value: r.fgtsMonth + r.fgtsTermination },
    { label: 'Multa do FGTS', value: r.fine, dica: TERMINATION_DICAS.multa },
    { label: 'Encargos da empresa', value: r.employerCharges },
  ]
  return (
    <section aria-labelledby="rescisao-resultado" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="rescisao-resultado" className="text-base font-bold">{r.kindLabel} em {formatDate(r.terminationDate)}</h3>
        <p className="text-sm text-muted-foreground">Pagamento até <span className="tabular-nums font-semibold">{formatDate(r.paymentDeadline)}</span></p>
      </div>
      <div className="rounded-md border border-primary-soft bg-primary-soft p-3">
        <p className="text-sm">Custo total para a empresa</p>
        <p className="text-2xl font-bold tabular-nums">{formatMoney(r.totalCost)}</p>
        <p className="text-xs text-muted-foreground">Proventos + FGTS + multa do FGTS + encargos da empresa.</p>
      </div>
      <ul className="grid gap-2 sm:grid-cols-3">
        {cards.map((c) => (
          <li key={c.label} className="rounded-md border border-border bg-card p-3">
            <p className="inline-flex items-center text-sm text-muted-foreground">{c.label}{c.dica ? <Dica nome={c.label} texto={c.dica} /> : null}</p>
            <p className={`tabular-nums ${c.strong ? 'text-lg font-bold' : 'font-semibold'}`}>{formatMoney(c.value)}</p>
          </li>
        ))}
      </ul>
      <p className="inline-flex flex-wrap items-center text-sm text-muted-foreground">
        Aviso prévio: {r.noticeDays} dias; {r.paidNoticeDays > 0 ? `${r.paidNoticeDays.toLocaleString('pt-BR')} dias indenizados, projeção até ${formatDate(r.projectedEnd)}` : 'nenhum dia indenizado'}
        <Dica nome="Aviso prévio indenizado" texto={TERMINATION_DICAS.avisoIndenizado} />
      </p>
      {r.warnings.map((w) => <p key={w} role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">{w}</p>)}
      <ul className="space-y-2">
        {r.lines.map((l) => (
          <li key={`${l.code}-${l.name}`} className="rounded-md border border-border bg-card p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="font-semibold">{l.name}{l.reference ? <span className="ml-1 text-sm font-normal text-muted-foreground tabular-nums">· {l.reference}</span> : null}</p>
              <p className={`tabular-nums font-semibold ${l.type === 'Deduction' ? 'text-crit-ink' : l.type === 'Information' ? 'text-muted-foreground' : ''}`}>
                {l.type === 'Deduction' ? '− ' : ''}{formatMoney(l.amount)}{l.type === 'Information' ? ' (informativo)' : ''}
              </p>
            </div>
            {l.memory.length > 0 ? (
              <details className="mt-1 text-sm">
                <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Memória de cálculo</summary>
                <ul className="mt-1 space-y-0.5 text-xs text-muted-foreground">{l.memory.map((m) => <li key={m}>{m}</li>)}</ul>
              </details>
            ) : null}
          </li>
        ))}
      </ul>
    </section>
  )
}

/** O formulário (CA 1 e 3): o tipo da matriz, a data (hoje na simulação; a do desligamento no cálculo), o aviso mais comum do tipo e o FGTS. */
function TerminationForm({ data, calculate, onResult }: { data: TerminationView; calculate: boolean; onResult: (r: TerminationResultView, steps: Step[]) => void }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const saved = data.termination
  const first = data.kinds[0]
  const initial = (): TerminationDraft => ({
    kind: saved?.kind ?? first?.kind ?? '', terminationDate: data.terminationDate ?? data.suggestedDate, noticeKind: saved?.noticeKind ?? first?.defaultNotice ?? '',
    contractEndDate: saved?.contractEndDate ?? '', fgtsBalance: saved?.fgtsBalance != null ? String(saved.fgtsBalance).replace('.', ',') : '', fgtsStatementDate: saved?.fgtsStatementDate ?? '',
  })
  const [draft, setDraft] = useState<TerminationDraft>(initial)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const option = data.kinds.find((k) => k.kind === draft.kind)
  const needsContractEnd = option?.needsContractEnd ?? false
  const hasFine = (option?.fgtsFinePct ?? 0) > 0
  const submit = async () => {
    const local = terminationDraftError(draft, { needsContractEnd, hasFine, calculate })
    if (local) { setError(local); return }
    setBusy(true)
    setError(null)
    try {
      if (calculate) {
        const done = await calculateTermination(data.employeeId, terminationBody(draft, needsContractEnd), idempotency.options)
        idempotency.renew()
        notifySuccess(done.message)
        await client.invalidateQueries({ queryKey: getGetEmployeeTerminationQueryKey(data.employeeId) })
        onResult(done.result, [
          { label: 'Gerar termo para conferência', onSelect: () => void downloadTerm(done.termination) },
          { label: done.run.isSimulation ? 'Ver folha de rescisão' : 'Fechar folha de rescisão', onSelect: () => void navigate({ to: '/app/rh/folha/$id', params: { id: done.run.id }, search: { aba: done.run.isSimulation ? 'holerites' : 'fechamento' } as never }) },
        ])
      } else {
        onResult(await simulateTermination(data.employeeId, terminationBody(draft, needsContractEnd)), [])
      }
    } catch (e) {
      setError(e instanceof ApiError ? e.message : calculate ? 'Não foi possível calcular a rescisão.' : 'Não foi possível simular a rescisão.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <form className="space-y-3 rounded-md border border-border bg-card p-4" onSubmit={(e) => { e.preventDefault(); void submit() }}>
      <FormError message={error ?? undefined} />
      {/* Grade de 12 trilhas (ADR 0044): Tipo 6 · Data 3 / Aviso 4 · Fim do contrato 3 / Saldo do FGTS 3 · Data do extrato 3 */}
      <FormGrid>
        <Field label="Tipo do desligamento" required size="lg">
          {(f) => (
            <select {...f} value={draft.kind} className={inputClass}
              onChange={(e) => setDraft({ ...draft, kind: e.target.value, noticeKind: data.kinds.find((k) => k.kind === e.target.value)?.defaultNotice ?? draft.noticeKind })}>
              {data.kinds.map((k) => <option key={k.kind} value={k.kind}>{k.label}</option>)}
            </select>
          )}
        </Field>
        <Field label={calculate ? 'Data do desligamento' : 'Data prevista'} required size="sm" hint={calculate ? 'A data registrada no desligamento.' : 'Vem com hoje.'}>
          {(f) => <DateInput {...f} value={draft.terminationDate} disabled={calculate} onChange={(v) => setDraft({ ...draft, terminationDate: v })} />}
        </Field>
        <Field label="Aviso prévio" size="md" dica={TERMINATION_DICAS.avisoIndenizado} hint={`${data.noticeDays} dias pelo tempo de casa (30 + 3 por ano completo, até 90).`}>
          {(f) => (
            <select {...f} value={draft.noticeKind} onChange={(e) => setDraft({ ...draft, noticeKind: e.target.value })} className={inputClass}>
              {Object.entries(NOTICE_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          )}
        </Field>
        {needsContractEnd ? (
          <Field label="Fim previsto do contrato de experiência" required size="sm">
            {(f) => <DateInput {...f} value={draft.contractEndDate} onChange={(v) => setDraft({ ...draft, contractEndDate: v })} />}
          </Field>
        ) : null}
        <Field label="Saldo do FGTS" size="sm" required={calculate && hasFine} dica={TERMINATION_DICAS.saldoFgts} hint={hasFine ? `Multa de ${option?.fgtsFinePct}% neste tipo.` : 'Sem multa neste tipo.'}>
          {(f) => <input {...f} inputMode="decimal" value={draft.fgtsBalance} onChange={(e) => setDraft({ ...draft, fgtsBalance: e.target.value.replace(/[^0-9,.]/g, '') })} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Data do extrato do FGTS" size="sm" required={draft.fgtsBalance.trim() !== ''}>
          {(f) => <DateInput {...f} value={draft.fgtsStatementDate} onChange={(v) => setDraft({ ...draft, fgtsStatementDate: v })} />}
        </Field>
      </FormGrid>
      <button type="submit" disabled={busy} className={button + ' bg-primary text-primary-foreground'}>
        {busy ? (calculate ? 'Calculando…' : 'Simulando…') : calculate ? (saved ? 'Recalcular rescisão' : 'Calcular rescisão') : 'Simular rescisão'}
      </button>
    </form>
  )
}

/** A rescisão calculada (CA 7, 8 e 10): a situação, a simulação, o prazo e as ações (termo, folha e estorno do desligamento). */
function TerminationCard({ t, employeeId, canEdit }: { t: TerminationItem; employeeId: string; canEdit: boolean }) {
  const client = useQueryClient()
  const [reason, setReason] = useState('')
  return (
    <section aria-labelledby="rescisao-situacao" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3">
      <div>
        <h3 id="rescisao-situacao" className="font-semibold">{t.kindLabel} · aviso {t.noticeLabel.toLowerCase()} ({t.noticeDays} dias)</h3>
        <p className="text-sm text-muted-foreground">Desligamento em <span className="tabular-nums">{formatDate(t.terminationDate)}</span> · pagamento até <span className="tabular-nums">{formatDate(t.paymentDeadline)}</span></p>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <StatusChip tone={STATUS_TONES[t.status] ?? 'neutral'}>{t.statusLabel}</StatusChip>
        {t.isSimulation ? <StatusChip tone="warn">Simulação</StatusChip> : null}
        {t.payrollRunId ? <button type="button" className={button} onClick={() => void downloadTerm(t)}>Gerar termo para conferência</button> : null}
        {t.payrollRunId ? <Link to="/app/rh/folha/$id" params={{ id: t.payrollRunId }} className="text-sm font-semibold text-primary">Ver folha de rescisão</Link> : null}
        {canEdit && t.status !== 'Closed' ? (
          <ConfirmDialog verb="Estornar desligamento" done="Desligamento estornado" destructive title="Estornar o desligamento?" onOpen={() => setReason('')}
            description="O funcionário volta a ativo e a folha de rescisão em rascunho sai. Com a folha fechada, reabra antes; com o pagamento feito, fale com a contadora."
            onConfirm={async () => {
              if (reason.trim().length < 5) throw new DialogMessage('Informe o motivo do estorno, de 5 a 200 caracteres.')
              await cancelTermination(t.id, { reason: reason.trim(), version: t.version })
              await client.invalidateQueries({ queryKey: getGetEmployeeTerminationQueryKey(employeeId) })
              await client.invalidateQueries({ queryKey: getGetEmployeeQueryKey(employeeId) })
            }}
            trigger={<button type="button" className={button + ' text-crit-ink'}>Estornar desligamento</button>}>
            <Field label="Motivo" required>{(f) => <input {...f} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
          </ConfirmDialog>
        ) : null}
      </div>
    </section>
  )
}

/** Aba "Rescisão" do funcionário (/app/rh/funcionarios/$id?aba=rescisao, FLH-05): simular antes, calcular depois do desligamento. */
export function EmployeeTermination({ employee, canEdit }: { employee: EmployeeDetail; canEdit: boolean }) {
  const query = useGetEmployeeTermination(employee.id)
  const canSensitive = useCan('hr.view_sensitive')
  const [result, setResult] = useState<TerminationResultView | null>(null)
  const [steps, setSteps] = useState<Step[]>([])
  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-24" /><Skeleton className="h-48" /></div>
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar a rescisão." message="Confira a conexão e tente de novo." onRetry={() => query.refetch()} />
  const data = query.data
  const t = data.termination
  const canCalculate = canEdit && canSensitive && t?.status !== 'Closed'
  return (
    <div className="max-w-4xl space-y-5">
      {steps.length > 0 ? <NextSteps steps={steps} onDismiss={() => setSteps([])} /> : null}
      {t ? <TerminationCard t={t} employeeId={employee.id} canEdit={canEdit} /> : null}
      <section aria-labelledby="rescisao-form" className="space-y-2">
        <h2 id="rescisao-form" className="text-lg font-bold">{data.terminated ? 'Calcular rescisão' : 'Simular rescisão'}</h2>
        <p className="text-sm text-muted-foreground">
          {data.terminated
            ? 'Com o desligamento registrado e o espelho de ponto do mês fechado, o cálculo gera a folha de rescisão com a memória de cada verba.'
            : 'Veja as verbas, o líquido e o custo total para a empresa antes de decidir. A simulação não grava nada.'}
        </p>
        {!canSensitive ? <EmptyState title="Sem acesso aos valores da folha." description="A simulação e o cálculo pedem a permissão de dados sensíveis do RH." />
          : canCalculate || (!data.terminated && canEdit)
            ? <TerminationForm key={t?.version ?? 0} data={data} calculate={data.terminated} onResult={(r, s) => { setResult(r); setSteps(s) }} />
            : null}
      </section>
      {result ? <ResultView r={result} /> : canSensitive ? <EmptyState title="Escolha o tipo e a data para simular." description={data.terminated ? 'Informe o saldo do FGTS do extrato e clique em "Calcular rescisão".' : 'Clique em "Simular rescisão" para ver as verbas e o custo total.'} /> : null}
    </div>
  )
}
