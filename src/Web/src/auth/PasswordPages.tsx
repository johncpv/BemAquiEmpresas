import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { forgotPassword, resetPassword } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { z } from '@/lib/zod'
import { AuthLayout } from './AuthLayout'
import { newPasswordSchema } from './password-schema'
import { linkClass, primaryButton } from './styles'

/** Esqueci minha senha (FND-04 CA 12): a resposta é sempre a mesma, exista ou não o e-mail. */
export function ForgotPasswordPage() {
  const search = useSearch({ strict: false }) as { email?: string }
  const form = useZodForm(z.object({ email: z.email('Informe um e-mail válido.') }), { defaultValues: { email: search.email ?? '' } })
  const [sent, setSent] = useState<string | null>(null)
  const submit = form.handleSubmit(async (values) => {
    try {
      setSent((await forgotPassword(values)).message)
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  return (
    <AuthLayout title="Esqueci minha senha">
      {sent ? (
        <div role="status" className="space-y-4">
          <p className="text-sm">{sent}</p>
          <Link to="/entrar" className={linkClass}>Voltar para Entrar</Link>
        </div>
      ) : (
        <form onSubmit={submit} noValidate className="space-y-4">
          <p className="text-sm text-muted-foreground">Informe o e-mail da sua conta. Se ele estiver cadastrado, enviamos um link para criar uma senha nova.</p>
          <FormError message={form.formState.errors.root?.server?.message} />
          <Field label="E-mail" error={form.formState.errors.email?.message}>
            {(p) => <input {...p} type="email" autoComplete="username" autoFocus {...form.register('email')} className={inputClass} />}
          </Field>
          <button type="submit" disabled={form.formState.isSubmitting} className={primaryButton}>Enviar link</button>
          <p className="text-center"><Link to="/entrar" className={linkClass}>Voltar para Entrar</Link></p>
        </form>
      )}
    </AuthLayout>
  )
}

/** Redefinir senha (FND-04 CA 13): link de uso único, 1 h; expirado oferece "Pedir novo link". */
export function ResetPasswordPage() {
  const { token = '' } = useSearch({ strict: false }) as { token?: string }
  const navigate = useNavigate()
  const form = useZodForm(newPasswordSchema, { defaultValues: { password: '', confirm: '' } })
  const [expired, setExpired] = useState<string | null>(null)
  const submit = form.handleSubmit(async ({ password }) => {
    try {
      await resetPassword({ token, password })
      notifySuccess('Senha redefinida. Entre com a senha nova.')
      await navigate({ to: '/entrar' })
    } catch (e) {
      if (e instanceof ApiError && e.code === 'auth.link_expired') setExpired(e.message)
      else applyServerErrors(form, e)
    }
  })

  return (
    <AuthLayout title="Criar senha nova">
      {expired ? (
        <div role="alert" className="space-y-4">
          <p className="text-sm">{expired}</p>
          <Link to="/esqueci-senha" className={primaryButton + ' block text-center'}>Pedir novo link</Link>
        </div>
      ) : (
        <form onSubmit={submit} noValidate className="space-y-4">
          <FormError message={form.formState.errors.root?.server?.message} />
          <Field label="Senha nova" hint="No mínimo 12 caracteres. Uma frase funciona bem." error={form.formState.errors.password?.message}>
            {(p) => <input {...p} type="password" autoComplete="new-password" autoFocus {...form.register('password')} className={inputClass} />}
          </Field>
          <Field label="Repita a senha" error={form.formState.errors.confirm?.message}>
            {(p) => <input {...p} type="password" autoComplete="new-password" {...form.register('confirm')} className={inputClass} />}
          </Field>
          <button type="submit" disabled={form.formState.isSubmitting} className={primaryButton}>Salvar senha</button>
        </form>
      )}
    </AuthLayout>
  )
}
