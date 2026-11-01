import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { useListWorkCenterDowntimes } from '@/api/generated/bemaqui'
import { createWorkCenter, deleteWorkCenter, inactivateWorkCenter, reactivateWorkCenter, updateWorkCenter } from '@/api/generated/bemaqui'
import type { Shift, WorkCenterDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { TimeInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { formatHours, hoursPerDay, shiftsError } from './shifts'

type Draft = { name: string; line: string; shifts: Shift[] }
const toDraft = (c: WorkCenterDetail | null, suggested: Shift[]): Draft => ({
  name: c?.name ?? '', line: c?.line ?? '', shifts: c ? c.shifts : suggested.length ? suggested : [{ start: '', end: '' }],
})

/**
 * Painel do centro (CAD-03 CA 12 a 14): turnos no RowsEditor com o total ao vivo; as horas por dia saem dos turnos. Centro
 * novo vem com os turnos do último cadastrado (propositivo).
 */
export function WorkCenterPanel({ center, suggested, onClose, onSaved }: { center: WorkCenterDetail | null; suggested: Shift[]; onClose: () => void; onSaved: (created: boolean) => void }) {
  const [initial] = useState(() => toDraft(center, suggested))
  const [draft, setDraft] = useState<Draft>(initial)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const [touched, setTouched] = useState(false)
  const canWrite = useCan(center ? 'production.edit' : 'production.create')
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const localShiftError = touched ? shiftsError(draft.shifts) : undefined
  const field = (name: string) => error?.fields[name]?.[0]

  const save = async () => {
    setTouched(true)
    if (shiftsError(draft.shifts)) return false
    const input = { name: draft.name.trim(), line: draft.line.trim() || null, shifts: draft.shifts }
    try {
      if (center) await updateWorkCenter(center.id, input)
      else await createWorkCenter(input)
      notifySuccess('Centro de trabalho salvo')
      onSaved(!center)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o centro.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={center ? `Centro ${center.name}` : 'Novo centro de trabalho'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          {center ? <WorkCenterActions center={center} onDone={() => onSaved(false)} /> : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar centro de trabalho</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Nome" required error={field('name')} hint="Ex.: Corte, Dobra, Montagem.">
          {(p) => <input {...p} value={draft.name} maxLength={100} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Linha" error={field('line')} dica="Agrupa os centros de uma mesma linha de produção, como Cabines ou Portas. É opcional.">
          {(p) => <input {...p} value={draft.line} maxLength={60} onChange={(e) => setDraft({ ...draft, line: e.target.value })} className={inputClass} />}
        </Field>
        <div className="space-y-1.5">
          <p className="text-sm font-medium">Turnos <span className="text-xs font-normal text-muted-foreground">(obrigatório)</span></p>
          <RowsEditor noun="turno" rows={draft.shifts} min={1} max={6} disabled={!canWrite} headers={['Início', 'Fim']} newRow={() => ({ start: '', end: '' })}
            onChange={(shifts) => { setTouched(true); setDraft({ ...draft, shifts }) }}
            renderRow={(row, i, update) => [
              <TimeInput key="s" aria-label={`Início do turno ${i + 1}`} value={row.start} onChange={(start) => update({ start })} />,
              <TimeInput key="e" aria-label={`Fim do turno ${i + 1}`} value={row.end} onChange={(end) => update({ end })} />,
            ]}
            summary={<>Total: <b>{formatHours(hoursPerDay(draft.shifts))} h</b> por dia</>} />
          {localShiftError ?? field('shifts') ? <p role="alert" className="text-sm text-crit-ink">{localShiftError ?? field('shifts')}</p> : null}
        </div>
      </fieldset>
      {center ? <CenterDowntimes centerId={center.id} /> : null}
      {center ? (
        <details className="mt-5 rounded-md border border-border bg-card px-3 py-2 text-sm">
          <summary className="cursor-pointer font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="WorkCenter" id={center.id} /></div>
        </details>
      ) : null}
    </SidePanel>
  )
}

/** Inativar, reativar e excluir (CA 14). Excluir recusado por uso oferece "Inativar" no próprio diálogo. */
function WorkCenterActions({ center, onDone }: { center: WorkCenterDetail; onDone: () => void }) {
  return (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="production.edit">
        {center.isActive ? (
          <ConfirmDialog verb="Inativar" done="Inativado" title={`Inativar o centro ${center.name}?`} description="Ele some dos seletores e a capacidade dele passa a 0 no planejamento."
            onConfirm={async () => { await inactivateWorkCenter(center.id); onDone() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar centro</button>} />
        ) : (
          <button type="button" onClick={async () => { await reactivateWorkCenter(center.id); notifySuccess('Reativado'); onDone() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
        )}
      </Can>
      <Can perm="production.delete">
        <ConfirmDialog verb="Excluir" done="Excluído" destructive title={`Excluir o centro ${center.name}?`} description="Só dá para excluir centro sem cargo, roteiro ou ordem de serviço."
          onConfirm={async () => { await deleteWorkCenter(center.id); onDone() }}
          recovery={{ verb: 'Inativar', done: 'Inativado', when: (code) => code === 'work_center_in_use', onSelect: async () => { await inactivateWorkCenter(center.id); onDone() } }}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir centro</button>} />
      </Can>
    </span>
  )
}

/**
 * "Paradas por manutenção" do centro (PAT-03b CA 10): as OS de manutenção das máquinas do centro, com o período e as horas paradas por dia
 * que saem da capacidade (de 30 dias atrás a 180 à frente). Só leitura: a parada muda pela OS de manutenção.
 */
function CenterDowntimes({ centerId }: { centerId: string }) {
  const query = useListWorkCenterDowntimes(centerId)
  const date = (v: string) => { const [y, m, d] = v.split('-').map(Number); return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, d))) }
  return (
    <section aria-label="Paradas por manutenção" className="mt-5 space-y-2 text-sm">
      <h3 className="font-semibold">Paradas por manutenção</h3>
      {query.isPending ? <p className="text-muted-foreground">Carregando as paradas…</p>
        : query.isError || !query.data ? <p role="alert" className="text-crit-ink">Não foi possível carregar as paradas. <button type="button" onClick={() => void query.refetch()} className="min-h-11 font-semibold underline">Tentar de novo</button></p>
        : query.data.length === 0 ? <p className="text-muted-foreground">Nenhuma parada por manutenção no período. As OS de manutenção das máquinas deste centro aparecem aqui.</p> : (
          <ul className="divide-y divide-border rounded-md border border-border bg-card">
            {query.data.map((d) => (
              <li key={d.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                <span>
                  <Link to={'/app/patrimonio/manutencao/$id' as never} params={{ id: d.orderId } as never} className="font-semibold text-primary hover:underline">{d.orderNumber ?? 'OS de manutenção'}</Link>
                  <span className="block text-xs text-muted-foreground">{d.assetTag ? `${d.assetTag} · ${d.assetName}` : ''}</span>
                </span>
                <span className="text-right tabular-nums">
                  {d.hoursPerDay.toLocaleString('pt-BR', { maximumFractionDigits: 2 })} h por dia
                  <span className="block text-xs text-muted-foreground">{date(d.startsOn)}{d.endsOn !== d.startsOn ? ` a ${date(d.endsOn)}` : ''}{d.ongoing ? ' · em andamento' : d.ended ? ' · encerrada' : ''}</span>
                </span>
              </li>
            ))}
          </ul>
        )}
    </section>
  )
}
