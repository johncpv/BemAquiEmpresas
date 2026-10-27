import * as Dialog from '@radix-ui/react-dialog'
import { ShieldCheck } from 'lucide-react'
import { type FormEvent, useEffect, useId, useRef, useState } from 'react'
import { stepUpTwoFactor } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { setAccessToken } from '@/api/session'
import { setStepUpHandler, STEP_UP_CONFIRM, STEP_UP_ENROLL } from '@/api/step-up'

const TWO_FACTOR_SETUP = '/app/conta/seguranca'

interface Pending {
  enrolled: boolean
  resolve: (confirmed: boolean) => void
}

/**
 * Passo "Confirme o código" reutilizável (BNK-01a CA 1, D6): um só diálogo no app, aberto pelo mutator quando a API recusa com
 * two_factor_required. Com o 2FA ativo, pede o código do aplicativo e troca só o access token (a sessão continua); sem 2FA, o estado
 * único "Para continuar, ative a verificação em duas etapas." com o link para ativar. A 400 px, ocupa a tela.
 */
export function StepUpHost() {
  const [pending, setPending] = useState<Pending | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const errorId = useId()

  useEffect(() => {
    setStepUpHandler((enrolled) => new Promise<boolean>((resolve) => {
      setCode('')
      setError(null)
      setPending({ enrolled, resolve })
    }))
    return () => setStepUpHandler(null)
  }, [])

  const close = (confirmed: boolean) => {
    pending?.resolve(confirmed)
    setPending(null)
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const value = code.replace(/\s/g, '')
    if (!/^\d{6}$/.test(value)) {
      setError('Informe os 6 números do aplicativo autenticador.')
      inputRef.current?.focus()
      return
    }

    setBusy(true)
    setError(null)
    try {
      const result = await stepUpTwoFactor({ code: value })
      setAccessToken(result.accessToken)
      close(true)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível confirmar o código. Tente de novo.')
      setCode('')
      inputRef.current?.focus()
    } finally {
      setBusy(false)
    }
  }

  const button = 'min-h-11 rounded-md border border-border bg-card px-3 py-1.5 text-sm font-semibold text-foreground'
  return (
    <Dialog.Root open={!!pending} onOpenChange={(open) => { if (!open) close(false) }}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[60] bg-foreground/30" />
        <Dialog.Content
          aria-describedby={undefined}
          onOpenAutoFocus={(e) => { e.preventDefault(); inputRef.current?.focus() }}
          className="fixed left-1/2 top-1/2 z-[61] w-[min(420px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2 rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg max-sm:inset-0 max-sm:w-full max-sm:translate-x-0 max-sm:translate-y-0 max-sm:rounded-none"
        >
          <Dialog.Title className="flex items-center gap-2 text-lg font-semibold">
            <ShieldCheck aria-hidden="true" className="size-5 text-primary" />
            {pending?.enrolled === false ? 'Ative a verificação em duas etapas' : 'Confirme o código'}
          </Dialog.Title>
          {pending?.enrolled === false ? (
            <div className="mt-3 space-y-4">
              <p role="status" className="text-sm">{STEP_UP_ENROLL}</p>
              <div className="flex flex-wrap justify-end gap-2">
                <button type="button" onClick={() => close(false)} className={button}>Voltar</button>
                <a href={TWO_FACTOR_SETUP} className="inline-flex min-h-11 items-center rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground">Ativar verificação em duas etapas</a>
              </div>
            </div>
          ) : (
            <form onSubmit={(e) => void submit(e)} noValidate className="mt-3 space-y-4">
              <p className="text-sm">{STEP_UP_CONFIRM}</p>
              <label className="block space-y-1 text-sm font-medium">
                Código do aplicativo
                <input
                  ref={inputRef}
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={7}
                  aria-invalid={!!error}
                  aria-describedby={error ? errorId : undefined}
                  className="block min-h-11 w-full rounded-md border border-input bg-background px-3 text-lg tabular-nums tracking-widest"
                />
              </label>
              {error ? <p id={errorId} role="alert" className="text-sm font-medium text-crit-ink">{error}</p> : null}
              <div className="flex flex-wrap justify-end gap-2">
                <button type="button" onClick={() => close(false)} className={button}>Cancelar</button>
                <button type="submit" disabled={busy} className="min-h-11 rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground disabled:opacity-60">
                  {busy ? 'Confirmando…' : 'Confirmar'}
                </button>
              </div>
            </form>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
