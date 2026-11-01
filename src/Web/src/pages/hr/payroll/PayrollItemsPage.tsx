import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { confirmPayrollItem, createPayrollItem, deletePayrollItem, getListPayrollItemsQueryKey, inactivatePayrollItem, reactivatePayrollItem, updatePayrollItem, useListPayrollItems } from '@/api/generated/bemaqui'
import type { PayrollCalculation, PayrollItemDetail, PayrollItemKind, PayrollTimesheetSource } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { HrTabs } from '../HrTabs'
import { CALCULATION_LABELS, KIND_LABELS, PAYROLL_DICAS } from './payroll'

const PENDING = 'A confirmar com a contadora'

/**
 * RH → Rubricas (RH-02a CA 1 e 2): a lista padrão "A confirmar com a contadora" e as do tenant, em painel lateral; "Marcar como
 * conferido", inativar e excluir (só sem uso: senão, "Inativar") com hr.edit.
 */
export function PayrollItemsPage() {
  const query = useListPayrollItems()
  const client = useQueryClient()
  const canEdit = useCan('hr.edit')
  const [editing, setEditing] = useState<{ item: PayrollItemDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListPayrollItemsQueryKey() })
  const newButton = canEdit ? <button type="button" onClick={() => setEditing({ item: null })} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova rubrica</button> : null
  const pending = (query.data ?? []).filter((i) => !i.confirmed).length
  return (
    <>
      <PageHeading title="RH: Rubricas">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Proventos, descontos e informativas dos holerites. {pending > 0 ? `${pending} ${pending === 1 ? 'rubrica a confirmar' : 'rubricas a confirmar'} com a contadora.` : ''}</p>
          {newButton}
        </div>
      </PageHeading>
      <HrTabs />
      {query.isPending ? <Skeleton className="h-48" /> : query.isError ? <ErrorState title="Não foi possível carregar as rubricas." onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Nenhuma rubrica cadastrada." description="Cadastre as rubricas que a contadora usa no holerite." action={newButton} />
      ) : (
        <ul aria-label="Rubricas" className="divide-y divide-border rounded-md border border-border">
          {query.data.map((i) => (
            <li key={i.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <button type="button" onClick={() => setEditing({ item: i })} className="min-h-11 text-left font-semibold text-primary hover:underline sm:min-h-0">
                <span className="tabular-nums">{i.code}</span> · {i.name}
              </button>
              <span className="flex flex-wrap items-center gap-1.5">
                <StatusChip tone="neutral">{KIND_LABELS[i.kind]}</StatusChip>
                {i.calculation && i.calculation !== 'None' ? <StatusChip tone="info">Regra: {CALCULATION_LABELS[i.calculation] ?? i.calculation}</StatusChip> : null}
                {i.confirmed ? <StatusChip tone="ok">Conferido</StatusChip> : <StatusChip tone="warn">{PENDING}</StatusChip>}
                {i.isActive ? null : <StatusChip tone="neutral">Inativa</StatusChip>}
              </span>
            </li>
          ))}
        </ul>
      )}
      {editing ? <ItemPanel item={editing.item} canEdit={canEdit} onClose={() => setEditing(null)} onSaved={(close) => { refresh(); if (close) setEditing(null) }} onChanged={(item) => { refresh(); setEditing({ item }) }} /> : null}
    </>
  )
}

