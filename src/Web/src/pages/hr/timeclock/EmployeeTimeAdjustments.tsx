import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import {
  attachTimeAdjustmentDocument, createEmployeeTimeAdjustment, createEmployeeTimeAdjustmentDay, useListAbsenceReasons, useListEmployeeTimeAdjustments,
} from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { useCan } from '@/pages/use-can'
import { AdjustmentDialog, type AdjustmentMode, LaunchDayDialog } from './AdjustmentDialogs'
import { AdjustmentStatus } from './AdjustmentPanel'
import { shortDay } from './time-adjustments'

const button = 'min-h-11 rounded-md border border-border px-3 text-sm font-semibold'

/**
 * Aba Ponto do funcionário, parte dos ajustes (RH-01b CA 2, 3, 7 e 13): "Lançar marcação", "Lançar o dia pela jornada" e "Abonar ausência"
 * com hr.create (o lançamento também passa por aprovação), e os pedidos do funcionário com o chip de situação e a marca "autoaprovado".
 */
export function EmployeeTimeAdjustments({ employee }: { employee: EmployeeDetail }) {
  const client = useQueryClient()
  const canCreate = useCan('hr.create')
  const canHealth = useCan('hr.view_health')
  const [mode, setMode] = useState<AdjustmentMode | null>(null)
  const [day, setDay] = useState(false)
  const idempotency = useIdempotencyKey()
  const reasons = useListAbsenceReasons({ query: { enabled: canCreate } })
  const query = useListEmployeeTimeAdjustments(employee.id, { query: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2 } })
  const refresh = () => client.invalidateQueries()

  let list
  if (query.isPending) list = <Skeleton className="h-16" />
  else if (query.isError) list = <ErrorState title="Não foi possível carregar os pedidos de ajuste." onRetry={() => query.refetch()} />
  else if (query.data.items.length === 0) list = <p className="text-sm text-muted-foreground">Nenhum pedido de ajuste. Esqueceram uma marcação? Use "Lançar marcação".</p>
  else {
    list = (
      <ul aria-label="Pedidos de ajuste do funcionário" className="divide-y divide-border rounded-md border border-border bg-card">
        {query.data.items.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
            <span><span className="tabular-nums">{shortDay(a.day)}</span> · {a.summary}</span>
            <span className="flex flex-wrap items-center gap-2">
              <AdjustmentStatus adjustment={a} />
              <Link to="/app/rh/ponto/ajustes" search={{ situacao: 'todos', funcionario: employee.id, pedido: a.id } as never} className="inline-flex min-h-11 items-center font-semibold text-primary">Ver pedido</Link>
            </span>
          </li>
        ))}
      </ul>
    )
  }

  return (
    <section aria-labelledby="ajustes-funcionario" className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="ajustes-funcionario" className="text-base font-bold">Ajustes do ponto</h3>
        {canCreate ? (
          <div className="flex flex-wrap gap-2">
            <button type="button" className={button} onClick={() => setMode('add')}>Lançar marcação</button>
            <button type="button" className={button} onClick={() => setDay(true)}>Lançar o dia pela jornada</button>
            <button type="button" className={button} onClick={() => setMode('absence')}>Abonar ausência</button>
          </div>
        ) : null}
      </div>
      {list}
      {canCreate ? (
        <>
          <AdjustmentDialog mode={mode ?? 'add'} open={mode !== null} onOpenChange={(o) => { if (!o) setMode(null) }}
            verb={mode === 'absence' ? 'Abonar ausência' : 'Lançar marcação'} done="Marcação enviada para aprovação"
            title={mode === 'absence' ? `Abonar ausência de ${employee.name}` : `Lançar marcação de ${employee.name}`}
            description="O lançamento do RH também passa por aprovação: nada muda no ponto até quem aprova decidir."
            reasons={reasons.data} canHealth={canHealth} allowDocument={canHealth}
            submit={async (body, file) => {
              const saved = await createEmployeeTimeAdjustment(employee.id, body, idempotency.options)
              idempotency.renew()
              if (file && canHealth) await attachTimeAdjustmentDocument(employee.id, saved.id, { file })
              await refresh()
            }} />
          <LaunchDayDialog employeeId={employee.id} open={day} onOpenChange={setDay}
            submit={async (d, entries, justification) => {
              await createEmployeeTimeAdjustmentDay(employee.id, { day: d, entries, justification }, idempotency.options)
              idempotency.renew()
              await refresh()
            }} />
        </>
      ) : null}
    </section>
  )
}
