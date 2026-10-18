import { Link, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { platformSetPassword } from '@/api/generated/bemaqui'
import { newPasswordSchema } from '@/auth/password-schema'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { PlatformCard } from './PlatformCard'

/** Definir senha pelo link da CLI (FND-13 CA 1): uso único, 24 h. O 2FA vem no primeiro login. */
export function PlatformSetPasswordPage() {
  const { token = '' } = useSearch({ strict: false }) as { token?: string }
  const form = useZodForm(newPasswordSchema, { defaultValues: { password: '', confirm: '' } })
  const [done, setDone] = useState(false)
  const submit = form.handleSubmit(async ({ password }) => {
    try {
      await platformSetPassword({ token, password })
      setDone(true)
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  return (
    <PlatformCard title="Definir senha">
      {done ? (
        <div className="space-y-3">
          <p className="text-sm">Senha definida. No primeiro acesso, configure a verificação em duas etapas.</p>
          <Link to="/plataforma/entrar" className="text-sm font-semibold text-primary">Entrar na plataforma</Link>
        </div>
      ) : (
        <form onSubmit={submit} noValidate className="space-y-4">
          <FormError message={form.formState.errors.root?.server?.message} />
          <Field label="Senha nova" hint="No mínimo 12 caracteres." error={form.formState.errors.password?.message}>{(p) => <input {...p} type="password" autoComplete="new-password" autoFocus {...form.register('password')} className={inputClass} />}</Field>
          <Field label="Repita a senha" error={form.formState.errors.confirm?.message}>{(p) => <input {...p} type="password" autoComplete="new-password" {...form.register('confirm')} className={inputClass} />}</Field>
          <button type="submit" disabled={form.formState.isSubmitting} className="w-full rounded-md bg-primary px-3 py-2.5 text-sm font-semibold text-primary-foreground">Salvar senha</button>
        </form>
      )}
    </PlatformCard>
  )
}
