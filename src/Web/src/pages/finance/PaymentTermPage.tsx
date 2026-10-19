import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { createPaymentTerm, deletePaymentTerm, getGetPaymentTermQueryKey, getListPaymentTermsQueryKey, inactivatePaymentTerm, reactivatePaymentTerm, updatePaymentTerm, useGetPaymentTerm } from '@/api/generated/bemaqui'
import type { PaymentTermDetail, PaymentTermInput } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { toastDone } from '@/components/ui/toast'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { PaymentTermForm } from './PaymentTermForm'
import { useCanFinance } from './terms'

const LIST = '/app/financeiro/condicoes-de-pagamento'

/** Nova condição (molde cadastro em seções, CAD-05b CA 11). */
export function NewPaymentTermPage() {
  const navigate = useNavigate()
  const client = useQueryClient()
  const submit = async (input: PaymentTermInput) => {
    const saved = await createPaymentTerm(input)
    toastDone('Condição de pagamento salva')
    void client.invalidateQueries({ queryKey: getListPaymentTermsQueryKey() })
    void navigate({ to: `${LIST}/${saved.id}`, ignoreBlocker: true } as never)
  }
  return (
    <>
      <PageHeading title="Nova condição de pagamento">
        <p className="mt-2"><Link to={LIST} className="text-sm font-semibold text-primary">Voltar às condições</Link></p>
      </PageHeading>
      <PaymentTermForm term={null} canEdit onSubmit={submit} />
      <div className="mt-4"><button type="submit" form="condicao" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar condição de pagamento</button></div>
    </>
  )
}

/** Condição existente (CA 11 a 17): editar, inativar ou excluir; em uso, excluir oferece "Inativar". */
export function PaymentTermPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetPaymentTerm(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const canEdit = useCanFinance('edit')

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Condição não encontrada.' : 'Não foi possível carregar a condição.'} message={notFound ? 'Ela pode ter sido excluída. Volte às condições de pagamento.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const term = query.data
  const refresh = (t: PaymentTermDetail) => { client.setQueryData(getGetPaymentTermQueryKey(id), t); void client.invalidateQueries({ queryKey: getListPaymentTermsQueryKey() }) }
  const submit = async (input: PaymentTermInput) => { refresh(await updatePaymentTerm(id, input)); toastDone('Condição de pagamento salva') }
  return (
    <>
      <PageHeading title={term.name}>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <Link to={LIST} className="text-sm font-semibold text-primary">Voltar às condições</Link>
          <StatusChip tone={term.isActive ? 'ok' : 'neutral'}>{term.isActive ? 'Ativa' : 'Inativa'}</StatusChip>
        </div>
      </PageHeading>
      <PaymentTermForm key={JSON.stringify(term.installments) + term.name} term={term} canEdit={canEdit} onSubmit={submit} />
      <div className="mt-4 flex flex-wrap gap-2">
        {canEdit ? <button type="submit" form="condicao" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar condição de pagamento</button> : null}
        <Can perm="finance.edit">
          {term.isActive ? (
            <ConfirmDialog verb="Inativar" done="Inativada" title={`Inativar a condição ${term.name}?`} description="Ela some dos seletores e continua nos terceiros e documentos que já a usam."
              onConfirm={async () => refresh(await inactivatePaymentTerm(id))} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
          ) : (
            <button type="button" onClick={async () => { refresh(await reactivatePaymentTerm(id)); toastDone('Reativada') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
          )}
        </Can>
        <Can perm="finance.delete">
          <ConfirmDialog verb="Excluir" done="Excluída" destructive title={`Excluir a condição ${term.name}?`} description="Condição em uso não é excluída: inative-a."
            onConfirm={async () => { await deletePaymentTerm(id); void client.invalidateQueries({ queryKey: getListPaymentTermsQueryKey() }); void navigate({ to: LIST, ignoreBlocker: true } as never) }}
            recovery={{ verb: 'Inativar', done: 'Inativada', when: (code) => code === 'payment_term_in_use', onSelect: async () => refresh(await inactivatePaymentTerm(id)) }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir condição de pagamento</button>} />
        </Can>
      </div>
    </>
  )
}
