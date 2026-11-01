import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListPayrollTaxTablesQueryKey, simulatePayrollTaxTables, useListPayrollTaxTables } from '@/api/generated/bemaqui'
import type { PayrollTaxSimulation, PayrollTaxTableDetail, PayrollTaxTableSection } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { currentPeriod } from './payroll'
import { TaxTableEditor } from './TaxTableEditor'
import { formatDay, statusChip, TAX_DICAS, TAX_KINDS, type TaxKind } from './tax-tables'

type Editing = { table: PayrollTaxTableDetail | null; kind: TaxKind; prefill?: PayrollTaxTableDetail | null }

/**
 * RH → Tabelas da folha (FLH-01): uma seção por tipo com a vigente, as futuras e as antigas; chip "A confirmar com a contadora" em cada
 * versão não conferida, "Marcar como conferido", editor de faixas, retificação e o simulador. Cadastrar e editar com hr.edit e
 * hr.view_sensitive; conferir com hr.approve.
 */
export function PayrollTaxTablesPage() {
  const query = useListPayrollTaxTables()
  const client = useQueryClient()
  const hrEdit = useCan('hr.edit')
  const hrSensitive = useCan('hr.view_sensitive')
  const canEdit = hrEdit && hrSensitive
  const canApprove = useCan('hr.approve')
  const [editing, setEditing] = useState<Editing | null>(null)
  const [onlyPending, setOnlyPending] = useState(false)
  const refresh = () => void client.invalidateQueries({ queryKey: getListPayrollTaxTablesQueryKey() })
  const view = query.data
  const total = view?.sections.reduce((n, s) => n + s.versions.length, 0) ?? 0
  const newButton = canEdit ? <button type="button" onClick={() => setEditing({ table: null, kind: 'Inss' })} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Cadastrar tabela</button> : null

  return (
    <>
      <PageHeading title="RH: Tabelas da folha">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">INSS, IRRF, salário-família, salário mínimo, FGTS, vale-transporte e encargos, cada um com a data em que passa a valer.</p>
          {newButton}
        </div>
      </PageHeading>
      <HrTabs />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? (
        <ErrorState title="Não foi possível carregar as tabelas da folha." message="Verifique a conexão e tente de novo. Se continuar, fale com o administrador." onRetry={() => query.refetch()} />
      ) : !view ? null : total === 0 ? (
        <EmptyState title="Cadastre as tabelas vigentes antes de calcular a folha." description="Comece pelo INSS e pelo IRRF do ano, com a fonte de cada uma." action={newButton} />
      ) : (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
          <div className="space-y-4">
            {view.unconfirmed > 0 ? (
              <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
                <span className="flex items-center gap-1 font-semibold">
                  {view.unconfirmed} {view.unconfirmed === 1 ? 'tabela a confirmar' : 'tabelas a confirmar'} com a contadora
                  <Dica nome="A confirmar com a contadora" texto={TAX_DICAS.aConfirmar} />
                </span>
                <button type="button" aria-pressed={onlyPending} onClick={() => setOnlyPending(!onlyPending)} className="min-h-11 rounded-md border border-border bg-card px-2 py-1 text-xs font-semibold text-foreground sm:min-h-0">
                  {onlyPending ? 'Mostrar todas' : 'Só as a confirmar'}
                </button>
              </div>
            ) : null}
            {view.sections.map((section) => (
              <Section key={section.kind} section={section} onlyPending={onlyPending} canEdit={canEdit}
                onOpen={(table) => setEditing({ table, kind: section.kind as TaxKind })}
                onNew={() => setEditing({ table: null, kind: section.kind as TaxKind, prefill: section.versions.find((v) => v.id === section.inForceId) ?? section.versions[0] ?? null })} />
            ))}
          </div>
          <Simulator suggestedBase={view.suggestedBase ?? null} />
        </div>
      )}
      {editing ? (
        <TaxTableEditor table={editing.table} kind={editing.kind} prefill={editing.prefill ?? null} canEdit={canEdit} canApprove={canApprove} singleApprover={!!view?.singleApprover}
          onClose={() => setEditing(null)} onChanged={(table) => { refresh(); setEditing(table ? { table, kind: table.kind as TaxKind } : null) }} />
      ) : null}
    </>
  )
}

