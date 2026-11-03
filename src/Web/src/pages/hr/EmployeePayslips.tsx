import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { getDownloadPayslipPdfUrl, useListEmployeePayslips } from '@/api/generated/bemaqui'
import type { EmployeeDetail, EmployeePayslip } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormSection } from '@/components/form/form'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { periodLabel, STATUS_LABELS, STATUS_TONES, typeLabel } from './payroll/payroll'
import { payslipFileName } from './payroll/payslip-downloads'

export const PAYSLIPS_EMPTY = 'Nenhum holerite ainda. Os holerites aparecem quando a folha do mês é lançada.'

function PayslipRow({ slip, registration }: { slip: EmployeePayslip; registration?: string | null }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const download = async () => {
    setBusy(true)
    setError(null)
    try {
      await apiDownload(getDownloadPayslipPdfUrl(slip.payrollRunId, slip.id), payslipFileName(slip.period, { payslipId: slip.id, registration }))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível baixar o holerite. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 py-2">
      <div className="min-w-0">
        <Link to="/app/rh/folha/$id" params={{ id: slip.payrollRunId }} search={{ aba: 'holerites' } as never} className="font-semibold text-primary hover:underline">
          {periodLabel(slip.period)} · {typeLabel(slip.type)}
        </Link>
        <p className="text-sm text-muted-foreground">Líquido <Money value={slip.net} className="tabular-nums" /></p>
        {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
      </div>
      <div className="flex items-center gap-2">
        <StatusChip tone={STATUS_TONES[slip.runStatus] ?? 'neutral'}>{STATUS_LABELS[slip.runStatus] ?? slip.runStatus}</StatusChip>
        <button type="button" onClick={() => void download()} disabled={busy} aria-busy={busy} className="min-h-11 font-semibold text-primary sm:min-h-0">
          {busy ? 'Gerando PDF…' : 'Baixar holerite'}
        </button>
      </div>
    </li>
  )
}

/**
 * Bloco "Holerites" da ficha (EXP-04 CA 18): os 6 últimos, de qualquer situação da folha, com "Baixar holerite" (o PDF da folha; rascunho
 * sai com a marca d'água). Sem <c>hr.view_sensitive</c>, o bloco não aparece e a chamada não é feita.
 */
export function EmployeePayslips({ employee }: { employee: EmployeeDetail }) {
  const canSensitive = useCan('hr.view_sensitive')
  const query = useListEmployeePayslips(employee.id, { take: 6 }, { query: { enabled: canSensitive } })
  if (!canSensitive) return null

  return (
    <FormSection title="Holerites" description="Os 6 últimos, de todas as folhas do funcionário." grid="none">
      <div>
        {query.isPending ? <Skeleton className="h-24" /> : query.isError ? (
          <ErrorState message="Não foi possível carregar os holerites." onRetry={() => void query.refetch()} />
        ) : query.data.length === 0 ? (
          <EmptyState title={PAYSLIPS_EMPTY} />
        ) : (
          <>
            <ul className="divide-y divide-border">{query.data.map((s) => <PayslipRow key={s.id} slip={s} registration={employee.registration} />)}</ul>
            <Link to="/app/rh/folha" className="mt-2 inline-block text-sm font-semibold text-primary hover:underline">Ver todos</Link>
          </>
        )}
      </div>
    </FormSection>
  )
}
