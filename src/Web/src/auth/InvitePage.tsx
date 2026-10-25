import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { acceptInvite, inspectInvite } from '@/api/generated/bemaqui'
import { setAccessToken } from '@/api/session'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { z } from '@/lib/zod'
import { AuthLayout } from './AuthLayout'
import { landingAfterLogin } from './home-route'
import { newPasswordSchema } from './password-schema'
import { pendingChallenge } from './pending-challenge'
import { linkClass, primaryButton } from './styles'

const schema = z.object({ name: z.string().min(2, 'Informe o seu nome.') }).and(newPasswordSchema)

/**
 * Aceitar convite (FND-04 CA 17 e 18): nome pré-preenchido, senha conforme a política e, ao concluir, a oferta de
 * configurar o 2FA (propositivo). Se a empresa exige 2FA, a configuração começa sozinha.
 */
export function InvitePage() {
  const { token = '' } = useSearch({ strict: false }) as { token?: string }
  const navigate = useNavigate()
  // O token vai no corpo (POST), nunca na URL da API; é uma consulta, então usa useQuery.
  const info = useQuery({ queryKey: ['invite', token], queryFn: () => inspectInvite({ token }), retry: false })
  const [done, setDone] = useState<{ offerTwoFactor: boolean; home: string } | null>(null)
  const form = useZodForm(schema, { values: { name: info.data?.name ?? '', password: '', confirm: '' } })

  const submit = form.handleSubmit(async ({ name, password }) => {
    try {
      const result = await acceptInvite({ token, name, password })
      if (result.twoFactor) {
        pendingChallenge.set({ ...result.twoFactor, purpose: 'setup' }) // sem volta: a página inicial de quem entrou
        await navigate({ to: '/entrar/configurar-2fa' })
        return
      }

      setAccessToken(result.accessToken)
      setDone({ offerTwoFactor: result.offerTwoFactor, home: await landingAfterLogin(undefined) }) // Proprietário: o painel (DSH-01a CA 3)
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  if (info.isPending) return <AuthLayout title="Convite"><Skeleton className="h-40" /></AuthLayout>
  if (info.isError) return <AuthLayout title="Convite"><ErrorState onRetry={() => info.refetch()} /></AuthLayout>

  if (done) {
    return (
      <AuthLayout title="Acesso criado">
        <div className="space-y-4">
          <p className="text-sm">Tudo pronto. Para proteger a conta, ative a verificação em duas etapas: leva um minuto.</p>
          {done.offerTwoFactor ? <Link to="/app/conta/seguranca" className={primaryButton + ' block text-center'}>Configurar verificação em duas etapas</Link> : null}
          <Link to={done.home as never} className={linkClass + ' block text-center'}>Fazer depois</Link>
        </div>
      </AuthLayout>
    )
  }

  if (info.data.state !== 'Valid') {
    return (
      <AuthLayout title="Convite">
        <div role="alert" className="space-y-4">
          <p className="text-sm">{info.data.message}</p>
          <Link to="/entrar" className={linkClass}>Ir para Entrar</Link>
        </div>
      </AuthLayout>
    )
  }

  const { errors } = form.formState
  return (
    <AuthLayout title="Criar acesso">
      <form onSubmit={submit} noValidate className="space-y-4">
        <p className="text-sm text-muted-foreground">Convite para <b className="text-foreground">{info.data.email}</b>.</p>
        <FormError message={errors.root?.server?.message} />
        <Field label="Seu nome" error={errors.name?.message}>{(p) => <input {...p} autoComplete="name" {...form.register('name')} className={inputClass} />}</Field>
        <Field label="Senha" hint="No mínimo 12 caracteres. Uma frase funciona bem." error={errors.password?.message}>
          {(p) => <input {...p} type="password" autoComplete="new-password" {...form.register('password')} className={inputClass} />}
        </Field>
        <Field label="Repita a senha" error={errors.confirm?.message}>{(p) => <input {...p} type="password" autoComplete="new-password" {...form.register('confirm')} className={inputClass} />}</Field>
        <button type="submit" disabled={form.formState.isSubmitting} className={primaryButton}>Criar acesso</button>
      </form>
    </AuthLayout>
  )
}
