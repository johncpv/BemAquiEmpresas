import type { CostType } from '@/api/generated/model'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { COST_SECTIONS } from '../../quotes/quotes'
import { searchProjects } from './payables'

export interface ProjectCost {
  project: ComboOption | null
  costType: CostType | ''
}

/** Projeto e natureza do custo (COM-04a CA 18): com projeto, a natureza é obrigatória e vem sugerida pela conta. */
export function ProjectCostFields({ value, onChange, suggested, errors, withCostType = true, disabled }: {
  value: ProjectCost
  onChange: (value: ProjectCost) => void
  suggested?: CostType | null
  errors: Partial<Record<string, string>>
  withCostType?: boolean
  disabled?: boolean
}) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <Field label="Projeto" error={errors.projectId} hint="Opcional. Projetos abertos.">
        {(p) => <Combobox {...p} queryKey={['finance-projects']} search={searchProjects} value={value.project?.id ?? null} selected={value.project} disabled={disabled} placeholder="Busque pelo número ou nome"
          onChange={(project) => onChange({ project, costType: project ? value.costType || suggested || '' : '' })} />}
      </Field>
      {withCostType && value.project ? (
        <Field label="Natureza do custo" required error={errors.costType} hint={suggested ? 'Sugerida pela conta.' : undefined}>
          {(p) => (
            <select {...p} value={value.costType} disabled={disabled} onChange={(e) => onChange({ ...value, costType: e.target.value as CostType | '' })} className={inputClass}>
              <option value="">Escolha</option>
              {COST_SECTIONS.map((s) => <option key={s.type} value={s.type}>{s.label}</option>)}
            </select>
          )}
        </Field>
      ) : null}
    </div>
  )
}
