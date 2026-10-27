import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Download } from 'lucide-react'
import { useState } from 'react'
import {
  confirmFiscalSettings, getExportFiscalSettingsPdfUrl, getGetFiscalSettingsQueryKey, saveFiscalSettings, undoFiscalSettingConfirmation, useGetFiscalSettings,
} from '@/api/generated/bemaqui'
import type { FiscalSettingReview, FiscalSettingsSaved, FiscalSettingsValues, FiscalSettingsView } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput } from '@/components/form/inputs'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { ToConfirmChip } from '@/components/ui/to-confirm-chip'
import { Dica } from '@/help/Dica'
import { useNavigation } from '@/navigation/navigation-context'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { FiscalNav } from './FiscalNav'
import { confirmedText, CRT_LABEL, crtOf, COST_TAX_OPTIONS, DICAS, fieldAnchor, FIRST_CONFIGURATION, formatCnae, parseCnaes, PRODUCTION_TEXT, SECTION_FIELDS, turnsOnProduction, type FieldConfig } from './fiscal-settings'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'

/**
 * Configurações → Fiscal (FIS-02a CA 3 a 10, ADR 0042): todos os parâmetros fiscais já preenchidos com os valores propostos, cada um
 * com o selo "A confirmar com a contadora" e "Marcar como conferido" (por campo ou seção); empresa só leitura (CAD-06); pré-condições;
 * produção com o diálogo "Ligar a emissão com valor fiscal" e o código do autenticador; "Baixar para a contadora". Sem settings.edit,
 * modo leitura. Módulo Fiscal não liberado: a tela padrão.
 */
