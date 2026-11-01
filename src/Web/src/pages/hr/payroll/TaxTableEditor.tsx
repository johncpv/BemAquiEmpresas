import { useState } from 'react'
import { checkPayrollTaxTable, createPayrollTaxTable, getPayrollTaxTableImpact, rectifyPayrollTaxTable, updatePayrollTaxTable } from '@/api/generated/bemaqui'
import type { PayrollTaxTableDetail, PayrollTaxTableImpact, PayrollTaxTableInput } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, DecimalInput, MoneyInput, PercentInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { periodLabel } from './payroll'
import {
  type BracketDraft, type ContentDraft, emptyDraft, formatDay, nextMonthStart, type ReductionDraft, type Rounding, ROUNDINGS, statusChip, TAX_DICAS, TAX_KINDS, type TaxContent,
  type TaxKind, toContent, toDraft, VALUE_FIELDS,
} from './tax-tables'

type ServerError = { message: string; fields: Record<string, string[]> }

const toServerError = (e: unknown, fallback: string): ServerError => (e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: fallback, fields: {} })

/**
 * Painel da versão (FLH-01 CA 2 a 7 e 13): editor das faixas e dos valores do tipo, "Salvar tabela" (só a versão a confirmar),
 * "Marcar como conferido" (quem não cadastrou nem editou; o Proprietário único com o dataset) e "Retificar" (a conferida), com o histórico.
 */
export function TaxTableEditor({ table, kind: initialKind, prefill, canEdit, canApprove, singleApprover, onClose, onChanged }: {
  table: PayrollTaxTableDetail | null
  kind: TaxKind
  prefill: PayrollTaxTableDetail | null
  canEdit: boolean
  canApprove: boolean
  singleApprover: boolean
  onClose: () => void
  onChanged: (table: PayrollTaxTableDetail | null) => void
}) {
  const source = table ?? prefill
  const startKind = (table?.kind ?? initialKind) as TaxKind
  const start = {
    kind: startKind,
    validFrom: table?.validFrom ?? nextMonthStart(),
    rounding: (source?.rounding ?? 'AwayFromZero') as Rounding,
    source: table?.source ?? '',
    draft: toDraft(startKind, source?.content as TaxContent | undefined),
  }
  const [state, setState] = useState(start)
  const [error, setError] = useState<ServerError | null>(null)
  const editable = canEdit && (!table || table.status === 'Unconfirmed')
  const setDraft = (draft: ContentDraft) => setState({ ...state, draft })
  const fieldError = (key: string) => error?.fields[key]?.[0]

  const save = async () => {
    const input: PayrollTaxTableInput = {
      kind: state.kind, validFrom: state.validFrom || null, rounding: state.rounding, source: state.source, version: table?.version ?? null,
      content: toContent(state.kind, state.draft) as PayrollTaxTableInput['content'],
    }
    try {
      const saved = table ? await updatePayrollTaxTable(table.id, input) : await createPayrollTaxTable(input)
      notifySuccess('Tabela salva')
      setError(null)
      onChanged(saved)
      return true
    } catch (e) {
      setError(toServerError(e, 'Não foi possível salvar a tabela. Tente de novo.'))
      return false
    }
  }

  const title = table ? `${table.kindLabel} · vale a partir de ${formatDay(table.validFrom)}` : 'Nova tabela da folha'
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={title} description={TAX_KINDS.find((k) => k.kind === state.kind)?.description}
      dirty={editable && JSON.stringify(state) !== JSON.stringify(start)} onSave={save}
      footer={(
        <>
          {table && table.status === 'Unconfirmed' && canApprove ? <CheckDialog table={table} singleApprover={singleApprover} onChecked={onChanged} /> : null}
          {table && table.status === 'Active' && canEdit ? <RectifyDialog table={table} onRectified={onChanged} /> : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {editable ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar tabela</button> : null}
        </>
      )}>
      <div className="space-y-4">
        {table ? <Status table={table} /> : null}
        {table && table.status !== 'Unconfirmed' ? (
          <p role="status" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">
            {table.status === 'Active' ? 'Tabela conferida não muda. Para corrigir, use "Retificar" (mesma vigência) ou cadastre uma nova versão com a vigência certa.' : 'Esta versão foi substituída por uma retificação conferida.'}
          </p>
        ) : null}
        <fieldset disabled={!editable} className="space-y-3">
          <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : error ? 'Confira os campos marcados.' : undefined} />
          <div className="grid gap-3 sm:grid-cols-2">
            {table ? null : (
              <Field label="Tipo" required>
                {(p) => (
                  <select {...p} value={state.kind} onChange={(e) => { const k = e.target.value as TaxKind; setState({ ...state, kind: k, draft: emptyDraft(k) }) }} className={inputClass}>
                    {TAX_KINDS.map((k) => <option key={k.kind} value={k.kind}>{k.label}</option>)}
                  </select>
                )}
              </Field>
            )}
            <Field label="Vale a partir de" required dica={TAX_DICAS.valeAPartirDe} error={fieldError('validFrom')} hint={table?.rectifiesId ? 'A retificação mantém a vigência.' : 'Primeiro dia do mês.'}>
              {(p) => <DateInput {...p} value={state.validFrom} onChange={(v) => setState({ ...state, validFrom: v })} disabled={!!table?.rectifiesId} />}
            </Field>
            <Field label="Arredondamento" required dica={TAX_DICAS.arredondamento} error={fieldError('rounding')}>
              {(p) => (
                <select {...p} value={state.rounding} onChange={(e) => setState({ ...state, rounding: e.target.value as Rounding })} className={inputClass}>
                  {ROUNDINGS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
                </select>
              )}
            </Field>
          </div>
          <Field label="Fonte" required error={fieldError('source')} hint="A norma e a data de publicação (ex.: portaria, lei ou decreto).">
            {(p) => <textarea {...p} rows={2} maxLength={300} value={state.source} onChange={(e) => setState({ ...state, source: e.target.value })} className={inputClass} />}
          </Field>
          <ContentEditor kind={state.kind} draft={state.draft} onChange={setDraft} fieldError={fieldError} disabled={!editable} />
        </fieldset>
        {table && table.warnings.length > 0 ? (
          <ul role="status" className="space-y-1 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
            {table.warnings.map((w) => <li key={w}>{w}</li>)}
          </ul>
        ) : null}
        {table ? (
          <details className="text-sm">
            <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Histórico de alterações</summary>
            <div className="mt-2"><RecordHistory entity="PayrollTaxTable" id={table.id} /></div>
          </details>
        ) : null}
      </div>
    </SidePanel>
  )
}

