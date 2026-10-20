import { useState } from 'react'
import { loseQuote } from '@/api/generated/bemaqui'
import type { QuoteLostReason } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

const REASONS: { value: NonNullable<QuoteLostReason>; label: string }[] = [
  { value: 'Price', label: 'Preço' }, { value: 'Deadline', label: 'Prazo' }, { value: 'Competitor', label: 'Concorrente' },
  { value: 'CustomerGaveUp', label: 'Desistência do cliente' }, { value: 'NoAnswer', label: 'Sem retorno' }, { value: 'Other', label: 'Outro' },
]

/** Registrar perda (COM-01b CA 11): motivo da lista e detalhe (obrigatório em "Outro"). */
export function LoseDialog({ quoteId, version, onDone }: { quoteId: string; version: number; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState<NonNullable<QuoteLostReason> | ''>('')
  const [detail, setDetail] = useState('')
  return (
    <ConfirmDialog verb="Registrar perda" done="Perda registrada" title="Registrar a perda do orçamento?" description="O orçamento fica só para consulta. Dá para copiar para um novo."
      preconditions={[{ ok: !!reason, text: 'Motivo escolhido.' }, { ok: reason !== 'Other' || detail.trim().length > 0, text: 'Detalhe escrito (obrigatório em "Outro").' }]}
      onConfirm={async () => { await loseQuote(quoteId, { reason: reason || null, detail: detail.trim() || null, version }); await onDone() }}
      trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Registrar perda</button>}>
      <div className="mt-3 space-y-3">
        <label className="block space-y-1 text-sm font-medium">Motivo
          <select value={reason} onChange={(e) => setReason(e.target.value as NonNullable<QuoteLostReason>)} className={inputClass}><option value="">Escolha</option>{REASONS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}</select>
        </label>
        <label className="block space-y-1 text-sm font-medium">Detalhe<textarea rows={3} maxLength={500} value={detail} onChange={(e) => setDetail(e.target.value)} className={inputClass} /></label>
      </div>
    </ConfirmDialog>
  )
}
