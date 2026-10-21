import type { ApprovalLevelView } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'

const STATUS: Record<string, { label: string; tone: 'ok' | 'warn' | 'neutral' | 'crit' }> = {
  Pending: { label: 'Aguardando', tone: 'warn' }, Approved: { label: 'Aprovado', tone: 'ok' }, Rejected: { label: 'Reprovado', tone: 'crit' },
}

/** Níveis de aprovação da OP (FIN-03b CA 19): regra aplicada, valor de referência, aviso de fracionamento e situação. */
export function OrderLevels({ levels }: { levels: ApprovalLevelView[] }) {
  if (levels.length === 0) return null
  const warning = levels.find((l) => l.splitWarning)?.splitWarning
  return (
    <div className="mb-4 space-y-2 text-sm">
      {warning ? <p role="note" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-warn-ink">{warning}</p> : null}
      <ol className="divide-y divide-border rounded-md border border-border bg-card" aria-label="Níveis de aprovação">
        {levels.map((l) => (
          <li key={l.level} className="flex flex-wrap items-center justify-between gap-2 p-3">
            <span><b>Nível {l.level}</b> · {l.approverName}</span>
            <span className="flex items-center gap-2"><span className="text-xs text-muted-foreground">referência <Money value={l.referenceAmount} /></span><StatusChip tone={STATUS[l.status]?.tone ?? 'neutral'}>{STATUS[l.status]?.label ?? l.status}</StatusChip></span>
          </li>
        ))}
      </ol>
    </div>
  )
}