function ItemPanel({ item, canEdit, onClose, onSaved, onChanged }: { item: PayrollItemDetail | null; canEdit: boolean; onClose: () => void; onSaved: (close: boolean) => void; onChanged: (item: PayrollItemDetail) => void }) {
  const start = { code: item?.code ?? '', name: item?.name ?? '', kind: (item?.kind ?? 'Earning') as PayrollItemKind, inssBase: item?.inssBase ?? false, fgtsBase: item?.fgtsBase ?? false, irrfBase: item?.irrfBase ?? false, timesheetSource: item?.timesheetSource ?? '', calculation: (item?.calculation ?? 'None') as PayrollCalculation, deductsIrrfBase: item?.deductsIrrfBase ?? false }
  const [draft, setDraft] = useState(start)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const save = async () => {
    try {
      const input = { ...draft, timesheetSource: (draft.timesheetSource || null) as PayrollTimesheetSource | null, deductsIrrfBase: draft.kind === 'Deduction' && draft.deductsIrrfBase, version: item?.version ?? null }
      if (item) await updatePayrollItem(item.id, input)
      else await createPayrollItem(input)
      notifySuccess('Rubrica salva')
      onSaved(true)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a rubrica.', fields: {} })
      return false
    }
  }
  const check = (key: 'inssBase' | 'fgtsBase' | 'irrfBase', label: string, dica?: string) => (
    <label className="flex min-h-11 items-center gap-2 text-sm sm:min-h-0">
      <input type="checkbox" checked={draft[key]} onChange={(e) => setDraft({ ...draft, [key]: e.target.checked })} className="size-4 accent-primary" />
      {label}{dica ? <Dica nome={label} texto={dica} /> : null}
    </label>
  )

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={item ? `${item.code} · ${item.name}` : 'Nova rubrica'} dirty={canEdit && JSON.stringify(draft) !== JSON.stringify(start)} onSave={save}
      footer={(
        <>
          {item && canEdit ? (
            <span className="mr-auto flex flex-wrap gap-2">
              <button type="button" onClick={async () => { onChanged(item.isActive ? await inactivatePayrollItem(item.id) : await reactivatePayrollItem(item.id)); notifySuccess(item.isActive ? 'Inativada' : 'Reativada') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">{item.isActive ? 'Inativar' : 'Reativar'}</button>
              {item.inUse ? null : (
                <ConfirmDialog verb="Excluir" done="Rubrica excluída" destructive title={`Excluir a rubrica ${item.code}?`} description="Só dá para excluir rubrica que nunca foi usada em holerite."
                  onConfirm={async () => { await deletePayrollItem(item.id); onSaved(true) }}
                  recovery={item.isActive ? { verb: 'Inativar', done: 'Inativada', when: (code) => code === 'payroll_item_in_use', onSelect: async () => onChanged(await inactivatePayrollItem(item.id)) } : undefined}
                  trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir</button>} />
              )}
            </span>
          ) : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar rubrica</button> : null}
        </>
      )}>
      <div className="space-y-4">
        {item ? (
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {item.confirmed ? (
              <StatusChip tone="ok">Conferido{item.confirmedByName ? ` por ${item.confirmedByName}` : ''}{item.confirmedAt ? ` em ${formatDateTime(item.confirmedAt)}` : ''}</StatusChip>
            ) : (
              <>
                <StatusChip tone="warn">{PENDING}</StatusChip>
                <Dica nome={PENDING} texto={PAYROLL_DICAS.aConfirmar} />
                {canEdit ? <button type="button" onClick={async () => { onChanged(await confirmPayrollItem(item.id)); notifySuccess('Conferido') }} className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold sm:min-h-0">Marcar como conferido</button> : null}
              </>
            )}
            {item.inUse ? <span className="text-muted-foreground">Usada em holerite: só inativa.</span> : null}
          </div>
        ) : null}
        <fieldset disabled={!canEdit} className="space-y-3">
          <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
          <div className="grid gap-3 sm:grid-cols-3">
            <Field label="Código" required error={error?.fields.code?.[0]} hint="Até 10. Pode ser o da contadora.">
              {(p) => <input {...p} value={draft.code} maxLength={10} onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} className={inputClass + ' tabular-nums'} />}
            </Field>
            <div className="sm:col-span-2">
              <Field label="Nome" required error={error?.fields.name?.[0]}>{(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
            </div>
          </div>
          <Field label="Tipo" required error={error?.fields.kind?.[0]} dica={PAYROLL_DICAS.informativa}>
            {(p) => (
              <select {...p} value={draft.kind} onChange={(e) => setDraft({ ...draft, kind: e.target.value as PayrollItemKind })} className={inputClass}>
                <option value="Earning">Provento (soma)</option>
                <option value="Deduction">Desconto (subtrai)</option>
                <option value="Information">Informativa (não entra no líquido)</option>
              </select>
            )}
          </Field>
          <div className="space-y-1">
            <p className="text-sm font-medium">Bases (informativas)</p>
            {check('inssBase', 'Base do INSS', PAYROLL_DICAS.baseInss)}
            {check('fgtsBase', 'Base do FGTS')}
            {check('irrfBase', 'Base do IRRF')}
          </div>
          <Field label="Regra da folha calculada" error={error?.fields.calculation?.[0]} dica={PAYROLL_DICAS.regra}
            hint="Na folha calculada, a rubrica com regra é refeita pelo sistema a cada cálculo. Sem regra, é lançamento avulso (com motivo). Uma rubrica ativa por regra.">
            {(p) => (
              <select {...p} value={draft.calculation} onChange={(e) => setDraft({ ...draft, calculation: e.target.value as PayrollCalculation })} className={inputClass}>
                {Object.entries(CALCULATION_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            )}
          </Field>
          {draft.kind === 'Deduction' ? (
            <div className="flex items-center gap-1 text-sm">
              <label className="flex min-h-11 items-center gap-2 sm:min-h-0">
                <input type="checkbox" checked={draft.deductsIrrfBase} onChange={(e) => setDraft({ ...draft, deductsIrrfBase: e.target.checked })} className="size-4 accent-primary" />
                Deduz da base do IRRF
              </label>
              <Dica nome="Deduz da base do IRRF" texto={PAYROLL_DICAS.deduzIrrf} />
            </div>
          ) : null}
          <Field label="Origem no ponto" hint="Total do espelho de ponto fechado que preenche a referência desta rubrica no holerite (“Usar horas do espelho”). O valor continua vindo da contadora."
            dica="Ligue a rubrica a um total do espelho: horas extras, faltas ou atrasos. No holerite, “Usar horas do espelho” preenche só a referência (as horas), nunca o valor.">
            {(p) => (
              <select {...p} value={draft.timesheetSource} onChange={(e) => setDraft({ ...draft, timesheetSource: e.target.value })} className={inputClass}>
                <option value="">Sem ligação com o ponto</option>
                <option value="Overtime">Horas extras do espelho</option>
                <option value="Absence">Faltas do espelho</option>
                <option value="Late">Atrasos do espelho</option>
              </select>
            )}
          </Field>
        </fieldset>
        {item ? (
          <details className="text-sm">
            <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Histórico de alterações</summary>
            <div className="mt-2"><RecordHistory entity="PayrollItem" id={item.id} /></div>
          </details>
        ) : null}
      </div>
    </SidePanel>
  )
}
