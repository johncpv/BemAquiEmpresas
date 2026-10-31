import { useQueryClient } from '@tanstack/react-query'
import { WifiOff } from 'lucide-react'
import { OFFLINE_MESSAGE } from '@/api/http'
import { FloorButton, FloorState } from '@/components/shopfloor/kit'
import { useOnline } from './floor-session'

/** "Sem conexão. Esta ação precisa de rede." com o botão grande "Tentar de novo" (PRD-03b CA 3, kit da PRD-03a). */
export function OfflineState({ onRetry, message = OFFLINE_MESSAGE }: { onRetry: () => void; message?: string }) {
  return (
    <FloorState tone="error" title={message} message="Confira o Wi-Fi do tablet. Nada fica guardado para enviar depois."
      action={<FloorButton onClick={onRetry}><WifiOff aria-hidden="true" className="size-6" />Tentar de novo</FloorButton>} />
  )
}

/**
 * Sem rede, o /fab cobre a tela com o estado "Sem conexão" (ADR 0026 §6: sem funcionamento sem rede, nada é guardado para depois).
 * "Tentar de novo" confere a rede e recarrega o que a tela mostra.
 */
export function OfflineGate({ message = OFFLINE_MESSAGE }: { message?: string } = {}) {
  const client = useQueryClient()
  const [online, setOnline] = useOnline()
  if (online) return null
  const retry = () => {
    if (navigator.onLine) {
      setOnline(true)
      void client.invalidateQueries()
    }
  }

  return (
    <div role="alertdialog" aria-modal="true" aria-label={message} className="fixed inset-0 z-[60] flex items-center justify-center bg-background/95 p-4">
      <div className="w-full max-w-xl"><OfflineState onRetry={retry} message={message} /></div>
    </div>
  )
}