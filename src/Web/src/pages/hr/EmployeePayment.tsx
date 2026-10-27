import { useQueryClient } from '@tanstack/react-query'
import { ShieldAlert } from 'lucide-react'
import { useEffect, useState } from 'react'
import {
  getGetEmployeeBankStatusQueryKey, getGetEmployeePaymentSensitiveQueryKey, getGetEmployeeQueryKey, revealEmployeeBankAccount, reviewEmployeeBankAccount, saveEmployeeAdditional,
  saveEmployeeBankAccount, useGetEmployeePaymentSensitive, useGetTwoFactorStatus,
} from '@/api/generated/bemaqui'
import type { EmployeeBankAccountRevealed, EmployeeDetail, EmployeePaymentDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ToggleGroup } from '@/components/form/choice'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CpfInput } from '@/components/form/inputs'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatInstantDate } from '@/lib/format'
import { onlyDigits } from '@/lib/masks'
import { useCan } from '@/pages/use-can'
import {
  ACCOUNT_TYPE_LABELS, type BankDraft, bankChip, bankDraftErrors, bankMismatches, COMPLEMENT_DICAS, emptyBankDraft, INSALUBRITY_GRADES, MISMATCH_REASON_MAX, MISMATCH_WARNING,
  mismatchConfirmed, PIX_TYPE_LABELS, REVIEW_NOTE_MAX,
} from './employee-complement'
import { EmployeeProtected } from './EmployeeProtected'

