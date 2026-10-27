import { useState } from 'react'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { notifySuccess } from './messages'
import { StatusChip } from './status-chip'

export const TO_CONFIRM_LABEL = 'A confirmar com a contadora'
export const TO_CONFIRM_DICA = 'Valor pré-configurado pelo sistema. Vale desde já, mas precisa da conferência da contadora; quem confere marca como conferido. Mudar o valor volta para "A confirmar".'

/**
 * Selo de conferência (ADR 0042 §1, BNK-01b CA 2): "A confirmar com a contadora" até alguém com settings.edit clicar "Marcar como
 * conferido" (mensagem "Conferido"); conferido, mostra quem e quando. Reutilizado pela FIS-02a e pela WPP-01.
 */
export function ToConfirmChip({ state, reviewedByName, reviewedAt, canEdit, onConfirm, field }: {
  state: 'ToConfirm' | 'Confirmed'
  reviewedByName?: string | null
  reviewedAt?: string | null
  canEdit: boolean
  onConfirm: () => Promise<unknown>
  /** Nome do campo, para o botão ter nome acessível próprio ("Marcar como conferido: Multa"). */
  field?: string
}) {
  const [busy, setBusy] = useState(false)
  if (state === 'Confirmed') {
    return <StatusChip tone="ok">Conferido{reviewedByName ? ` por ${reviewedByName}` : ''}{reviewedAt ? ` em ${formatDateTime(reviewedAt)}` : ''}</StatusChip>
  }

  const confirm = async () => {
    setBusy(true)
    try {
      await onConfirm()
      notifySuccess('Conferido')
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <StatusChip tone="warn">{TO_CONFIRM_LABEL}</StatusChip>
      <Dica nome={TO_CONFIRM_LABEL} texto={TO_CONFIRM_DICA} />
      {canEdit ? (
        <button type="button" disabled={busy} onClick={() => void confirm()} aria-label={field ? `Marcar como conferido: ${field}` : undefined}
          className="min-h-11 rounded-md border border-border px-2 py-1 text-xs font-semibold disabled:opacity-60 sm:min-h-0">
          Marcar como conferido
        </button>
      ) : null}
    </span>
  )
}
