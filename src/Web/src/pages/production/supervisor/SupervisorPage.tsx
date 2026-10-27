import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { RefreshCw } from 'lucide-react'
import { type ComponentType, useState } from 'react'
import { refreshSupervisorPanel, useGetSupervisorOverview } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { cn } from '@/lib/utils'
import { PageHeading } from '@/shell/PageHeading'
import { ProductionTabs } from '../ProductionTabs'
import { BoardBlock, type BlockProps, CapacityBlock, IndicatorsBlock, QualityBlock, ShortagesBlock } from './blocks'
import { DESKTOP, MOBILE_ORDER } from './supervisor'

const TITLE = 'Produção: Painel do supervisor'
export const REFRESH_LIMITED = 'Aguarde alguns segundos para atualizar de novo.'

/** Cada bloco na posição do desktop (grade de 12) e com a ordem do celular. */
const LAYOUT: Record<string, { Block: ComponentType<BlockProps>; span: string }> = {
  board: { Block: BoardBlock, span: 'lg:col-span-12' },
  quality: { Block: QualityBlock, span: 'lg:col-span-6' },
  shortages: { Block: ShortagesBlock, span: 'lg:col-span-6' },
  capacity: { Block: CapacityBlock, span: 'lg:col-span-7' },
  indicators: { Block: IndicatorsBlock, span: 'lg:col-span-5' },
}

const select = 'min-h-[44px] rounded-md border border-input bg-card px-2 py-2 text-sm sm:min-h-0'

/**
 * Fábrica → Produção → Painel do supervisor (DSH-03): os blocos Quadro, Qualidade, Faltas de material, Capacidade e Indicadores, cada um no
 * seu `DashboardCard` e com o seu endpoint, o filtro de linha na URL (?linha=, padrão "Todas") e "Atualizar" para a capacidade e os
 * indicadores. Tela da Fábrica: exige production.view, não o painel do proprietário.
 */
export function SupervisorPage() {
  const { linha } = useSearch({ strict: false }) as { linha?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const overview = useGetSupervisorOverview()
  const [refreshing, setRefreshing] = useState(false)
  const [notice, setNotice] = useState<string>()
  const line = linha || undefined

  const setLine = (value: string) => void navigate({ to: '.', replace: true, search: (prev: Record<string, unknown>) => ({ ...prev, linha: value || undefined }) } as never)
  const refresh = async () => {
    setNotice(undefined)
    setRefreshing(true)
    try {
      await refreshSupervisorPanel()
      await client.invalidateQueries({ predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith('/api/v1/dashboard/supervisor') })
    } catch (e) {
      setNotice(e instanceof ApiError && e.status === 429 ? REFRESH_LIMITED : 'Não foi possível atualizar agora. Tente de novo.')
    } finally {
      setRefreshing(false)
    }
  }

  const data = overview.data
  return (
    <>
      <PageHeading title={TITLE}>
        <p className="mt-1 text-muted-foreground">Onde agir primeiro: a qualidade, o material que falta, as OS atrasadas, a ocupação e as paradas.</p>
      </PageHeading>
      <ProductionTabs />
      {overview.isPending ? (
        <div aria-busy="true" aria-label="Carregando o painel do supervisor" className="grid gap-4 lg:grid-cols-12">
          <Skeleton className="h-40 lg:col-span-12" /><Skeleton className="h-64 lg:col-span-6" /><Skeleton className="h-64 lg:col-span-6" />
        </div>
      ) : overview.isError || !data ? (
        <ErrorState title="Não foi possível montar o painel do supervisor." onRetry={() => void overview.refetch()} />
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <label className="flex w-full flex-col gap-1 text-sm sm:w-auto">
              <span className="font-semibold">Linha</span>
              <select className={select} value={line ?? ''} onChange={(e) => setLine(e.target.value)}>
                <option value="">Todas</option>
                {data.lines.map((l) => <option key={l} value={l}>{l}</option>)}
                {line && !data.lines.includes(line) ? <option value={line}>{line}</option> : null}
              </select>
            </label>
            <div className="flex flex-col items-end gap-1">
              <button type="button" onClick={() => void refresh()} disabled={refreshing}
                className="inline-flex min-h-[44px] items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60">
                <RefreshCw aria-hidden="true" className={cn('size-4', refreshing && 'animate-spin')} />
                {refreshing ? 'Atualizando…' : 'Atualizar'}
              </button>
              <p role="status" className="text-xs text-warn-ink">{notice}</p>
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
            {DESKTOP.filter((code) => data.blocks.includes(code)).map((code) => {
              const { Block, span } = LAYOUT[code]
              return <Block key={`${code}-${line ?? ''}`} line={line} overview={data} className={cn('min-w-0', MOBILE_ORDER[code], 'lg:order-none', span)} />
            })}
          </div>
        </div>
      )}
    </>
  )
}
