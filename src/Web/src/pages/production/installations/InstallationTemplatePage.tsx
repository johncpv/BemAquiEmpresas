import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetInstallationTemplateQueryKey, saveInstallationTemplate, useGetInstallationTemplate } from '@/api/generated/bemaqui'
import type { InstallationTemplateView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'

const toText = (lines: readonly string[]) => lines.join('\n')
const toLines = (text: string) => text.split('\n').map((l) => l.trim()).filter(Boolean)

/** Produção → Modelo da OS de instalação (COM-06 CA 4, P-22): etapas e checklist copiados por toda OS nova. */
export function InstallationTemplatePage() {
  const query = useGetInstallationTemplate()
  return (
    <>
      <PageHeading title="Produção: Modelo da OS de instalação">
        <p className="mt-2 text-muted-foreground">Toda OS de instalação nova copia estas etapas e este checklist. Mudar aqui não altera as OS já criadas.</p>
      </PageHeading>
      <ProductionTabs />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : <TemplateForm key={JSON.stringify(query.data)} template={query.data} />}
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

  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
      <FormError message={error} />
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormSection title="Etapas" description="Uma por linha, na ordem. A última é sempre Entrega técnica, concluída pelo aceite.">
          <textarea aria-label="Etapas" rows={6} value={steps} onChange={(e) => setSteps(e.target.value)} className={inputClass} />
        </FormSection>
        <FormSection title="Checklist de entrega técnica" description="Um item por linha.">
          <textarea aria-label="Checklist de entrega técnica" rows={8} value={checklist} onChange={(e) => setChecklist(e.target.value)} className={inputClass} />
        </FormSection>
        {canEdit ? <div className="flex justify-end"><button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar modelo</button></div> : null}
      </fieldset>
    </form>
  )
}
