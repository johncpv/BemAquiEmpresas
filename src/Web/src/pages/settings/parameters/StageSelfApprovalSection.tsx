import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetCommercialParametersQueryKey, updateCommercialParameters, useGetCommercialParameters } from '@/api/generated/bemaqui'
import type { CommercialParametersView } from '@/api/generated/model'
import { FormSection } from '@/components/form/form'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { SERVICE_PROFILES } from '@/navigation/modules'
import { useHasProfile, useNavigation } from '@/navigation/navigation-context'
import { modeOf, needsCode, ONLY_OWNER_NOTE, type SelfApprovalModeName } from './self-approval'

const STAGE_SELF_APPROVAL_QUESTION = 'Quem concluiu a etapa pode aprová-la?'

/** As três opções da autoaprovação da etapa (EXE-03b CA 5), com a mesma codificação da FIN-10a. */
const STAGE_SELF_APPROVAL_OPTIONS: readonly { value: SelfApprovalModeName; label: string; description: string }[] = [
  { value: 'Never', label: 'Nunca', description: 'Outra pessoa com permissão de aprovar projetos aprova.' },
  { value: 'OwnerOnly', label: 'Só o Proprietário', description: 'Quem é do Proprietário aprova a etapa que concluiu, e fica registrado (padrão).' },
  { value: 'AnyApprover', label: 'Qualquer aprovador', description: 'Quem tem permissão de aprovar projetos aprova a etapa que concluiu, e fica registrado.' },
]

const labelOf = (mode: SelfApprovalModeName) => STAGE_SELF_APPROVAL_OPTIONS.find((o) => o.value === mode)?.label ?? 'Nunca'

/**
 * Autoaprovação da etapa do projeto de serviço (EXE-03b CA 5, parâmetro projects.stage_self_approval): só no perfil Serviços; troca só pelo
 * Proprietário, com o código do autenticador, como a FIN-10a.
 */
export function StageSelfApprovalSection() {
  const services = useHasProfile(SERVICE_PROFILES)
  const query = useGetCommercialParameters({ query: { enabled: services } })
  if (!services) return null
  if (query.isPending) return <Skeleton className="h-32" />
  if (query.isError || !query.data) return null
  return <StageSelfApprovalForm key={String(query.data.stageSelfApprovalMode)} params={query.data} />
}

function StageSelfApprovalForm({ params }: { params: CommercialParametersView }) {
  const client = useQueryClient()
  const { isOwner } = useNavigation()
  const seen = modeOf(params.stageSelfApprovalMode ?? 'OwnerOnly')
  const [mode, setMode] = useState<SelfApprovalModeName>(seen)
  const [code, setCode] = useState('')
  const [open, setOpen] = useState(false)
  const send = async () => {
    const saved = await updateCommercialParameters({
      defaultPct: params.defaultPct ?? null, validityDays: params.validityDays ?? 15, stageSelfApprovalMode: mode, stageSelfApprovalModeSeen: seen, code,
    })
    client.setQueryData(getGetCommercialParametersQueryKey(), saved)
  }

  return (
    <FormSection title="Etapas do projeto de serviço" description="Etapa com &quot;Exige aprovação&quot; fica aguardando quem aprova projetos." grid="none">
      <fieldset disabled={!isOwner} className="space-y-2">
        <legend className="inline-flex items-center text-sm font-semibold">
          {STAGE_SELF_APPROVAL_QUESTION}<Dica nome="Autoaprovada" texto="A etapa aprovada por quem a concluiu ganha o selo Autoaprovada." />
        </legend>
        {STAGE_SELF_APPROVAL_OPTIONS.map((o) => (
          <label key={o.value} className="flex items-start gap-2 text-sm">
            <input type="radio" name="stage-self-approval" value={o.value} checked={mode === o.value} onChange={() => setMode(o.value)} className="mt-0.5 size-4 accent-primary" />
            <span><b>{o.label}</b><span className="block text-xs text-muted-foreground">{o.description}</span></span>
          </label>
        ))}
        <p className="text-xs text-muted-foreground">{isOwner ? 'Trocar pede o código do autenticador e avisa os outros Proprietários.' : ONLY_OWNER_NOTE}</p>
      </fieldset>
      {isOwner && needsCode(seen, mode) ? (
        <button type="button" onClick={() => { setCode(''); setOpen(true) }} className="mt-3 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar regra</button>
      ) : null}
      <ConfirmDialog size="lg" verb="Confirmar" done="Regra de aprovação salva" title="Confirme com o código do autenticador" open={open} onOpenChange={setOpen}
        description={`${STAGE_SELF_APPROVAL_QUESTION} De "${labelOf(seen)}" para "${labelOf(mode)}". Os outros Proprietários serão avisados.`}
        preconditions={[{ ok: code.length === 6, text: 'Código do autenticador com 6 dígitos.' }]}
        recovery={{ verb: 'Recarregar', when: (c) => c === 'parameters_changed', onSelect: () => client.invalidateQueries({ queryKey: getGetCommercialParametersQueryKey() }) }}
        onConfirm={send}>
        <TwoFactorCodeField code={code} onChange={setCode} />
      </ConfirmDialog>
    </FormSection>
  )
}
