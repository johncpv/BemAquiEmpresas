import { useQueryClient } from '@tanstack/react-query'
import { getGetEmployeeSensitiveQueryKey, getGetEmployeeTimelineQueryKey, useGetEmployeeSensitive, useGetPositionSalary } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { FormSection } from '@/components/form/form'
import { Money } from '@/components/ui/money'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { outsideRange } from './employee'
import { SalaryDialog } from './EmployeeDialogs'
import { EmployeeProtected } from './EmployeeProtected'

/** Contrato e salário (CA 9 e 14): salário base de /sensitive; o reajuste vai para a linha do tempo. */
export function EmployeeContract({ employee, canEdit, onChanged }: { employee: EmployeeDetail; canEdit: boolean; onChanged: (e: EmployeeDetail) => void }) {
  const client = useQueryClient()
  const canSee = useCan('hr.view_sensitive')
  const query = useGetEmployeeSensitive(employee.id, { query: { enabled: canSee, gcTime: 0, staleTime: 0 } })
  const range = useGetPositionSalary(employee.positionId, { query: { enabled: canSee } })
  if (!canSee) return <EmployeeProtected />
  if (query.isPending) return <Skeleton className="h-48 max-w-3xl" />
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar o salário." onRetry={() => query.refetch()} />

  const salary = query.data.baseSalary
  const warning = outsideRange(String(salary), range.data ?? null)
  const done = async (e: EmployeeDetail) => {
    onChanged(e)
    await client.invalidateQueries({ queryKey: getGetEmployeeSensitiveQueryKey(employee.id) })
    await client.invalidateQueries({ queryKey: getGetEmployeeTimelineQueryKey(employee.id) })
  }

  return (
    <div className="max-w-3xl space-y-4">
      <FormSection title="Contrato" description="Dado pessoal protegido pela LGPD. Cada leitura desta aba fica registrada.">
        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <div><dt className="text-muted-foreground">Cargo</dt><dd className="font-semibold">{employee.positionName}</dd></div>
          <div><dt className="text-muted-foreground">Centro de trabalho</dt><dd>{employee.workCenterName ?? '—'}</dd></div>
          <div><dt className="text-muted-foreground">Admissão</dt><dd className="tabular-nums">{formatDate(employee.admissionDate)}</dd></div>
          <div><dt className="text-muted-foreground">Desligamento</dt><dd className="tabular-nums">{employee.terminationDate ? formatDate(employee.terminationDate) : '—'}</dd></div>
          <div>
            <dt className="text-muted-foreground">Salário base</dt>
            <dd className="text-lg font-semibold"><Money value={salary} /></dd>
            {warning ? <dd role="status" className="text-xs font-medium text-warn-ink">{warning}</dd> : null}
          </div>
        </dl>
      </FormSection>
      {canEdit && employee.status === 'Active' ? <SalaryDialog employee={employee} current={salary} onDone={(e) => void done(e)} /> : null}
    </div>
  )
}
