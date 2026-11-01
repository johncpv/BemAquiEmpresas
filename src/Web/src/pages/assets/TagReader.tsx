import { useNavigate } from '@tanstack/react-router'
import { ScanLine } from 'lucide-react'
import { useState } from 'react'
import { QrScanner } from '@/components/shopfloor/QrScanner'
import { FloorButton } from '@/components/shopfloor/kit'
import { useCan } from '@/pages/use-can'
import { useFloorSession } from '@/pages/shopfloor/floor-session'
import { CAMERA_FALLBACK, tagFrom } from './labels'

/**
 * "Ler etiqueta" (PAT-01b CA 5 e 9): câmera pelo `BarcodeDetector`; sem ela, digitar a etiqueta. O resultado sempre abre a rota interna
 * `/b/{etiqueta}` (o endereço lido nunca é aberto).
 */
export function ReadTagSheet({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const navigate = useNavigate()
  return (
    <QrScanner open={open} onOpenChange={onOpenChange} title="Ler etiqueta" description="Aponte a câmera para o QR code da etiqueta do bem."
      fallbackMessage={CAMERA_FALLBACK} invalidMessage="Este código não é de uma etiqueta do patrimônio. Confira e tente de novo."
      manualLabel="Número da etiqueta" manualPrefix="PAT-" parse={tagFrom}
      onResult={(tag) => { onOpenChange(false); void navigate({ to: '/b/$tag', params: { tag } }) }} />
  )
}

/**
 * Entrada do `/fab` (CA 5 e 10): com `assets.view` e fora da sessão por PIN do tablet (a leitura não está na lista da sessão por PIN).
 */
export function ReadTagEntry({ tone = 'entry' }: { tone?: 'entry' | 'button' }) {
  const canView = useCan('assets.view')
  const { pin } = useFloorSession()
  const [open, setOpen] = useState(false)
  if (!canView || pin) return null
  return (
    <>
      {tone === 'button' ? (
        <FloorButton tone="secondary" onClick={() => setOpen(true)}><ScanLine aria-hidden="true" className="size-6" />Ler outra etiqueta</FloorButton>
      ) : (
        <button type="button" data-floor-target="" onClick={() => setOpen(true)}
          className="flex min-h-[56px] w-full items-center justify-between gap-2 rounded-md border-2 border-primary bg-card px-4 text-left text-[18px] font-semibold">
          <span className="inline-flex items-center gap-2"><ScanLine aria-hidden="true" className="size-6" />Ler etiqueta</span>
          <span className="text-muted-foreground">Ficha da máquina pelo QR code</span>
        </button>
      )}
      <ReadTagSheet open={open} onOpenChange={setOpen} />
    </>
  )
}
