import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useCallback, useEffect, useState } from 'react'
import { FAB_ENTRY } from '@/api/http'
import { setAccessToken } from '@/api/session'
import { FloorButton, FloorHeader } from '@/components/shopfloor/kit'
import { useHelp } from '@/help/help-context'
import { endFloorSession, useFloorSession } from '@/pages/shopfloor/floor-session'
import { TimeClockPanel } from './TimeClockPanel'

/** Segundos do comprovante na tela antes de o tablet voltar à lista de nomes (CA 7). */
export const BACK_TO_NAMES_SECONDS = 8

/**
 * "Meu ponto" no tablet da fábrica (/fab/ponto, RH-01a CA 7): layout do /fab, botões de 56 px. Na sessão por PIN, marcar encerra a
 * sessão no servidor: o comprovante fica alguns segundos e o tablet volta à lista de nomes para o próximo funcionário.
 */
export function FabTimeClockPage() {
  const help = useHelp()
  const navigate = useNavigate()
  const client = useQueryClient()
  const { pin } = useFloorSession()
  const [leaving, setLeaving] = useState<number | null>(null)

  useEffect(() => { document.title = 'Meu ponto · App do operador · BemAqui Empresas' }, [])

  const backToNames = useCallback(() => {
    // A sessão por PIN já acabou no servidor: sem chamar o logout, só tira o token da memória.
    setAccessToken(null)
    client.clear()
    void navigate({ to: FAB_ENTRY })
  }, [client, navigate])

  useEffect(() => {
    if (leaving === null) return
    if (leaving <= 0) {
      backToNames()
      return
    }

    const timer = setTimeout(() => setLeaving(leaving - 1), 1000)
    return () => clearTimeout(timer)
  }, [leaving, backToNames])

  return (
    <>
      <FloorHeader onHelp={() => help.setOpen(true)} title="Meu ponto" subtitle="App do operador" />
      <div className="mx-auto max-w-3xl space-y-4 p-4">
        <TimeClockPanel onSessionEnded={() => setLeaving(BACK_TO_NAMES_SECONDS)} />
        {leaving !== null ? (
          <div className="space-y-2">
            <p role="status" aria-live="polite" className="text-center text-[18px] font-semibold">Voltando à lista de nomes em {leaving} s.</p>
            <FloorButton className="w-full" onClick={backToNames}>Pronto</FloorButton>
          </div>
        ) : (
          <div className="flex flex-wrap gap-[8px]">
            <Link to="/fab" data-floor-target="" className="inline-flex min-h-[56px] flex-1 items-center justify-center rounded-md border-2 border-input bg-card px-5 text-[18px] font-semibold">Voltar à fila</Link>
            {pin ? <FloorButton tone="secondary" className="flex-1" onClick={() => void endFloorSession((to) => void navigate({ to }))}>Sair</FloorButton> : null}
          </div>
        )}
      </div>
    </>
  )
}
