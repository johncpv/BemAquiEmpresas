import { useState } from 'react'
import { beginTwoFactorSetup, confirmTwoFactorSetup, disableTwoFactor, useGetTwoFactorStatus } from '@/api/generated/bemaqui'
import { setAccessToken } from '@/api/session'
import { TwoFactorSetup } from '@/auth/TwoFactorSetup'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { toastDone } from '@/components/ui/toast'
import { z } from '@/lib/zod'
import { PageHeading } from '@/shell/PageHeading'

const profileApi = { begin: () => beginTwoFactorSetup(), confirm: (code: string) => confirmTwoFactorSetup({ code }) }
const disableSchema = z.object({ password: z.string().min(1, 'Informe a senha.'), code: z.string().min(6, 'Informe o código do app.') })

/** Minha conta → Verificação em duas etapas (FND-05 CA 1, 2 e 10): ativar, ver códigos restantes e desativar. */
export function AccountSecurityPage() {
  const status = useGetTwoFactorStatus()
  const [activating, setActivating] = useState(false)
  const form = useZodForm(disableSchema, { defaultValues: { password: '', code: '' } })

  const disable = form.handleSubmit(async (values) => {
    try {
      setAccessToken((await disableTwoFactor(values)).accessToken)
      form.reset()
      toastDone('Verificação desativada')
      await status.refetch()
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  return (
    <>
      <PageHeading title="Verificação em duas etapas">
        <p className="mt-1 text-muted-foreground">Além da senha, um código do app autenticador do seu celular. Uma senha vazada não basta para entrar.</p>
      </PageHeading>
      {status.isPending ? (
        <Skeleton className="h-32" />
      ) : status.isError ? (
        <ErrorState onRetry={() => status.refetch()} />
      ) : status.data.enabled ? (
        <section className="max-w-md space-y-4 rounded-md border border-border bg-card p-5">
          <p className="flex items-center gap-2">
            <StatusChip tone="ok">Ativa</StatusChip>
            <span className="text-sm text-muted-foreground tabular-nums">{status.data.recoveryCodesLeft} códigos de recuperação restantes</span>
          </p>
          {status.data.required ? (
            <p className="text-sm text-muted-foreground">A empresa exige a verificação para a sua conta, então ela não pode ser desativada.</p>
          ) : (
            <form onSubmit={disable} noValidate className="space-y-3">
              <p className="text-sm font-semibold">Desativar</p>
              <FormError message={form.formState.errors.root?.server?.message} />
              <Field label="Senha" error={form.formState.errors.password?.message}>{(p) => <input {...p} type="password" autoComplete="current-password" {...form.register('password')} className={inputClass} />}</Field>
              <Field label="Código do app" error={form.formState.errors.code?.message}>{(p) => <input {...p} inputMode="numeric" autoComplete="one-time-code" {...form.register('code')} className={inputClass} />}</Field>
              <button type="submit" disabled={form.formState.isSubmitting} className="rounded-md bg-crit px-3 py-2 text-sm font-semibold text-critical-foreground">Desativar</button>
            </form>
          )}
        </section>
      ) : activating ? (
        <section className="max-w-md rounded-md border border-border bg-card p-5">
          <TwoFactorSetup api={profileApi} onActivated={(token) => setAccessToken(token)} onDone={() => { setActivating(false); toastDone('Verificação ativada'); void status.refetch() }} />
        </section>
      ) : (
        <section className="max-w-md space-y-3 rounded-md border border-border bg-card p-5">
          <StatusChip tone={status.data.required ? 'warn' : 'info'}>{status.data.required ? 'Obrigatória, ainda não configurada' : 'Desativada'}</StatusChip>
          <button type="button" onClick={() => setActivating(true)} className="block rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Ativar verificação em duas etapas</button>
        </section>
      )}
    </>
  )
}
