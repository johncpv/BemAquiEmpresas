import { type FormEvent, useState } from 'react'
import { rotateBankIntegration, saveBankIntegration } from '@/api/generated/bemaqui'
import type { BankIntegrationDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Dica } from '@/help/Dica'
import { ENVIRONMENTS, HINTS } from './inter'

type Mode = 'save' | 'rotate'
type CertificateKind = 'pem' | 'pfx'

const fileClass = 'block w-full text-sm file:mr-3 file:min-h-11 file:rounded-md file:border file:border-border file:bg-card file:px-3 file:font-semibold file:text-foreground'

/**
 * "Salvar integração bancária" (primeira configuração) e "Trocar credenciais" (BNK-01a CA 3, 4, 12 e 14). Os segredos vão uma vez
 * para o servidor e nunca voltam: os campos começam vazios. Na troca, o que ficar em branco continua o atual e o servidor testa a
 * combinação nova antes de trocar. Com a cobrança real liberada, ligar a Produção pede "Ligar a cobrança real". O 2FA de novo vem
 * do passo único do app (o servidor pede).
 */
export function InterCredentialsForm({ detail, mode, onDone, onCancel }: { detail: BankIntegrationDetail; mode: Mode; onDone: (saved: BankIntegrationDetail) => void; onCancel: () => void }) {
  const [environment, setEnvironment] = useState(detail.environment || 'Sandbox')
  const [clientId, setClientId] = useState('')
  const [clientSecret, setClientSecret] = useState('')
  const [accountNumber, setAccountNumber] = useState(detail.accountNumber ?? '')
  const [kind, setKind] = useState<CertificateKind>('pem')
  const [certificate, setCertificate] = useState<File | null>(null)
  const [privateKey, setPrivateKey] = useState<File | null>(null)
  const [pfx, setPfx] = useState<File | null>(null)
  const [pfxPassword, setPfxPassword] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [confirmProduction, setConfirmProduction] = useState(false)
  const rotate = mode === 'rotate'
  const goingLive = environment === 'Production' && detail.productionAllowed && (!rotate || detail.environment !== 'Production')

  const validate = () => {
    const next: Record<string, string> = {}
    if (!rotate && !clientId.trim()) next.clientId = 'Informe o client id gerado no Inter.'
    if (!rotate && !clientSecret.trim()) next.clientSecret = 'Informe o client secret gerado no Inter.'
    const hasCertificate = kind === 'pem' ? !!certificate || !!privateKey : !!pfx
    if (!rotate || hasCertificate) {
      if (kind === 'pem' && (!certificate || !privateKey)) next.certificate = 'Envie o certificado (.crt) e a chave privada (.key).'
      if (kind === 'pfx' && !pfx) next.certificate = 'Envie o arquivo .pfx com a senha.'
    }
    if (rotate && !clientId.trim() && !clientSecret.trim() && !hasCertificate) next.clientSecret = 'Envie o client secret novo, o client id novo ou o certificado novo.'
    if (accountNumber && !/^\d{1,20}$/.test(accountNumber.trim())) next.accountNumber = 'Informe a conta corrente só com números.'
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const send = async (confirm: boolean): Promise<boolean> => {
    setBusy(true)
    setFormError(undefined)
    try {
      const body = {
        environment: rotate && environment === detail.environment ? undefined : environment,
        clientId: clientId.trim() || undefined,
        clientSecret: clientSecret.trim() || undefined,
        accountNumber: accountNumber.trim() || undefined,
        certificate: kind === 'pem' ? certificate ?? undefined : undefined,
        privateKey: kind === 'pem' ? privateKey ?? undefined : undefined,
        pfx: kind === 'pfx' ? pfx ?? undefined : undefined,
        pfxPassword: kind === 'pfx' && pfxPassword ? pfxPassword : undefined,
        confirmProduction: confirm || undefined,
        version: detail.version ?? undefined,
      }
      const saved = rotate ? await rotateBankIntegration(detail.bankAccountId, body) : await saveBankIntegration(detail.bankAccountId, body)
      setClientSecret('')
      setPfxPassword('')
      onDone(saved)
      return true
    } catch (e) {
      if (e instanceof ApiError) {
        const fields = Object.fromEntries(Object.entries(e.fields).map(([k, v]) => [k, v[0] ?? '']))
        setErrors(fields)
        if (Object.keys(fields).length === 0 || e.code === 'bank_connection_refused' || e.code === 'bank_credential_can_pay') setFormError(e.message)
      } else {
        setFormError('Não foi possível salvar. Tente de novo.')
      }
      return false
    } finally {
      setBusy(false)
    }
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!validate()) return
    if (goingLive) {
      setConfirmProduction(true)
      return
    }
    void send(false)
  }

  const verb = rotate ? 'Trocar credenciais' : 'Salvar integração bancária'
  return (
    <form onSubmit={submit} noValidate aria-label={verb} className="space-y-4 rounded-md border border-border bg-card p-4 sm:p-5">
      <FormError message={formError} />
      {rotate ? <p className="text-sm text-muted-foreground">Envie só o que mudou. O que ficar em branco continua como está. As credenciais novas são testadas no Inter antes de trocar; se falharem, as atuais continuam valendo.</p> : null}
      <fieldset className="space-y-1">
        <legend className="flex items-center text-sm font-medium">Ambiente<Dica texto={HINTS.environment} nome="Ambiente" /></legend>
        <div className="flex flex-wrap gap-2">
          {ENVIRONMENTS.map((e) => (
            <label key={e.value} className="flex min-h-11 items-center gap-2 rounded-md border border-border px-3 text-sm has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
              <input type="radio" name="environment" value={e.value} checked={environment === e.value} onChange={() => setEnvironment(e.value)} className="accent-primary" />
              {e.label}
            </label>
          ))}
        </div>
        {environment === 'Production' && !detail.productionAllowed ? (
          <p role="status" className="text-xs text-muted-foreground">A credencial de produção pode ser salva e testada. A cobrança real será ligada na virada do sistema.</p>
        ) : null}
      </fieldset>
      <Field label="Client id" error={errors.clientId} hint={rotate ? 'Em branco: continua o atual.' : undefined} required={!rotate}>
        {(p) => <input {...p} value={clientId} onChange={(e) => setClientId(e.target.value)} autoComplete="off" spellCheck={false} className={inputClass} />}
      </Field>
      <Field label="Client secret" error={errors.clientSecret} dica={HINTS.clientSecret} hint="Guardado cifrado; nunca é mostrado de novo." required={!rotate}>
        {(p) => <input {...p} type="password" value={clientSecret} onChange={(e) => setClientSecret(e.target.value)} autoComplete="new-password" spellCheck={false} className={inputClass} />}
      </Field>
      <Field label="Conta corrente no Inter" error={errors.accountNumber} hint="Só os números, como aparecem no Internet Banking.">
        {(p) => <input {...p} value={accountNumber} onChange={(e) => setAccountNumber(e.target.value)} inputMode="numeric" autoComplete="off" className={inputClass + ' tabular-nums'} />}
      </Field>
      <fieldset className="space-y-2">
        <legend className="flex items-center text-sm font-medium">Certificado do Inter<Dica texto={HINTS.certificate} nome="Certificado do Inter" />{rotate ? <span className="ml-1 text-xs font-normal text-muted-foreground">(em branco: continua o atual)</span> : null}</legend>
        <div className="flex flex-wrap gap-2">
          <label className="flex min-h-11 items-center gap-2 rounded-md border border-border px-3 text-sm has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
            <input type="radio" name="certificateKind" checked={kind === 'pem'} onChange={() => setKind('pem')} className="accent-primary" />Arquivos .crt e .key
          </label>
          <label className="flex min-h-11 items-center gap-2 rounded-md border border-border px-3 text-sm has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
            <input type="radio" name="certificateKind" checked={kind === 'pfx'} onChange={() => setKind('pfx')} className="accent-primary" />Arquivo .pfx
          </label>
        </div>
        {kind === 'pem' ? (
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="space-y-1 text-sm font-medium">Certificado (.crt)
              <input type="file" accept=".crt,.pem,.cer" onChange={(e) => setCertificate(e.target.files?.[0] ?? null)} className={fileClass} />
            </label>
            <label className="space-y-1 text-sm font-medium">Chave privada (.key)
              <input type="file" accept=".key,.pem" onChange={(e) => setPrivateKey(e.target.files?.[0] ?? null)} className={fileClass} />
            </label>
          </div>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="space-y-1 text-sm font-medium">Arquivo .pfx
              <input type="file" accept=".pfx,.p12" onChange={(e) => setPfx(e.target.files?.[0] ?? null)} className={fileClass} />
            </label>
            <Field label="Senha do .pfx" error={errors.pfx ?? errors.pfxPassword}>
              {(p) => <input {...p} type="password" value={pfxPassword} onChange={(e) => setPfxPassword(e.target.value)} autoComplete="new-password" className={inputClass} />}
            </Field>
          </div>
        )}
        {errors.certificate || errors.privateKey ? <p role="alert" className="text-xs font-medium text-crit-ink">{errors.certificate ?? errors.privateKey}</p> : null}
        <p className="text-xs text-muted-foreground">Até 20 KB por arquivo. O arquivo não fica salvo em pasta nenhuma: só o conteúdo cifrado.</p>
      </fieldset>
      <div className="flex flex-wrap justify-end gap-2 pt-1">
        <button type="button" onClick={onCancel} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold">Voltar</button>
        <button type="submit" disabled={busy} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground disabled:opacity-60">
          {busy ? (rotate ? 'Testando as credenciais novas…' : 'Conferindo no Inter…') : verb}
        </button>
      </div>
      <ConfirmDialog
        open={confirmProduction}
        onOpenChange={setConfirmProduction}
        verb="Ligar a cobrança real"
        done="Cobrança real ligada"
        title="Ligar a cobrança real?"
        description="Boletos emitidos em produção são cobranças reais ao cliente."
        size="lg"
        onConfirm={async () => {
          if (!(await send(true))) throw new DialogMessage('O Inter não aceitou. Confira o motivo no formulário.')
        }}
      />
    </form>
  )
}
