import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { Calculator } from 'lucide-react'
import { type ReactNode, useState } from 'react'
import {
  activateTaxRule, confirmTaxRule, createTaxRule, deactivateTaxRule, getGetTaxRuleQueryKey, getListTaxRulesQueryKey, updateTaxRule, useGetTaxRule, useGetTaxRuleVersion,
} from '@/api/generated/bemaqui'
import type { IssLocation, TaxRuleValues, TaxRuleView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, PercentInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { TO_CONFIRM_DICA, TO_CONFIRM_LABEL } from '@/components/ui/to-confirm-chip'
import { useTableFilters } from '@/components/data-table/table-search'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { COST_TAX_OPTIONS } from '../settings/fiscal/fiscal-settings'
import { DICAS, DIFAL_NOTICE, firstErrors, isService, newRuleValues, OPERATION_LABEL, OPERATIONS, parseList, RECIPIENT_LABEL, RECIPIENTS, STATUS, suggestInterstateCfop, switchOperation } from './tax-rules'
import { TaxSimulationPanel } from './TaxSimulationPanel'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'
const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

/** "Nova regra fiscal" (`/app/fiscal/regras/nova`): começa pela venda de produção própria, em rascunho. */
export function NewTaxRulePage() {
  const navigate = useNavigate()
  const client = useQueryClient()
  return (
    <>
      <PageHeading title="Nova regra fiscal">
        <p className="mt-2 text-muted-foreground">A regra nasce em rascunho, &quot;A confirmar com a contadora&quot;. Outra pessoa com permissão ativa depois de simular.</p>
      </PageHeading>
      <RuleForm initial={newRuleValues(today())} editable onSaved={async (view) => {
        await client.invalidateQueries({ queryKey: getListTaxRulesQueryKey() })
        void navigate({ to: '/app/fiscal/regras/$id', params: { id: view.ruleId } })
      }} save={(values) => createTaxRule(values)} />
    </>
  )
}

/** Fiscal → Regras fiscais → regra (`/app/fiscal/regras/$id`, `?versao=` abre outra versão, `?simular=1` abre o painel). */
export function TaxRulePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { versao } = useTableFilters(['versao'])
  const head = useGetTaxRule(id, { query: { enabled: !versao, retry: false } })
  const version = useGetTaxRuleVersion(id, versao ?? '', { query: { enabled: !!versao, retry: false } })
  const query = versao ? version : head
  return (
    <>
      <PageHeading title={query.data?.values.name ?? 'Regra fiscal'} />
      {query.isPending ? (
        <div className="space-y-3"><Skeleton className="h-10" /><Skeleton className="h-64" /></div>
      ) : query.isError || !query.data ? (
        <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : (
        <RuleDetail key={query.data.versionId + query.data.status + query.data.reviewState} view={query.data} />
      )}
    </>
  )
}

type Dialog = 'activate' | 'deactivate' | 'confirm' | null

function RuleDetail({ view }: { view: TaxRuleView }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const { simular } = useTableFilters(['simular'])
  const canEdit = useCan('fiscal.edit') && view.canEdit
  const canApprove = useCan('fiscal.approve') && view.canApprove
  const [dialog, setDialog] = useState<Dialog>(null)
  const [reason, setReason] = useState('')
  const [simulated, setSimulated] = useState(false)
  const [activated, setActivated] = useState(false)
  const latest = view.versions[0]?.versionId === view.versionId
  const editable = canEdit && latest
  const status = STATUS[view.status]
  const setSearch = (patch: Record<string, string | undefined>) => void navigate({ to: '.', search: (s: Record<string, unknown>) => ({ ...s, ...patch }) } as never)
  const openSimulation = () => { setSimulated(true); setSearch({ simular: '1' }) }
  const refresh = async (next: TaxRuleView) => {
    client.setQueryData(getGetTaxRuleQueryKey(next.ruleId), next)
    await client.invalidateQueries({ queryKey: getListTaxRulesQueryKey() })
    setSearch({ versao: undefined })
    await client.invalidateQueries({ queryKey: getGetTaxRuleQueryKey(next.ruleId) })
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <StatusChip tone={status.tone}>{status.label}</StatusChip>
        <span className="text-sm text-muted-foreground">Versão <span className="tabular-nums">{view.ruleVersion}</span> · {OPERATION_LABEL[view.values.operation]}</span>
        {view.reviewState === 'Confirmed'
          ? <StatusChip tone="ok">Conferida{view.confirmedByName ? ` por ${view.confirmedByName}` : ''}{view.confirmedAt ? ` em ${formatDateTime(view.confirmedAt)}` : ''}</StatusChip>
          : <span className="inline-flex items-center"><StatusChip tone="warn">{TO_CONFIRM_LABEL}</StatusChip><Dica nome={TO_CONFIRM_LABEL} texto={TO_CONFIRM_DICA} /></span>}
        {view.activatedAt ? <span className="text-sm text-muted-foreground">Ativada por {view.activatedByName ?? 'usuário removido'} em {formatDateTime(view.activatedAt)}{view.selfActivated ? ' (Autoaprovado)' : ''}</span> : null}
      </div>
      {view.needsReview ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm font-semibold text-warn-ink">Os valores padrão mudaram em Configurações → Fiscal. Revise esta regra.</p> : null}
      {view.values.difal ? <p role="note" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{DIFAL_NOTICE}<Dica nome="DIFAL" texto={DICAS.difal} /></p> : null}
      {view.deactivationReason ? <p className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Inativada: {view.deactivationReason}</p> : null}
      {!latest ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Versão antiga, só leitura. <button type="button" onClick={() => setSearch({ versao: undefined })} className="font-semibold text-primary underline">Abrir a versão atual</button></p> : null}
      {!canEdit ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Modo leitura: só quem edita o Fiscal altera as regras.</p> : null}
      {activated ? (
        <NextSteps steps={[
          { label: 'Simular uma nota', onSelect: openSimulation },
          { label: 'Ver romaneios a faturar', onSelect: () => void navigate({ to: '/app/producao/expedicao' }) },
        ]} onDismiss={() => setActivated(false)} />
      ) : null}

      <div className="flex flex-wrap gap-2">
        <button type="button" onClick={openSimulation} className={secondary}><Calculator aria-hidden="true" className="size-4" />Simular</button>
        {canApprove && view.status === 'Draft' ? (
          <button type="button" disabled={!simulated || !!view.activateBlockedReason} onClick={() => setDialog('activate')} className={primary}
            title={view.activateBlockedReason ?? (!simulated ? 'Simule antes de ativar.' : undefined)}>Ativar regra</button>
        ) : null}
        {canApprove && view.reviewState !== 'Confirmed' ? <button type="button" onClick={() => setDialog('confirm')} className={secondary}>Marcar como conferida</button> : null}
        {canApprove && view.status !== 'Inactive' ? <button type="button" onClick={() => { setReason(''); setDialog('deactivate') }} className={secondary}>Inativar regra</button> : null}
      </div>
      {canApprove && view.status === 'Draft' ? (
        <p className="text-xs text-muted-foreground">{view.activateBlockedReason ?? (!simulated ? 'Abra o simulador e confira o cálculo antes de ativar.' : 'Pronto para ativar.')}</p>
      ) : null}
      {view.versions.length > 1 ? (
        <nav aria-label="Versões" className="flex flex-wrap gap-2 text-sm">
          {view.versions.map((v) => (
            <button key={v.versionId} type="button" aria-current={v.versionId === view.versionId} onClick={() => setSearch({ versao: v.versionId === view.versions[0]?.versionId ? undefined : v.versionId })}
              className="min-h-11 rounded-md border border-border px-2 aria-[current=true]:border-primary aria-[current=true]:font-semibold">
              Versão {v.ruleVersion} · {STATUS[v.status].label}
            </button>
          ))}
        </nav>
      ) : null}

      <RuleForm initial={view.values} editable={editable} icmsCsts={view.icmsCsts}
        note={editable && view.status !== 'Draft' ? `Salvar cria a versão ${view.ruleVersion + 1} em rascunho; esta continua valendo até a nova ser ativada.` : undefined}
        save={(values) => updateTaxRule(view.ruleId, values)} onSaved={refresh} />

      <details className="rounded-md border border-border bg-card p-3 text-sm">
        <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
        <div className="mt-2"><RecordHistory entity="TaxRule" id={view.versionId} /></div>
      </details>

      <TaxSimulationPanel open={simular === '1'} onOpenChange={(o) => { if (o) setSimulated(true); setSearch({ simular: o ? '1' : undefined }) }} ruleVersionId={view.versionId} operation={view.values.operation} />

      <ConfirmDialog open={dialog === 'activate'} onOpenChange={(o) => { if (!o) setDialog(null) }} verb="Ativar regra" done="Regra ativada" title={`Ativar ${view.values.name}`}
        description={view.versions.some((v) => v.status === 'Active') ? 'A versão ativa de agora deixa de valer e as próximas notas usam esta.' : 'As próximas notas desta operação passam a usar esta regra.'}
        onConfirm={async () => { await refresh(await activateTaxRule(view.ruleId, view.versionId)); setActivated(true) }} />
      <ConfirmDialog open={dialog === 'confirm'} onOpenChange={(o) => { if (!o) setDialog(null) }} verb="Marcar como conferida" done="Regra conferida" title="Marcar como conferida"
        description="Confirme só depois que a contadora conferir esta versão. Editar a regra volta o selo para &quot;A confirmar&quot;."
        onConfirm={async () => refresh(await confirmTaxRule(view.ruleId, view.versionId))} />
      <ConfirmDialog open={dialog === 'deactivate'} onOpenChange={(o) => { if (!o) setDialog(null) }} verb="Inativar regra" done="Regra inativada" title={`Inativar ${view.values.name}`}
        description="Sem regra ativa para a operação, a nota não é emitida."
        onConfirm={async () => {
          if (reason.trim().length < 3) throw new DialogMessage('Informe o motivo da inativação.')
          await refresh(await deactivateTaxRule(view.ruleId, view.versionId, { reason }))
        }}>
        <Field label="Motivo" required>{(p) => <textarea {...p} value={reason} onChange={(e) => setReason(e.target.value)} maxLength={500} className={inputClass} />}</Field>
      </ConfirmDialog>
    </div>
  )
}

function Section({ title, dica, dicaName, children }: { title: string; dica?: string; dicaName?: string; children: ReactNode }) {
  return (
    <section className="min-w-0 rounded-md border border-border bg-card p-4">
      <h2 className="mb-3 flex items-center text-[15px] font-semibold">{title}{dica ? <Dica nome={dicaName ?? title} texto={dica} /> : null}</h2>
      <div className="grid gap-4 sm:grid-cols-2">{children}</div>
    </section>
  )
}

/** Formulário em seções por tributo (CA 2); erros do servidor no campo, com foco no primeiro. */
function RuleForm({ initial, editable, icmsCsts, note, save, onSaved }: {
  initial: TaxRuleValues
  editable: boolean
  icmsCsts?: string[]
  note?: string
  save: (values: TaxRuleValues) => Promise<TaxRuleView>
  onSaved: (view: TaxRuleView) => Promise<void> | void
}) {
  const [v, setV] = useState<TaxRuleValues>(initial)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [states, setStates] = useState(initial.conditions.states.join(', '))
  const [ncms, setNcms] = useState(initial.conditions.ncmPrefixes.join(', '))
  const service = isService(v.operation)
  const set = (patch: Partial<TaxRuleValues>) => setV((x) => ({ ...x, ...patch }))
  const err = (key: string) => errors[key]
  const suggestion = suggestInterstateCfop(v.cfopInternal)

  const submit = async () => {
    setBusy(true)
    setErrors({})
    setFormError(undefined)
    try {
      const view = await save({ ...v, conditions: { ...v.conditions, states: parseList(states), ncmPrefixes: parseList(ncms, true) } })
      notifySuccess('Regra fiscal salva')
      await onSaved(view)
    } catch (e) {
      if (e instanceof ApiError) {
        const fields = firstErrors(e.fields)
        setErrors(fields)
        setFormError(e.message)
        const first = Object.keys(fields)[0]
        if (first) document.getElementById('campo-' + first)?.querySelector<HTMLElement>('input,select,textarea')?.focus()
      } else {
        setFormError('Não foi possível salvar a regra fiscal. Tente de novo.')
      }
    } finally {
      setBusy(false)
    }
  }

  const pct = (key: string, label: string, value: number, onChange: (n: number) => void, hint?: string) => (
    <div id={'campo-' + key}>
      <Field label={label} error={err(key)} hint={hint}>{(p) => <PercentInput {...p} scale={4} value={String(value)} onChange={(x) => onChange(Number(x ?? 0))} />}</Field>
    </div>
  )
  const text = (key: string, label: string, value: string | null | undefined, onChange: (s: string) => void, opts: { maxLength?: number; hint?: string; dica?: string; dicaName?: string } = {}) => (
    <div id={'campo-' + key}>
      <Field label={label} error={err(key)} hint={opts.hint} dica={opts.dica}>
        {(p) => <input {...p} value={value ?? ''} maxLength={opts.maxLength} spellCheck={false} onChange={(e) => onChange(e.target.value)} className={inputClass + ' tabular-nums'} />}
      </Field>
    </div>
  )
  const toggle = (label: string, on: boolean, onChange: (b: boolean) => void, hint?: string) => (
    <label className="flex min-h-11 items-start gap-2 text-sm sm:col-span-2">
      <input type="checkbox" checked={on} onChange={(e) => onChange(e.target.checked)} className="mt-0.5 size-4 accent-primary" />
      <span><b className="font-medium">{label}</b>{hint ? <span className="block text-xs text-muted-foreground">{hint}</span> : null}</span>
    </label>
  )

  return (
    <form onSubmit={(e) => { e.preventDefault(); void submit() }} noValidate aria-label="Regra fiscal" className="space-y-4">
      <FormError message={formError} />
      {note ? <p role="note" className="rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">{note}</p> : null}
      <fieldset disabled={!editable} className="min-w-0 space-y-4">
        <Section title="Operação">
          <div id="campo-operation">
            <Field label="Operação" error={err('operation')}>
              {(p) => (
                <select {...p} value={v.operation} onChange={(e) => setV((x) => switchOperation(x, e.target.value as TaxRuleValues['operation']))} className={inputClass}>
                  {OPERATIONS.map((o) => <option key={o} value={o}>{OPERATION_LABEL[o]}</option>)}
                </select>
              )}
            </Field>
          </div>
          {text('name', 'Descrição', v.name, (s) => set({ name: s }), { maxLength: 120 })}
          {text('natureOfOperation', 'Natureza da operação', v.natureOfOperation, (s) => set({ natureOfOperation: s }), { maxLength: 60, hint: 'O texto que vai na nota.' })}
          <div id="campo-validFrom"><Field label="Vale desde" error={err('validFrom')}>{(p) => <DateInput {...p} value={v.validFrom} onChange={(d) => set({ validFrom: d })} />}</Field></div>
          <div id="campo-validTo"><Field label="Vale até" error={err('validTo')} hint="Vazio: sem data para acabar.">{(p) => <DateInput {...p} value={v.validTo ?? ''} onChange={(d) => set({ validTo: d || null })} />}</Field></div>
          {!service ? (
            <>
              {text('cfopInternal', 'CFOP dentro do estado', v.cfopInternal, (s) => set({ cfopInternal: s }), { maxLength: 4, dica: DICAS.cfop })}
              <div>
                {text('cfopInterstate', 'CFOP fora do estado', v.cfopInterstate, (s) => set({ cfopInterstate: s }), { maxLength: 4 })}
                {editable && suggestion && suggestion !== v.cfopInterstate ? (
                  <button type="button" onClick={() => set({ cfopInterstate: suggestion })} className="mt-1 min-h-11 text-sm font-semibold text-primary underline sm:min-h-0">Usar {suggestion}? Confirme</button>
                ) : null}
              </div>
            </>
          ) : null}
        </Section>

        <Section title="Condições">
          <div id="campo-conditions.states"><Field label="UFs de destino" error={err('conditions.states')} hint="Separe por vírgula. Vazio: todas.">{(p) => <input {...p} value={states} onChange={(e) => setStates(e.target.value)} className={inputClass} />}</Field></div>
          <div id="campo-conditions.ncmPrefixes"><Field label="NCM ou prefixo" error={err('conditions.ncmPrefixes')} hint="Ex.: 8428. Vazio: todos. A regra com NCM ganha da sem NCM.">{(p) => <input {...p} value={ncms} onChange={(e) => setNcms(e.target.value)} className={inputClass + ' tabular-nums'} />}</Field></div>
          <fieldset className="sm:col-span-2">
            <legend className="text-sm font-medium">Tipo de destinatário <span className="text-xs font-normal text-muted-foreground">(nenhum marcado: todos)</span></legend>
            <div className="flex flex-wrap gap-3">
              {RECIPIENTS.map((r) => (
                <label key={r} className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" className="size-4 accent-primary" checked={v.conditions.recipientTypes.includes(r)}
                    onChange={(e) => set({ conditions: { ...v.conditions, recipientTypes: e.target.checked ? [...v.conditions.recipientTypes, r] : v.conditions.recipientTypes.filter((x) => x !== r) } })} />
                  {RECIPIENT_LABEL[r]}
                </label>
              ))}
            </div>
          </fieldset>
        </Section>

        {v.icms ? (
          <Section title="ICMS" dica={DICAS.cst} dicaName="CST">
            <div id="campo-icms.cst">
              <Field label="CST do ICMS" error={err('icms.cst')}>
                {(p) => (
                  <select {...p} value={v.icms!.cst} onChange={(e) => set({ icms: { ...v.icms!, cst: e.target.value } })} className={inputClass}>
                    {(icmsCsts?.length ? icmsCsts : [v.icms!.cst]).map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                )}
              </Field>
            </div>
            {pct('icms.internalRate', 'Alíquota interna', v.icms.internalRate, (n) => set({ icms: { ...v.icms!, internalRate: n } }))}
            {pct('icms.interstateSouthSoutheastRate', 'Interestadual Sul e Sudeste (exceto ES)', v.icms.interstateSouthSoutheastRate, (n) => set({ icms: { ...v.icms!, interstateSouthSoutheastRate: n } }))}
            {pct('icms.interstateOtherRate', 'Interestadual Norte, Nordeste, Centro-Oeste e ES', v.icms.interstateOtherRate, (n) => set({ icms: { ...v.icms!, interstateOtherRate: n } }))}
            {pct('icms.importedRate', 'Interestadual de mercadoria importada', v.icms.importedRate, (n) => set({ icms: { ...v.icms!, importedRate: n } }))}
            {pct('icms.baseReduction', 'Redução da base', v.icms.baseReduction, (n) => set({ icms: { ...v.icms!, baseReduction: n } }), 'Só nos CST 20, 51, 70 e 90.')}
            {toggle('ICMS-ST', !!v.icmsSt, (b) => set({ icmsSt: b ? { baseModality: 4, mva: 0, internalRate: 18, baseReduction: 0 } : null }), 'Substituição tributária (CST 10, 30, 70 ou 90).')}
            {v.icmsSt ? (
              <>
                <div className="flex items-end">{pct('icmsSt.mva', 'MVA', v.icmsSt.mva, (n) => set({ icmsSt: { ...v.icmsSt!, mva: n } }))}<Dica nome="MVA" texto={DICAS.mva} /></div>
                {pct('icmsSt.internalRate', 'Alíquota interna do destino (ST)', v.icmsSt.internalRate, (n) => set({ icmsSt: { ...v.icmsSt!, internalRate: n } }))}
              </>
            ) : null}
            {toggle('FCP', !!v.fcp, (b) => set({ fcp: b ? { rate: 2 } : null }), 'Fundo de Combate à Pobreza na operação interna.')}
            {v.fcp ? pct('fcp.rate', 'Alíquota do FCP', v.fcp.rate, (n) => set({ fcp: { rate: n } })) : null}
            {toggle('Marca DIFAL', !!v.difal, (b) => set({ difal: b ? { destinationInternalRate: v.icms!.internalRate } : null }), 'Venda a não contribuinte de outro estado: o cálculo mostra o DIFAL e a emissão é bloqueada.')}
            {v.difal ? pct('difal.destinationInternalRate', 'Alíquota interna do destino (DIFAL)', v.difal.destinationInternalRate, (n) => set({ difal: { destinationInternalRate: n } })) : null}
          </Section>
        ) : null}

        {v.ipi ? (
          <Section title="IPI">
            {text('ipi.cst', 'CST do IPI', v.ipi.cst, (s) => set({ ipi: { ...v.ipi!, cst: s } }), { maxLength: 2, hint: 'Saída: 50 a 55 ou 99.' })}
            {text('ipi.framework', 'Enquadramento', v.ipi.framework, (s) => set({ ipi: { ...v.ipi!, framework: s } }), { maxLength: 3, hint: '999 quando não há enquadramento específico.' })}
            {toggle('Pela TIPI do NCM', v.ipi.fromNcm, (b) => set({ ipi: { ...v.ipi!, fromNcm: b } }), 'A alíquota vem da TIPI pelo NCM de cada item.')}
            {!v.ipi.fromNcm ? pct('ipi.rate', 'Alíquota do IPI', v.ipi.rate, (n) => set({ ipi: { ...v.ipi!, rate: n } })) : null}
          </Section>
        ) : null}

        <Section title="PIS e COFINS">
          {text('pis.cst', 'CST do PIS', v.pis?.cst, (s) => set({ pis: { rate: v.pis?.rate ?? 0, cst: s } }), { maxLength: 2 })}
          {pct('pis.rate', 'Alíquota do PIS', v.pis?.rate ?? 0, (n) => set({ pis: { cst: v.pis?.cst ?? '01', rate: n } }))}
          {text('cofins.cst', 'CST da COFINS', v.cofins?.cst, (s) => set({ cofins: { rate: v.cofins?.rate ?? 0, cst: s } }), { maxLength: 2 })}
          {pct('cofins.rate', 'Alíquota da COFINS', v.cofins?.rate ?? 0, (n) => set({ cofins: { cst: v.cofins?.cst ?? '01', rate: n } }))}
        </Section>

        {v.iss ? (
          <Section title="ISS (NFS-e)">
            {text('iss.serviceItem', 'Item da LC 116', v.iss.serviceItem, (s) => set({ iss: { ...v.iss!, serviceItem: s } }), { maxLength: 5, hint: 'Ex.: 7.02.' })}
            {pct('iss.rate', 'Alíquota do ISS', v.iss.rate, (n) => set({ iss: { ...v.iss!, rate: n } }))}
            <Field label="Local de incidência">
              {(p) => (
                <select {...p} value={v.iss!.location} onChange={(e) => set({ iss: { ...v.iss!, location: e.target.value as IssLocation } })} className={inputClass}>
                  <option value="WorkSite">Município da obra</option>
                  <option value="Company">Município da empresa</option>
                </select>
              )}
            </Field>
            {toggle('ISS retido pelo tomador', v.iss.withheld, (b) => set({ iss: { ...v.iss!, withheld: b } }))}
          </Section>
        ) : null}

        <Section title="IBS e CBS" dica={DICAS.ibsCbs} dicaName="IBS/CBS">
          {toggle('Calcular IBS e CBS', !!v.ibsCbs, (b) => set({ ibsCbs: b ? { cst: '000', classification: '000001', cbsRate: 0.9, ibsStateRate: 0.1, ibsCityRate: 0 } : null }))}
          {v.ibsCbs ? (
            <>
              {text('ibsCbs.cst', 'CST do IBS/CBS', v.ibsCbs.cst, (s) => set({ ibsCbs: { ...v.ibsCbs!, cst: s } }), { maxLength: 3 })}
              {text('ibsCbs.classification', 'Classificação tributária (cClassTrib)', v.ibsCbs.classification, (s) => set({ ibsCbs: { ...v.ibsCbs!, classification: s } }), { maxLength: 6 })}
              {pct('ibsCbs.cbsRate', 'CBS', v.ibsCbs.cbsRate, (n) => set({ ibsCbs: { ...v.ibsCbs!, cbsRate: n } }))}
              {pct('ibsCbs.ibsStateRate', 'IBS estadual', v.ibsCbs.ibsStateRate, (n) => set({ ibsCbs: { ...v.ibsCbs!, ibsStateRate: n } }))}
              {pct('ibsCbs.ibsCityRate', 'IBS municipal', v.ibsCbs.ibsCityRate, (n) => set({ ibsCbs: { ...v.ibsCbs!, ibsCityRate: n } }))}
            </>
          ) : null}
        </Section>

        <Section title="Custo do projeto e informação complementar" dica={DICAS.custo} dicaName="Entra no custo do projeto">
          <fieldset id="campo-costTaxes" className="sm:col-span-2">
            <legend className="text-sm font-medium">Tributos que entram no custo do projeto</legend>
            <div className="flex flex-wrap gap-3">
              {COST_TAX_OPTIONS.map((o) => (
                <label key={o.value} className="flex min-h-11 items-center gap-2 text-sm">
                  <input type="checkbox" className="size-4 accent-primary" checked={v.costTaxes.includes(o.value)}
                    onChange={(e) => set({ costTaxes: e.target.checked ? [...v.costTaxes, o.value] : v.costTaxes.filter((t) => t !== o.value) })} />
                  {o.label}
                </label>
              ))}
            </div>
            {err('costTaxes') ? <p className="text-xs text-crit-ink">{err('costTaxes')}</p> : null}
          </fieldset>
          <div id="campo-complementaryInfo" className="sm:col-span-2">
            <Field label="Informação complementar padrão" error={err('complementaryInfo')}>{(p) => <textarea {...p} value={v.complementaryInfo ?? ''} maxLength={2000} onChange={(e) => set({ complementaryInfo: e.target.value || null })} className={inputClass} />}</Field>
          </div>
        </Section>

        {editable ? (
          <div className="flex flex-wrap justify-end gap-2">
            <Link to="/app/fiscal/regras" className={secondary}>Voltar</Link>
            <button type="submit" disabled={busy} className={primary}>{busy ? 'Salvando…' : 'Salvar regra fiscal'}</button>
          </div>
        ) : null}
      </fieldset>
    </form>
  )
}
