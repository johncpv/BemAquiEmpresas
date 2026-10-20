import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetFinanceLimitsQueryKey, updateFinanceLimits, useGetFinanceLimits } from '@/api/generated/bemaqui'
import type { FinanceLimits } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, PercentInput } from '@/components/form/inputs'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'

/** Limites do Financeiro (FIN-01 CA 13, 14 e 18, D8): desconto, cancelamento e data retroativa; só com finance.view. */
export function FinanceLimitsSection({ canEdit }: { canEdit: boolean }) {
  const canSee = useCan('finance.view')
  const query = useGetFinanceLimits({ query: { enabled: canSee } })
  if (!canSee) return null
  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError || !query.data) return null
  return <LimitsForm key={JSON.stringify(query.data)} limits={query.data} canEdit={canEdit} />
}

function LimitsForm({ limits, canEdit }: { limits: FinanceLimits; canEdit: boolean }) {
  const client = useQueryClient()
  const [pct, setPct] = useState<string | null>(String(limits.discountApprovalPct))
  const [discount, setDiscount] = useState<string | null>(String(limits.discountApprovalAmount))
  const [cancel, setCancel] = useState<string | null>(String(limits.cancelApprovalAmount))
  const [days, setDays] = useState(String(limits.backdateDays))
  const [ownApproval, setOwnApproval] = useState(limits.ownerCanApproveOwn ?? false)
  const [notify, setNotify] = useState<string | null>(String(limits.transferNotifyAmount ?? 10000))
  const [tolerance, setTolerance] = useState<string | null>(String(limits.paymentTolerance ?? 0))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const save = async () => {
    try {
      const saved = await updateFinanceLimits({ discountApprovalPct: Number(pct ?? 0), discountApprovalAmount: Number(discount ?? 0), cancelApprovalAmount: Number(cancel ?? 0), backdateDays: Number(days || 0), ownerCanApproveOwn: ownApproval, paymentTolerance: Number(tolerance ?? 0), transferNotifyAmount: Number(notify ?? 0) })
      client.setQueryData(getGetFinanceLimitsQueryKey(), saved)
      notifySuccess('Limites do financeiro salvos')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar os limites.', fields: {} })
    }
  }

  const field = (k: string) => error?.fields[k]?.[0]
  return (
    <FormSection title="Limites do financeiro" description="Acima destes valores, só quem aprova em Caixa e pagamentos confirma. Datas muito antigas pedem motivo e avisam o Proprietário.">
      <fieldset disabled={!canEdit} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Desconto: % do saldo" error={field('discountApprovalPct')}>{(p) => <PercentInput {...p} value={pct} onChange={setPct} />}</Field>
          <Field label="Desconto: valor" error={field('discountApprovalAmount')}>{(p) => <MoneyInput {...p} value={discount} onChange={setDiscount} />}</Field>
          <Field label="Cancelamento: saldo acima de" error={field('cancelApprovalAmount')}>{(p) => <MoneyInput {...p} value={cancel} onChange={setCancel} />}</Field>
          <Field label="Data retroativa: dias" error={field('backdateDays')} hint="Baixa com data mais antiga que isso pede motivo.">
            {(p) => <input {...p} inputMode="numeric" maxLength={3} value={days} onChange={(e) => setDays(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}
          </Field>
          <Field label="Tolerância no pagamento" error={field('paymentTolerance')} hint="Quanto o pagamento pode passar do aprovado sem nova aprovação.">{(p) => <MoneyInput {...p} value={tolerance} onChange={setTolerance} />}</Field>
          <Field label="Aviso de transferência acima de" error={field('transferNotifyAmount')} hint="Transferência maior que isso avisa o Proprietário.">{(p) => <MoneyInput {...p} value={notify} onChange={setNotify} />}</Field>
        </div>
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" checked={ownApproval} onChange={(e) => setOwnApproval(e.target.checked)} className="mt-0.5 size-4 accent-primary" />
          <span><b>Proprietário aprova a própria ordem</b><span className="block text-xs text-muted-foreground">Com mais de um Proprietário, quem solicitou a ordem também pode aprová-la. A troca avisa todos os Proprietários.</span></span>
        </label>
        {canEdit ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar limites</button> : null}
      </fieldset>
    </FormSection>
  )
}
