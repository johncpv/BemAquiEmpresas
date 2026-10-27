import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { getGetShopfloorDeviceQueryKey, searchShopfloorOrder, useGetQualityQueue, useGetShopfloorQueue } from '@/api/generated/bemaqui'
import { CreatePinSheet, DeviceButton } from './DeviceSheet'
import { useFloorDevice, useFloorSession } from './floor-session'
import { useCan } from '@/pages/use-can'
import type { ShopfloorQueueStep } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorDica, FloorHeader, FloorSheet, FloorSkeleton, FloorState, NumericKeypad } from '@/components/shopfloor/kit'
import { useHelp } from '@/help/help-context'
import { refreshShopfloor, useProductionLive } from '@/pages/production/kanban/useBoardLive'
import { FAB_DICAS, filterQueue, floorDate, loadMyCenter, markStepSeen, NO_EMPLOYEE, saveMyCenter, stepSeen } from './shopfloor'
import { QueueCard } from './QueueCard'



/**
 * Fila do operador (/fab, CA 1 a 4): "Olá, {nome}", o centro do cargo e a data; a etapa em andamento em destaque; todas as etapas
 * prontas, as do meu centro primeiro; "Só meu centro" lembrado no aparelho; "Buscar OS pelo número" com teclado numérico.
 */
export function FabQueuePage() {
  const help = useHelp()
  const navigate = useNavigate()
  const queue = useGetShopfloorQueue()
  useProductionLive(refreshShopfloor)
  const [onlyMine, setOnlyMine] = useState(loadMyCenter)
  const [searching, setSearching] = useState(false)
  const current = queue.data?.current

  useEffect(() => {
    if (current && !stepSeen()) {
      markStepSeen()
      void navigate({ to: '/fab/etapa/$stepId', params: { stepId: current.stepId }, replace: true })
    }
  }, [current, navigate])

  useEffect(() => { document.title = 'App do operador · BemAqui Empresas' }, [])

  const toggle = () => {
    saveMyCenter(!onlyMine)
    setOnlyMine(!onlyMine)
  }

  const operator = queue.data?.operator
  const header = (
    <FloorHeader onHelp={() => help.setOpen(true)} title={operator ? `Olá, ${operator.firstName}` : 'App do operador'}
      subtitle={operator ? `${operator.line ?? operator.workCenterName ?? 'Sem centro definido'}, ${floorDate(operator.today)}` : undefined}>
      <DeviceButton />
    </FloorHeader>
  )

  if (queue.isPending) return <>{header}<div className="mx-auto max-w-3xl p-4"><FloorSkeleton /></div></>
  if (queue.isError) {
    const notActive = queue.error instanceof ApiError && queue.error.code === 'employee_not_active'
    return (
      <>{header}<div className="mx-auto max-w-3xl space-y-4 p-4">
        <QualityEntry />
        {notActive ? <FloorState title={NO_EMPLOYEE} message="Sem funcionário ativo ligado ao seu usuário, o apontamento fica bloqueado." />
          : <FloorState tone="error" title="Não foi possível carregar a fila." message="Verifique a conexão do tablet e tente de novo."
            action={<FloorButton onClick={() => void queue.refetch()}>Tentar de novo</FloorButton>} />}
      </div></>
    )
  }

  const steps = filterQueue(queue.data.steps, onlyMine)
  return (
    <>
      {header}
      <div className="mx-auto max-w-3xl space-y-4 p-4">
        {current ? (
          <section aria-labelledby="minha-etapa" className="space-y-2">
            <h1 id="minha-etapa" className="text-[20px] font-bold">Sua etapa em andamento</h1>
            <QueueCard step={current} highlight />
          </section>
        ) : <h1 className="sr-only">Fila de etapas</h1>}
        <CreatePinOffer />
        <QualityEntry />
        <div className="flex flex-wrap items-center gap-[8px]">
          <FloorButton tone={onlyMine ? 'primary' : 'secondary'} aria-pressed={onlyMine} onClick={toggle}>Só meu centro</FloorButton>
          <FloorDica nome="Só meu centro" texto={FAB_DICAS.meuCentro} />
          <FloorButton tone="secondary" className="ml-auto" onClick={() => setSearching(true)}><Search aria-hidden="true" className="size-6" />Buscar OS pelo número</FloorButton>
        </div>
        <section aria-labelledby="proximas" className="space-y-3">
          <h2 id="proximas" className="text-[20px] font-bold">{current ? 'Próximas na sua fila' : 'Prontas para apontar'}</h2>
          {steps.length === 0 ? (
            onlyMine && queue.data.steps.length > 0
              ? <FloorState title="Nada pronto no seu centro. Desligue o filtro para ver os outros centros." action={<FloorButton tone="secondary" onClick={toggle}>Ver todos os centros</FloorButton>} />
              : <FloorState title="Nada pronto para apontar agora. Fale com o supervisor se estiver esperando uma etapa." action={<FloorButton tone="secondary" onClick={() => void queue.refetch()}>Atualizar a fila</FloorButton>} />
          ) : (
            <ul className="space-y-[12px]">{steps.map((s) => <li key={s.stepId}><QueueCard step={s} /></li>)}</ul>
          )}
        </section>
        {/* RH-02b CA 6 (ADR 0035 §4): salário não aparece em aparelho compartilhado. */}
        <p className="text-[16px] text-muted-foreground">Veja o seu holerite no celular ou no computador, com o seu login.</p>
      </div>
      <SearchSheet open={searching} onOpenChange={setSearching} />
    </>
  )
}

