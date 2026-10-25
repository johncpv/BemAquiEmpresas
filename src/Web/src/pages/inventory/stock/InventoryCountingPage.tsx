import { useQueryClient } from '@tanstack/react-query'
import { Outlet, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { countInventoryLine, getGetInventoryCountingQueryKey, useGetInventoryCounting } from '@/api/generated/bemaqui'
import type { InventoryCountingLine, InventoryCountingView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorDica, FloorHeader, FloorMessages, FloorSheet, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { notifySuccess } from '@/components/ui/messages'
import { FloorHelp } from '@/help/FloorHelp'
import { useHelp } from '@/help/help-context'
import { cn } from '@/lib/utils'
import { ModuleGate } from '@/pages/ModuleGate'
import { countingOrder, INVENTORY_DICAS, parseCount, pressKey, toKeypad } from './inventory-counts'
import { formatQuantity } from './stock'

/** Tela dedicada da contagem no celular (CA 4, padrão 1): sem dock nem topbar, com a ajuda e as mensagens do kit chão de fábrica. */
export function CountingLayout() {
  return (
    <FloorHelp>
      <main id="conteudo" className="min-h-dvh bg-background pb-8 text-[18px] text-foreground">
        <ModuleGate code="inventory">
          <FloorMessages />
          <Outlet />
        </ModuleGate>
      </main>
    </FloorHelp>
  )
}

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', ',', '0']

/** Folha de tela cheia da contagem de um item: teclado numérico com vírgula, a unidade e "Salvar contagem". */
function CountSheet({ view, line, onClose }: { view: InventoryCountingView; line: InventoryCountingLine; onClose: () => void }) {
  const client = useQueryClient()
  const [text, setText] = useState(toKeypad(line.enteredQuantity ?? line.countedQty))
  const [unit, setUnit] = useState(line.units.find((u) => u.code === line.enteredUnitCode)?.id ?? line.stockUnitId)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<{ message: string; reload: boolean } | null>(null)
  const quantity = parseCount(text)
  const unitCode = line.units.find((u) => u.id === unit)?.code ?? line.unitCode
  const reload = async () => { await client.invalidateQueries({ queryKey: getGetInventoryCountingQueryKey(view.countId) }) }
  const save = async () => {
    if (quantity === null) return
    setBusy(true)
    setError(null)
    try {
      await countInventoryLine(view.countId, line.id, { quantity, unitId: unit === line.stockUnitId ? null : unit, version: line.version })
      notifySuccess(`Contagem salva: ${line.itemCode} ${formatQuantity(quantity)} ${unitCode}`)
      await reload()
      onClose()
    } catch (e) {
      setError({ message: e instanceof ApiError ? e.message : 'Não foi possível salvar. Tente de novo.', reload: e instanceof ApiError && e.status === 409 })
    } finally {
      setBusy(false)
    }
  }
  return (
    <FloorSheet open onOpenChange={(o) => { if (!o) onClose() }} title={line.itemCode} description={`${line.itemName}${line.lotNumber ? ` · lote ${line.lotNumber}` : ''}${line.isRecount ? ' · recontagem' : ''}`}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={onClose} disabled={busy}>Voltar</FloorButton>
          <FloorButton className="flex-1" onClick={() => void save()} disabled={busy || quantity === null}>{busy ? 'Gravando…' : 'Salvar contagem'}</FloorButton>
        </div>
      )}>
      <output aria-label={`Quantidade contada em ${unitCode}`} aria-live="polite" className="block min-h-[64px] rounded-md border-2 border-input bg-card px-4 py-3 text-center font-display text-[30px] font-bold tabular-nums">
        {text || <span className="text-muted-foreground">—</span>} <span className="text-[20px] text-muted-foreground">{unitCode}</span>
      </output>
      {line.units.length > 1 ? (
        <div role="group" aria-label="Unidade" className="flex flex-wrap gap-[8px]">
          {line.units.map((u) => <FloorButton key={u.id} tone={u.id === unit ? 'primary' : 'secondary'} aria-pressed={u.id === unit} onClick={() => setUnit(u.id)}>{u.code}</FloorButton>)}
        </div>
      ) : null}
      <div role="group" aria-label="Teclado numérico" className="grid grid-cols-3 gap-[8px]">
        {KEYS.map((k) => <FloorButton key={k} tone="secondary" className="min-h-[64px] text-[24px] tabular-nums" aria-label={k === ',' ? 'Vírgula' : k} onClick={() => setText((t) => pressKey(t, k))}>{k}</FloorButton>)}
        <FloorButton tone="secondary" className="min-h-[64px]" aria-label="Apagar o último número" onClick={() => setText((t) => t.slice(0, -1))}>⌫</FloorButton>
      </div>
      <FloorButton tone="secondary" className="w-full" onClick={() => setText('')}>Limpar</FloorButton>
      {error ? (
        <div role="alert" className="space-y-2 rounded-md border border-crit-soft bg-crit-soft p-4 text-crit-ink">
          <p>{error.message}</p>
          {error.reload ? <FloorButton tone="secondary" onClick={() => { void reload(); onClose() }}>Recarregar</FloorButton> : null}
        </div>
      ) : null}
    </FloorSheet>
  )
}

