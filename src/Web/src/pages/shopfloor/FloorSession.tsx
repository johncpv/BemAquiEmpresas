import { useNavigate } from '@tanstack/react-router'
import { useEffect, useRef, useState } from 'react'
import { sessionDeadline } from '@/api/session'
import { FloorButton, FloorSheet } from '@/components/shopfloor/kit'
import { FAB_ENTRY_TEXT, idleState, type IdleState } from './device'
import { endFloorSession, useFloorSession } from './floor-session'

/**
 * Saída por inatividade (PRD-03b CA 15): com sessão por PIN (ou o login completo do tablet), 15 min sem toque abre o aviso
 * "Você vai sair em 60 s. Tocar para continuar."; sem toque, sai e volta à lista de nomes. As 12 h absolutas também encerram.
 */
export function IdleGuard() {
  const navigate = useNavigate()
  const { pin, ephemeral } = useFloorSession()
  const active = pin || ephemeral
  const last = useRef(0)
  const [state, setState] = useState<IdleState>({ phase: 'active' })

  useEffect(() => {
    if (!active) return
    last.current = Date.now()
    const touch = () => { last.current = Date.now() }
    const events = ['pointerdown', 'keydown', 'touchstart', 'wheel'] as const
    events.forEach((e) => window.addEventListener(e, touch, { passive: true }))
    const timer = setInterval(() => {
      const next = idleState(last.current, Date.now(), pin ? sessionDeadline() : null)
      setState((prev) => (prev.phase === next.phase && (next.phase !== 'warning' || (prev.phase === 'warning' && prev.secondsLeft === next.secondsLeft)) ? prev : next))
      if (next.phase === 'expired') {
        clearInterval(timer)
        void endFloorSession((to) => void navigate({ to }))
      }
    }, 1000)
    return () => {
      clearInterval(timer)
      events.forEach((e) => window.removeEventListener(e, touch))
    }
  }, [active, pin, navigate])

  if (!active || state.phase !== 'warning') return null
  const keep = () => {
    last.current = Date.now()
    setState({ phase: 'active' })
  }

  return (
    <FloorSheet role="alertdialog" open onOpenChange={(o) => { if (!o) keep() }} title={FAB_ENTRY_TEXT.idle}
      description={`Saindo em ${state.secondsLeft} s. Toque em qualquer lugar para continuar.`}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => void endFloorSession((to) => void navigate({ to }))}>Sair agora</FloorButton>
          <FloorButton className="flex-1" onClick={keep}>Continuar</FloorButton>
        </div>
      )}>
      <p className="text-center font-display text-[48px] font-bold tabular-nums" aria-live="polite">{state.secondsLeft}</p>
    </FloorSheet>
  )
}