import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { confirmBillingSetting, getGetBillingSettingsQueryKey, updateBillingSettings, useGetBillingSettings } from '@/api/generated/bemaqui'
import type { BillingReviewItem, BillingSettingsView, OverdueFineFrom } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { ToConfirmChip } from '@/components/ui/to-confirm-chip'
import { useNavigation } from '@/navigation/navigation-context'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { CollectionRuleSection } from './CollectionRuleSection'
import { BILLING_DICAS, FINE_KEY, INTEREST_KEY, OVERDUE_KEY, OVERDUE_OPTIONS, pendingText } from './billing-settings'

/**
 * Configurações → Cobrança (BNK-01b CA 1 a 3, ADR 0042): multa, juros, desconto padrão, envio do boleto e a regra do vencido, já
 * pré-configurados; multa, juros e a regra do vencido com o selo "A confirmar com a contadora" até alguém conferir. Sem settings.edit,
 * modo leitura. Módulo Financeiro não liberado: a tela padrão.
 */
export function BillingSettingsPage() {
  const query = useGetBillingSettings({ query: { retry: false } })
  const { all } = useNavigation()
  const finance = all.find((m) => m.code === 'finance')
  if (finance?.locked || (query.error instanceof ApiError && query.error.code === 'module_not_released')) {
    return finance ? <ModuleNotReleased module={finance} /> : null
  }

  return (
    <>
      <PageHeading title="Cobrança">
        <p className="mt-2 text-muted-foreground">Multa, juros, desconto, envio do boleto com Pix e a régua de cobrança por e-mail. Os valores já vêm preenchidos; confira com a contadora.</p>
      </PageHeading>
      <SettingsTabs />
      {query.isPending ? (
        <div className="space-y-3"><Skeleton className="h-10" /><Skeleton className="h-64" /></div>
      ) : query.isError || !query.data ? (
        <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : (
        <BillingForm key={JSON.stringify(query.data)} view={query.data} />
      )}
      <CollectionRuleSection />
      <ScrollToHash />
    </>
  )
}

/** Abre a tela já na seção do endereço (ex.: #regua), depois que ela aparece. */
function ScrollToHash() {
  useEffect(() => {
    const hash = window.location.hash.slice(1)
    if (!hash) return
    const timer = window.setTimeout(() => document.getElementById(hash)?.scrollIntoView({ block: 'start' }), 300)
    return () => window.clearTimeout(timer)
  }, [])
  return null
}

function BillingForm({ view }: { view: BillingSettingsView }) {
  const client = useQueryClient()
  const canEdit = view.canEdit
  const [fine, setFine] = useState<string | null>(String(view.finePct))
  const [interest, setInterest] = useState<string | null>(String(view.interestMonthPct))
  const [discount, setDiscount] = useState<string | null>(String(view.defaultDiscountPct))
  const [autoEmail, setAutoEmail] = useState(view.autoEmail)
  const [limit, setLimit] = useState(String(view.dailyEmailLimit))
  const [from, setFrom] = useState<OverdueFineFrom>(view.overdueFineFrom)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const [busy, setBusy] = useState(false)
  const review = (key: string) => view.reviews.find((r) => r.key === key)
  const field = (k: string) => error?.fields[k]?.[0]

  const save = async () => {
    setBusy(true)
    setError(null)
    try {
      const saved = await updateBillingSettings({ finePct: Number(fine ?? 0), interestMonthPct: Number(interest ?? 0), defaultDiscountPct: Number(discount ?? 0), autoEmail, dailyEmailLimit: Number(limit || 0), overdueFineFrom: from })
      client.setQueryData(getGetBillingSettingsQueryKey(), saved)
      notifySuccess('Cobrança salva')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a cobrança. Tente de novo.', fields: {} })
    } finally {
      setBusy(false)
    }
  }

  const confirm = async (key: string) => {
    const saved = await confirmBillingSetting({ key })
    client.setQueryData(getGetBillingSettingsQueryKey(), saved)
  }

  const chip = (item: BillingReviewItem | undefined, label: string) => item ? (
    <ToConfirmChip state={item.reviewState} reviewedByName={item.reviewedByName} reviewedAt={item.reviewedAt} canEdit={canEdit} field={label} onConfirm={() => confirm(item.key)} />
  ) : null

  // CA 23: "Histórico de alterações" de cada valor conferível (antes e depois, quem e quando).
  const history = (item: BillingReviewItem | undefined) => item?.parameterId ? (
    <details className="text-sm">
      <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Histórico de alterações</summary>
      <div className="mt-2"><RecordHistory entity="FinanceParameter" id={item.parameterId} /></div>
    </details>
  ) : null

  return (
    <div className="space-y-4">
      {view.pendingCount > 0 ? (
        <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm font-semibold text-warn-ink">{pendingText(view.pendingCount)}</p>
      ) : null}
      {!canEdit ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Modo leitura: só quem edita Configurações altera a cobrança.</p> : null}
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <FormSection title="Multa e juros" description="Valem para todo boleto novo. Acima destes valores, só quem aprova no Financeiro emite.">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <Field label="Multa" dica={BILLING_DICAS.multa} error={field('finePct')} hint="De 0,00% a 2,00%.">{(p) => <PercentInput {...p} value={fine} onChange={setFine} />}</Field>
              {chip(review(FINE_KEY), 'Multa')}
              {history(review(FINE_KEY))}
            </div>
            <div className="space-y-1">
              <Field label="Juros de mora (ao mês)" dica={BILLING_DICAS.juros} error={field('interestMonthPct')} hint="De 0,00% a 1,00% ao mês.">{(p) => <PercentInput {...p} value={interest} onChange={setInterest} />}</Field>
              {chip(review(INTEREST_KEY), 'Juros de mora')}
              {history(review(INTEREST_KEY))}
            </div>
          </div>
        </FormSection>
        <FormSection title="Desconto" description="Desconto sugerido no boleto, até o vencimento. O limite de desconto sem aprovação fica em Parâmetros.">
          <Field label="Desconto padrão" error={field('defaultDiscountPct')} hint="De 0% a 100% do valor do boleto.">{(p) => <PercentInput {...p} value={discount} onChange={setDiscount} />}</Field>
        </FormSection>
        <FormSection title="Envio do boleto" description="O boleto sai pela fila de e-mails, com o PDF do banco anexado.">
          <label className="flex min-h-11 items-start gap-2 text-sm">
            <input type="checkbox" checked={autoEmail} onChange={(e) => setAutoEmail(e.target.checked)} className="mt-0.5 size-4 accent-primary" />
            <span><b>Enviar o boleto por e-mail ao emitir</b><span className="block text-xs text-muted-foreground">Para o contato do cliente marcado "recebe cobrança". Dá para desmarcar em cada emissão.</span></span>
          </label>
          <Field label="Limite diário de e-mails" error={field('dailyEmailLimit')} hint="De 1 a 2.000 por dia, somando orçamentos, pedidos de compra e boletos. Acima, o boleto sai no dia seguinte às 08:00.">
            {(p) => <input {...p} inputMode="numeric" maxLength={4} value={limit} onChange={(e) => setLimit(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums sm:max-w-40'} />}
          </Field>
        </FormSection>
        <FormSection title="Vencido e dia não útil" description="Título vencido recebe boleto com vencimento novo; o título mantém o vencimento original.">
          <div className="space-y-1">
            <Field label="Multa e juros do título vencido contam do" error={field('overdueFineFrom')}>
              {(p) => (
                <select {...p} value={from} onChange={(e) => setFrom(e.target.value as OverdueFineFrom)} className={inputClass + ' sm:max-w-sm'}>
                  {OVERDUE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              )}
            </Field>
            {chip(review(OVERDUE_KEY), 'Multa e juros do título vencido')}
            {history(review(OVERDUE_KEY))}
          </div>
          <p className="text-sm"><b>Vencimento em dia não útil:</b> {view.nonBusinessDayRule}</p>
        </FormSection>
        {canEdit ? <button type="button" disabled={busy} onClick={() => void save()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar cobrança</button> : null}
      </fieldset>
    </div>
  )
}
