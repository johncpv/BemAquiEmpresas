import { useQueryClient } from '@tanstack/react-query'
import { getListFinanceDeadLettersQueryKey, reprocessFinanceDeadLetter, useListFinanceDeadLetters } from '@/api/generated/bemaqui'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { useCan } from '@/pages/use-can'

/** Eventos do financeiro que falharam (COM-04b CA 5): "Reprocessar" devolve a mensagem para a fila. Só com finance.edit. */
export function DeadLetterBanner() {
  const canEdit = useCan('finance.edit')
  const client = useQueryClient()
  const list = useListFinanceDeadLetters({ query: { enabled: canEdit } })
  if (!canEdit || !list.data?.length) return null
  return (
    <section aria-label="Eventos com falha" className="mb-3 space-y-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
      {list.data.map((d) => (
        <p key={d.id} className="flex flex-wrap items-center justify-between gap-2">
          <span>Não foi possível processar "{d.event}"{d.contractNumber ? ` do contrato ${d.contractNumber}` : ''}.</span>
          <ConfirmDialog verb="Reprocessar" done="Reenviado para processamento" title="Reprocessar o evento?" description="O sistema tenta de novo. Se o problema continuar, o aviso volta."
            onConfirm={async () => { await reprocessFinanceDeadLetter(d.id); await client.invalidateQueries({ queryKey: getListFinanceDeadLettersQueryKey() }) }}
            trigger={<button type="button" className="rounded-md border border-border bg-card px-2.5 py-1 font-semibold text-foreground">Reprocessar</button>} />
        </p>
      ))}
    </section>
  )
}
