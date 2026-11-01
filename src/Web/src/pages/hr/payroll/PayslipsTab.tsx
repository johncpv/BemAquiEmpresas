import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import {
  getGetPayrollRunQueryKey, getGetPayrollReviewSensitiveQueryKey, getGetPayslipNoticesSensitiveQueryKey, getListPayslipsSensitiveQueryKey, useGetPayslipNoticesSensitive, useListPayslipsSensitive,
} from '@/api/generated/bemaqui'
import type { PayrollRunSummary } from '@/api/generated/model'
import { Money } from '@/components/ui/money'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { ImportDialog } from './ImportDialog'
import { PayslipPanel } from './PayslipPanel'
import { BatchButton, BatchReady, NoticeCell, NotifyDialog } from './PayslipNotices'
import { downloadPayslip } from './payslip-downloads'

/**
 * Aba Holerites (RH-02a CA 5, 6 e 8; RH-02b CA 1, 2, 7 e 9): um por funcionário, com os totais; abrir edita no painel; "Importar
 * lançamentos" pelo CSV v1; "Baixar holerite" e "Baixar todos"; com a folha fechada, "Avisar funcionários" e a situação do aviso.
 */
export function PayslipsTab({ run, editable, canNotify, importing, onImportingChange, onReviewed }: {
  run: PayrollRunSummary; editable: boolean; canNotify: boolean; importing: boolean; onImportingChange: (open: boolean) => void; onReviewed: () => void
}) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const query = useListPayslipsSensitive(run.id)
  const closed = run.status !== 'Draft'
  const notices = useGetPayslipNoticesSensitive(run.id, { query: { enabled: closed } })
  const [open, setOpen] = useState<string | null>(null)
  const [imported, setImported] = useState(false)
  const lote = typeof search.lote === 'string' ? search.lote : null
  const refresh = () => {
    void client.invalidateQueries({ queryKey: getListPayslipsSensitiveQueryKey(run.id) })
    void client.invalidateQueries({ queryKey: getGetPayrollReviewSensitiveQueryKey(run.id) })
    void client.invalidateQueries({ queryKey: getGetPayrollRunQueryKey(run.id) })
  }
  const refreshNotices = async () => { await client.invalidateQueries({ queryKey: getGetPayslipNoticesSensitiveQueryKey(run.id) }) }

  if (query.isPending) return <Skeleton className="h-48" />
  if (query.isError) return <ErrorState title="Não foi possível carregar os holerites." onRetry={() => query.refetch()} />
  const rows = query.data
  const total = rows.reduce((s, r) => s + r.net, 0)
  const noticeOf = (id: string) => notices.data?.rows.find((n) => n.payslipId === id)
  return (
    <div className="space-y-3">
      {lote ? <BatchReady run={run} attachmentId={lote} onDismiss={() => void navigate({ to: '.', search: { ...search, lote: undefined } as never, replace: true })} /> : null}
      {imported ? <NextSteps onDismiss={() => setImported(false)} steps={[{ label: 'Conferir os totais', onSelect: () => { setImported(false); onReviewed() } }]} /> : null}
      <div className="flex flex-wrap items-start justify-between gap-2">
        <p className="text-sm text-muted-foreground">Líquido da folha <Money value={total} className="font-semibold text-foreground" /></p>
        <div className="flex flex-wrap gap-2">
          {editable && run.mode !== 'Calculated' ? <button type="button" onClick={() => onImportingChange(true)} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Importar lançamentos</button> : null}
          {rows.length > 0 ? <BatchButton run={run} /> : null}
          {closed && canNotify && rows.length > 0 ? (
            <NotifyDialog run={run} notices={notices.data} onDone={refreshNotices}
              trigger={<button type="button" className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Avisar funcionários</button>} />
          ) : null}
        </div>
      </div>
      {!closed && rows.length > 0 ? <p className="text-sm text-muted-foreground">{run.isSimulation ? 'Na simulação, o holerite sai só como prévia, com a marca "Simulação: não é o holerite oficial", e nunca vai para o funcionário.' : "Com a folha em lançamento, o holerite sai com a marca d'água “RASCUNHO — folha não fechada”. Avise os funcionários depois de fechar a folha."}</p> : null}
      {closed && notices.isError ? <ErrorState title="Não foi possível carregar a situação dos avisos." onRetry={() => notices.refetch()} /> : null}
      {rows.length === 0 ? (
        <EmptyState title="Nenhum holerite nesta folha." description="A folha não tem funcionário ativo na competência. Confira as admissões no cadastro." />
      ) : (
        <ul aria-label="Holerites" className="divide-y divide-border rounded-md border border-border">
          {rows.map((r) => {
            const notice = closed ? noticeOf(r.id) : undefined
            return (
              <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
                <button type="button" onClick={() => setOpen(r.id)} className="min-h-11 text-left font-semibold text-primary hover:underline sm:min-h-0">
                  {r.employeeName}{r.registration ? <span className="font-normal text-muted-foreground"> · {r.registration}</span> : null}
                </button>
                <span className="flex flex-wrap items-center gap-3 tabular-nums text-muted-foreground">
                  {r.lineCount === 0 ? <span>{run.mode === 'Calculated' ? 'Sem cálculo' : 'Sem lançamento'}</span> : <span>{r.lineCount === 1 ? '1 linha' : `${r.lineCount} linhas`}</span>}
                  <span>Líquido <Money value={r.net} className="font-semibold text-foreground" /></span>
                  {notice ? <NoticeCell run={run} row={notice} canNotify={canNotify} onChanged={refreshNotices} /> : null}
                  <button type="button" onClick={() => void downloadPayslip(run, { payslipId: r.id, registration: r.registration })} className="min-h-11 font-semibold text-primary sm:min-h-0">Baixar</button>
                </span>
              </li>
            )
          })}
        </ul>
      )}
      {open ? <PayslipPanel runId={run.id} payslipId={open} editable={editable} onClose={() => setOpen(null)} onSaved={refresh} /> : null}
      {importing ? <ImportDialog run={run} onClose={() => onImportingChange(false)} onImported={() => { refresh(); setImported(true) }} /> : null}
    </div>
  )
}
