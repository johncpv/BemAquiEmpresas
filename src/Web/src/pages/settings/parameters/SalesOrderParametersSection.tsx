import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetSalesOrderParametersQueryKey, updateSalesOrderParameters, useGetSalesOrderParameters } from '@/api/generated/bemaqui'
import type { SalesOrderParametersView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormGrid, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'

const KEYS = ['discountLimitPct', 'appDiscountLimitPct', 'holdHours', 'appHoldHours', 'chargeDays', 'appChargeDays', 'appMaxLineQty', 'appMaxOrderQty', 'appUnpaidAlertCount'] as const
type Key = (typeof KEYS)[number]

const LABELS: Record<Key, { label: string; hint: string }> = {
  discountLimitPct: { label: 'Limite de desconto (%)', hint: 'Padrão 10%. Acima, só quem aprova em Vendas confirma.' },
  appDiscountLimitPct: { label: 'Limite de desconto do aplicativo (%)', hint: 'Padrão 0%. Acima, o pedido fica com "Desconto para aprovar".' },
  holdHours: { label: 'Prazo da reserva sem cobrança (horas)', hint: 'Padrão 48 horas.' },
  appHoldHours: { label: 'Prazo da reserva do aplicativo (horas)', hint: 'Padrão 2 horas.' },
  chargeDays: { label: 'Vencimento da cobrança (dias úteis)', hint: 'Padrão 1 dia útil.' },
  appChargeDays: { label: 'Vencimento da cobrança do aplicativo (dias úteis)', hint: 'Até 3.' },
  appMaxLineQty: { label: 'Quantidade máxima por linha (aplicativo)', hint: 'Padrão 50.' },
  appMaxOrderQty: { label: 'Quantidade máxima no pedido (aplicativo)', hint: 'Padrão 200.' },
  appUnpaidAlertCount: { label: 'Alerta de pedidos sem pagamento (aplicativo)', hint: 'Padrão 10: acima disso, os Proprietários recebem o aviso.' },
}

/** Configurações → Parâmetros → Pedidos de venda (VND-02a): aparece para quem vê Vendas; salvar com settings.edit; com o histórico. */
export function SalesOrderParametersSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('sales.view')
  const query = useGetSalesOrderParameters({ query: { enabled: canSee, retry: false } })
  if (!canSee || query.isError) return null
  if (query.isPending) return <Skeleton className="h-24" />
  return <Form key={JSON.stringify(query.data)} params={query.data} canEdit={canEdit} />
}

function Form({ params, canEdit }: { params: SalesOrderParametersView; canEdit: boolean }) {
  const client = useQueryClient()
  const [values, setValues] = useState<Record<Key, string>>(() => Object.fromEntries(KEYS.map((k) => [k, String(params[k]).replace('.', ',')])) as Record<Key, string>)
  const [method, setMethod] = useState(params.bankSlipPaymentMethodId ?? '')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string>()
  const save = async () => {
    try {
      const body = Object.fromEntries(KEYS.map((k) => [k, Number(values[k].replace(',', '.'))])) as Record<Key, number>
      const saved = await updateSalesOrderParameters({ ...body, bankSlipPaymentMethodId: method || null })
      client.setQueryData(getGetSalesOrderParametersQueryKey(), saved)
      notifySuccess('Parâmetros de pedidos de venda salvos')
      setErrors({})
      setError(undefined)
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fields).length) setErrors(Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0]])))
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os parâmetros de pedidos de venda.')
    }
  }

  return (
    <FormSection title="Pedidos de venda" description="Limites de desconto, prazos da reserva e da cobrança, limites dos aplicativos conectados e a forma de pagamento do boleto." grid="none">
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormGrid>
          {KEYS.map((k) => (
            <Field key={k} label={LABELS[k].label} size="md" hint={LABELS[k].hint} error={errors[k]}>
              {(p) => <input {...p} inputMode="decimal" maxLength={9} value={values[k]} onChange={(e) => setValues((v) => ({ ...v, [k]: e.target.value.replace(/[^0-9,.]/g, '') }))} className={inputClass + ' tabular-nums'} />}
            </Field>
          ))}
          <Field label="Forma de pagamento do boleto" size="md" error={errors.bankSlipPaymentMethodId}
            hint={params.bankSlipMethods.length === 0 ? 'Cadastre uma forma do tipo "Boleto com Pix" em Financeiro → Formas de pagamento.' : 'Sem ela, o pedido não gera cobrança.'}>
            {(p) => (
              <select {...p} value={method} onChange={(e) => setMethod(e.target.value)} className={inputClass}>
                <option value="">Não configurada</option>
                {params.bankSlipMethods.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
              </select>
            )}
          </Field>
        </FormGrid>
        {error && Object.keys(errors).length === 0 ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
        {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar pedidos de venda</button> : null}
      </fieldset>
      {params.id ? (
        <details className="mt-3 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="SalesOrderParameters" id={params.id} /></div>
        </details>
      ) : null}
    </FormSection>
  )
}
