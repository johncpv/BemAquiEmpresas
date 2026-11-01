import { useQueryClient } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { getListNumberGapsQueryKey, invalidateNumberGaps, useListNumberGaps } from '@/api/generated/bemaqui'
import type { NumberGapRangeView } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { cn } from '@/lib/utils'
import { EVENT_DICAS, EVENT_TEXTS, rangeNumbers } from './outgoing-invoices'

const danger = 'inline-flex min-h-11 items-center rounded-md border border-crit-soft bg-card px-3 text-sm font-semibold text-crit-ink disabled:opacity-60'

/**
 * Filtro "A inutilizar" de Notas de saída (FIS-03b CA 10 e 12): os números gastos por rejeição, já em faixas contínuas por série, com o
 * motivo e a data; mais de 30 dias em destaque. "Inutilizar números" só para quem tem fiscal.approve, com o impacto em lista, a palavra
 * digitada e o 2FA de novo (o passo global pede o código).
 */
export function NumberGapsPanel() {
  const list = useListNumberGaps()
  if (list.isPending) return <div className="space-y-2"><Skeleton className="h-24" /><Skeleton className="h-24" /></div>
  if (list.isError) return <ErrorState title="Não foi possível carregar os números a inutilizar." onRetry={() => void list.refetch()} />
  const data = list.data
  if (data.ranges.length === 0) return <EmptyState title={EVENT_TEXTS.noGaps} description="Números gastos por rejeição da SEFAZ aparecem aqui, em faixas, para inutilizar." />
  return (
    <div className="space-y-2">
      <p className="flex items-center text-sm tabular-nums">{data.total} números a inutilizar em {data.ranges.length} faixas<Dica nome="Inutilizar" texto={EVENT_DICAS.inutilizar} /></p>
      <ul className="space-y-2" aria-label="Faixas a inutilizar">
        {data.ranges.map((r) => (
          <li key={`${r.series}-${r.firstNumber}`} className={cn('rounded-md border border-border bg-card p-3 text-sm', r.old && 'border-l-4 border-l-crit')}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="font-semibold tabular-nums">Série {r.series} · {r.firstNumber === r.lastNumber ? `número ${r.label}` : `números ${r.label}`} ({r.count})</p>
              <div className="flex flex-wrap items-center gap-2">
                {r.old ? <StatusChip tone="crit">Há mais de 30 dias</StatusChip> : null}
                {data.canInvalidate ? <InvalidateDialog range={r} /> : null}
              </div>
            </div>
            <ul className="mt-2 divide-y divide-border text-muted-foreground" aria-label={`Números da série ${r.series}`}>
              {r.numbers.map((n) => (
                <li key={n.id} className="flex flex-wrap justify-between gap-2 py-1 tabular-nums">
                  <span>{n.number}</span>
                  <span>{n.reason === 'Rejected' ? 'Rejeitada pela SEFAZ' : n.reason}</span>
                  <span>{formatInstantDate(n.createdAt)}</span>
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
      {!data.canInvalidate ? <p role="note" className="text-sm text-muted-foreground">Só o Proprietário inutiliza números.</p> : null}
    </div>
  )
}

function InvalidateDialog({ range: r }: { range: NumberGapRangeView }) {
  const [justification, setJustification] = useState('')
  const [error, setError] = useState<string | null>(null)
  const client = useQueryClient()
  const id = useId()
  const length = justification.replace(/\s+/g, ' ').trim().length
  const numbers = rangeNumbers(r.firstNumber, r.lastNumber)
  return (
    <ConfirmDialog destructive size="lg" verb="Inutilizar números" done="" title={`Inutilizar ${r.count === 1 ? 'o número' : 'os números'} ${r.label} da série ${r.series}?`}
      description="A SEFAZ registra que estes números não vão virar nota. Número com nota autorizada ou cancelada é recusado antes do envio."
      trigger={<button type="button" className={danger}>Inutilizar números</button>}
      onOpen={() => { setJustification(''); setError(null) }}
      typed={{ word: 'INUTILIZAR', label: 'Para confirmar, digite INUTILIZAR' }}
      preconditions={[{ ok: length >= 15 && length <= 255, text: 'Escreva a justificativa com 15 a 255 caracteres.' }]}
      impactList={{ label: 'Números da faixa', items: [...numbers.map((n) => ({ key: String(n), primary: `Série ${r.series}, número ${n}` })), { key: 'irreversivel', primary: EVENT_TEXTS.irreversible }] }}
      onFieldErrors={(e) => { const m = e.fields.justification?.[0]; setError(m ?? null); return !!m }}
      onReload={async () => client.invalidateQueries({ queryKey: getListNumberGapsQueryKey() })}
      onConfirm={async () => {
        const result = await invalidateNumberGaps({ series: r.series, firstNumber: r.firstNumber, lastNumber: r.lastNumber, justification })
        notifySuccess(result.status === 'Registered' ? EVENT_TEXTS.invalidated : result.message)
        await client.invalidateQueries({ queryKey: getListNumberGapsQueryKey() })
      }}>
      <div className="space-y-1 text-sm">
        <label htmlFor={id} className="font-semibold">Justificativa (vai para a SEFAZ)</label>
        <textarea id={id} rows={3} value={justification} onChange={(e) => setJustification(e.target.value)} aria-invalid={!!error} className="min-h-11 w-full rounded-md border border-input bg-background px-3 py-2" />
        <p className="text-xs tabular-nums text-muted-foreground">{length} de 15 a 255 caracteres</p>
        {error ? <p role="alert" className="text-xs font-medium text-crit-ink">{error}</p> : null}
      </div>
    </ConfirmDialog>
  )
}
