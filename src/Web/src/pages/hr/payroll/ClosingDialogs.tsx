import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { closePayrollRun, reopenPayrollRun, useGetPayrollClosePreviewSensitive, useGetTwoFactorStatus } from '@/api/generated/bemaqui'
import type { PayrollCloseResult, PayrollRunSummary } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { Money } from '@/components/ui/money'
import { Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate, formatInstantDate } from '@/lib/format'
import { PAYROLL_DICAS, periodLabel } from './payroll'

const button = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/**
 * Fechar folha (RH-02c CA 1 e 2): as pendências com o link para resolver, a data de pagamento sugerida pelo tipo (editável), os avisos
 * "Novos na folha" e "Conta alterada" e o impacto em títulos a pagar; confirma com o 2FA de novo. Nada é gravado se um título falhar.
 */
export function CloseRunDialog({ run, onClosed }: { run: PayrollRunSummary; onClosed: (result: PayrollCloseResult) => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const navigate = useNavigate()
  const preview = useGetPayrollClosePreviewSensitive(run.id, { query: { enabled: open, staleTime: 0 } })
  const twoFactor = useGetTwoFactorStatus({ query: { enabled: open } })
  const idempotency = useIdempotencyKey()
  const [date, setDate] = useState('')
  const [code, setCode] = useState('')
  const p = preview.data
  const paymentDate = date || p?.suggestedPaymentDate || ''
  const blockers = p?.blockers ?? []

  return (
    <ConfirmDialog size="lg" verb="Fechar folha" done="Folha fechada" title={`Fechar a folha de ${periodLabel(run.period)}?`}
      description="Os valores ficam congelados e os títulos a pagar nascem em aberto, para seguir para ordem de pagamento."
      open={open} onOpenChange={setOpen} onOpen={() => { setDate(''); setCode('') }}
      twoFactorRequired={twoFactor.data ? !twoFactor.data.enabled : false}
      preconditions={[
        ...blockers.map((b) => ({ ok: false, text: b.message, fix: b.link ? { label: 'Resolver', onSelect: () => { setOpen(false); void navigate({ to: b.link as never }) } } : undefined })),
        { ok: !!paymentDate, text: 'Data de pagamento informada.' },
      ]}
      impact={p ? (
        <p className="text-sm tabular-nums">
          Gera {p.salaryCount} {p.salaryCount === 1 ? 'título a pagar de salário' : 'títulos a pagar de salários'}, total <Money value={p.salaryTotal} />, e {p.chargeCount} {p.chargeCount === 1 ? 'título de guia' : 'títulos de guias'}, total <Money value={p.chargeTotal} />, que seguem para ordem de pagamento.
        </p>
      ) : undefined}
      onConfirm={async () => {
        if (code.length !== 6) throw new DialogMessage('Digite os 6 dígitos do aplicativo autenticador.')
        const result = await closePayrollRun(run.id, { paymentDate: paymentDate || null, twoFactorCode: code, version: p?.version ?? run.version }, idempotency.options)
        idempotency.renew()
        await onClosed(result)
      }}
      trigger={<button type="button" className={button}>Fechar folha</button>}>
      {preview.isPending ? <Skeleton className="mt-3 h-24" /> : null}
      {preview.isError ? <p role="alert" className="mt-3 text-sm text-crit-ink">Não foi possível carregar as pendências. Feche e abra de novo.</p> : null}
      {p ? (
        <div className="mt-3 space-y-3">
          <Field label="Data de pagamento" dica={PAYROLL_DICAS.dataPagamento} required
            hint={p.paymentDateRequired ? 'Férias e Rescisão: informe a data combinada.' : 'Sugerida pelo tipo da folha. Dia não útil antecipa.'}>
            {(f) => <DateInput {...f} value={paymentDate} onChange={setDate} />}
          </Field>
          {!p.paymentDateRequired && !p.paymentDateConfirmed ? <StatusChip tone="warn">A confirmar com a contadora</StatusChip> : null}
          {p.newcomers.length > 0 ? (
            <div className="rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm">
              <p className="font-semibold">Novos na folha</p>
              <p>{p.newcomers.map((n) => n.name).join(', ')}</p>
            </div>
          ) : null}
          {p.accountChanged.length > 0 ? (
            <div className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
              <p className="font-semibold">Conta alterada</p>
              <ul>{p.accountChanged.map((c) => <li key={c.employeeId}>{c.name}: conta em quarentena{c.quarantineUntil ? ` até ${formatInstantDate(c.quarantineUntil)}` : ''}, fora do pagamento automático.</li>)}</ul>
            </div>
          ) : null}
          <TwoFactorCodeField code={code} onChange={setCode} />
        </div>
      ) : null}
    </ConfirmDialog>
  )
}

/** Reabrir folha (RH-02c CA 13): com o motivo; os títulos em aberto são cancelados e quem cuida do Financeiro é avisado. */
export function ReopenRunDialog({ run, onReopened }: { run: PayrollRunSummary; onReopened: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog size="lg" verb="Reabrir folha" done="Folha reaberta" title={`Reabrir a folha de ${periodLabel(run.period)}?`}
      description="A folha volta para lançamento e os títulos a pagar dela são cancelados. Com pagamento em andamento, estorne ou cancele a ordem antes."
      onOpen={() => setReason('')}
      preconditions={[{ ok: reason.trim().length >= 5, text: 'Motivo com pelo menos 5 caracteres.' }]}
      impact={run.payableCount ? <p className="text-sm">Cancela {run.payableCount} {run.payableCount === 1 ? 'título a pagar' : 'títulos a pagar'} desta folha.</p> : undefined}
      onConfirm={async () => {
        await reopenPayrollRun(run.id, { reason: reason.trim(), version: run.version })
        await onReopened()
      }}
      trigger={<button type="button" className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Reabrir folha</button>}>
      <Field label="Motivo" required hint="Fica no histórico da folha.">
        {(f) => <textarea {...f} value={reason} maxLength={200} rows={3} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}

/** Faixa de modo leitura (RH-02c): "Folha fechada em {data}. Para mudar, reabra a folha." */
export function ClosedBand({ run }: { run: PayrollRunSummary }) {
  return (
    <p role="status" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">
      {run.status === 'Paid' ? `Folha paga${run.paidAt ? ` em ${formatInstantDate(run.paidAt)}` : ''}. ` : ''}
      Folha fechada{run.closedAt ? ` em ${formatInstantDate(run.closedAt)}` : ''}{run.paymentDate ? `, pagamento em ${formatDate(run.paymentDate)}` : ''}. Para mudar, reabra a folha.
    </p>
  )
}
