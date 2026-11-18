import { Link } from '@tanstack/react-router'
import { CheckCircle2, ChevronRight, Circle, PartyPopper } from 'lucide-react'
import { useState } from 'react'
import { useGetDashboardFirstSteps } from '@/api/generated/bemaqui'
import type { FirstStep, FirstStepsCard } from '@/api/generated/model'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import type { NavModule } from '@/navigation/modules'
import { useHasProfile, useNavigation } from '@/navigation/navigation-context'
import { canIn } from '../use-can'

/** Perfil que tem o checklist (PERF-04): o Comércio. A fábrica não vê o cartão nem faz a chamada. */
const FIRST_STEPS_PROFILES = ['retail'] as const

export const ASK_ADMIN = 'Peça a quem administra o sistema.'

const DISMISS_KEY = 'bq.primeiros-passos.dispensado'

function readDismissed(): boolean {
  try {
    return globalThis.localStorage?.getItem(DISMISS_KEY) === '1'
  } catch {
    return false
  }
}

function saveDismissed() {
  try {
    globalThis.localStorage?.setItem(DISMISS_KEY, '1')
  } catch {
    // sem armazenamento local: o cartão volta no próximo acesso, sem prejuízo
  }
}

type Modules = readonly Pick<NavModule, 'code' | 'locked' | 'actions'>[]

function Step({ step, index, all }: { step: FirstStep; index: number; all: Modules }) {
  const allowed = !step.permission || canIn(all, step.permission)
  const title = `${index + 1}. ${step.title}`
  const icon = step.done
    ? <CheckCircle2 aria-hidden="true" className="mt-0.5 size-5 flex-none text-ok" />
    : <Circle aria-hidden="true" className="mt-0.5 size-5 flex-none text-muted-foreground" />
  const status = step.done
    ? <StatusChip tone="ok">Concluído</StatusChip>
    : !step.available ? <StatusChip tone="neutral">Em breve</StatusChip> : null
  const body = (
    <>
      {icon}
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-2">
          <b className="font-semibold">{title}</b>
          {status}
        </span>
        <span className="block text-sm text-muted-foreground">{step.text}</span>
        {step.available && !step.done && !allowed ? <span className="block text-sm font-medium">{ASK_ADMIN}</span> : null}
      </span>
    </>
  )
  if (step.available && step.url && allowed && !step.done) {
    return (
      <Link to={step.url as never} className="flex min-h-[44px] gap-3 rounded-md border border-border bg-card p-3 hover:border-primary">
        {body}
        <ChevronRight aria-hidden="true" className="mt-0.5 size-4 flex-none text-muted-foreground" />
      </Link>
    )
  }

  return <div className="flex gap-3 rounded-md border border-border bg-card p-3" data-step={step.key}>{body}</div>
}

/**
 * Cartão "Primeiros passos" (PERF-04 CA 6 a 9): os cinco passos com o progresso calculado dos dados. Passo sem a permissão da tela fica sem
 * link, com "Peça a quem administra o sistema."; passo de story futura fica "Em breve". Com tudo pronto, mostra "Tudo pronto", o próximo
 * passo sugerido e pode ser dispensado.
 */
export function PrimeirosPassosCard({ card, all, onDismiss }: { card: FirstStepsCard; all: Modules; onDismiss: () => void }) {
  if (card.allDone) {
    return (
      <section aria-labelledby="primeiros-passos" className="mb-8 rounded-md border border-border bg-card p-4">
        <h2 id="primeiros-passos" className="flex items-center gap-2 text-[17px] font-semibold">
          <PartyPopper aria-hidden="true" className="size-5 text-primary" /> Tudo pronto
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">A loja está pronta para vender.</p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {card.nextUrl ? (
            <Link to={card.nextUrl as never} className="inline-flex min-h-[44px] items-center rounded-md border border-border px-3 text-sm font-semibold hover:border-primary sm:min-h-9">
              {card.nextTitle}
            </Link>
          ) : null}
          <button type="button" onClick={onDismiss} className="inline-flex min-h-[44px] items-center rounded-md px-3 text-sm font-semibold text-muted-foreground hover:text-foreground sm:min-h-9">
            Dispensar
          </button>
        </div>
      </section>
    )
  }

  return (
    <section aria-labelledby="primeiros-passos" className="mb-8">
      <h2 id="primeiros-passos" className="mb-1 flex items-center text-[17px] font-semibold">
        Primeiros passos
        <Dica nome="Primeiros passos" texto="O que falta para a primeira venda. Cada passo se marca sozinho quando o sistema encontra o dado." />
      </h2>
      <p className="mb-3 text-sm text-muted-foreground tabular-nums" aria-live="polite">{card.done} de {card.total} concluídos</p>
      <div className="mb-3 h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-label="Progresso dos primeiros passos" aria-valuemin={0} aria-valuemax={card.total} aria-valuenow={card.done}>
        <div className="h-full bg-primary" style={{ width: `${card.total ? (card.done / card.total) * 100 : 0}%` }} />
      </div>
      <ol className="grid gap-2 md:grid-cols-2">
        {card.steps.map((s, i) => <li key={s.key}><Step step={s} index={i} all={all} /></li>)}
      </ol>
    </section>
  )
}

/** Início: o cartão só para o Comércio, com esqueleto ao carregar e erro com "Tentar de novo". */
export function PrimeirosPassos() {
  const { all } = useNavigation()
  const retail = useHasProfile(FIRST_STEPS_PROFILES)
  const enabled = retail && canIn(all, 'dashboard.view')
  const query = useGetDashboardFirstSteps({ query: { enabled } })
  const [dismissed, setDismissed] = useState(readDismissed)
  if (!enabled) return null
  if (query.isPending) {
    return (
      <section aria-labelledby="primeiros-passos" className="mb-8" aria-busy="true">
        <h2 id="primeiros-passos" className="mb-3 text-[17px] font-semibold">Primeiros passos</h2>
        <Skeleton className="h-40" />
      </section>
    )
  }

  if (query.isError) {
    return (
      <section aria-labelledby="primeiros-passos" className="mb-8">
        <h2 id="primeiros-passos" className="mb-3 text-[17px] font-semibold">Primeiros passos</h2>
        <ErrorState title="Não foi possível conferir os primeiros passos." onRetry={() => void query.refetch()} />
      </section>
    )
  }

  const card = query.data
  if (!card?.applies || (card.allDone && dismissed)) return null
  return (
    <PrimeirosPassosCard card={card} all={all} onDismiss={() => {
      saveDismissed()
      setDismissed(true)
    }} />
  )
}
