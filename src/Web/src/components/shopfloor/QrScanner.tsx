import { Camera, Keyboard } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { FloorButton, FloorSheet } from '@/components/shopfloor/kit'
import { canScanQr } from './qr'

type Detector = { detect: (source: HTMLVideoElement) => Promise<{ rawValue?: string }[]> }
type DetectorCtor = new (options: { formats: string[] }) => Detector

/**
 * Leitura de QR em folha de tela cheia (PAT-01b CA 5 e 9; reaproveitável na PRD-03a): câmera traseira pelo `BarcodeDetector` do navegador.
 * Sem câmera, com a permissão negada ou sem a API, a mensagem `fallbackMessage` e o campo para digitar, já com o prefixo (propositivo).
 * `parse` transforma o que foi lido ou digitado no valor esperado (null = não serve, mostra `invalidMessage`); `onResult` recebe o valor.
 */
export function QrScanner({ open, onOpenChange, title, description, fallbackMessage, invalidMessage, manualLabel, manualPrefix, parse, onResult }: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: string
  fallbackMessage: string
  invalidMessage: string
  manualLabel: string
  manualPrefix?: string
  parse: (raw: string) => string | null
  onResult: (value: string) => void
}) {
  const [mode, setMode] = useState<'starting' | 'camera' | 'manual'>('starting')
  const [fallback, setFallback] = useState(false)
  const [typed, setTyped] = useState('')
  const [error, setError] = useState<string | null>(null)
  const video = useRef<HTMLVideoElement>(null)
  const handlers = useRef({ parse, onResult, invalidMessage })
  useEffect(() => { handlers.current = { parse, onResult, invalidMessage } })
  const inputId = useId()

  useEffect(() => {
    if (!open) return
    let alive = true
    let stream: MediaStream | null = null
    let timer: ReturnType<typeof setTimeout> | undefined
    const manual = () => {
      if (!alive) return
      setFallback(true)
      setMode('manual')
    }
    void (async () => {
      if (!(await canScanQr())) return manual()
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false })
        if (!alive || !video.current) return
        video.current.srcObject = stream
        await video.current.play()
        setMode('camera')
        const Ctor = (window as unknown as { BarcodeDetector: DetectorCtor }).BarcodeDetector
        const detector = new Ctor({ formats: ['qr_code'] })
        const tick = async () => {
          if (!alive || !video.current) return
          try {
            const codes = await detector.detect(video.current)
            const raw = codes.find((c) => c.rawValue)?.rawValue
            if (raw) {
              const value = handlers.current.parse(raw)
              if (value) {
                handlers.current.onResult(value)
                return
              }
              setError(handlers.current.invalidMessage)
            }
          } catch {
            // quadro ainda sem imagem: tenta no próximo
          }

          timer = setTimeout(() => void tick(), 300)
        }
        void tick()
      } catch {
        manual()
      }
    })()
    return () => {
      alive = false
      clearTimeout(timer)
      stream?.getTracks().forEach((t) => t.stop())
    }
  }, [open])

  const submit = () => {
    const value = parse(typed)
    if (value) onResult(value)
    else setError(invalidMessage)
  }

  return (
    <FloorSheet open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) { setTyped(''); setError(null); setMode('starting'); setFallback(false) } }} title={title} description={description}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => { onOpenChange(false); setTyped(''); setError(null); setMode('starting'); setFallback(false) }}>Voltar</FloorButton>
          {mode === 'manual'
            ? <FloorButton className="flex-1" disabled={!typed.trim()} onClick={submit}>Abrir</FloorButton>
            : <FloorButton tone="secondary" className="flex-1" onClick={() => setMode('manual')}><Keyboard aria-hidden="true" className="size-6" />Digitar</FloorButton>}
        </div>
      )}>
      {mode === 'manual' ? (
        <form noValidate className="space-y-3" onSubmit={(e) => { e.preventDefault(); submit() }}>
          {fallback ? <p role="alert" className="rounded-md border border-warn-soft bg-warn-soft p-4 text-[18px] font-semibold text-warn-ink">{fallbackMessage}</p> : null}
          <label htmlFor={inputId} className="block text-[18px] font-semibold">{manualLabel}</label>
          <div className="flex min-h-[56px] items-center rounded-md border-2 border-input bg-card px-4 focus-within:border-primary">
            {manualPrefix ? <span aria-hidden="true" className="font-display text-[24px] font-bold tabular-nums text-muted-foreground">{manualPrefix}</span> : null}
            <input id={inputId} value={typed} autoFocus autoComplete="off" inputMode="numeric" enterKeyHint="go"
              onChange={(e) => { setTyped(e.target.value); setError(null) }}
              className="min-h-[52px] w-full bg-transparent font-display text-[24px] font-bold tabular-nums outline-none" />
          </div>
        </form>
      ) : (
        <div className="space-y-3">
          <div className="relative overflow-hidden rounded-md border-2 border-border bg-muted">
            <video ref={video} muted playsInline aria-label="Imagem da câmera" className="aspect-[3/4] w-full object-cover sm:aspect-video" />
            {mode === 'starting' ? <p role="status" className="absolute inset-0 grid place-items-center text-[18px] font-semibold"><span className="inline-flex items-center gap-2"><Camera aria-hidden="true" className="size-6" />Abrindo a câmera…</span></p> : null}
          </div>
          <p className="text-[18px] text-muted-foreground">Aponte a câmera para o QR code da etiqueta.</p>
        </div>
      )}
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
    </FloorSheet>
  )
}