/**
 * Contagem pelo celular (CA 3 e 4, padrão 1): um cartão por item e lote (alvo ≥ 56 px, texto de 18 px), sem o saldo do sistema; o
 * toque abre a folha de tela cheia com o teclado numérico. O que falta contar vem primeiro; a recontagem só aparece para quem não
 * contou a original.
 */
export function InventoryCountingPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetInventoryCounting(id)
  const help = useHelp()
  const navigate = useNavigate()
  const [open, setOpen] = useState<string | null>(null)
  const back = () => void navigate({ to: '/app/estoque/inventarios/$id' as never, params: { id } as never })
  if (query.isPending) return <div className="mx-auto max-w-3xl px-4 py-4"><FloorSkeleton /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return (
      <div className="mx-auto max-w-3xl px-4 py-6">
        <FloorState tone="error" title={notFound ? 'Inventário não encontrado.' : 'Não foi possível carregar a contagem.'} message={notFound ? 'Volte à lista de inventários.' : 'Verifique a conexão e tente de novo.'}
          action={<FloorButton onClick={() => (notFound ? void navigate({ to: '/app/estoque/inventarios' as never }) : void query.refetch())}>{notFound ? 'Ver inventários' : 'Tentar de novo'}</FloorButton>} />
      </div>
    )
  }

  const view = query.data
  const lines = countingOrder(view.lines)
  const current = lines.find((l) => l.id === open)
  return (
    <>
      <FloorHeader title={`Contagem ${view.number}`} subtitle={`${view.locationName} · ${view.progress}`} onHelp={() => help.setOpen(true)}>
        <FloorButton tone="secondary" onClick={back} className="px-3">Sair</FloorButton>
      </FloorHeader>
      <div className="mx-auto max-w-3xl space-y-[8px] px-4 py-4">
        <p className="flex items-center text-base text-muted-foreground">Digite o que está na prateleira.<FloorDica nome="Contagem cega" texto={INVENTORY_DICAS.cega} /></p>
        {!view.canCount ? <FloorState title={view.status === 'Counting' ? 'Você não conta neste inventário.' : 'Contagem encerrada.'} message={view.status === 'Counting' ? 'Peça a permissão de contar no Estoque.' : 'O inventário foi fechado ou cancelado: a contagem fica só para leitura.'} /> : null}
        {view.canCount && lines.length === 0 ? <FloorState title="Nada para você contar." message="As linhas que faltam são recontagens de itens que você contou: outra pessoa reconta." action={<FloorButton onClick={back}>Voltar ao inventário</FloorButton>} /> : null}
        <ul className="space-y-[8px]" aria-label="Itens para contar">
          {lines.map((l) => (
            <li key={l.id}>
              <button type="button" data-floor-target="" disabled={!view.canCount || (l.countedQty !== null && !l.countedByMe)} onClick={() => setOpen(l.id)}
                className={cn('flex min-h-[72px] w-full items-center gap-3 rounded-md border-2 bg-card px-4 py-3 text-left disabled:opacity-80', l.countedQty === null ? 'border-warn' : 'border-border')}>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-semibold">{l.itemCode} — {l.itemName}</span>
                  <span className="block text-base text-muted-foreground">{l.lotNumber ? `Lote ${l.lotNumber} · ` : ''}{l.isRecount ? 'Recontagem · ' : ''}{l.countedQty === null ? 'Falta contar' : `Contado ${formatQuantity(l.countedQty)} ${l.unitCode}${l.countedByMe ? ' por você' : l.countedByName ? ` por ${l.countedByName}` : ''}`}</span>
                </span>
                {l.countedQty === null || l.countedByMe ? <span className="shrink-0 font-semibold text-primary">{l.countedQty === null ? 'Contar' : 'Corrigir'}</span> : null}
              </button>
            </li>
          ))}
        </ul>
      </div>
      {current ? <CountSheet key={current.id + current.version} view={view} line={current} onClose={() => setOpen(null)} /> : null}
    </>
  )
}
