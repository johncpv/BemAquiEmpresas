import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { deleteMyPosPin, getGetMyPosPinQueryKey, setMyPosPin, useGetMyPosPin } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatInstantDate as formatDate } from '@/lib/format'
import { pinProblem } from '@/pages/access/operation-users'

/**
 * Meu perfil → Segurança → PIN do caixa (PDV-01 CA 11): o PIN de 6 números que abre o caixa do PDV em qualquer caixa registrado da empresa. Só
 * para quem vende no caixa (pos.create) e não tem ação reservada; quem aprova (gerente) usa e-mail e senha. Só no Comércio.
 */
export function MyPosPinSection() {
  const client = useQueryClient()
  const mine = useGetMyPosPin()
  const [pin, setPin] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const save = async () => {
    const problem = pinProblem(pin, confirmation)
    if (problem) { setError(problem); return }
    setBusy(true)
    setError(null)
    try {
      await setMyPosPin({ pin, confirmation })
      await client.invalidateQueries({ queryKey: getGetMyPosPinQueryKey() })
      setPin('')
      setConfirmation('')
      notifySuccess('PIN do caixa salvo')
    } catch (e) {
      setError(e instanceof ApiError ? Object.values(e.fields).flat()[0] ?? e.message : 'Não foi possível salvar o PIN.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-labelledby="pin-caixa" className="mt-6 max-w-md space-y-3 rounded-md border border-border bg-card p-5">
      <h2 id="pin-caixa" className="font-semibold">PIN do caixa<Dica nome="PIN do caixa" texto="Número de 6 dígitos para entrar no caixa do PDV: toque no seu nome no caixa e digite o PIN. Vale em todos os caixas registrados da empresa." /></h2>
      {mine.isPending ? <Skeleton className="h-16" /> : mine.isError ? <ErrorState onRetry={() => mine.refetch()} /> : !mine.data.canUse ? (
        <p className="text-sm text-muted-foreground">O seu perfil não usa PIN no caixa: quem aprova ou tem acesso reservado entra com e-mail e senha.</p>
      ) : (
        <>
          <p className="text-sm text-muted-foreground tabular-nums">
            {mine.data.hasPin ? `Criado em ${formatDate(mine.data.createdAt!)}${mine.data.lastUsedAt ? ` · usado em ${formatDate(mine.data.lastUsedAt)}` : ' · ainda não usado'}.` : 'Você ainda não tem PIN do caixa.'}
          </p>
          <form onSubmit={(e) => { e.preventDefault(); void save() }} noValidate className="space-y-3">
            <FormError message={error ?? undefined} />
            <Field label={mine.data.hasPin ? 'PIN novo' : 'PIN'} hint="6 números, sem sequência nem repetição.">
              {(p) => <input {...p} inputMode="numeric" autoComplete="off" type="password" maxLength={6} value={pin} onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))} className={inputClass} />}
            </Field>
            <Field label="Repita o PIN">
              {(p) => <input {...p} inputMode="numeric" autoComplete="off" type="password" maxLength={6} value={confirmation} onChange={(e) => setConfirmation(e.target.value.replace(/\D/g, ''))} className={inputClass} />}
            </Field>
            <div className="flex flex-wrap gap-2">
              <button type="submit" disabled={busy} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{mine.data.hasPin ? 'Trocar o PIN do caixa' : 'Criar PIN do caixa'}</button>
              {mine.data.hasPin ? (
                <ConfirmDialog verb="Apagar o PIN do caixa" done="PIN apagado" destructive title="Apagar o seu PIN do caixa?"
                  description="O seu nome sai da lista dos caixas e a sua sessão num caixa cai. Para voltar, crie outro PIN aqui."
                  onConfirm={async () => { await deleteMyPosPin(); await client.invalidateQueries({ queryKey: getGetMyPosPinQueryKey() }) }}
                  trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Apagar</button>} />
              ) : null}
            </div>
          </form>
        </>
      )}
    </section>
  )
}