export function FiscalSettingsPage() {
  const query = useGetFiscalSettings({ query: { retry: false, gcTime: 0 } })
  const { all } = useNavigation()
  const fiscal = all.find((m) => m.code === 'fiscal')
  if (fiscal?.locked || (query.error instanceof ApiError && query.error.code === 'module_not_released')) {
    return fiscal ? <ModuleNotReleased module={fiscal} /> : null
  }

  return (
    <>
      <PageHeading title="Fiscal">
        <p className="mt-2 text-muted-foreground">Regime, série, operações, alíquotas e NFS-e das notas da empresa. Os valores já vêm preenchidos; confira cada um com a contadora.</p>
      </PageHeading>
      <SettingsTabs />
      <FiscalNav />
      {query.isPending ? (
        <div className="space-y-3"><Skeleton className="h-10" /><Skeleton className="h-64" /></div>
      ) : query.isError || !query.data ? (
        <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : (
        <FiscalForm key={JSON.stringify(query.data.values)} view={query.data} />
      )}
    </>
  )
}

type Pending = { kind: 'production' } | { kind: 'series'; message: string } | null

function FiscalForm({ view }: { view: FiscalSettingsView }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const canEdit = view.canEdit
  const [values, setValues] = useState<FiscalSettingsValues>({ ...view.values, cnaeMain: formatCnae(view.values.cnaeMain) })
  const [cnaes, setCnaes] = useState(view.values.cnaeSecondary.map(formatCnae).join(', '))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [pending, setPending] = useState<Pending>(null)
  const [code, setCode] = useState('')
  const [notice, setNotice] = useState<string | null>(null)
  const [sectionToConfirm, setSectionToConfirm] = useState<string | null>(null)
  const [stepsOpen, setStepsOpen] = useState(true)
  const reviews = new Map(view.reviews.map((r) => [r.key, r]))
  const set = <K extends keyof FiscalSettingsValues>(key: K, value: FiscalSettingsValues[K]) => setValues((v) => ({ ...v, [key]: value }))
  const store = (data: FiscalSettingsView) => client.setQueryData(getGetFiscalSettingsQueryKey(), data)
  const current = (): FiscalSettingsValues => ({ ...values, cnaeSecondary: parseCnaes(cnaes) })

  const send = async (extra: { code?: string; confirmSeriesChange?: boolean }) => {
    const saved: FiscalSettingsSaved = await saveFiscalSettings({ values: current(), code: extra.code ?? null, confirmSeriesChange: extra.confirmSeriesChange ?? false })
    store(saved.settings)
    setNotice(saved.notice ?? null)
    notifySuccess('Configuração fiscal salva')
  }

  const fail = (e: unknown) => {
    if (e instanceof ApiError) {
      const fields = Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0] ?? '']))
      setErrors(fields)
      const first = Object.keys(fields)[0]
      if (first) document.getElementById(fieldAnchor(first))?.querySelector<HTMLElement>('input,select,textarea')?.focus()
      if (Object.keys(fields).length === 0) setFormError(e.message)
      if (e.code === 'fiscal_series_in_use') setPending({ kind: 'series', message: e.message })
    } else {
      setFormError('Não foi possível salvar a configuração fiscal. Tente de novo.')
    }
  }

  const save = async () => {
    setErrors({})
    setFormError(undefined)
    if (turnsOnProduction(view.values, current())) {
      setCode('')
      setPending({ kind: 'production' })
      return
    }

    setBusy(true)
    try {
      await send({})
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  const confirmKeys = async (keys: string[]) => store(await confirmFiscalSettings({ keys }))
  const undo = async (key: string) => {
    store(await undoFiscalSettingConfirmation(key))
    notifySuccess('Conferência desfeita')
  }

  const download = () => apiDownload(getExportFiscalSettingsPdfUrl(), 'configuracao-fiscal-para-a-contadora.pdf')
  const allConfirmed = view.confirmedCount === view.totalCount
  const steps = [
    ...(view.certificate.status === 'None' || view.certificate.status === 'Expired' ? [{ label: 'Enviar certificado', onSelect: () => void navigate({ to: '/app/configuracoes/fiscal/certificado' }) }] : []),
    { label: 'Baixar para a contadora', onSelect: () => void download() },
  ]

  return (
    <div className="space-y-4">
      <EnvironmentBanner service="sefaz" test={view.values.environment !== 'Production'} />
      <div className={view.firstConfiguration ? 'rounded-md border border-warn-soft bg-warn-soft p-3' : 'rounded-md border border-border bg-card p-3'}>
        {view.firstConfiguration ? <p className="text-sm font-semibold text-warn-ink">{FIRST_CONFIGURATION}</p> : null}
        <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
          <p role="status" className="text-sm font-semibold tabular-nums">{confirmedText(view.confirmedCount, view.totalCount)}</p>
          {canEdit ? (
            <button type="button" onClick={() => void download()} className={view.firstConfiguration ? primary : secondary}>
              <Download aria-hidden="true" className="mr-1 inline size-4" />Baixar para a contadora
            </button>
          ) : null}
        </div>
      </div>
      {allConfirmed && stepsOpen ? <NextSteps steps={steps} onDismiss={() => setStepsOpen(false)} /> : null}
      {view.preconditions.length > 0 ? (
        <div role="alert" className="space-y-1 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
          <p className="font-semibold">A emissão fica bloqueada até resolver:</p>
          <ul className="list-disc pl-5">
            {view.preconditions.map((p) => <li key={p.key}><a href={p.link} className="underline">{p.message}</a></li>)}
          </ul>
        </div>
      ) : null}
      {notice ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm font-semibold text-warn-ink">{notice}</p> : null}
      {!canEdit ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Modo leitura: só quem edita Configurações altera e confere os valores fiscais.</p> : null}

      <div className="grid gap-4 lg:grid-cols-[220px_1fr]">
        <details open className="h-fit rounded-md border border-border bg-card p-3 lg:sticky lg:top-4 max-lg:[&:not([open])]:pb-3">
          <summary className="cursor-pointer text-sm font-semibold">Seções</summary>
          <nav aria-label="Seções da configuração fiscal" className="mt-2">
            <ul className="space-y-1 text-sm">
              <li><a href="#secao-empresa" className="flex min-h-11 items-center rounded px-2 hover:bg-muted lg:min-h-0 lg:py-1">Empresa</a></li>
              {view.sections.map((s) => (
                <li key={s.code}>
                  <a href={'#' + (s.firstPendingKey ? fieldAnchor(s.firstPendingKey) : 'secao-' + s.code)} className="flex min-h-11 items-center justify-between gap-2 rounded px-2 hover:bg-muted lg:min-h-0 lg:py-1">
                    <span>{s.title}</span>
                    {s.pending > 0 ? <span className="rounded-full bg-warn-soft px-2 text-xs font-semibold tabular-nums text-warn-ink">{s.pending}</span> : <span className="text-xs text-ok-ink">ok</span>}
                  </a>
                </li>
              ))}
            </ul>
          </nav>
          <Link to="/app/configuracoes/fiscal/certificado" className="mt-3 flex min-h-11 items-center px-2 text-sm font-semibold text-primary underline lg:min-h-0">Certificado digital</Link>
        </details>

        <form onSubmit={(e) => { e.preventDefault(); void save() }} noValidate aria-label="Configuração fiscal" className="min-w-0 space-y-4">
          <FormError message={formError} />
          <section id="secao-empresa" aria-labelledby="t-empresa" className="rounded-md border border-border bg-card p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="t-empresa" className="text-[15px] font-semibold">Empresa</h2>
              <Link to="/app/configuracoes/empresa" className="text-sm font-semibold text-primary underline">Editar dados da empresa</Link>
            </div>
            <p className="mb-2 text-xs text-muted-foreground">Vem de Configurações → Empresa. O Fiscal lê daqui, sem copiar.</p>
            <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
              <dt className="text-muted-foreground">Razão social</dt><dd className="min-w-0 break-words">{view.company.legalName}</dd>
              <dt className="text-muted-foreground">CNPJ</dt><dd className="tabular-nums">{view.company.document ?? '—'}</dd>
              <dt className="text-muted-foreground">Inscrição estadual</dt><dd className="tabular-nums">{view.company.stateRegistration ?? 'Não informada'}</dd>
              <dt className="text-muted-foreground">Inscrição municipal</dt><dd className="tabular-nums">{view.company.municipalRegistration ?? 'Não informada'}</dd>
              <dt className="text-muted-foreground">Endereço</dt><dd className="min-w-0 break-words">{view.company.address ?? 'Não informado'}</dd>
              <dt className="text-muted-foreground">Código IBGE do município</dt><dd className="tabular-nums">{view.company.cityIbge ?? 'Não informado'}</dd>
            </dl>
          </section>

          <fieldset disabled={!canEdit} className="min-w-0 space-y-4">
            {view.sections.map((section) => (
              <section key={section.code} id={'secao-' + section.code} aria-labelledby={'t-' + section.code} className="min-w-0 rounded-md border border-border bg-card p-4">
                <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                  <h2 id={'t-' + section.code} className="text-[15px] font-semibold">{section.title}</h2>
                  {canEdit && section.pending > 0 ? (
                    <button type="button" onClick={() => setSectionToConfirm(section.code)} className={secondary}>Marcar seção como conferida</button>
                  ) : null}
                </div>
                <div className="grid gap-4 sm:grid-cols-2">
                  {SECTION_FIELDS[section.code]?.map((field) => {
                    const review = reviews.get(field.key)
                    return (
                      <div key={field.key} id={fieldAnchor(field.key)} className="min-w-0 space-y-1 scroll-mt-4">
                        <FieldInput field={field} label={review?.label ?? field.key} values={values} cnaes={cnaes} error={errors[field.key]} set={set} setCnaes={setCnaes} />
                        {review ? <ReviewChip review={review} canEdit={canEdit} onConfirm={() => confirmKeys([review.key])} onUndo={() => undo(review.key)} /> : null}
                      </div>
                    )
                  })}
                </div>
              </section>
            ))}
            {canEdit ? (
              <div className="flex flex-wrap justify-end gap-2">
                <button type="submit" disabled={busy} className={primary}>{busy ? 'Salvando…' : 'Salvar configuração fiscal'}</button>
              </div>
            ) : null}
          </fieldset>
          {view.id ? (
            <details className="rounded-md border border-border bg-card p-3 text-sm">
              <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
              <div className="mt-2"><RecordHistory entity="FiscalSettings" id={view.id} /></div>
            </details>
          ) : null}
        </form>
      </div>

      <ConfirmDialog
        open={sectionToConfirm !== null}
        onOpenChange={(o) => { if (!o) setSectionToConfirm(null) }}
        verb="Marcar seção como conferida"
        done="Conferido"
        title={'Conferir ' + (view.sections.find((s) => s.code === sectionToConfirm)?.title ?? '')}
        description="Confirme só o que a contadora já conferiu. Mudar um valor depois volta o selo para &quot;A confirmar&quot;."
        onConfirm={() => confirmKeys(view.reviews.filter((r) => r.section === sectionToConfirm && r.reviewState !== 'Confirmed').map((r) => r.key))}
      >
        <ul className="list-disc space-y-1 pl-5 text-sm">
          {view.reviews.filter((r) => r.section === sectionToConfirm && r.reviewState !== 'Confirmed').map((r) => <li key={r.key}>{r.label}</li>)}
        </ul>
      </ConfirmDialog>

      <ConfirmDialog
        open={pending?.kind === 'production'}
        onOpenChange={(o) => { if (!o) setPending(null) }}
        verb="Ligar a emissão com valor fiscal"
        done="Configuração fiscal salva"
        title="Ligar a emissão com valor fiscal"
        description={PRODUCTION_TEXT}
        size="lg"
        onConfirm={async () => {
          if (!/^\d{6}$/.test(code)) throw new DialogMessage('Digite os 6 números do aplicativo autenticador.')
          await saveFiscalSettings({ values: current(), code, confirmSeriesChange: false }).then((saved) => { store(saved.settings); setNotice(saved.notice ?? null) })
        }}
        onFieldErrors={(e) => { fail(e); setPending(null); return true }}
      >
        <TwoFactorCodeField code={code} onChange={setCode} />
      </ConfirmDialog>

      <ConfirmDialog
        open={pending?.kind === 'series'}
        onOpenChange={(o) => { if (!o) setPending(null) }}
        verb="Trocar a série"
        done="Configuração fiscal salva"
        title="Trocar a série da NF-e"
        description={pending?.kind === 'series' ? pending.message : undefined}
        onConfirm={async () => {
          const saved = await saveFiscalSettings({ values: current(), code: null, confirmSeriesChange: true })
          store(saved.settings)
          setNotice(saved.notice ?? null)
        }}
      />
    </div>
  )
}

function ReviewChip({ review, canEdit, onConfirm, onUndo }: { review: FiscalSettingReview; canEdit: boolean; onConfirm: () => Promise<unknown>; onUndo: () => Promise<unknown> }) {
  return (
    <span className="flex flex-wrap items-center gap-2">
      <ToConfirmChip state={review.reviewState} reviewedByName={review.reviewedByName} reviewedAt={review.reviewedAt} canEdit={canEdit} field={review.label} onConfirm={onConfirm} />
      {canEdit && review.reviewState === 'Confirmed' ? (
        <button type="button" onClick={() => void onUndo()} aria-label={'Desfazer conferência: ' + review.label} className="min-h-11 rounded-md px-2 text-xs font-semibold text-muted-foreground underline sm:min-h-0">
          Desfazer conferência
        </button>
      ) : null}
    </span>
  )
}

function FieldInput({ field, label, values, cnaes, error, set, setCnaes }: {
  field: FieldConfig
  label: string
  values: FiscalSettingsValues
  cnaes: string
  error?: string
  set: <K extends keyof FiscalSettingsValues>(key: K, value: FiscalSettingsValues[K]) => void
  setCnaes: (v: string) => void
}) {
  if (field.kind === 'derived') {
    return (
      <div className="space-y-1">
        <p className="flex items-center text-sm font-medium">{label}<Dica nome={label} texto={DICAS.crt} /></p>
        <p className="rounded-md border border-border bg-muted px-3 py-2 text-sm tabular-nums">{CRT_LABEL[crtOf(values.regime)]}</p>
        <p className="text-xs text-muted-foreground">Sai do regime tributário.</p>
      </div>
    )
  }

  if (field.kind === 'fixed') {
    return (
      <div className="space-y-1 sm:col-span-2">
        <p className="flex items-center text-sm font-medium">{label}{field.dica ? <Dica nome={label} texto={field.dica} /> : null}</p>
        <p className="rounded-md border border-border bg-muted px-3 py-2 text-sm">{field.text}</p>
      </div>
    )
  }

  const key = field.key as keyof FiscalSettingsValues
  if (field.kind === 'bool') {
    return (
      <label className="flex min-h-11 items-start gap-2 text-sm">
        <input type="checkbox" checked={Boolean(values[key])} onChange={(e) => set(key, e.target.checked as never)} className="mt-0.5 size-4 accent-primary" />
        <span><b className="font-medium">{label}</b>{field.hint ? <span className="block text-xs text-muted-foreground">{field.hint}</span> : null}</span>
      </label>
    )
  }

  if (field.kind === 'costTaxes') {
    const chosen = new Set(values.costTaxes)
    return (
      <fieldset className="space-y-1 sm:col-span-2">
        <legend className="flex items-center text-sm font-medium">{label}{field.dica ? <Dica nome="Entra no custo do projeto" texto={field.dica} /> : null}</legend>
        <div className="flex flex-wrap gap-3">
          {COST_TAX_OPTIONS.map((o) => (
            <label key={o.value} className="flex min-h-11 items-center gap-2 text-sm">
              <input type="checkbox" checked={chosen.has(o.value)} className="size-4 accent-primary"
                onChange={(e) => set('costTaxes', e.target.checked ? [...values.costTaxes, o.value] : values.costTaxes.filter((t) => t !== o.value))} />
              {o.label}
            </label>
          ))}
        </div>
        {error ? <p className="text-xs text-crit-ink">{error}</p> : null}
      </fieldset>
    )
  }

  return (
    <Field label={label} error={error} hint={field.hint} dica={field.dica}>
      {(p) => {
        switch (field.kind) {
          case 'select':
            return (
              <select {...p} value={String(values[key])} onChange={(e) => set(key, (field.key === 'freightMode' ? Number(e.target.value) : e.target.value) as never)} className={inputClass}>
                {field.options?.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            )
          case 'percent':
            return <PercentInput {...p} scale={4} value={String(values[key])} onChange={(v) => set(key, Number(v ?? 0) as never)} />
          case 'int':
            return (
              <input {...p} inputMode="numeric" maxLength={field.maxLength} value={values[key] == null ? '' : String(values[key])}
                onChange={(e) => { const d = e.target.value.replace(/\D/g, ''); set(key, (d === '' ? (field.key === 'nfeSeries' ? null : 0) : Number(d)) as never) }}
                className={inputClass + ' tabular-nums sm:max-w-40'} />
            )
          case 'cnaes':
            return <input {...p} value={cnaes} onChange={(e) => setCnaes(e.target.value)} className={inputClass + ' tabular-nums'} />
          default:
            return (
              <input {...p} maxLength={field.maxLength} value={(values[key] as string | null) ?? ''} spellCheck={false}
                onChange={(e) => set(key, e.target.value as never)} className={inputClass + ' tabular-nums'} />
            )
        }
      }}
    </Field>
  )
}
