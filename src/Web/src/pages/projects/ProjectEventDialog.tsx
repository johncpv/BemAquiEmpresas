import { useState } from 'react'
import { registerProjectDelivery, registerProjectFabrication, useGetContract } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { today } from '../finance/receivables/receivables'

type Kind = 'fabrication' | 'delivery'

const TRIGGERS: Record<Kind, string[]> = { fabrication: ['FabricationDone'], delivery: ['Delivered', 'TechnicalAcceptance'] }

const addDays = (iso: string, days: number) => { const d = new Date(iso + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0, 10) }

/**
 * "Registrar fabricação concluída" e "Registrar entrega" (COM-04b CA 9 e 10): data até hoje; o diálogo mostra as parcelas
 * que serão liberadas, com valor e vencimento para quem tem finance.view.
 */
export function ProjectEventDialog({ project, kind, onDone }: { project: ProjectDetail; kind: Kind; onDone: () => Promise<void> }) {
  const [date, setDate] = useState(today())
  const canFinance = useCan('finance.view')
  const contract = useGetContract(project.contractId)
  const milestones = (contract.data?.milestones ?? []).filter((m) => m.trigger === 'Event' && TRIGGERS[kind].includes(m.triggerEvent ?? '') && (!m.receivable || m.receivable.status === 'Planned'))
  const verb = kind === 'fabrication' ? 'Registrar fabricação concluída' : 'Registrar entrega'
  const impact = milestones.length === 0
    ? <p className="text-sm text-muted-foreground">Nenhuma parcela depende deste marco.</p>
    : canFinance
      ? <ul className="list-disc space-y-1 pl-5 text-sm">{milestones.map((m) => <li key={m.id}>Libera a parcela {m.description} de <Money value={m.amount} />, vencendo em {date ? formatDate(addDays(date, m.daysAfterEvent ?? 0)) : '—'}.</li>)}</ul>
      : <ul className="list-disc space-y-1 pl-5 text-sm">{milestones.map((m) => <li key={m.id}>Libera a parcela do marco {m.description}.</li>)}</ul>
  return (
    <ConfirmDialog size="lg" verb={verb} done={kind === 'fabrication' ? 'Fabricação registrada' : 'Entrega registrada'}
      title={kind === 'fabrication' ? 'Registrar a fabricação concluída?' : 'Registrar a entrega ao cliente?'}
      description={kind === 'fabrication' ? 'O projeto passa a fabricado.' : 'O projeto passa a entregue. Use quando não há instalação.'}
      onOpen={() => setDate(today())}
      preconditions={[{ ok: !!date && date <= today(), text: 'A data não pode ser no futuro.' }]}
      impact={impact}
      onConfirm={async () => {
        const input = { date, version: project.version }
        await (kind === 'fabrication' ? registerProjectFabrication(project.id, input) : registerProjectDelivery(project.id, input))
        await onDone()
      }}
      trigger={<button type="button" className={kind === 'delivery' ? 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{verb}</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">
        <span className="inline-flex items-center">{kind === 'fabrication' ? 'Data da fabricação' : 'Data da entrega'}{kind === 'delivery' ? <Dica nome="Registrar entrega" texto="Use quando não há instalação; com OS de instalação, a entrega vem do aceite técnico." /> : null}</span>
        <DateInput value={date} onChange={setDate} />
      </label>
    </ConfirmDialog>
  )
}
