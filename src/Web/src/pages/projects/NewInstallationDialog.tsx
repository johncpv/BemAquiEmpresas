import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createInstallation, listInstallers, useGetInstallationDefaults } from '@/api/generated/bemaqui'
import type { ProjectDetail } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { Combobox, type ComboOption } from '@/components/form/combobox'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'
import { today, toNumber } from '../finance/receivables/receivables'
import { INSTALLATION_DICAS } from '../production/installations/installations'

const searchInstallers = async (q: string): Promise<ComboOption[]> => (await listInstallers({ q: q || undefined })).map((i) => ({ id: i.id, label: i.name }))

/** "Nova OS de instalação" (COM-06 CA 2, 5): itens livres marcados, valor pelo orçado de instalação e o último instalador do cliente. */
export function NewInstallationDialog({ project }: { project: ProjectDetail }) {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const defaults = useGetInstallationDefaults({ projectId: project.id }, { query: { enabled: open } })
  const [items, setItems] = useState<string[] | null>(null)
  const [installer, setInstaller] = useState<ComboOption | null>(null)
  const [value, setValue] = useState<string | null>(null)
  const [start, setStart] = useState(today())
  const [end, setEnd] = useState(today())
  const idempotency = useIdempotencyKey()
  const d = defaults.data
  const free = (d?.items ?? []).filter((i) => !i.coveredBy).map((i) => i.id)
  const chosen = items ?? free
  const who = installer ?? (d?.suggestedInstallerId ? { id: d.suggestedInstallerId, label: d.suggestedInstallerName ?? '' } : null)
  const amount = value ?? (d?.suggestedValue ? String(d.suggestedValue) : null)
  return (
    <ConfirmDialog size="lg" open={open} onOpenChange={setOpen} verb="Criar OS de instalação" done="OS de instalação criada" title="Nova OS de instalação"
      description="O valor combinado vira um título a pagar previsto ao instalador, que fica certo no aceite técnico."
      onOpen={() => { setItems(null); setInstaller(null); setValue(null); setStart(today()); setEnd(today()) }}
      preconditions={[
        { ok: chosen.length > 0, text: 'Escolha ao menos um item do projeto.' },
        { ok: !!who, text: 'Escolha o instalador.' },
        { ok: toNumber(amount) > 0, text: 'Informe o valor combinado.' },
        { ok: !!start && !!end && end >= start, text: 'O fim previsto não pode ser antes do início.' },
      ]}
      onConfirm={async () => {
        const created = await createInstallation({ projectId: project.id, projectItemIds: chosen, installerId: who!.id, contractValue: toNumber(amount), plannedStart: start, plannedEnd: end }, idempotency.options)
        idempotency.renew()
        await navigate({ to: '/app/producao/instalacoes/$id', params: { id: created.id } } as never)
      }}
      trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova OS de instalação</button>}>
      <fieldset className="mt-3 space-y-1 text-sm">
        <legend className="font-medium">Itens</legend>
        {(d?.items ?? []).map((i) => (
          <label key={i.id} className="flex items-center gap-2">
            <input type="checkbox" disabled={!!i.coveredBy} checked={chosen.includes(i.id)} onChange={(e) => setItems(e.target.checked ? [...chosen, i.id] : chosen.filter((x) => x !== i.id))} />
            {i.sequence}. {i.description}{i.coveredBy ? <span className="text-xs text-muted-foreground"> (na {i.coveredBy})</span> : null}
          </label>
        ))}
      </fieldset>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium">Instalador
          <Combobox aria-label="Instalador" queryKey={['installers']} search={searchInstallers} value={who?.id ?? null} selected={who} placeholder="Busque pelo nome" onChange={setInstaller} />
        </label>
        <label className="space-y-1 text-sm font-medium"><span className="inline-flex items-center">Valor combinado<Dica nome="Valor combinado" texto={INSTALLATION_DICAS.valor} /></span>
          <MoneyInput value={amount} onChange={setValue} />
        </label>
        <label className="space-y-1 text-sm font-medium">Início previsto<DateInput value={start} onChange={setStart} /></label>
        <label className="space-y-1 text-sm font-medium">Fim previsto<DateInput value={end} onChange={setEnd} /></label>
      </div>
    </ConfirmDialog>
  )
}