function Status({ table }: { table: PayrollTaxTableDetail }) {
  return (
    <div className="space-y-1 text-sm">
      <div className="flex flex-wrap items-center gap-1.5">
        {table.inForceToday ? <StatusChip tone="info">Vigente</StatusChip> : null}
        {statusChip(table.status, table.isFuture, table.validFrom).map((c) => <StatusChip key={c.label} tone={c.tone}>{c.label}</StatusChip>)}
        {table.status === 'Unconfirmed' ? <Dica nome="A confirmar com a contadora" texto={TAX_DICAS.aConfirmar} /> : null}
      </div>
      <p className="text-xs text-muted-foreground">
        Cadastrada por {table.createdByName ?? (table.seedPackage ? `sistema (pacote ${table.seedPackage})` : 'usuário removido')} em {formatDateTime(table.createdAt)}
        {table.editedByNames.length > 0 ? `; editada por ${table.editedByNames.join(', ')}` : ''}
        {table.checkedAt ? `; conferida por ${table.checkedByName ?? 'usuário removido'} em ${formatDateTime(table.checkedAt)}` : ''}.
      </p>
      {table.checkedNote ? <p className="text-xs">{table.checkedNote}</p> : null}
      {table.rectifyReason ? <p className="text-xs">Retificação: {table.rectifyReason}</p> : null}
    </div>
  )
}

