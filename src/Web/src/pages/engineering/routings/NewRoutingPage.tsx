import { useNavigate, useSearch } from '@tanstack/react-router'
import { createRoutingTemplate, useDuplicateRoutingTemplate, useListRoutingProductTypes, useListRoutingWorkCenters, useListSuggestedRoutingWorkCenters } from '@/api/generated/bemaqui'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { RoutingEditor } from './RoutingEditor'
import { exampleDraft, type RoutingDraft, toDraftSteps, toStepsInput } from './routings'

/**
 * Novo roteiro (CA 2, 9): vem com as 5 etapas de exemplo (P-04) e o centro sugerido; com ?de={id}, abre a duplicação com as
 * etapas, as letras e os checklists, sem os produtos. Depois de salvar, abre o detalhe com os próximos passos.
 */
export function NewRoutingPage() {
  const { de } = useSearch({ strict: false }) as { de?: string }
  const navigate = useNavigate()
  const canCreate = useCan('engineering.create')
  const centers = useListRoutingWorkCenters()
  const suggestions = useListSuggestedRoutingWorkCenters()
  const types = useListRoutingProductTypes()
  const copy = useDuplicateRoutingTemplate(de ?? '', { query: { enabled: !!de } })
  const loading = centers.isPending || suggestions.isPending || (!!de && copy.isPending)
  const failed = centers.isError || suggestions.isError || (!!de && copy.isError)

  const initial = (): RoutingDraft => {
    const list = centers.data ?? []
    if (de && copy.data) return { name: copy.data.name, productType: copy.data.productType ?? '', steps: toDraftSteps(copy.data.steps.map((s) => ({ ...s, id: null })), list) }
    return exampleDraft(suggestions.data ?? [], list)
  }

  const submit = async (draft: RoutingDraft) => {
    const saved = await createRoutingTemplate({ name: draft.name.trim(), productType: draft.productType.trim() || null, steps: toStepsInput(draft.steps) })
    notifySuccess('Roteiro salvo')
    return () => void navigate({ to: '/app/engenharia/roteiros/$id', params: { id: saved.id }, search: { criado: 1 } } as never)
  }

  return (
    <>
      <PageHeading title={de ? 'Duplicar roteiro' : 'Novo roteiro'}>
        <p className="mt-2 text-muted-foreground">{de ? 'Ajuste o nome e o que muda; os produtos não vêm junto, porque cada produto tem um roteiro só.' : 'Começa com as etapas de exemplo. Ajuste os nomes, as letras, os centros e as horas.'}</p>
      </PageHeading>
      {loading ? <Skeleton className="h-96" /> : failed ? <ErrorState title="Não foi possível abrir o roteiro." onRetry={() => { void centers.refetch(); void suggestions.refetch(); if (de) void copy.refetch() }} /> : (
        <RoutingEditor initial={initial()} centers={centers.data ?? []} suggestions={suggestions.data ?? []} productTypes={types.data ?? []} canEdit={canCreate} onSubmit={submit}
          footerExtra={<button type="button" onClick={() => void navigate({ to: '/app/engenharia/roteiros' } as never)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>} />
      )}
    </>
  )
}