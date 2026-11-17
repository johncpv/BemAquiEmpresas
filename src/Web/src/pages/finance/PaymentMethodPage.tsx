import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import {
  addAcquirerFee, confirmPaymentMethod, createPaymentMethod, deletePaymentMethod, getGetPaymentMethodQueryKey, getListAcquirerFeesQueryKey,
  getListPaymentMethodsQueryKey, inactivatePaymentMethod, reactivatePaymentMethod, updatePaymentMethod, useGetPaymentMethod, useListAcquirerFees,
} from '@/api/generated/bemaqui'
import type { AcquirerFeeView, CardBrand, PaymentMethodDetail, PaymentMethodRequest } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, PercentInput } from '@/components/form/inputs'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { ToConfirmChip } from '@/components/ui/to-confirm-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { PaymentMethodForm } from './PaymentMethodForm'
import { acceptsAcquirer, BRAND_LABELS, describeFee, describeInstallments, installmentRange, minValidFrom } from './payment-methods'
import { useCanFinance } from './terms'

const LIST = '/app/financeiro/formas-de-pagamento'
const D_N = 'Em quantos dias o dinheiro cai na conta depois da venda.'
const TO_CONFIRM_DICA = 'Forma sugerida pelo sistema, com taxa e prazo a cadastrar. Confira com o contrato da maquininha e marque como conferido.'
const today = () => new Date().toLocaleDateString('en-CA', { timeZone: 'America/Sao_Paulo' })

/** Nova forma de pagamento (PAG-01 CA 1). As taxas entram depois de salvar, na própria forma. */
export function NewPaymentMethodPage() {
  const navigate = useNavigate()
  const client = useQueryClient()
  const submit = async (input: PaymentMethodRequest) => {
    const saved = await createPaymentMethod(input)
    notifySuccess('Forma de pagamento salva')
    void client.invalidateQueries({ queryKey: getListPaymentMethodsQueryKey() })
    void navigate({ to: `${LIST}/${saved.id}`, ignoreBlocker: true } as never)
  }
  return (
    <>
      <PageHeading title="Nova forma de pagamento" meta={<Link to={LIST as never} className="text-sm font-semibold text-primary">Voltar às formas</Link>} />
      <PaymentMethodForm method={null} canEdit onSubmit={submit} />
      <div className="mt-4"><button type="submit" form="forma-de-pagamento" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar forma de pagamento</button></div>
    </>
  )
}