function ContentEditor({ kind, draft, onChange, fieldError, disabled }: { kind: TaxKind; draft: ContentDraft; onChange: (d: ContentDraft) => void; fieldError: (key: string) => string | undefined; disabled: boolean }) {
  const setValue = (key: keyof ContentDraft['values'], value: string | null) => onChange({ ...draft, values: { ...draft.values, [key]: value } })
  const brackets = kind === 'Inss' || kind === 'Irrf'
  return (
    <div className="space-y-3">
      {brackets ? (
        <div className="space-y-1">
          <p className="text-sm font-medium">Faixas</p>
          <p className="text-xs text-muted-foreground">{kind === 'Inss' ? 'Limite superior de cada faixa; a última é o teto do salário de contribuição.' : 'Limite superior de cada faixa; deixe o da última em branco ("acima de").'}</p>
          <RowsEditor<BracketDraft> noun="faixa" rows={draft.brackets} min={1} max={20} disabled={disabled} onChange={(rows) => onChange({ ...draft, brackets: rows })}
            newRow={() => ({ upTo: null, rate: null, deduction: kind === 'Irrf' ? '0' : null })} rowError={(i) => fieldError(`content.brackets[${i}]`)}
            columns={[
              { label: 'Até (R$)', render: (row, _i, update) => <MoneyInput aria-label="Até" value={row.upTo} onChange={(v) => update({ upTo: v })} /> },
              { label: 'Alíquota', render: (row, _i, update) => <PercentInput aria-label="Alíquota" scale={4} value={row.rate} onChange={(v) => update({ rate: v })} /> },
              ...(kind === 'Irrf' ? [{ label: 'Parcela a deduzir', render: (row: BracketDraft, _i: number, update: (p: Partial<BracketDraft>) => void) => <MoneyInput aria-label="Parcela a deduzir" value={row.deduction} onChange={(v) => update({ deduction: v })} /> }] : []),
            ]} />
          {fieldError('content.brackets') ? <p className="text-xs text-crit-ink">{fieldError('content.brackets')}</p> : null}
        </div>
      ) : null}
      {VALUE_FIELDS[kind].length > 0 ? (
        <div className="grid gap-3 sm:grid-cols-2">
          {kind === 'EmployerCharges' ? (
            <Field label="Regime tributário" required error={fieldError('content.taxRegime')}>
              {(p) => <input {...p} value={draft.taxRegime} maxLength={40} onChange={(e) => onChange({ ...draft, taxRegime: e.target.value })} className={inputClass} />}
            </Field>
          ) : null}
          {VALUE_FIELDS[kind].map((f) => (
            <Field key={f.key} label={f.label} required hint={f.hint} error={fieldError(`content.${f.key}`)}>
              {(p) => f.type === 'money' ? <MoneyInput {...p} value={draft.values[f.key] ?? null} onChange={(v) => setValue(f.key, v)} />
                : f.type === 'pct' ? <PercentInput {...p} scale={4} value={draft.values[f.key] ?? null} onChange={(v) => setValue(f.key, v)} />
                  : <DecimalInput {...p} scale={4} value={draft.values[f.key] ?? null} onChange={(v) => setValue(f.key, v)} />}
            </Field>
          ))}
        </div>
      ) : null}
      {kind === 'Irrf' ? (
        <div className="space-y-1">
          <p className="flex items-center text-sm font-medium">Redução adicional<Dica nome="Redução adicional" texto={TAX_DICAS.reducao} /></p>
          <p className="text-xs text-muted-foreground">Redução = valor fixo − percentual × rendimento do mês, para o rendimento até o limite. Sem redução na norma, deixe sem faixas.</p>
          <RowsEditor<ReductionDraft> noun="faixa de redução" rows={draft.reductions} max={20} disabled={disabled} onChange={(rows) => onChange({ ...draft, reductions: rows })}
            newRow={() => ({ upTo: null, fixedAmount: null, rate: '0' })} rowError={(i) => fieldError(`content.reductions[${i}]`)}
            columns={[
              { label: 'Rendimento até (R$)', render: (row, _i, update) => <MoneyInput aria-label="Rendimento até" value={row.upTo} onChange={(v) => update({ upTo: v })} /> },
              { label: 'Valor fixo', render: (row, _i, update) => <MoneyInput aria-label="Valor fixo" value={row.fixedAmount} onChange={(v) => update({ fixedAmount: v })} /> },
              { label: 'Percentual', render: (row, _i, update) => <PercentInput aria-label="Percentual" scale={4} value={row.rate} onChange={(v) => update({ rate: v })} /> },
            ]} />
        </div>
      ) : null}
    </div>
  )
}