/** Propositivo (PRD-03b): depois do login completo num aparelho registrado, "Criar PIN para entrar mais rápido". */
function CreatePinOffer() {
  const device = useFloorDevice()
  const { pin } = useFloorSession()
  const client = useQueryClient()
  const [open, setOpen] = useState(false)
  const data = device.data
  const offer = !pin && !!data?.registered && data.canCreatePin && !data.hasMyPin
  const sheet = <CreatePinSheet open={open} onOpenChange={setOpen} onCreated={(next) => client.setQueryData(getGetShopfloorDeviceQueryKey(), next)} />
  // A folha fica no mesmo lugar da árvore: continua aberta em "PIN criado" mesmo depois que a oferta some.
  return (
    <>
      {offer ? (
        <section aria-label="Criar PIN" className="flex flex-wrap items-center justify-between gap-[8px] rounded-md border-2 border-primary bg-card p-4">
          <p className="text-[18px] font-semibold">Criar PIN para entrar mais rápido neste tablet</p>
          <FloorButton onClick={() => setOpen(true)}>Criar PIN</FloorButton>
        </section>
      ) : null}
      {sheet}
    </>
  )
}

/** Entrada da fila de qualidade (PRD-05 CA 2) para quem tem production.approve_quality: quantas OS aguardam você. */
function QualityEntry() {
  const canDecide = useCan('production.approve_quality')
  const queue = useGetQualityQueue(undefined, { query: { enabled: canDecide, retry: false } })
  if (!canDecide) return null
  const mine = queue.data?.mineCount ?? 0
  return (
    <Link to="/fab/qualidade" data-floor-target="" className="flex min-h-[56px] items-center justify-between gap-2 rounded-md border-2 border-primary bg-card px-4 text-[18px] font-semibold">
      <span>Qualidade</span>
      <span className="tabular-nums text-muted-foreground">{queue.isPending ? 'Carregando…' : mine === 1 ? '1 OS aguardando você' : `${mine} OS aguardando você`}</span>
    </Link>
  )
}

/** "Buscar OS pelo número" (CA 3): teclado numérico grande; mostra as etapas prontas, em andamento ou pausadas da OS. */
function SearchSheet({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const [number, setNumber] = useState('')
  const [found, setFound] = useState<ShopfloorQueueStep[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const search = async () => {
    setBusy(true)
    setError(null)
    try {
      setFound(await searchShopfloorOrder({ number }))
    } catch (e) {
      setFound(null)
      setError(e instanceof ApiError && e.status === 404 ? 'OS não encontrada.' : 'Não foi possível buscar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }
  return (
    <FloorSheet open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) { setNumber(''); setFound(null); setError(null) } }} title="Buscar OS pelo número"
      description="Digite o número da OS (por exemplo, 41 para a OS-2026-00041)."
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => onOpenChange(false)}>Voltar</FloorButton>
          <FloorButton className="flex-1" disabled={!number || busy} onClick={() => void search()}>{busy ? 'Buscando…' : 'Buscar'}</FloorButton>
        </div>
      )}>
      <NumericKeypad label="Número da OS" value={number} onChange={(v) => { setNumber(v); setError(null) }} />
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
      {found ? (
        <ul aria-label="Etapas encontradas" className="space-y-3">{found.map((s) => <li key={s.stepId}><QueueCard step={s} /></li>)}</ul>
      ) : null}
    </FloorSheet>
  )
}