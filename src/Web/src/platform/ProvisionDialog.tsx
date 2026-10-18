import * as Dialog from '@radix-ui/react-dialog'
import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { provisionTenant, useListPlans } from '@/api/generated/bemaqui'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { isValidCnpj } from '@/lib/cnpj'
import { z } from '@/lib/zod'

const schema = z.object({
  key: z.string().regex(/^[a-z][a-z0-9-]{1,29}[a-z0-9]$/, 'Use de 3 a 31 letras minúsculas, números ou hífen, começando por letra.'),
  legalName: z.string().min(2, 'Informe a razão social.'),
  tradeName: z.string().optional(),
  document: z.string().optional().refine((v) => !v || isValidCnpj(v), 'CNPJ inválido. Confira os dígitos.'),
  host: z.string().regex(/^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]*$/, 'Informe um endereço válido, como empresa.bemaquiapp.com.br.'),
  ownerEmail: z.email('Informe um e-mail válido.'),
  plan: z.string().optional(),
})

/** "Provisionar tenant" (FND-03b CA 2 e 3): valida no cliente e no servidor; roda em segundo plano e abre o progresso. */
export function ProvisionDialog() {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const plans = useListPlans({ query: { enabled: open } })
  const form = useZodForm(schema, { defaultValues: { key: '', legalName: '', tradeName: '', document: '', host: '', ownerEmail: '', plan: '' } })
  const { errors } = form.formState
  const submit = form.handleSubmit(async (v) => {
    try {
      // Plano pré-selecionado quando só existe um (propositivo).
      const plan = v.plan || (plans.data?.length === 1 ? plans.data[0] : null)
      const accepted = await provisionTenant({ key: v.key, legalName: v.legalName, tradeName: v.tradeName || null, document: v.document || null, host: v.host, ownerEmail: v.ownerEmail, plan })
      setOpen(false)
      form.reset()
      await navigate({ to: '/plataforma/tenants/$key', params: { key: accepted.key } })
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  const text = (name: 'key' | 'legalName' | 'tradeName' | 'document' | 'host' | 'ownerEmail', label: string, hint?: string, type = 'text') => (
    <Field label={label} hint={hint} error={errors[name]?.message}>{(p) => <input {...p} type={type} {...form.register(name)} className={inputClass} />}</Field>
  )

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Provisionar tenant</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-[5vh] z-50 max-h-[90vh] w-[min(480px,calc(100vw-32px))] -translate-x-1/2 overflow-y-auto rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <Dialog.Title className="text-base font-semibold">Provisionar tenant</Dialog.Title>
          <Dialog.Description className="mt-1 text-sm text-muted-foreground">Cria o banco, aplica a estrutura, carrega os dados iniciais e convida o proprietário.</Dialog.Description>
          <form onSubmit={submit} noValidate className="mt-3 space-y-3">
            <FormError message={errors.root?.server?.message} />
            {text('key', 'Chave', 'Identifica o tenant no sistema. Ex.: konebras')}
            {text('legalName', 'Razão social')}
            {text('tradeName', 'Nome fantasia')}
            {text('document', 'CNPJ')}
            {text('host', 'Endereço', 'O DNS precisa apontar para o servidor.')}
            {text('ownerEmail', 'E-mail do proprietário', 'Recebe o convite para criar o acesso.', 'email')}
            <Field label="Plano" error={errors.plan?.message}>
              {(p) => (
                <select {...p} {...form.register('plan')} className={inputClass}>
                  {(plans.data ?? []).length !== 1 ? <option value="">Padrão</option> : null}
                  {(plans.data ?? []).map((name) => <option key={name} value={name}>{name}</option>)}
                </select>
              )}
            </Field>
            <div className="flex justify-end gap-2 pt-2">
              <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
              <button type="submit" disabled={form.formState.isSubmitting} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Provisionar</button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
