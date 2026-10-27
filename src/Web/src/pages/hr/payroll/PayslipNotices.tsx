import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import {
  generatePayslipBatch, notifyPayslipAgain, notifyPayslips,
} from '@/api/generated/bemaqui'
import type { PayrollRunSummary, PayslipNoticeRow, PayslipNotices } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { periodLabel } from './payroll'
import { downloadBatch, downloadPayslip } from './payslip-downloads'
import { noticeLabel, PAYSLIP_DICAS, plural, saveInviteSuggestion } from './payslip-notices'

const outline = 'min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold'
const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'

/** "Baixar todos os holerites" (CA 2): até 20, o ZIP na hora; acima, "Gerando {n} holerites…" e o aviso no sino quando pronto. */
export function BatchButton({ run }: { run: PayrollRunSummary }) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const start = async () => {
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      const result = await generatePayslipBatch(run.id)
      if (result.queued) setMessage(`${result.message} Você recebe um aviso no sino quando estiver pronto.`)
      else if (result.attachmentId) {
        await downloadBatch(run, result.attachmentId)
        notifySuccess(result.message)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Não foi possível gerar os holerites. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-col gap-1">
      <button type="button" onClick={() => void start()} disabled={busy} className={outline}>{busy ? 'Gerando holerites…' : 'Baixar todos os holerites'}</button>
      {message ? <p role="status" className="text-sm text-muted-foreground">{message}</p> : null}
      {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
    </div>
  )
}

/** Faixa do ZIP pronto, quando se chega pelo aviso do sino (?lote=). */
export function BatchReady({ run, attachmentId, onDismiss }: { run: PayrollRunSummary; attachmentId: string; onDismiss: () => void }) {
  return (
    <section aria-label="Holerites prontos" className="flex flex-wrap items-center gap-2 rounded-md border border-primary-soft bg-primary-soft px-3 py-2 text-sm">
      <b>Os holerites de {periodLabel(run.period)} estão prontos.</b>
      <button type="button" className={primary} onClick={() => void downloadBatch(run, attachmentId)}>Baixar ZIP</button>
      <button type="button" className="ml-auto text-sm font-semibold text-primary" onClick={onDismiss}>Fechar</button>
    </section>
  )
}

/**
 * "Avisar funcionários" (CA 7, 8 e 10): quantos recebem o aviso, os sem usuário (com "Convidar usuário" já preenchido) e os desligados
 * (com "Baixar holerite" para entregar pessoalmente). Confirmar enfileira um e-mail por funcionário, sem anexo nem valor.
 */
export function NotifyDialog({ run, notices, onlyChanged = false, open, onOpenChange, onDone, trigger }: {
  run: PayrollRunSummary
  notices?: PayslipNotices
  onlyChanged?: boolean
  open?: boolean
  onOpenChange?: (open: boolean) => void
  onDone: () => Promise<void>
  trigger?: React.ReactNode
}) {
  const navigate = useNavigate()
  const idempotency = useIdempotencyKey()
  const n = notices ? (onlyChanged ? notices.changed : notices.pending) : 0
  const invite = (name: string, email?: string | null) => {
    saveInviteSuggestion(name, email)
    void navigate({ to: '/app/acessos/usuarios', search: { convidar: '1' } as never })
  }

  return (
    <ConfirmDialog size="lg" verb={onlyChanged ? 'Avisar os alterados' : `Avisar ${plural(n, 'funcionário', 'funcionários')}`} done={`${plural(n, 'aviso', 'avisos')} na fila de envio`}
      title={onlyChanged ? 'Avisar os funcionários dos holerites alterados?' : `Avisar os funcionários da folha de ${periodLabel(run.period)}?`}
      description={<>Cada funcionário recebe um e-mail: "Entre no sistema e abra Meus holerites." O e-mail não leva anexo nem valor.<Dica nome="Aviso" texto={PAYSLIP_DICAS.aviso} /></>}
      open={open} onOpenChange={onOpenChange} trigger={trigger}
      preconditions={[{ ok: n > 0, text: n > 0 ? `${plural(n, 'funcionário recebe', 'funcionários recebem')} o aviso.` : 'Nenhum funcionário a avisar agora.' }]}
      onConfirm={async () => {
        await notifyPayslips(run.id, { onlyChanged }, idempotency.options)
        idempotency.renew()
        await onDone()
      }}>
      {!notices ? <Skeleton className="mt-3 h-20" /> : (
        <div className="mt-3 space-y-3 text-sm">
          {notices.alreadyNotified > 0 && !onlyChanged ? <p>{plural(notices.alreadyNotified, 'funcionário já foi avisado', 'funcionários já foram avisados')}: para repetir, use "Avisar de novo" no holerite.</p> : null}
          {notices.withoutUser.length > 0 ? (
            <section aria-label="Sem usuário" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-warn-ink">
              <p className="font-semibold">{plural(notices.withoutUser.length, 'funcionário sem usuário', 'funcionários sem usuário')} (ficam sem o aviso)</p>
              <ul className="mt-1 space-y-1">
                {notices.withoutUser.map((p) => (
                  <li key={p.employeeId} className="flex flex-wrap items-center justify-between gap-2">
                    <span>{p.name}{p.registration ? ` · ${p.registration}` : ''}</span>
                    <button type="button" className="min-h-11 font-semibold text-primary underline sm:min-h-0" onClick={() => invite(p.name, p.suggestedEmail)}>Convidar usuário</button>
                  </li>
                ))}
              </ul>
            </section>
          ) : null}
          {notices.terminated.length > 0 ? (
            <section aria-label="Desligados" className="rounded-md border border-border bg-muted px-3 py-2">
              <p className="font-semibold">{plural(notices.terminated.length, 'desligado', 'desligados')}: entregue o holerite pessoalmente</p>
              <ul className="mt-1 space-y-1">
                {notices.terminated.map((p) => {
                  const row = notices.rows.find((r) => r.employeeId === p.employeeId)
                  return (
                    <li key={p.employeeId} className="flex flex-wrap items-center justify-between gap-2">
                      <span>{p.name}</span>
                      {row ? <button type="button" className="min-h-11 font-semibold text-primary underline sm:min-h-0" onClick={() => void downloadPayslip(run, row)}>Baixar holerite</button> : null}
                    </li>
                  )
                })}
              </ul>
            </section>
          ) : null}
        </div>
      )}
    </ConfirmDialog>
  )
}

/** Coluna de aviso e de leitura na aba Holerites (CA 9 e 10): situação, "Visto em", "Alterado depois do aviso" e "Avisar de novo". */
export function NoticeCell({ run, row, canNotify, onChanged }: { run: PayrollRunSummary; row: PayslipNoticeRow; canNotify: boolean; onChanged: () => Promise<void> }) {
  const label = noticeLabel(row)
  const idempotency = useIdempotencyKey()
  const again = row.status !== 'NoUser' && row.status !== 'Terminated' && row.status !== 'NotNotified'
  return (
    <span className="flex flex-wrap items-center gap-2">
      <StatusChip tone={label.tone}>{label.text}</StatusChip>
      {row.firstViewedAt ? <span className="text-xs text-muted-foreground">Visto em {formatInstantDate(row.firstViewedAt)}</span> : null}
      {row.changedAfterNotice ? <StatusChip tone="warn">Alterado depois do aviso</StatusChip> : null}
      {canNotify && again ? (
        <ConfirmDialog verb="Avisar de novo" done="Aviso na fila de envio" title={`Avisar ${row.employeeName} de novo?`}
          description="Sai mais um e-mail só de aviso, sem anexo nem valor."
          onConfirm={async () => {
            await notifyPayslipAgain(run.id, row.payslipId, idempotency.options)
            idempotency.renew()
            await onChanged()
          }}
          trigger={<button type="button" className="min-h-11 text-sm font-semibold text-primary sm:min-h-0">Avisar de novo</button>} />
      ) : null}
    </span>
  )
}
