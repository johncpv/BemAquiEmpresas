import * as Dialog from '@radix-ui/react-dialog'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { provisionTenant, useListBusinessProfiles, useListPlanModules, useListPlans } from '@/api/generated/bemaqui'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { isValidCnpj } from '@/lib/masks'
import { z } from '@/lib/zod'
import { ProfileCards, SegmentPicker } from './ProfileStep'
import { profileChoices, profilesWithSegments, toggleProfile, uncoveredModules, type SegmentChoice } from './profile-rules'

const schema = z.object({
  key: z.string().regex(/^[a-z][a-z0-9-]{1,29}[a-z0-9]$/, 'Use de 3 a 31 letras minúsculas, números ou hífen, começando por letra.'),
  legalName: z.string().min(2, 'Informe a razão social.'),
  tradeName: z.string().optional(),
  document: z.string().optional().refine((v) => !v || isValidCnpj(v), 'CNPJ inválido. Confira os dígitos.'),
  host: z.string().regex(/^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]*$/, 'Informe um endereço válido, como empresa.bemaquiapp.com.br.'),
  ownerEmail: z.email('Informe um e-mail válido.'),
  plan: z.string().optional(),
})

type Step = 'dados' | 'perfil' | 'segmento' | 'plano'
const STEP_NAMES: Record<Step, string> = { dados: 'Empresa', perfil: 'Perfil de negócio', segmento: 'Segmento', plano: 'Plano' }
// Plano usado quando o super admin deixa "Padrão" (EfProvisioningCatalog.DefaultPlan).
const DEFAULT_PLAN = 'Piloto'

/**
 * "Provisionar tenant" (FND-03b CA 2 e 3; PERF-02 CA 1 a 3): dados da empresa, perfil de negócio (um ou mais), segmento dos perfis que
 * têm, e o plano, com o aviso do que o plano não cobre. Roda em segundo plano e abre o progresso. Sem mexer, o perfil é Fábrica.
 */
