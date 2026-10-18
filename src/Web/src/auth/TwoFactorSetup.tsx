import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { InlineError, Skeleton } from '@/components/ui/states'
import { RecoveryCodes } from './RecoveryCodes'

export interface SetupApi {
  begin: () => Promise<{ manualKey: string; otpAuthUri: string }>
  confirm: (code: string) => Promise<{ recoveryCodes: string[]; accessToken: string }>
}

/**
 * Ativação do 2FA (FND-05 CA 1 e 2): QR desenhado no navegador (sem serviço externo) e chave manual; só liga depois de
 * um código válido; aí mostra os códigos de recuperação uma única vez.
 */
export function TwoFactorSetup({ api, onActivated, onDone }: { api: SetupApi; onActivated: (accessToken: string) => void; onDone: () => void }) {
  const [setup, setSetup] = useState<{ manualKey: string; qr: string } | null>(null)
  const [failed, setFailed] = useState<string | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string>()
  const [codes, setCodes] = useState<string[] | null>(null)

  useEffect(() => {
    let alive = true
    api.begin().then(
      async (s) => alive && setSetup({ manualKey: s.manualKey, qr: await QRCode.toDataURL(s.otpAuthUri, { margin: 1, width: 192 }) }),
      (e: unknown) => alive && setFailed(e instanceof ApiError ? e.message : 'Não foi possível começar a ativação.'),
    )
    return () => {
      alive = false
    }
  }, [api])

  if (codes) return <RecoveryCodes codes={codes} onDone={onDone} />
  if (failed) return <p role="alert" className="text-sm text-crit-ink">{failed}</p>
  if (!setup) return <Skeleton className="mx-auto size-48" />

  const confirm = async () => {
    setError(undefined)
    try {
      const result = await api.confirm(code)
      onActivated(result.accessToken)
      setCodes(result.recoveryCodes)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível ativar. Tente de novo.')
    }
  }

  return (
    <form onSubmit={(e) => { e.preventDefault(); void confirm() }} className="space-y-4">
      <ol className="list-decimal space-y-1 pl-5 text-sm text-muted-foreground">
        <li>Abra o app autenticador (Google Authenticator, Microsoft Authenticator ou outro).</li>
        <li>Leia o QR code ou digite a chave.</li>
        <li>Informe o código de 6 dígitos que aparece no app.</li>
      </ol>
      <img src={setup.qr} alt="QR code para o app autenticador" className="mx-auto size-48 rounded-md" />
      <p className="text-center font-mono text-sm tracking-wider" aria-label="Chave para digitar no app">{setup.manualKey}</p>
      <div>
        <label htmlFor="codigo-2fa" className="block text-sm font-medium">Código do app</label>
        <input id="codigo-2fa" value={code} onChange={(e) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" maxLength={7} aria-invalid={!!error} aria-describedby={error ? 'codigo-2fa-erro' : undefined} className={inputClass + ' mt-1 text-center font-mono tracking-[0.3em]'} />
        <InlineError id="codigo-2fa-erro" message={error} />
      </div>
      <button type="submit" className="w-full rounded-md bg-primary px-3 py-2.5 text-sm font-semibold text-primary-foreground">Ativar</button>
    </form>
  )
}
