import { Link, useParams } from '@tanstack/react-router'
import { ChevronLeft, ChevronRight, Pause, Play } from 'lucide-react'
import { type CSSProperties, type RefObject, useCallback, useEffect, useRef, useState } from 'react'
import { useGetKioskScreenPreview } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { useProductionLive } from '@/pages/production/kanban/useBoardLive'
import { rotationViews } from '@/pages/tv/panel'
import { TvSurface } from '@/pages/tv/TvSurface'
import { useRotation } from '@/pages/tv/useRotation'
import { useTvData } from '@/pages/tv/useTvData'
import { PageHeading } from '@/shell/PageHeading'

/** A TV de referência (CA 10 e 11): 1920 × 1080, escalada à janela. */
export const PREVIEW_WIDTH = 1920
export const PREVIEW_HEIGHT = 1080

/** Tokens de leitura a distância como numa TV de 1080 linhas (os da TV usam vh, que aqui seria a altura da janela). */
const TV_1080: CSSProperties = { ['--tv-title' as string]: `${0.034 * PREVIEW_HEIGHT}px`, ['--tv-line' as string]: `${0.029 * PREVIEW_HEIGHT}px`, ['--tv-count' as string]: `${0.05 * PREVIEW_HEIGHT}px` }

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

/**
 * Prévia da tela no backoffice (/app/painel-tv/$id/previa, PNL-01b CA 10, ADR 0030 §5): as visões exatamente como a TV mostraria, com o
 * escopo da tela (linha, visões e rotação), pela API do usuário (kiosk.view e production.view), sem sessão de quiosque. O supervisor
 * confere a tela antes de ir até a TV (propositivo).
 */
export function KioskPreviewPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const screen = useGetKioskScreenPreview(id, { query: { retry: false } })
  const [paused, setPaused] = useState(false)
  const tv = useTvData({ kind: 'preview', id }, screen.data?.views ?? [], !!screen.data)
  const views = rotationViews(screen.data?.views ?? [], tv.data.delays.data)
  const rotation = useRotation(views.length, screen.data?.rotationSeconds ?? 20, paused)
  const { reloadAll } = tv
  const reload = useCallback(() => reloadAll(), [reloadAll])
  const live = useProductionLive(reload, { schedule: true })
  const frame = useRef<HTMLDivElement>(null)
  const scale = useScale(frame)

  const title = screen.data ? `Prévia: ${screen.data.name}` : 'Prévia da tela'
  return (
    <>
      <PageHeading title={title}>
        <p className="mt-2 max-w-3xl text-muted-foreground">As visões como a TV mostra, a 1920 × 1080, com a linha, as visões e a rotação desta tela.</p>
      </PageHeading>
      {screen.isPending ? <Skeleton className="aspect-video w-full" /> : screen.isError ? (
        screen.error instanceof ApiError && (screen.error.status === 404 || screen.error.status === 409)
          ? <EmptyState title="Esta tela não tem prévia." description={screen.error.message} action={<Link to={'/app/painel-tv' as never} className={button}>Voltar ao Painel da TV</Link>} />
          : <ErrorState onRetry={() => screen.refetch()} />
      ) : (
        <div className="space-y-3">
          <div role="note" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-primary-soft bg-primary-soft px-3 py-2">
            <p className="text-sm font-semibold">Prévia: esta é a tela {screen.data.name}</p>
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={rotation.previous} disabled={views.length <= 1} className={button}><ChevronLeft aria-hidden="true" className="size-4" /> Visão anterior</button>
              <button type="button" onClick={rotation.next} disabled={views.length <= 1} className={button}>Próxima visão <ChevronRight aria-hidden="true" className="size-4" /></button>
              <button type="button" onClick={() => setPaused((p) => !p)} aria-pressed={paused} className={button}>
                {paused ? <Play aria-hidden="true" className="size-4" /> : <Pause aria-hidden="true" className="size-4" />}
                {paused ? 'Retomar rotação' : 'Pausar rotação'}
              </button>
            </div>
          </div>
          <div ref={frame} className="w-full overflow-hidden rounded-md border border-border" style={{ height: PREVIEW_HEIGHT * scale }}>
            <div data-surface="tv" data-preview="" className="theme-tv origin-top-left overflow-hidden p-8"
              style={{ ...TV_1080, width: PREVIEW_WIDTH, height: PREVIEW_HEIGHT, transform: `scale(${scale})` }}>
              <TvSurface screen={screen.data} data={tv.data} status={live} updatedAt={tv.updatedAt} width={PREVIEW_WIDTH - 64} views={views} rotation={rotation} className="h-full" />
            </div>
          </div>
        </div>
      )}
    </>
  )
}

/** Escala da TV de 1920 px à largura da área (nunca maior que 1). */
function useScale(ref: RefObject<HTMLDivElement | null>): number {
  const [scale, setScale] = useState(0.5)
  useEffect(() => {
    const element = ref.current
    if (!element) return
    const update = () => setScale(Math.min(1, element.clientWidth / PREVIEW_WIDTH) || 0.5)
    update()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(update)
    observer.observe(element)
    return () => observer.disconnect()
  })
  return scale
}
