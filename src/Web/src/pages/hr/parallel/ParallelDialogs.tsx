import { useState } from 'react'
import { acceptPayrollParallel, attachPayrollParallelTerm, revertPayrollToEntered, switchPayrollToCalculated } from '@/api/generated/bemaqui'
import type { PayrollModeView, PayrollParallelDetail, PayrollParallelOverview } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { periodLabel } from '../payroll/payroll'
import { PARALLEL_DICAS, TYPE_LABELS, validCrc } from './parallel'

const primary = 'inline-flex min-h-11 items-center gap-2 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'inline-flex min-h-11 items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

/**
 * "Aceitar competência {mês}" (CA 3, só o Proprietário): a contadora responsável (nome e CRC) e o termo assinado; o termo é enviado e
 * passa pelo antivírus antes do aceite. Sem diferença, só.
 */
export function AcceptDialog({ detail, onAccepted }: { detail: PayrollParallelDetail; onAccepted: () => Promise<void> }) {
  const run = detail.run
  const month = periodLabel(run.period)
  const idempotency = useIdempotencyKey()
  const [name, setName] = useState('')
  const [crc, setCrc] = useState('')
  const [file, setFile] = useState<File | null>(null)
  return (
    <ConfirmDialog size="lg" verb={`Aceitar competência ${month}`} done="Competência aceita" title={`Aceitar a competência ${month}?`}
      description="A folha calculada bateu com a da contadora em todas as rubricas e no líquido. O aceite fica registrado com a contadora e o termo assinado, e conta para a virada."
      onOpen={() => { setName(''); setCrc(''); setFile(null) }}
      preconditions={[
        { ok: name.trim().length >= 3, text: 'Nome da contadora responsável informado.' },
        { ok: validCrc(crc), text: 'CRC da contadora informado (ex.: SP-123456/O).' },
        { ok: file !== null, text: 'Termo de conferência assinado anexado.' },
      ]}
      impact={<p>{run.employees} {run.employees === 1 ? 'funcionário' : 'funcionários'}, diferença zero. A competência não muda mais depois do aceite.</p>}
      onConfirm={async () => {
        if (!file) throw new DialogMessage('Anexe o termo de conferência assinado.')
        const term = await attachPayrollParallelTerm(run.id, { file })
        await acceptPayrollParallel(run.id, { accountantName: name, accountantCrc: crc, signedTermAttachmentId: term.id, version: run.version }, idempotency.options)
        idempotency.renew()
        await onAccepted()
      }}
      trigger={<button type="button" className={primary}>Aceitar competência {month}</button>}>
      <Field label="Nome da contadora responsável" required>
        {(f) => <input {...f} value={name} maxLength={120} onChange={(e) => setName(e.target.value)} className={inputClass} />}
      </Field>
      <Field label="CRC" required hint="Como está no registro do conselho (ex.: SP-123456/O).">
        {(f) => <input {...f} value={crc} maxLength={30} onChange={(e) => setCrc(e.target.value)} className={inputClass} />}
      </Field>
      <Field label="Termo de conferência assinado" required dica={PARALLEL_DICAS.termo} hint="PDF ou imagem (PNG, JPG) até 10 MB. O antivírus confere o arquivo antes do aceite.">
        {(f) => <input {...f} type="file" accept="application/pdf,image/png,image/jpeg" onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="block text-sm" />}
      </Field>
    </ConfirmDialog>
  )
}

/** "Virar para a folha calculada" (CA 5, padrão 7): o impacto em lista e a confirmação; só o Proprietário vê o botão. */
export function SwitchDialog({ view, onDone }: { view: PayrollParallelOverview; onDone: () => Promise<void> }) {
  const idempotency = useIdempotencyKey()
  const from = view.switchFrom ? periodLabel(view.switchFrom) : ''
  const accepted = view.runs.filter((r) => r.countsForSwitch)
  const withCase = view.modes.filter((m) => m.type !== 'Monthly' && m.mode === 'Entered' && m.acceptedCase)
  return (
    <ConfirmDialog size="lg" verb="Virar para a folha calculada" done="Folha calculada é a oficial" title="Virar para a folha calculada?"
      description={`A partir de ${from}, a folha calculada Mensal fecha como oficial e gera os títulos a pagar e o caixa. A folha lançada continua disponível.`}
      impactList={{
        label: 'O que muda',
        items: [
          ...accepted.map((r) => ({ key: r.id, primary: `Competência ${periodLabel(r.period)} aceita`, secondary: r.accountantName ? `Contadora ${r.accountantName}, CRC ${r.accountantCrc}` : undefined })),
          { key: 'monthly', primary: `Folha Mensal calculada é a oficial a partir de ${from}`, secondary: 'Fechar a folha calculada passa a gerar os títulos.' },
          ...withCase.map((m) => ({ key: m.type, primary: `${TYPE_LABELS[m.type] ?? m.label}: vira junto (caso conferido)`, secondary: m.situation })),
        ],
      }}
      onConfirm={async () => {
        await switchPayrollToCalculated({ confirmed: true }, idempotency.options)
        idempotency.renew()
        await onDone()
      }}
      trigger={<button type="button" className={primary}>Virar para a folha calculada</button>} />
  )
}

/** "Voltar para a folha lançada" (CA 7): por tipo ou todos, com o motivo; a contagem das seguidas recomeça. */
export function RevertDialog({ modes, onDone }: { modes: PayrollModeView[]; onDone: () => Promise<void> }) {
  const idempotency = useIdempotencyKey()
  const calculated = modes.filter((m) => m.mode === 'Calculated')
  const [type, setType] = useState<string>('')
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog size="lg" verb="Voltar para a folha lançada" done="Folha lançada é a oficial" title="Voltar para a folha lançada?"
      description="A folha da contadora volta a ser a oficial; a calculada volta a ser simulação. A contagem das competências seguidas aceitas recomeça."
      onOpen={() => { setType(''); setReason('') }}
      preconditions={[{ ok: reason.trim().length >= 10, text: 'Motivo informado (de 10 a 500 caracteres).' }]}
      onConfirm={async () => {
        await revertPayrollToEntered({ type: type === '' ? null : (type as PayrollModeView['type']), reason }, idempotency.options)
        idempotency.renew()
        await onDone()
      }}
      trigger={<button type="button" className={secondary}>Voltar para a folha lançada</button>}>
      <Field label="Tipo" hint="A Mensal leva todos: férias, 13º e rescisão dependem dela.">
        {(f) => (
          <select {...f} value={type} onChange={(e) => setType(e.target.value)} className={inputClass}>
            <option value="">Todos</option>
            {calculated.map((m) => <option key={m.type} value={m.type}>{TYPE_LABELS[m.type] ?? m.label}</option>)}
          </select>
        )}
      </Field>
      <Field label="Motivo" required>
        {(f) => <textarea {...f} value={reason} rows={3} maxLength={500} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}
