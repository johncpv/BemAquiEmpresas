import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetInstallationTemplateQueryKey, saveInstallationTemplate, useGetInstallationTemplate } from '@/api/generated/bemaqui'
import type { InstallationTemplateView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormActions, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { FormLayout } from '@/components/layout/form-layout'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'

const toText = (lines: readonly string[]) => lines.join('\n')
const toLines = (text: string) => text.split('\n').map((l) => l.trim()).filter(Boolean)

/** Produção → Modelo da OS de instalação (COM-06 CA 4, P-22): etapas e checklist copiados por toda OS nova. */
export function InstallationTemplatePage() {
  const query = useGetInstallationTemplate()
  return (
    <>
      <PageHeading title="Produção: Modelo da OS de instalação" description="Toda OS de instalação nova copia estas etapas e este checklist. Mudar aqui não altera as OS já criadas." />
      {query.isPending ? <Skeleton className="h-64 max-w-4xl" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : <TemplateForm key={JSON.stringify(query.data)} template={query.data} />}
    </>
  )
}

function TemplateForm({ template }: { template: InstallationTemplateView }) {
  const canEdit = useCan('production.edit')
  const client = useQueryClient()
  const [steps, setSteps] = useState(toText(template.steps))
  const [checklist, setChecklist] = useState(toText(template.checklist))
  const [error, setError] = useState<string | undefined>()
  const save = async () => {
    try {
      await saveInstallationTemplate({ steps: toLines(steps), checklist: toLines(checklist) })
      notifySuccess('Modelo salvo')
      await client.invalidateQueries({ queryKey: getGetInstallationTemplateQueryKey() })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o modelo.')
    }
  }

  // Formulário simples (padrão de layout, ADR 0044): coluna à esquerda, textareas na largura toda e o botão no fim.
  return (
    <FormLayout>
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="space-y-5">
        <FormError message={error} />
        <fieldset disabled={!canEdit} className="space-y-5">
          <FormSection title="Etapas" description="Uma por linha, na ordem. A última é sempre Entrega técnica, concluída pelo aceite." grid="none">
            <textarea aria-label="Etapas" rows={6} value={steps} onChange={(e) => setSteps(e.target.value)} className={inputClass} />
          </FormSection>
          <FormSection title="Checklist de entrega técnica" description="Um item por linha." grid="none">
            <textarea aria-label="Checklist de entrega técnica" rows={8} value={checklist} onChange={(e) => setChecklist(e.target.value)} className={inputClass} />
          </FormSection>
          {canEdit ? <FormActions><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar modelo</button></FormActions> : null}
        </fieldset>
      </form>
    </FormLayout>
  )
}
