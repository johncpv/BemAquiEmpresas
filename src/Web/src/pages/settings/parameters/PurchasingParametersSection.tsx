import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetPurchasingParametersQueryKey, updatePurchasingParameters, useGetPurchasingParameters } from '@/api/generated/bemaqui'
import type { CostType, PurchasingParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { COST_SECTIONS } from '@/pages/quotes/quotes'
import { useCan } from '@/pages/use-can'

import { accountingPending, ATTACHMENT_DICA, costTypeLabel, FREIGHT_DICA, PAYABLE_DICA, parseTolerance, PENDING_LABEL, purchasingChanged, TOLERANCE_DICA } from './purchasing-parameters'

const LEAD_DICA = 'Quantos dias antes da necessidade a compra precisa ser feita. A falta da BOM pede o material para a data prometida do projeto menos este prazo; a solicitação manual e a do estoque mínimo sugerem hoje mais este prazo.'

/**
 * Compras (CMP-01, ADR 0016 adendo M6; CMP-03a, decisão do PO de 21/10): o prazo padrão de compra (15 dias, de 0 a 365) e a conta e a
 * natureza dos títulos do pedido (padrão 2.1/Material) e do frete (2.4/Freight), já preenchidas e marcadas "A confirmar com a contadora"
 * até alguém marcar a conferência. Aparece com purchasing.view; salvar com settings.edit; com o histórico.
 */
export function PurchasingParametersSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('purchasing.view')
  const query = useGetPurchasingParameters({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-24" />
  if (query.isError || !query.data.accounts) return null
  const p = query.data
  return <PurchasingForm key={[p.defaultLeadDays, p.payableAccountCode, p.payableCostType, p.freightAccountCode, p.freightCostType, p.accountingConfirmed, p.receiptTolerancePct, p.quoteAttachmentAmount].join('|')} params={p} canEdit={canEdit} />
}

function PurchasingForm({ params, canEdit }: { params: PurchasingParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const [days, setDays] = useState(String(params.defaultLeadDays))
  const [account, setAccount] = useState(params.payableAccountCode)
  const [costType, setCostType] = useState<CostType>(params.payableCostType)
  const [freightAccount, setFreightAccount] = useState(params.freightAccountCode)
  const [freightCostType, setFreightCostType] = useState<CostType>(params.freightCostType)
  const [confirmed, setConfirmed] = useState(params.accountingConfirmed)
  const [tolerance, setTolerance] = useState(String(params.receiptTolerancePct ?? 0).replace('.', ','))
  const [attachment, setAttachment] = useState<string | null>(params.quoteAttachmentAmount == null ? null : String(params.quoteAttachmentAmount))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | undefined>()
  const value = Number(days)
  const invalid = days === '' || value < 0 || value > 365 ? 'Informe de 0 a 365 dias.' : undefined
  const tolerancePct = parseTolerance(tolerance)
  const toleranceInvalid = tolerancePct === null ? 'Informe a tolerância de 0% a 50%, com até 2 casas.' : undefined
  const changed = purchasingChanged(params, {
    defaultLeadDays: value, payableAccountCode: account, payableCostType: costType, freightAccountCode: freightAccount, freightCostType, accountingConfirmed: confirmed, receiptTolerancePct: tolerancePct ?? undefined,
    quoteAttachmentAmount: attachment === null ? undefined : Number(attachment),
  })
  const save = async () => {
    try {
      const saved = await updatePurchasingParameters({
        defaultLeadDays: value, payableAccountCode: account, payableCostType: costType, freightAccountCode: freightAccount, freightCostType, accountingConfirmed: confirmed,
        receiptTolerancePct: tolerancePct ?? 0, quoteAttachmentAmount: attachment === null ? null : Number(attachment),
      })
      client.setQueryData(getGetPurchasingParametersQueryKey(), saved)
      void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/PurchasingParameter/') })
      notifySuccess('Parâmetros de compras salvos')
      setError(undefined)
      setErrors({})
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length) setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]])))
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os parâmetros de compras.')
    }
  }

  const accountSelect = (current: string, onChange: (v: string) => void, props: Record<string, unknown>) => (
    <select {...props} value={current} onChange={(e) => onChange(e.target.value)} className={inputClass}>
      {params.accounts.some((a) => a.code === current) ? null : <option value={current}>{current} (não encontrada)</option>}
      {params.accounts.map((a) => <option key={a.code} value={a.code}>{a.code} {a.name}</option>)}
    </select>
  )
  const costSelect = (current: CostType, onChange: (v: CostType) => void, props: Record<string, unknown>) => (
    <select {...props} value={current} onChange={(e) => onChange(e.target.value as CostType)} className={inputClass}>
      {COST_SECTIONS.map((c) => <option key={c.type} value={c.type}>{costTypeLabel(c.type)}</option>)}
    </select>
  )
  const pending = accountingPending(params) ? <StatusChip tone="warn">{PENDING_LABEL}</StatusChip> : null

  return (
    <FormSection title="Compras" description="Prazo usado para calcular quando o material precisa estar comprado, a tolerância do recebimento e a classificação dos títulos dos pedidos.">
      <fieldset disabled={!canEdit} className="space-y-3 sm:col-span-2">
        <Field label="Prazo padrão de compra (dias)" dica={LEAD_DICA} error={invalid ?? errors.defaultLeadDays} hint="Padrão 15 dias.">
          {(p) => <input {...p} inputMode="numeric" maxLength={3} value={days} onChange={(e) => setDays(e.target.value.replace(/[^0-9]/g, ''))} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Tolerância do recebimento (%)" dica={TOLERANCE_DICA} error={toleranceInvalid ?? errors.receiptTolerancePct} hint="Padrão 0%: o recebimento não passa do pedido.">
          {(p) => <input {...p} inputMode="decimal" maxLength={5} value={tolerance} onChange={(e) => setTolerance(e.target.value.replace(/[^0-9,.]/g, ''))} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Anexo da proposta obrigatório acima de" dica={ATTACHMENT_DICA} error={errors.quoteAttachmentAmount} hint="Padrão R$ 5.000,00 no custo total da resposta.">
          {(p) => <MoneyInput {...p} value={attachment} onChange={setAttachment} />}
        </Field>
        <div role="group" aria-label="Títulos do pedido de compra" className="space-y-3 rounded-md border border-border p-3">
          <p className="flex flex-wrap items-center gap-2 text-sm font-semibold">Títulos do pedido de compra<Dica nome="Títulos do pedido de compra" texto={PAYABLE_DICA} />{pending}</p>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conta do pedido" error={errors.payableAccountCode} hint="Padrão 2.1 Materiais.">{(p) => accountSelect(account, setAccount, p)}</Field>
            <Field label="Natureza do pedido" hint="Padrão Material.">{(p) => costSelect(costType, setCostType, p)}</Field>
          </div>
          <p className="flex flex-wrap items-center gap-2 text-sm font-semibold">Frete de outro fornecedor<Dica nome="Frete de outro fornecedor" texto={FREIGHT_DICA} />{pending}</p>
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Conta do frete" error={errors.freightAccountCode} hint="Padrão 2.4 Fretes.">{(p) => accountSelect(freightAccount, setFreightAccount, p)}</Field>
            <Field label="Natureza do frete" hint="Padrão Frete.">{(p) => costSelect(freightCostType, setFreightCostType, p)}</Field>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} className="size-4 accent-primary" />
            Conferido com a contadora
          </label>
        </div>
        {error && Object.keys(errors).length === 0 ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
        {canEdit ? <button type="button" disabled={!changed || !!invalid || !!toleranceInvalid} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar parâmetros de compras</button> : null}
      </fieldset>
      {params.defaultLeadDaysId ? (
        <details className="rounded-md border border-border bg-card px-3 py-2 text-sm sm:col-span-2">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="PurchasingParameter" id={params.defaultLeadDaysId} /></div>
        </details>
      ) : null}
    </FormSection>
  )
}
