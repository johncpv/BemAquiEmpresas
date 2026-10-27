import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetPayrollRunQueryKey, getGetPayrollReviewSensitiveQueryKey, getListPayslipsSensitiveQueryKey, useListPayslipsSensitive } from '@/api/generated/bemaqui'
import type { PayrollRunSummary } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { ImportDialog } from './ImportDialog'
import { PayslipPanel } from './PayslipPanel'

/** Aba Holerites (CA 5, 6 e 8): um por funcionário, com os totais; abrir edita no painel; "Importar lançamentos" pelo CSV v1. */
export function PayslipsTab({ run, editable, importing, onImportingChange, onReviewed }: { run: PayrollRunSummary; editable: boolean; importing: boolean; onImportingChange: (open: boolean) => void; onReviewed: () => void }) {
  const client = useQueryClient()
  const query = useListPayslipsSensitive(run.id)
  const [open, setOpen] = useState<string | null>(null)
  const [imported, setImported] = useState(false)
  const refresh = () => {
    void client.invalidateQueries({ queryKey: getListPayslipsSensitiveQueryKey(run.id) })
    void client.invalidateQueries({ queryKey: getGetPayrollReviewSensitiveQueryKey(run.id) })
    void client.invalidateQueries({ queryKey: getGetPayrollRunQueryKey(run.id) })
  }

  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os holerites." onRetry={() => query.refetch()} />
  const rows = query.data
  const total = rows.reduce((s, r) => s + r.net, 0)
  return (
    <div className="space-y-3">
      {imported ? <NextSteps onDismiss={() => setImported(false)} steps={[{ label: 'Conferir os totais', onSelect: () => { setImported(false); onReviewed() } }]} /> : null}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">Líquido da folha <Money value={total} className="font-semibold text-foreground" /></p>
        {editable ? <button type="button" onClick={() => onImportingChange(true)} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Importar lançamentos</button> : null}
      </div>
      {rows.length === 0 ? (
        <EmptyState title="Nenhum holerite nesta folha." description="A folha não tem funcionário ativo na competência. Confira as admissões no cadastro." />
      ) : (
        <ul aria-label="Holerites" className="divide-y divide-border rounded-md border border-border">
          {rows.map((r) => (
            <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <button type="button" onClick={() => setOpen(r.id)} className="min-h-11 text-left font-semibold text-primary hover:underline sm:min-h-0">
                {r.employeeName}{r.registration ? <span className="font-normal text-muted-foreground"> · {r.registration}</span> : null}
              </button>
              <span className="flex flex-wrap items-center gap-3 tabular-nums text-muted-foreground">
                {r.lineCount === 0 ? <span>Sem lançamento</span> : <span>{r.lineCount === 1 ? '1 linha' : `${r.lineCount} linhas`}</span>}
                <span>Líquido <Money value={r.net} className="font-semibold text-foreground" /></span>
              </span>
            </li>
          ))}
        </ul>
      )}
      {open ? <PayslipPanel runId={run.id} payslipId={open} editable={editable} onClose={() => setOpen(null)} onSaved={refresh} /> : null}
      {importing ? <ImportDialog run={run} onClose={() => onImportingChange(false)} onImported={() => { refresh(); setImported(true) }} /> : null}
    </div>
  )
}