const button = 'min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/** Aba Pagamento e adicionais (CAD-04c2 CA 4 a 10): conta mascarada, quarentena, revisão, adicionais e vale-transporte. */
export function EmployeePayment({ employee, canEdit }: { employee: EmployeeDetail; canEdit: boolean }) {
  const canSee = useCan('hr.view_sensitive')
  const query = useGetEmployeePaymentSensitive(employee.id, { query: { enabled: canSee, gcTime: 0, staleTime: 0 } })
  const client = useQueryClient()
  const [flow, setFlow] = useState(false)
  const [reviewing, setReviewing] = useState(false)
  const [steps, setSteps] = useState(false)
  if (!canSee) return <EmployeeProtected />
  if (query.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar a conta para pagamento." onRetry={() => query.refetch()} />
  const payment = query.data
  const changed = (next: EmployeePaymentDetail) => {
    client.setQueryData(getGetEmployeePaymentSensitiveQueryKey(employee.id), next)
    void client.invalidateQueries({ queryKey: getGetEmployeeBankStatusQueryKey(employee.id) })
    void client.invalidateQueries({ queryKey: getGetEmployeeQueryKey(employee.id) })
  }

  return (
    <div className="max-w-3xl space-y-6">
      {steps && payment.status.canReview ? <NextSteps onDismiss={() => setSteps(false)} steps={[{ label: 'Revisar conta', onSelect: () => { setSteps(false); setReviewing(true) } }]} /> : null}
      <BankSection payment={payment} employee={employee} canEdit={canEdit} onChange={() => setFlow(true)} onReview={() => setReviewing(true)} />
      {flow ? <BankAccountFlow employee={employee} payment={payment} onClose={() => setFlow(false)} onReload={() => query.refetch()} onDone={(next) => { changed(next); setSteps(next.status.inQuarantine) }} /> : null}
      {reviewing ? <ReviewDialog employee={employee} payment={payment} onClose={() => setReviewing(false)} onReload={() => query.refetch()} onDone={changed} /> : null}
      <AdditionalForm key={payment.version} employee={employee} payment={payment} canEdit={canEdit} onSaved={changed} onReload={() => query.refetch()} />
    </div>
  )
}

function BankSection({ payment, employee, canEdit, onChange, onReview }: { payment: EmployeePaymentDetail; employee: EmployeeDetail; canEdit: boolean; onChange: () => void; onReview: () => void }) {
  const [shown, setShown] = useState<EmployeeBankAccountRevealed | null>(null)
  const [error, setError] = useState<string>()
  const account = payment.account
  const status = payment.status
  const chip = bankChip(status, formatInstantDate)
  const reveal = async () => {
    setError(undefined)
    try { setShown(await revealEmployeeBankAccount(employee.id)) } catch (e) { setError(e instanceof ApiError ? e.message : 'Não foi possível mostrar a conta.') }
  }

  if (!account) {
    return (
      <FormSection title="Conta para pagamento" description="Banco e Pix para receber o salário. Dado protegido pela LGPD; cada leitura fica registrada.">
        <EmptyState title="Nenhuma conta para pagamento." description="Registre a conta do funcionário antes da primeira folha." action={canEdit ? <button type="button" onClick={onChange} className={primary}>Registrar conta para pagamento</button> : undefined} />
      </FormSection>
    )
  }

  const row = (label: string, masked: string | null | undefined, clear?: string | null) => (
    <div className="flex flex-wrap justify-between gap-2 py-1.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="tabular-nums font-medium">{(shown ? clear : masked) || '—'}</dd>
    </div>
  )

  return (
    <FormSection title="Conta para pagamento" description="Agência, conta, titular e chave Pix ficam mascarados. Mostrar registra a leitura.">
      <div className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          {chip ? <StatusChip tone={chip.tone}>{chip.label}</StatusChip> : null}
          {chip?.tone === 'warn' ? <Dica nome="Conta em quarentena" texto={COMPLEMENT_DICAS.quarentena} /> : null}
          {status.isFirstAccount ? <StatusChip tone="info">Novo na folha</StatusChip> : null}
          {status.changedAt ? <span className="text-xs text-muted-foreground">{status.isFirstAccount ? 'Registrada' : 'Trocada'} em <span className="tabular-nums">{formatDateTime(status.changedAt)}</span>{status.changedByName ? ` por ${status.changedByName}` : ''}</span> : null}
        </div>
        {account.mismatches.length > 0 ? (
          <p role="note" className="flex items-start gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
            <ShieldAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
            {MISMATCH_WARNING.replace(' Confira antes de salvar.', '')} {account.mismatches.includes('Holder') ? 'O titular é outra pessoa.' : 'A chave Pix é o CPF de outra pessoa.'}
          </p>
        ) : null}
        <dl className="divide-y divide-border rounded-md border border-border bg-card px-3 text-sm">
          {row('Banco', account.bankCode, shown?.bankCode)}
          {row('Agência', account.agency, shown?.agency)}
          {row('Conta', account.account, shown?.account)}
          {row('Tipo', account.accountType ? ACCOUNT_TYPE_LABELS[account.accountType] : null, shown?.accountType ? ACCOUNT_TYPE_LABELS[shown.accountType] : null)}
          {row('Titular', `${account.holderName} · CPF ${account.holderCpf}`, shown ? `${shown.holderName ?? ''} · CPF ${shown.holderCpf ?? ''}` : null)}
          {row('Chave Pix', account.pixKeyType ? `${PIX_TYPE_LABELS[account.pixKeyType]}: ${account.pixKey}` : null, shown?.pixKeyType ? `${PIX_TYPE_LABELS[shown.pixKeyType]}: ${shown.pixKey}` : null)}
        </dl>
        <FormError message={error} />
        <div className="flex flex-wrap gap-2">
          {shown ? <button type="button" onClick={() => setShown(null)} className={button}>Ocultar</button> : <button type="button" onClick={() => void reveal()} className={button}>Mostrar</button>}
          {canEdit ? <button type="button" onClick={onChange} className={button}>Trocar conta</button> : null}
          {status.canReview ? <span className="inline-flex items-center"><button type="button" onClick={onReview} className={primary}>Revisar conta</button><Dica nome="Revisar conta" texto={COMPLEMENT_DICAS.revisar} /></span> : null}
        </div>
        {status.inQuarantine && !status.canReview && status.cannotReviewReason ? <p className="text-xs text-muted-foreground">{status.cannotReviewReason} Outra pessoa do Proprietário revisa.</p> : null}
      </div>
    </FormSection>
  )
}

/** Mensagem do 2FA de novo recusado: o servidor diz o que fazer; sem 2FA ativo, o diálogo leva a "Ativar 2FA". */
const CodeField = TwoFactorCodeField

/**
 * Registrar ou trocar a conta (CA 4 e 5): formulário → segunda confirmação (quando a chave ou o titular não é o CPF do funcionário)
 * → 2FA de novo. O titular vem preenchido com o funcionário e a chave Pix como CPF (propositivo).
 */
function BankAccountFlow({ employee, payment, onClose, onDone, onReload }: { employee: EmployeeDetail; payment: EmployeePaymentDetail; onClose: () => void; onDone: (p: EmployeePaymentDetail) => void; onReload: () => Promise<unknown> }) {
  const twoFactor = useGetTwoFactorStatus()
  const [step, setStep] = useState<'form' | 'mismatch' | 'code' | null>(null)
  const [current, setCurrent] = useState<EmployeeBankAccountRevealed | null>(null)
  const [draft, setDraft] = useState<BankDraft | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [checked, setChecked] = useState(false)
  const [reason, setReason] = useState('')
  const [code, setCode] = useState('')
  const [loadError, setLoadError] = useState<string>()
  const [attempt, setAttempt] = useState(0)
  const close = (from: string) => (o: boolean) => { if (!o) setStep((s) => { if (s === from) { onClose(); return null } return s }) }
  const hasAccount = !!payment.account

  useEffect(() => {
    let alive = true
    // O diálogo precisa do nome e do CPF do funcionário e da conta atual em claro: a leitura fica registrada (CA 9).
    revealEmployeeBankAccount(employee.id).then((r) => {
      if (!alive) return
      setCurrent(r)
      setDraft(hasAccount && r.holderCpf ? {
        bankCode: r.bankCode ?? '', agency: r.agency ?? '', account: r.account ?? '', accountType: r.accountType ?? '', holderName: r.holderName ?? r.employeeName,
        holderCpf: r.holderCpf, pixKeyType: r.pixKeyType ?? '', pixKey: r.pixKey ?? '',
      } : emptyBankDraft(r.employeeName, r.employeeCpf))
      setLoadError(undefined)
      setStep('form')
    }, (e: unknown) => {
      if (!alive) return
      setLoadError(e instanceof ApiError ? e.message : 'Não foi possível abrir a conta.')
      setStep('form')
    })
    return () => { alive = false }
  }, [employee.id, hasAccount, attempt])

  if (loadError || !draft || !current) {
    return step === 'form' ? (
      <ConfirmDialog open onOpenChange={close('form')} verb="Tentar de novo" done="" title="Não foi possível abrir a conta" description={loadError}
        onConfirm={async () => { setStep(null); setAttempt((a) => a + 1) }} />
    ) : null
  }

  const set = (patch: Partial<BankDraft>) => { setErrors({}); setDraft((d) => (d ? { ...d, ...patch } : d)) }
  const mismatches = bankMismatches(draft, current.employeeCpf)
  const first = !payment.account
  const verb = 'Salvar conta para pagamento'
  const text = (k: keyof BankDraft, label: string, max: number, extra: { required?: boolean; dica?: string; numeric?: boolean } = {}) => (
    <Field label={label} required={extra.required} error={errors[k]} dica={extra.dica}>
      {(p) => <input {...p} value={draft[k]} maxLength={max} autoComplete="off" inputMode={extra.numeric ? 'numeric' : undefined} onChange={(e) => set({ [k]: e.target.value })} className={inputClass + (extra.numeric ? ' tabular-nums' : '')} />}
    </Field>
  )
  const save = async () => {
    const body = {
      bankCode: onlyDigits(draft.bankCode) || null, agency: draft.agency.trim() || null, account: draft.account.trim() || null, accountType: draft.bankCode ? draft.accountType || null : null,
      holderName: draft.holderName.trim(), holderCpf: onlyDigits(draft.holderCpf), pixKeyType: draft.pixKeyType || null, pixKey: draft.pixKeyType === 'Cpf' ? onlyDigits(draft.pixKey) : draft.pixKeyType ? draft.pixKey.trim() : null,
      checkedWithEmployee: mismatches.length > 0 && checked, mismatchReason: mismatches.length > 0 ? reason.trim() : null, twoFactorCode: code, version: payment.version,
    }
    onDone(await saveEmployeeBankAccount(employee.id, body))
  }

  return (
    <>
      <ConfirmDialog open={step === 'form'} onOpenChange={close('form')} size="lg" verb={verb} done="" title={first ? `Registrar conta para pagamento de ${employee.name}` : `Trocar a conta para pagamento de ${employee.name}`}
        description={first ? 'A primeira conta entra direto na folha.' : 'A conta nova fica 30 dias em quarentena, até outra pessoa revisar. O Proprietário e o funcionário são avisados.'}
        onConfirm={async () => {
          const found = bankDraftErrors(draft)
          if (Object.keys(found).length > 0) { setErrors(found); throw new DialogMessage('Confira os campos destacados.') }
          setStep(mismatches.length > 0 ? 'mismatch' : 'code')
        }}>
        <div className="grid gap-3 sm:grid-cols-2">
          {text('bankCode', 'Banco (código COMPE)', 3, { numeric: true, dica: 'Os 3 dígitos do banco, ex.: 001 (Banco do Brasil), 077 (Inter), 341 (Itaú).' })}
          <Field label="Tipo da conta" error={errors.accountType}>
            {(p) => (
              <select {...p} value={draft.accountType} onChange={(e) => set({ accountType: e.target.value as BankDraft['accountType'] })} className={inputClass}>
                <option value="">Escolha</option>
                {Object.entries(ACCOUNT_TYPE_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            )}
          </Field>
          {text('agency', 'Agência', 10, { numeric: true })}
          {text('account', 'Conta com dígito', 20, { numeric: true })}
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          {text('holderName', 'Titular da conta', 150, { required: true, dica: COMPLEMENT_DICAS.titular })}
          <Field label="CPF do titular" required error={errors.holderCpf}>{(p) => <CpfInput {...p} value={draft.holderCpf} onChange={(holderCpf) => set({ holderCpf })} />}</Field>
          <Field label="Tipo da chave Pix" error={errors.pixKeyType}>
            {(p) => (
              <select {...p} value={draft.pixKeyType} onChange={(e) => { const t = e.target.value as BankDraft['pixKeyType']; set({ pixKeyType: t, pixKey: t === 'Cpf' ? current.employeeCpf : '' }) }} className={inputClass}>
                <option value="">Sem chave Pix</option>
                {Object.entries(PIX_TYPE_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
              </select>
            )}
          </Field>
          {draft.pixKeyType ? text('pixKey', 'Chave Pix', 77) : null}
        </div>
        {mismatches.length > 0 ? (
          <p role="status" className="flex items-start gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
            <ShieldAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />{MISMATCH_WARNING}
          </p>
        ) : null}
      </ConfirmDialog>
      <ConfirmDialog open={step === 'mismatch'} onOpenChange={close('mismatch')} size="lg" verb="Salvar mesmo assim" done="" title="Conferir a conta com o funcionário"
        description={MISMATCH_WARNING}
        preconditions={[{ ok: mismatchConfirmed(checked, reason), text: 'Marque "Conferi com o funcionário" e informe o motivo, de 10 a 200 caracteres.' }]}
        onConfirm={async () => setStep('code')}>
        <ul aria-label="O que não bate" className="list-disc space-y-1 pl-5 text-sm tabular-nums">{mismatches.map((m) => <li key={m.kind}>{m.text}</li>)}</ul>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="size-4" checked={checked} onChange={(e) => setChecked(e.target.checked)} />
          Conferi com o funcionário
        </label>
        <Field label="Motivo" required hint="Ex.: conta conjunta com a esposa, conferida por telefone. Fica no histórico, mascarado.">
          {(p) => <textarea {...p} value={reason} maxLength={MISMATCH_REASON_MAX} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
        </Field>
      </ConfirmDialog>
      <ConfirmDialog open={step === 'code'} onOpenChange={close('code')} verb="Confirmar" done={first ? 'Conta para pagamento registrada' : 'Conta para pagamento trocada'} title="Confirme com o 2FA"
        description="Trocar a conta de salário exige o código do aplicativo autenticador de novo." twoFactorRequired={twoFactor.data ? !twoFactor.data.enabled : false} onReload={onReload}
        onOpen={() => setCode('')}
        onConfirm={async () => {
          if (code.length !== 6) throw new DialogMessage('Digite os 6 dígitos do aplicativo autenticador.')
          await save()
        }}>
        <CodeField code={code} onChange={setCode} />
      </ConfirmDialog>
    </>
  )
}

/** Revisão expressa (CA 8): confere os dados (mascarados, com Mostrar), informa como conferiu e libera com o 2FA de novo. */
function ReviewDialog({ employee, payment, onClose, onDone, onReload }: { employee: EmployeeDetail; payment: EmployeePaymentDetail; onClose: () => void; onDone: (p: EmployeePaymentDetail) => void; onReload: () => Promise<unknown> }) {
  const twoFactor = useGetTwoFactorStatus()
  const [note, setNote] = useState('')
  const [code, setCode] = useState('')
  const [shown, setShown] = useState<EmployeeBankAccountRevealed | null>(null)
  const account = payment.account
  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} size="lg" verb="Liberar conta" done="Conta liberada" title={`Revisar a conta de ${employee.name}`}
      description={`Trocada em ${payment.status.changedAt ? formatDateTime(payment.status.changedAt) : '—'}${payment.status.changedByName ? ` por ${payment.status.changedByName}` : ''}. Confira com o funcionário antes de liberar.`}
      twoFactorRequired={twoFactor.data ? !twoFactor.data.enabled : false} onReload={onReload}
      preconditions={[{ ok: note.trim().length > 0, text: 'Informe como conferiu a conta.' }]}
      onConfirm={async () => {
        if (code.length !== 6) throw new DialogMessage('Digite os 6 dígitos do aplicativo autenticador.')
        onDone(await reviewEmployeeBankAccount(employee.id, { note: note.trim(), twoFactorCode: code, version: payment.version }))
      }}>
      {account ? (
        <div className="rounded-md border border-border bg-muted px-3 py-2 text-sm tabular-nums">
          <p>Banco {(shown ? shown.bankCode : account.bankCode) ?? '—'} · agência {(shown ? shown.agency : account.agency) ?? '—'} · conta {(shown ? shown.account : account.account) ?? '—'}</p>
          <p>Titular {shown ? shown.holderName : account.holderName} · CPF {shown ? shown.holderCpf : account.holderCpf}</p>
          {account.pixKeyType ? <p>Pix {PIX_TYPE_LABELS[account.pixKeyType]}: {shown ? shown.pixKey : account.pixKey}</p> : null}
          {shown ? null : <button type="button" onClick={async () => setShown(await revealEmployeeBankAccount(employee.id))} className="mt-1 font-semibold text-primary underline">Mostrar</button>}
        </div>
      ) : null}
      <Field label="Como conferiu" required hint="Ex.: liguei para o funcionário no telefone do cadastro.">
        {(p) => <textarea {...p} value={note} maxLength={REVIEW_NOTE_MAX} rows={2} onChange={(e) => setNote(e.target.value)} className={inputClass} />}
      </Field>
      <CodeField code={code} onChange={setCode} />
    </ConfirmDialog>
  )
}

/** Adicionais e vale-transporte (CA 10): o valor do vale e o desconto vêm da folha. */
function AdditionalForm({ employee, payment, canEdit, onSaved, onReload }: { employee: EmployeeDetail; payment: EmployeePaymentDetail; canEdit: boolean; onSaved: (p: EmployeePaymentDetail) => void; onReload: () => Promise<unknown> }) {
  const [insalubrity, setInsalubrity] = useState(payment.insalubrityPct?.toString() ?? '')
  const [hazard, setHazard] = useState(payment.hazardPay)
  const [voucher, setVoucher] = useState<'uses' | 'no'>(payment.usesTransportVoucher ? 'uses' : 'no')
  const [error, setError] = useState<{ message: string; code?: string }>()
  const save = async () => {
    setError(undefined)
    try {
      onSaved(await saveEmployeeAdditional(employee.id, { insalubrityPct: insalubrity ? Number(insalubrity) : null, hazardPay: hazard, usesTransportVoucher: voucher === 'uses', version: payment.version }))
      notifySuccess('Adicionais salvos')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, code: e.code } : { message: 'Não foi possível salvar os adicionais.' })
    }
  }

  return (
    <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }}>
      <FormSection title="Adicionais e vale-transporte" description="O valor do vale-transporte e o desconto saem na folha.">
        <FormError message={error?.message} />
        {error?.code === 'concurrency_conflict' ? <button type="button" onClick={() => void onReload()} className={button}>Recarregar</button> : null}
        <fieldset disabled={!canEdit} className="grid gap-3 sm:grid-cols-2">
          <Field label="Insalubridade">
            {(p) => (
              <select {...p} value={insalubrity} onChange={(e) => setInsalubrity(e.target.value)} className={inputClass}>
                <option value="">Não recebe</option>
                {INSALUBRITY_GRADES.map((g) => <option key={g} value={g}>Grau {g === 10 ? 'mínimo' : g === 20 ? 'médio' : 'máximo'} ({g}%)</option>)}
              </select>
            )}
          </Field>
          <label className="flex min-h-11 items-center gap-2 self-end text-sm">
            <input type="checkbox" className="size-4" checked={hazard} onChange={(e) => setHazard(e.target.checked)} />
            Recebe periculosidade
          </label>
          <div className="space-y-1">
            <span className="flex items-center text-sm font-medium">Vale-transporte<Dica nome="Vale-transporte" texto={COMPLEMENT_DICAS.valeTransporte} /></span>
            <ToggleGroup label="Vale-transporte" value={voucher} onChange={setVoucher} disabled={!canEdit} options={[{ value: 'uses', label: 'Usa' }, { value: 'no', label: 'Não usa' }]} />
          </div>
        </fieldset>
        {canEdit ? <div className="mt-3 flex justify-end"><button type="submit" className={primary}>Salvar adicionais</button></div> : null}
      </FormSection>
    </form>
  )
}

/** Chip do cabeçalho do funcionário (CA 7): só a situação da conta, sem dado bancário. */
export function BankStatusChip({ status }: { status: { inQuarantine: boolean; quarantineUntil?: string | null; reviewedAt?: string | null; selfReviewed: boolean; changedAt?: string | null } }) {
  const chip = bankChip(status, formatInstantDate)
  if (!chip || chip.tone !== 'warn') return null
  return (
    <span className="inline-flex items-center">
      <StatusChip tone="warn">{chip.label}</StatusChip>
      <Dica nome="Conta em quarentena" texto={`${COMPLEMENT_DICAS.quarentena}${status.changedAt ? ` Trocada em ${formatInstantDate(status.changedAt)}.` : ''}`} />
    </span>
  )
}