/** Forma existente (CA 2 a 8): dados, "Taxas e prazos" com vigência, o histórico das taxas e o das alterações. */
export function PaymentMethodPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetPaymentMethod(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const canEdit = useCanFinance('edit')

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Forma de pagamento não encontrada.' : 'Não foi possível carregar a forma de pagamento.'} message={notFound ? 'Ela pode ter sido excluída. Volte às formas de pagamento.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const method = query.data
  const refresh = (m: PaymentMethodDetail) => { client.setQueryData(getGetPaymentMethodQueryKey(id), m); void client.invalidateQueries({ queryKey: getListPaymentMethodsQueryKey() }) }
  const submit = async (input: PaymentMethodRequest) => { refresh(await updatePaymentMethod(id, input)); notifySuccess('Forma de pagamento salva') }
  return (
    <>
      <PageHeading title={method.name}
        meta={
          <>
            <StatusChip tone={method.isActive ? 'ok' : 'neutral'}>{method.isActive ? 'Ativa' : 'Inativa'}</StatusChip>
            <ToConfirmChip state={method.confirmed ? 'Confirmed' : 'ToConfirm'} reviewedByName={method.confirmedByName} reviewedAt={method.confirmedAt} canEdit={canEdit}
              label="A confirmar" dica={TO_CONFIRM_DICA} field={method.name} onConfirm={async () => refresh(await confirmPaymentMethod(id))} />
            <Link to={LIST as never} className="ml-2 text-sm font-semibold text-primary">Voltar às formas</Link>
          </>
        } />
      <PaymentMethodForm key={JSON.stringify([method.name, method.type, method.destinationAccountId, method.acquirerPartyId, method.nfePaymentCode])} method={method} canEdit={canEdit} onSubmit={submit} />
      <div className="mt-4 flex flex-wrap gap-2">
        {canEdit ? <button type="submit" form="forma-de-pagamento" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar forma de pagamento</button> : null}
        <Can perm="finance.edit">
          {method.isActive ? (
            <ConfirmDialog verb="Desativar" done="Desativada" title={`Desativar a forma ${method.name}?`} description="Ela some dos caixas e continua nas vendas que já a usaram."
              onConfirm={async () => refresh(await inactivatePaymentMethod(id))} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Desativar</button>} />
          ) : (
            <button type="button" onClick={async () => { refresh(await reactivatePaymentMethod(id)); notifySuccess('Reativada') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
          )}
        </Can>
        <Can perm="finance.delete">
          <ConfirmDialog verb="Excluir" done="Excluída" destructive title={`Excluir a forma ${method.name}?`} description="Forma já usada numa venda não é excluída: desative-a."
            onConfirm={async () => { await deletePaymentMethod(id); void client.invalidateQueries({ queryKey: getListPaymentMethodsQueryKey() }); void navigate({ to: LIST, ignoreBlocker: true } as never) }}
            recovery={{ verb: 'Desativar', done: 'Desativada', when: (code) => code === 'payment_method_in_use', onSelect: async () => refresh(await inactivatePaymentMethod(id)) }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir forma de pagamento</button>} />
        </Can>
      </div>
      {acceptsAcquirer(method.type) ? <FeesSection method={method} canEdit={canEdit} onChange={() => void query.refetch()} /> : null}
      <section aria-labelledby="historico-forma" className="mt-8 space-y-2">
        <h2 id="historico-forma" className="text-base font-semibold">Histórico de alterações</h2>
        <RecordHistory entity="PaymentMethod" id={method.id} />
      </section>
    </>
  )
}

/** "Taxas e prazos" (CA 2 e 3): as vigentes por bandeira e parcelas, "Nova taxa" com vigência e o histórico com quem cadastrou e quando. */
function FeesSection({ method, canEdit, onChange }: { method: PaymentMethodDetail; canEdit: boolean; onChange: () => void }) {
  const fees = useListAcquirerFees(method.id)
  const client = useQueryClient()
  const [min, max] = installmentRange(method.type)
  const [brand, setBrand] = useState<CardBrand>('Any')
  const [installments, setInstallments] = useState(String(min))
  const [fee, setFee] = useState<string | null>(null)
  const [days, setDays] = useState('')
  const [validFrom, setValidFrom] = useState<string | null>(today())
  const all: AcquirerFeeView[] = fees.data ?? []
  const n = Number(installments)
  const earliest = minValidFrom(all, brand, n, today())
  const reset = (b: CardBrand = 'Any', i = min, current?: AcquirerFeeView) => {
    setBrand(b); setInstallments(String(i)); setFee(current ? String(current.feePercent) : null); setDays(current ? String(current.settlementDays) : '')
    setValidFrom(minValidFrom(all, b, i, today()) ?? today())
  }

  const add = async () => {
    await addAcquirerFee(method.id, { brand, installments: n, feePercent: Number(fee ?? 0), settlementDays: Number(days || 0), validFrom: validFrom ?? today() })
    void client.invalidateQueries({ queryKey: getListAcquirerFeesQueryKey(method.id) })
    onChange()
  }

  const dialog = (trigger: React.ReactNode, current?: AcquirerFeeView) => (
    <ConfirmDialog verb="Salvar taxa" done="Taxa salva" size="lg" title={current ? 'Alterar taxa' : 'Nova taxa'} trigger={trigger}
      description="A taxa em vigor fica no histórico e vale até a véspera da nova. Taxa passada não se altera."
      onOpen={() => reset(current?.brand ?? 'Any', current?.installments ?? min, current)}
      preconditions={[
        { ok: !!method.acquirerPartyId, text: 'Informe a adquirente da forma e salve antes de cadastrar as taxas.' },
        { ok: fee !== null && days !== '', text: 'Informe a taxa e o prazo.' },
        { ok: !earliest || (validFrom ?? '') >= earliest, text: earliest ? `A nova taxa começa a partir de ${formatDate(earliest)}.` : '' },
      ]}
      onConfirm={add}>
      <FormGrid>
        <Field label="Bandeira" size="md">
          {(p) => (
            <select {...p} value={brand} disabled={!!current} onChange={(e) => { const b = e.target.value as CardBrand; setBrand(b); setValidFrom(minValidFrom(all, b, n, today()) ?? validFrom) }} className={inputClass}>
              {(Object.keys(BRAND_LABELS) as CardBrand[]).map((b) => <option key={b} value={b}>{BRAND_LABELS[b]}</option>)}
            </select>
          )}
        </Field>
        {min === max ? null : (
          <Field label="A partir de quantas parcelas" size="sm" hint={`De ${min} a ${max}.`}>
            {(p) => <input {...p} value={installments} disabled={!!current} inputMode="numeric" onChange={(e) => setInstallments(e.target.value.replace(/\D/g, ''))} className={inputClass} />}
          </Field>
        )}
        <Field label="Taxa" size="sm">{(p) => <PercentInput {...p} scale={4} value={fee} onChange={setFee} />}</Field>
        <Field label="Prazo (D+N, em dias)" size="sm" dica={D_N}>
          {(p) => <input {...p} value={days} inputMode="numeric" onChange={(e) => setDays(e.target.value.replace(/\D/g, ''))} className={inputClass} />}
        </Field>
        <Field label="Vale a partir de" size="md">{(p) => <DateInput {...p} value={validFrom} onChange={setValidFrom} />}</Field>
      </FormGrid>
    </ConfirmDialog>
  )

  const current = method.currentFees
  return (
    <section aria-labelledby="taxas" className="mt-8 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="taxas" className="flex items-center text-base font-semibold">Taxas e prazos <Dica nome="D+N" texto={D_N} /></h2>
        {canEdit ? dialog(<button type="button" className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Nova taxa</button>) : null}
      </div>
      {current.length === 0 ? (
        <EmptyState title={method.confirmed ? 'Sem taxa cadastrada.' : 'Taxa e prazo zerados, a confirmar.'}
          description="Sem taxa, a venda entra pelo valor cheio e cai no mesmo dia. Cadastre a taxa e o prazo do contrato da maquininha." />
      ) : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <thead className="bg-muted text-left"><tr><th className="px-3 py-2">Bandeira</th><th className="px-3 py-2">Parcelas</th><th className="px-3 py-2">Taxa e prazo</th><th className="px-3 py-2">Desde</th><th className="px-3 py-2"><span className="sr-only">Ações</span></th></tr></thead>
            <tbody>
              {current.map((f) => (
                <tr key={f.id} className="border-t border-border">
                  <td className="px-3 py-2">{BRAND_LABELS[f.brand]}</td>
                  <td className="px-3 py-2">{describeInstallments(f.installments, method.type)}</td>
                  <td className="px-3 py-2 tabular-nums">{describeFee(f)}</td>
                  <td className="px-3 py-2 tabular-nums">{formatDate(f.validFrom)}{f.validTo ? ` até ${formatDate(f.validTo)} (exclusive)` : ''}</td>
                  <td className="px-3 py-2 text-right">{canEdit ? dialog(<button type="button" className="text-sm font-semibold text-primary">Alterar taxa</button>, f) : null}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <h3 className="text-sm font-semibold">Histórico das taxas</h3>
      {fees.isPending ? <Skeleton className="h-16" /> : fees.isError ? <ErrorState title="Não foi possível carregar o histórico das taxas." onRetry={() => fees.refetch()} /> : all.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhuma taxa cadastrada ainda.</p>
      ) : (
        <ol className="space-y-1 text-sm">
          {all.map((f) => (
            <li key={f.id} className="flex flex-wrap gap-x-2">
              <span>{BRAND_LABELS[f.brand]} · {describeInstallments(f.installments, method.type)} · <span className="tabular-nums">{describeFee(f)}</span></span>
              <span className="text-muted-foreground tabular-nums">de {formatDate(f.validFrom)}{f.validTo ? ` a ${formatDate(f.validTo)} (exclusive)` : ' em diante'}</span>
              <span className="text-muted-foreground">· cadastrada por {f.createdByName ?? 'sistema'} em <time dateTime={f.createdAt} className="tabular-nums">{formatDateTime(f.createdAt)}</time></span>
              {f.inForce ? <StatusChip tone="ok">Em vigor</StatusChip> : null}
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