function Section({ section, onlyPending, canEdit, onOpen, onNew }: { section: PayrollTaxTableSection; onlyPending: boolean; canEdit: boolean; onOpen: (t: PayrollTaxTableDetail) => void; onNew: () => void }) {
  const meta = TAX_KINDS.find((k) => k.kind === section.kind)
  const versions = onlyPending ? section.versions.filter((v) => v.status === 'Unconfirmed') : section.versions
  if (onlyPending && versions.length === 0) return null
  return (
    <section aria-labelledby={`tabela-${section.kind}`} className="rounded-md border border-border">
      <header className="flex flex-wrap items-center justify-between gap-2 border-b border-border px-3 py-2">
        <div>
          <h2 id={`tabela-${section.kind}`} className="font-semibold">{section.label}</h2>
          <p className="text-xs text-muted-foreground">{meta?.description}</p>
        </div>
        {canEdit ? <button type="button" onClick={onNew} className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold sm:min-h-0">Nova versão</button> : null}
      </header>
      {versions.length === 0 ? (
        <p className="px-3 py-3 text-sm text-muted-foreground">{section.pendingNote ?? 'Nenhuma versão cadastrada.'} {canEdit ? 'Use "Nova versão" para cadastrar.' : ''}</p>
      ) : (
        <ul className="divide-y divide-border">
          {versions.map((v) => (
            <li key={v.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <button type="button" onClick={() => onOpen(v)} className="min-h-11 text-left font-semibold text-primary hover:underline sm:min-h-0">
                Vale a partir de <span className="tabular-nums">{formatDay(v.validFrom)}</span>{v.rectifiesId ? ' · retificação' : ''}
              </button>
              <span className="flex flex-wrap items-center gap-1.5">
                {v.inForceToday ? <StatusChip tone="info">Vigente</StatusChip> : null}
                {statusChip(v.status, v.isFuture, v.validFrom).map((c) => <StatusChip key={c.label} tone={c.tone}>{c.label}</StatusChip>)}
                {v.seedPackage ? <StatusChip tone="neutral">Pacote {v.seedPackage}</StatusChip> : null}
                {v.warnings.length > 0 ? <StatusChip tone="warn">Com aviso</StatusChip> : null}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/** Simulador (CA 10): base, dependentes e filhos na competência, pelas versões que o motor usaria, com a memória. */
function Simulator({ suggestedBase }: { suggestedBase: number | null }) {
  const [competence, setCompetence] = useState(currentPeriod())
  const [base, setBase] = useState<string | null>(suggestedBase === null ? null : String(suggestedBase))
  const [dependents, setDependents] = useState('0')
  const [children, setChildren] = useState('0')
  const [result, setResult] = useState<PayrollTaxSimulation | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const run = async () => {
    setBusy(true)
    setError(null)
    try {
      setResult(await simulatePayrollTaxTables({ competence, base: Number(base ?? 0), dependents: Number(dependents || 0), children: Number(children || 0) }))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível simular. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  const inss = result?.lines.find((l) => l.kind === 'Inss')
  const irrf = result?.lines.find((l) => l.kind === 'Irrf')
  return (
    <section aria-labelledby="simulador" className="space-y-3 rounded-md border border-border p-3 lg:sticky lg:top-4 lg:self-start">
      <h2 id="simulador" className="font-semibold">Simulador</h2>
      <p className="text-xs text-muted-foreground">Mostra o que a folha calcularia com as tabelas da competência, conferidas ou não.{suggestedBase !== null ? ' A base vem com o salário médio da empresa.' : ''}</p>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Competência" hint="aaaa-mm">{(p) => <input {...p} value={competence} maxLength={7} onChange={(e) => setCompetence(e.target.value)} className={inputClass + ' tabular-nums'} />}</Field>
        <Field label="Base">{(p) => <MoneyInput {...p} value={base} onChange={setBase} />}</Field>
        <Field label="Dependentes" hint="IRRF">{(p) => <input {...p} inputMode="numeric" value={dependents} maxLength={2} onChange={(e) => setDependents(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}</Field>
        <Field label="Filhos" hint="Salário-família">{(p) => <input {...p} inputMode="numeric" value={children} maxLength={2} onChange={(e) => setChildren(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}</Field>
      </div>
      <button type="button" disabled={busy} onClick={() => void run()} className="min-h-11 w-full rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Simular</button>
      <FormError message={error ?? undefined} />
      {result ? (
        <div aria-live="polite" className="space-y-2 text-sm">
          <p className="font-semibold tabular-nums">
            Base <Money value={result.base} /> → INSS {inss ? <Money value={inss.amount} /> : '—'}, IRRF {irrf ? <Money value={irrf.amount} /> : '—'}
          </p>
          {result.missing.map((m) => <p key={m} className="text-warn-ink">{m}</p>)}
          <ul className="space-y-2">
            {result.lines.map((l) => (
              <li key={l.kind} className="rounded-md bg-muted px-2 py-1.5">
                <div className="flex flex-wrap items-center justify-between gap-1">
                  <span className="font-semibold">{l.label}</span>
                  <span className="tabular-nums"><Money value={l.amount} /></span>
                </div>
                <p className="text-xs text-muted-foreground">Tabela de {formatDay(l.validFrom)}{l.status === 'Unconfirmed' ? ', a confirmar com a contadora' : ''}</p>
                <details className="text-xs">
                  <summary className="cursor-pointer text-muted-foreground">Memória do cálculo</summary>
                  <ul className="mt-1 list-disc pl-4 tabular-nums">{l.memory.map((m, i) => <li key={i}>{m}</li>)}</ul>
                </details>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </section>
  )
}