/** "Marcar como conferido" (CA 5 e 6): com quem e quando; quem cadastrou ou editou só confere se for o único que aprova, com o dataset. */
function CheckDialog({ table, singleApprover, onChecked }: { table: PayrollTaxTableDetail; singleApprover: boolean; onChecked: (t: PayrollTaxTableDetail) => void }) {
  const [accountant, setAccountant] = useState('')
  const [on, setOn] = useState('')
  const [dataset, setDataset] = useState('')
  const needsDataset = table.mineToCheck && singleApprover
  if (table.mineToCheck && !singleApprover) {
    return <p className="mr-auto self-center text-xs text-muted-foreground">Você cadastrou ou editou: outra pessoa com permissão de aprovar confere.</p>
  }

  return (
    <span className="mr-auto">
      <ConfirmDialog size="lg" verb="Marcar como conferido" done="Tabela conferida" title={`Conferir ${table.kindLabel} de ${formatDay(table.validFrom)}?`}
        description="Depois de conferida, a tabela não muda: correção só por retificação, com nova conferência."
        onOpen={() => { setAccountant(''); setOn(''); setDataset('') }}
        preconditions={[
          { ok: accountant.trim().length > 0, text: 'Com quem foi conferida.' },
          { ok: !!on, text: 'Data da conferência.' },
          ...(needsDataset ? [{ ok: dataset.trim().length > 0, text: 'Dataset de referência da contadora desta vigência.' }] : []),
        ]}
        onConfirm={async () => {
          if (!accountant.trim() || !on) throw new DialogMessage('Informe com quem e quando a tabela foi conferida.')
          onChecked(await checkPayrollTaxTable(table.id, { accountant: accountant.trim(), checkedOn: on, dataset: needsDataset ? dataset : null, version: table.version }))
        }}
        trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Marcar como conferido</button>}>
        <div className="mt-3 space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conferido com" required hint="Nome da contadora ou do escritório.">{(p) => <input {...p} value={accountant} maxLength={100} onChange={(e) => setAccountant(e.target.value)} className={inputClass} />}</Field>
            <Field label="Em" required>{(p) => <DateInput {...p} value={on} onChange={setOn} />}</Field>
          </div>
          {needsDataset ? (
            <Field label="Dataset da contadora" required hint='Uma linha por caso: "base;dependentes;esperado" (ex.: 3.000,00;1;123,45). O simulador precisa bater em todos.'>
              {(p) => <textarea {...p} rows={6} value={dataset} onChange={(e) => setDataset(e.target.value)} className={inputClass + ' font-mono text-xs'} />}
            </Field>
          ) : null}
          {needsDataset ? <p className="text-xs text-muted-foreground">Você é a única pessoa que aprova e cadastrou ou editou esta tabela: a conferência fica registrada como &quot;Conferência pelo Proprietário único, com dataset da contadora&quot;.</p> : null}
        </div>
      </ConfirmDialog>
    </span>
  )
}

/** Retificar (CA 13): motivo obrigatório; nasce uma versão a confirmar com a mesma vigência e o conteúdo da conferida, para corrigir. */
function RectifyDialog({ table, onRectified }: { table: PayrollTaxTableDetail; onRectified: (t: PayrollTaxTableDetail) => void }) {
  const [reason, setReason] = useState('')
  const [impact, setImpact] = useState<PayrollTaxTableImpact | null>(null)
  return (
    <span className="mr-auto">
      <ConfirmDialog size="lg" verb="Retificar" done="Retificação criada" title={`Retificar ${table.kindLabel} de ${formatDay(table.validFrom)}?`}
        description={<>Nasce uma versão a confirmar com a mesma vigência, para você corrigir os valores; até a nova conferência, a folha continua com a conferida. Folha fechada nunca é recalculada. <Dica nome="Retificar" texto={TAX_DICAS.retificar} /></>}
        onOpen={() => { setReason(''); setImpact(null); void getPayrollTaxTableImpact(table.id).then(setImpact).catch(() => setImpact({ draftRuns: [], closedRuns: [] })) }}
        preconditions={[{ ok: reason.trim().length >= 5, text: 'Motivo com pelo menos 5 caracteres.' }]}
        impactList={impact ? {
          label: 'Folhas calculadas com esta tabela',
          items: [...impact.draftRuns.map((r) => ({ key: r.runId, primary: `Folha de ${periodLabel(r.period)}`, secondary: 'Em rascunho: ganha "Recalcular".' })),
            ...impact.closedRuns.map((r) => ({ key: r.runId, primary: `Folha de ${periodLabel(r.period)}`, secondary: 'Fechada: não é recalculada; ganha "Ver diferença".' }))],
          total: impact.draftRuns.length + impact.closedRuns.length === 0 ? 'Nenhuma folha calculada usa esta tabela.' : undefined,
        } : undefined}
        onConfirm={async () => {
          onRectified(await rectifyPayrollTaxTable(table.id, { reason: reason.trim(), content: null, rounding: null, source: null, version: table.version }))
        }}
        trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Retificar</button>}>
        <Field label="Motivo" required hint="Fica no histórico da tabela.">
          {(p) => <textarea {...p} value={reason} maxLength={200} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
        </Field>
      </ConfirmDialog>
    </span>
  )
}
