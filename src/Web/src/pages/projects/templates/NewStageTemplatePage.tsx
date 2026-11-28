import { useNavigate } from '@tanstack/react-router'
import { createStageTemplate, useListStageTemplatePositions, useListStageTemplateSegments } from '@/api/generated/bemaqui'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { StageTemplateEditor } from './StageTemplateEditor'
import { emptyDraft, type TemplateDraft, toInput } from './stage-templates'

/** Novo modelo (EXE-02 CA 2): começa com Execução, Entrega e Aceite e os marcos 50/50; depois de salvar, abre o modelo com os próximos passos. */
export function NewStageTemplatePage() {
  const navigate = useNavigate()
  const canEdit = useCan('projects.edit')
  const positions = useListStageTemplatePositions()
  const segments = useListStageTemplateSegments()

  const submit = async (draft: TemplateDraft) => {
    const saved = await createStageTemplate(toInput(draft))
    notifySuccess('Modelo salvo')
    return () => void navigate({ to: '/app/projetos/modelos/$id', params: { id: saved.id }, search: { criado: 1 } } as never)
  }

  return (
    <>
      <PageHeading title="Novo modelo de etapas" description="Começa com uma etapa de Execução, a Entrega e o Aceite do cliente. Ajuste os nomes, as letras, as horas e os marcos." />
      {positions.isPending || segments.isPending ? <Skeleton className="h-96" /> : positions.isError || segments.isError ? (
        <ErrorState title="Não foi possível abrir o editor." message="Os cargos ou os segmentos não carregaram. Tente de novo." onRetry={() => { void positions.refetch(); void segments.refetch() }} />
      ) : (
        <StageTemplateEditor initial={emptyDraft()} positions={positions.data} segments={segments.data} canEdit={canEdit} onSubmit={submit}
          footerExtra={<button type="button" onClick={() => void navigate({ to: '/app/projetos/modelos' } as never)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>} />
      )}
    </>
  )
}
