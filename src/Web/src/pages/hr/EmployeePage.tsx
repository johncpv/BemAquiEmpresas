import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deactivateUser, deleteEmployee, getGetEmployeeQueryKey, getGetEmployeeTimelineQueryKey, getGetEmployeeUserQueryKey, useGetEmployee, useGetEmployeeUser } from '@/api/generated/bemaqui'
import type { EmployeeDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { notifySuccess } from '@/components/ui/messages'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { STATUS_LABELS } from './employee'
import { EmployeeContract } from './EmployeeContract'
import { PositionDialog, TerminationDialog } from './EmployeeDialogs'
import { EmployeeGeneral } from './EmployeeGeneral'
import { EmployeePersonal } from './EmployeePersonal'
import { EmployeeProtected } from './EmployeeProtected'
import { EmployeeTimeline } from './EmployeeTimeline'

const button = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Funcionário (CAD-04b, molde "detalhe com abas"): Dados gerais, Dados pessoais, Contrato e salário, Linha do tempo e Histórico. */
export function EmployeePage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetEmployee(id)
  const linked = useGetEmployeeUser(id)
  const canEdit = useCan('hr.edit')
  const canSensitive = useCan('hr.view_sensitive')
  const canUsers = useCan('identity.edit')
  const [steps, setSteps] = useState<'created' | 'terminated' | null>(search.novo === '1' ? 'created' : null)
  const [, setTab] = useUrlTab('geral')
  const [linking, setLinking] = useState(false)
  const [terminating, setTerminating] = useState(false)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-96" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Funcionário não encontrado.' : 'Não foi possível carregar o funcionário.'} message={notFound ? 'Ele pode ter sido excluído. Volte à lista de funcionários.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const employee = query.data
  const active = employee.status === 'Active'
  const user = linked.data?.user ?? null
  const setEmployee = (e: EmployeeDetail) => {
    client.setQueryData(getGetEmployeeQueryKey(id), e)
    void client.invalidateQueries({ queryKey: getGetEmployeeTimelineQueryKey(id) })
  }

  const createdSteps = [
    ...(canEdit && canSensitive ? [{ label: 'Completar dados pessoais', onSelect: () => setTab('pessoais') }] : []),
    ...(canEdit && canUsers && !user ? [{ label: 'Vincular usuário', onSelect: () => { setTab('geral'); setLinking(true) } }] : []),
  ]
  const terminatedSteps = canUsers && user ? [{
    label: `Desativar o usuário ${user.name}`,
    onSelect: async () => { await deactivateUser(user.userId); notifySuccess('Usuário desativado'); setSteps(null); void client.invalidateQueries({ queryKey: getGetEmployeeUserQueryKey(id) }) },
  }] : []
  const shown = steps === 'created' ? createdSteps : steps === 'terminated' ? terminatedSteps : []

  return (
    <>
      <PageHeading title={employee.name}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <StatusChip tone={active ? 'ok' : 'neutral'}>{STATUS_LABELS[employee.status]}</StatusChip>
            <Tag>{employee.positionName}</Tag>
            {employee.registration ? <span className="text-sm tabular-nums text-muted-foreground">Matrícula {employee.registration}</span> : null}
            <Link to="/app/rh/funcionarios" className="ml-2 text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          <div className="flex flex-wrap gap-2">
            {canEdit && active ? (
              <>
                <PositionDialog employee={employee} onDone={setEmployee} />
                <button type="button" onClick={() => setTerminating(true)} className={button}>Registrar desligamento</button>
              </>
            ) : null}
            <Can perm="hr.delete">
              <ConfirmDialog verb="Excluir" done="Funcionário excluído" destructive title={`Excluir ${employee.name}?`} description="Só dá para excluir funcionário sem linha do tempo, apontamento nem usuário vinculado. Os demais têm o desligamento registrado."
                onConfirm={async () => { await deleteEmployee(id); await navigate({ to: '/app/rh/funcionarios' }) }}
                recovery={active && canEdit ? { verb: 'Registrar desligamento', when: (code) => code === 'employee_has_history', onSelect: async () => setTerminating(true) } : undefined}
                trigger={<button type="button" className={button + ' text-crit-ink'}>Excluir</button>} />
            </Can>
          </div>
        </div>
      </PageHeading>
      <TerminationDialog employee={employee} open={terminating} onOpenChange={setTerminating} onDone={(e) => { setEmployee(e); setSteps('terminated') }} />
      {shown.length > 0 ? <div className="mb-4"><NextSteps onDismiss={() => setSteps(null)} steps={shown} /></div> : null}
      <UrlTabs label="Seções do funcionário" fallback="geral" tabs={[
        { id: 'geral', label: 'Dados gerais', keepMounted: true, content: <EmployeeGeneral key={employee.id + employee.positionId} employee={employee} canEdit={canEdit} onSaved={setEmployee} linking={linking} onLinkingChange={setLinking} /> },
        { id: 'pessoais', label: 'Dados pessoais', content: <EmployeePersonal employee={employee} canEdit={canEdit} /> },
        { id: 'contrato', label: 'Contrato e salário', content: <EmployeeContract employee={employee} canEdit={canEdit} onChanged={setEmployee} /> },
        { id: 'linha-do-tempo', label: 'Linha do tempo', content: <EmployeeTimeline id={employee.id} /> },
        { id: 'historico', label: 'Histórico de alterações', content: canSensitive ? <RecordHistory entity="Employee" id={employee.id} /> : <EmployeeProtected /> },
      ]} />
    </>
  )
}
