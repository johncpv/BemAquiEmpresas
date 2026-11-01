import { useQueryClient } from '@tanstack/react-query'
import {
  createAssetSite, getListAssetSitesQueryKey, listAssetInvoiceChoices, listAssetSupplierChoices, suggestAssetPlacement, useListAssetResponsibleChoices, useListAssetSites,
  useListAssetWorkCenterChoices,
} from '@/api/generated/bemaqui'
import type { AssetType } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { notifyError, notifySuccess } from '@/components/ui/messages'
import { ASSET_DICAS, type AssetDraft, applySuggestion, invoiceLabel, TYPE_OPTIONS } from './assets'

interface Props {
  draft: AssetDraft
  onChange: (next: AssetDraft) => void
  errors: Partial<Record<string, string>>
  /** assets.edit: vê e informa o valor de aquisição e cria local no combobox. */
  canEditValue: boolean
  disabled?: boolean
  /** Responsável que já estava no bem e foi desligado (CA 4): continua na lista, marcado. */
  terminatedResponsible?: { id: string; name: string } | null
}

/** Formulário do bem em seções (Identificação, Aquisição, Onde está), usado no Novo bem e na aba Dados. */
export function AssetForm({ draft, onChange, errors, canEditValue, disabled, terminatedResponsible }: Props) {
  const client = useQueryClient()
  const responsibles = useListAssetResponsibleChoices()
  const centers = useListAssetWorkCenterChoices()
  const sites = useListAssetSites()
  const set = (patch: Partial<AssetDraft>) => onChange({ ...draft, ...patch })
  const activeSites = (sites.data ?? []).filter((s) => s.isActive || s.id === draft.siteId)

  const chooseCenter = async (workCenterId: string) => {
    const next = { ...draft, workCenterId }
    onChange(next)
    if (!workCenterId) return
    try {
      onChange(applySuggestion(next, await suggestAssetPlacement({ workCenterId })))
    } catch {
      // Sem sugestão: o usuário escolhe.
    }
  }

  const createSite = async (name: string) => {
    try {
      const site = await createAssetSite({ name })
      await client.invalidateQueries({ queryKey: getListAssetSitesQueryKey() })
      notifySuccess('Local criado')
      set({ siteId: site.id, touched: { ...draft.touched, site: true } })
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível criar o local.')
    }
  }

  return (
    <fieldset disabled={disabled} className="space-y-4">
      <FormSection title="Identificação">
        <Field label="Nome" required error={errors.name}>
          {(p) => <input {...p} value={draft.name} maxLength={150} autoComplete="off" onChange={(e) => set({ name: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Tipo" required error={errors.type}>
          {(p) => (
            <select {...p} value={draft.type} onChange={(e) => set({ type: e.target.value as AssetType | '' })} className={inputClass}>
              <option value="">Escolha</option>
              {TYPE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          )}
        </Field>
        <Field label="Marca" error={errors.brand}>{(p) => <input {...p} value={draft.brand} maxLength={80} onChange={(e) => set({ brand: e.target.value })} className={inputClass} />}</Field>
        <Field label="Modelo" error={errors.model}>{(p) => <input {...p} value={draft.model} maxLength={80} onChange={(e) => set({ model: e.target.value })} className={inputClass} />}</Field>
        <Field label="Número de série" error={errors.serialNumber}>{(p) => <input {...p} value={draft.serialNumber} maxLength={80} autoComplete="off" onChange={(e) => set({ serialNumber: e.target.value })} className={inputClass} />}</Field>
        <Field label="Observação" error={errors.notes}>{(p) => <input {...p} value={draft.notes} maxLength={1000} onChange={(e) => set({ notes: e.target.value })} className={inputClass} />}</Field>
      </FormSection>
      <FormSection title="Aquisição" description="Com a nota de entrada do sistema, o fornecedor e a data vêm dela.">
        <Field label="NF de entrada" error={errors.incomingInvoiceId} hint={draft.incomingInvoiceId ? undefined : 'Só notas importadas no Fiscal. Bem antigo? Use a referência ao lado.'}>
          {(p) => (
            <Combobox {...p} queryKey={['asset-invoices']} placeholder="Buscar nota pelo número"
              selected={draft.incomingInvoiceId ? { id: draft.incomingInvoiceId, label: draft.invoiceLabel ?? 'Nota escolhida' } : null}
              search={async (q) => (await listAssetInvoiceChoices({ q })).map((i) => ({ id: i.id, label: invoiceLabel(i.number, i.series, i.issuerName), hint: i.issueDate, invoice: i }))}
              onChange={(o) => {
                const invoice = (o as { invoice?: { issuerPartyId?: string | null; issuerName: string; issueDate: string } } | null)?.invoice
                set(o && invoice
                  ? { incomingInvoiceId: o.id, invoiceLabel: o.label, invoiceRef: '', supplierPartyId: invoice.issuerPartyId ?? draft.supplierPartyId, supplierName: invoice.issuerPartyId ? invoice.issuerName : draft.supplierName, acquisitionDate: invoice.issueDate }
                  : { incomingInvoiceId: null, invoiceLabel: null })
              }} />
          )}
        </Field>
        <Field label="Referência da nota" error={errors.invoiceRef} hint="Número e série, sem nota no sistema.">
          {(p) => <input {...p} value={draft.invoiceRef} maxLength={30} disabled={!!draft.incomingInvoiceId} onChange={(e) => set({ invoiceRef: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Fornecedor" error={errors.supplierPartyId}>
          {(p) => (
            <Combobox {...p} queryKey={['asset-suppliers']} placeholder="Buscar fornecedor"
              selected={draft.supplierPartyId ? { id: draft.supplierPartyId, label: draft.supplierName ?? 'Fornecedor escolhido' } : null}
              search={async (q) => (await listAssetSupplierChoices({ q })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))}
              onChange={(o) => set({ supplierPartyId: o?.id ?? null, supplierName: o?.label ?? null })} />
          )}
        </Field>
        <Field label="Data de aquisição" error={errors.acquisitionDate}>{(p) => <DateInput {...p} value={draft.acquisitionDate} onChange={(acquisitionDate) => set({ acquisitionDate })} />}</Field>
        {canEditValue ? (
          <Field label="Valor de aquisição" error={errors.acquisitionValue} hint="Só quem edita o patrimônio vê e informa.">
            {(p) => <MoneyInput {...p} value={draft.acquisitionValue} onChange={(acquisitionValue) => set({ acquisitionValue })} />}
          </Field>
        ) : null}
      </FormSection>
      <FormSection title="Onde está">
        <Field label="Centro de trabalho" dica={ASSET_DICAS.centro} error={errors.workCenterId}>
          {(p) => (
            <select {...p} value={draft.workCenterId} onChange={(e) => void chooseCenter(e.target.value)} className={inputClass}>
              <option value="">Sem centro</option>
              {(centers.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Local" error={errors.siteId} hint={draft.touched.site ? undefined : 'O do último bem do centro, enquanto você não trocar.'}>
          {(p) => (
            <Combobox {...p} queryKey={['asset-sites', activeSites.length]} placeholder="Escolher local"
              selected={draft.siteId ? { id: draft.siteId, label: activeSites.find((s) => s.id === draft.siteId)?.name ?? 'Local escolhido' } : null}
              search={async (q) => activeSites.filter((s) => s.name.toLowerCase().includes(q.toLowerCase())).map((s) => ({ id: s.id, label: s.name }))}
              onChange={(o) => set({ siteId: o?.id ?? '', touched: { ...draft.touched, site: true } })}
              onCreate={canEditValue ? (name) => void createSite(name) : undefined} createLabel="Criar local" />
          )}
        </Field>
        <Field label="Responsável" error={errors.responsibleEmployeeId} hint={draft.touched.responsible ? undefined : 'O mais comum entre os bens do centro, enquanto você não trocar.'}
          warning={terminatedResponsible && draft.responsibleEmployeeId === terminatedResponsible.id ? 'Desligado. Troque o responsável.' : undefined}>
          {(p) => (
            <select {...p} value={draft.responsibleEmployeeId} onChange={(e) => set({ responsibleEmployeeId: e.target.value, touched: { ...draft.touched, responsible: true } })} className={inputClass}>
              <option value="">Sem responsável</option>
              {terminatedResponsible ? <option value={terminatedResponsible.id}>{terminatedResponsible.name} (Desligado)</option> : null}
              {(responsibles.data ?? []).map((r) => <option key={r.id} value={r.id}>{r.name}{r.registration ? ` · ${r.registration}` : ''} · {r.positionName}</option>)}
            </select>
          )}
        </Field>
      </FormSection>
    </fieldset>
  )
}
