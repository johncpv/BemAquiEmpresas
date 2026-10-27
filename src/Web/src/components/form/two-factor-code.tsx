import { onlyDigits } from '@/lib/masks'
import { Field } from './form'
import { inputClass } from './form-utils'

/** Código do aplicativo autenticador para o "2FA de novo" (ADR 0008, D6): 6 dígitos. */
export function TwoFactorCodeField({ code, onChange }: { code: string; onChange: (c: string) => void }) {
  return (
    <Field label="Código do aplicativo autenticador" required hint="Abra o aplicativo autenticador e digite os 6 dígitos.">
      {(p) => <input {...p} value={code} inputMode="numeric" autoComplete="one-time-code" maxLength={6} onChange={(e) => onChange(onlyDigits(e.target.value))} className={inputClass + ' tabular-nums tracking-widest'} />}
    </Field>
  )
}
