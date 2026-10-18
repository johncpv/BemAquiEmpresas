import { useNavigate, useSearch } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { platformBeginTwoFactorSetup, platformConfirmTwoFactorSetup, platformLogin, platformVerifyTwoFactor } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { TwoFactorSetup } from '@/auth/TwoFactorSetup'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { InlineError } from '@/components/ui/states'
import { z } from '@/lib/zod'
import { PlatformCard } from './PlatformCard'

const schema = z.object({ email: z.email('Informe um e-mail válido.'), password: z.string().min(1, 'Informe a senha.') })
const button = 'w-full rounded-md bg-primary px-3 py-2.5 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/** Volta só para caminhos da plataforma ou para o painel de jobs (sem open redirect). */
const back = (volta?: string) => (volta === '/hangfire' || volta?.startsWith('/plataforma/') ? volta : '/plataforma/tenants')

/** Entrar na plataforma (FND-13 CA 3): senha e sempre 2FA (configuração no primeiro acesso, desafio depois). */
export function PlatformLoginPage() {
  const { volta } = useSearch({ strict: false }) as { volta?: string }
  const navigate = useNavigate()
  const form = useZodForm(schema, { defaultValues: { email: '', password: '' } })
  const [challenge, setChallenge] = useState<{ challenge: string; purpose: string } | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string>()
  const done = () => (back(volta) === '/hangfire' ? window.location.assign('/hangfire') : void navigate({ to: back(volta) as '/plataforma/tenants' }))

  const setupApi = useMemo(
    () =>
      challenge && {
        begin: () => platformBeginTwoFactorSetup({ challenge: challenge.challenge }),
        confirm: async (c: string) => ({ ...(await platformConfirmTwoFactorSetup({ challenge: challenge.challenge, code: c })), accessToken: '' }),
      },
    [challenge],
  )

  const submit = form.handleSubmit(async (values) => {
    try {
      setChallenge(await platformLogin(values))
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  const verify = async () => {
    setError(undefined)
    try {
      await platformVerifyTwoFactor({ challenge: challenge!.challenge, code, recoveryCode: null })
      done()
    } catch (x) {
      setError(x instanceof ApiError ? x.message : 'Não foi possível conferir o código.')
    }
  }

  if (challenge?.purpose === 'setup' && setupApi) {
    return (
      <PlatformCard title="Configure a verificação em duas etapas">
        <p className="mb-4 text-sm text-muted-foreground">Na plataforma, a verificação em duas etapas é obrigatória.</p>
        <TwoFactorSetup api={setupApi} onActivated={() => {}} onDone={done} />
      </PlatformCard>
    )
  }

  if (challenge) {
    return (
      <PlatformCard title="Verificação em duas etapas">
        <form onSubmit={(e) => { e.preventDefault(); void verify() }} className="space-y-4">
          <label htmlFor="codigo-plataforma" className="block text-sm font-medium">Código do app autenticador</label>
          <input id="codigo-plataforma" autoFocus value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" aria-invalid={!!error} aria-describedby={error ? 'codigo-plataforma-erro' : undefined} className={inputClass + ' text-center font-mono tracking-[0.3em]'} />
          <InlineError id="codigo-plataforma-erro" message={error} />
          <button type="submit" className={button}>Confirmar</button>
        </form>
      </PlatformCard>
    )
  }

  return (
    <PlatformCard title="Entrar na plataforma">
      <form onSubmit={submit} noValidate className="space-y-4">
        <FormError message={form.formState.errors.root?.server?.message} />
        <Field label="E-mail" error={form.formState.errors.email?.message}>{(p) => <input {...p} type="email" autoComplete="username" autoFocus {...form.register('email')} className={inputClass} />}</Field>
        <Field label="Senha" error={form.formState.errors.password?.message}>{(p) => <input {...p} type="password" autoComplete="current-password" {...form.register('password')} className={inputClass} />}</Field>
        <button type="submit" disabled={form.formState.isSubmitting} className={button}>Entrar</button>
      </form>
    </PlatformCard>
  )
}