export function ProvisionDialog() {
  const [open, setOpen] = useState(false)
  const [step, setStep] = useState<Step>('dados')
  const [selected, setSelected] = useState<string[]>(['factory'])
  const [segments, setSegments] = useState<SegmentChoice>({ factory: null })
  const [profileError, setProfileError] = useState<string>()
  const navigate = useNavigate()
  const plans = useListPlans({ query: { enabled: open } })
  const planModules = useListPlanModules({ query: { enabled: open } })
  const catalog = useListBusinessProfiles({ query: { enabled: open, staleTime: Infinity } })
  const form = useZodForm(schema, { defaultValues: { key: '', legalName: '', tradeName: '', document: '', host: '', ownerEmail: '', plan: '' } })
  const { errors } = form.formState
  const profiles = catalog.data ?? []
  const withSegments = profilesWithSegments(profiles, selected)
  const steps: Step[] = withSegments.length > 0 ? ['dados', 'perfil', 'segmento', 'plano'] : ['dados', 'perfil', 'plano']
  const index = steps.indexOf(step)

  const reset = () => { form.reset(); setStep('dados'); setSelected(['factory']); setSegments({ factory: null }); setProfileError(undefined) }
  const chosenPlan = () => form.getValues('plan') || (plans.data?.length === 1 ? plans.data[0] : null)

  const submit = form.handleSubmit(async (v) => {
    try {
      const accepted = await provisionTenant({
        key: v.key, legalName: v.legalName, tradeName: v.tradeName || null, document: v.document || null, host: v.host, ownerEmail: v.ownerEmail, plan: chosenPlan(),
        profiles: profileChoices(selected, segments),
      })
      setOpen(false)
      reset()
      await navigate({ to: '/plataforma/tenants/$key', params: { key: accepted.key } })
    } catch (e) {
      applyServerErrors(form, e)
      // Erro de campo dos dados da empresa: volta para o passo onde ele está.
      if (Object.keys(form.formState.errors).some((k) => k !== 'root' && k !== 'plan')) setStep('dados')
    }
  })

  const next = async () => {
    if (step === 'dados' && !(await form.trigger(['key', 'legalName', 'tradeName', 'document', 'host', 'ownerEmail']))) return
    if (step === 'perfil' && selected.length === 0) { setProfileError('Escolha ao menos um perfil.'); return }
    setStep(steps[index + 1])
  }

  const text = (name: 'key' | 'legalName' | 'tradeName' | 'document' | 'host' | 'ownerEmail', label: string, hint?: string, type = 'text') => (
    <Field label={label} hint={hint} error={errors[name]?.message}>{(p) => <input {...p} type={type} {...form.register(name)} className={inputClass} />}</Field>
  )

  const planName = form.watch('plan') || (plans.data?.length === 1 ? plans.data[0] : DEFAULT_PLAN)
  const uncovered = uncoveredModules(profiles, selected, planModules.data?.find((p) => p.name === planName)?.modules)

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); if (!o) reset() }}>
      <Dialog.Trigger className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Provisionar tenant</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-[5vh] z-50 max-h-[90vh] w-[min(560px,calc(100vw-32px))] -translate-x-1/2 overflow-y-auto rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Provisionar tenant</Dialog.Title>
          <Dialog.Description className="mt-1 text-sm text-muted-foreground">Cria o banco, aplica a estrutura e o perfil de negócio, carrega os dados iniciais e convida o proprietário.</Dialog.Description>
          <div className="mt-3"><StageIndicator stages={steps.map((s) => STEP_NAMES[s])} current={index} /></div>
          <form onSubmit={(e) => { if (step !== 'plano') { e.preventDefault(); void next() } else void submit(e) }} noValidate className="mt-3 space-y-3">
            <FormError message={errors.root?.server?.message} />
            {step === 'dados' ? (
              <>
                {text('key', 'Chave', 'Identifica o tenant no sistema. Ex.: konebras')}
                {text('legalName', 'Razão social')}
                {text('tradeName', 'Nome fantasia')}
                {text('document', 'CNPJ')}
                {text('host', 'Endereço', 'O DNS precisa apontar para o servidor.')}
                {text('ownerEmail', 'E-mail do proprietário', 'Recebe o convite para criar o acesso.', 'email')}
              </>
            ) : null}
            {step === 'perfil' ? (
              <ProfileCards
                profiles={profiles}
                loading={catalog.isPending}
                failed={catalog.isError}
                onRetry={() => void catalog.refetch()}
                selected={selected}
                error={profileError}
                onToggle={(code) => { const [s, seg] = toggleProfile(selected, segments, code); setSelected(s); setSegments(seg); setProfileError(undefined) }}
              />
            ) : null}
            {step === 'segmento' ? (
              <SegmentPicker profiles={withSegments} segments={segments} onChange={(code, segment) => setSegments((s) => ({ ...s, [code]: segment }))} />
            ) : null}
            {step === 'plano' ? (
              <>
                <Field label="Plano" error={errors.plan?.message}>
                  {(p) => (
                    <select {...p} {...form.register('plan')} className={inputClass}>
                      {(plans.data ?? []).length !== 1 ? <option value="">Padrão</option> : null}
                      {(plans.data ?? []).map((name) => <option key={name} value={name}>{name}</option>)}
                    </select>
                  )}
                </Field>
                {uncovered.length > 0 ? (
                  <p role="note" className="rounded-md bg-warn-soft p-3 text-sm text-warn-ink">
                    O plano {planName} não cobre {uncovered.join(', ')}. Esses módulos do perfil vão aparecer com cadeado até o plano mudar ou você liberar na aba Módulos.
                  </p>
                ) : null}
              </>
            ) : null}
            <div className="flex justify-end gap-2 pt-2">
              {index === 0
                ? <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
                : <button type="button" onClick={() => setStep(steps[index - 1])} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>}
              <button type="submit" disabled={form.formState.isSubmitting} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
                {step === 'plano' ? 'Provisionar' : 'Avançar'}
              </button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
