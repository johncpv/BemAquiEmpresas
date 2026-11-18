import * as Popover from '@radix-ui/react-popover'
import { Link } from '@tanstack/react-router'
import { Lock, PanelLeftClose, PanelLeftOpen } from 'lucide-react'
import { type ComponentPropsWithRef, type KeyboardEvent, useState } from 'react'
import { cn } from '@/lib/utils'
import type { NavArea } from '@/navigation/modules'
import { useNavigation } from '@/navigation/navigation-context'
import { AreaMenu } from './AreaMenu'
import { usePeopleExtras } from './people-extras'

/** Menu lateral recolhido (só os ícones das áreas), lembrado neste navegador: "1" recolhido, "0" aberto. */
export const COLLAPSED_KEY = 'bq.menu.recolhido'

/** Abaixo disto (tablet), sem escolha guardada, o menu começa recolhido para sobrar largura para a tela. */
const NARROW = '(max-width: 1023px)'

function remembered() {
  try {
    const stored = localStorage.getItem(COLLAPSED_KEY)
    if (stored === '1' || stored === '0') return stored === '1'
  } catch {
    // Sem armazenamento: vale o padrão pela largura.
  }
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(NARROW).matches
}

function remember(collapsed: boolean) {
  try {
    localStorage.setItem(COLLAPSED_KEY, collapsed ? '1' : '0')
  } catch {
    // Sem armazenamento (janela privada, bloqueio): a escolha só não fica lembrada.
  }
}

/**
 * Setas, Home e End movem o foco entre os itens do menu (links ou botões das áreas); Tab segue normal. O popover da área
 * recolhida fica num portal, mas o evento do React sobe pela árvore até aqui: tecla vinda de fora do menu (o AreaMenu já
 * tratou) é ignorada, senão o foco pularia para o primeiro ícone e o popover fecharia.
 */
function moveFocus(event: KeyboardEvent<HTMLElement>) {
  if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
  if (!event.currentTarget.contains(event.target as Node)) return
  const items = [...event.currentTarget.querySelectorAll<HTMLElement>('[data-menu-item]')]
  if (items.length === 0) return
  const index = items.indexOf(document.activeElement as HTMLElement)
  const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length
  items[next]?.focus()
  event.preventDefault()
}

const link =
  'relative flex items-center gap-2.5 rounded-[9px] py-[7px] pl-3 pr-2 text-[13.5px] font-medium text-rail-dim hover:bg-rail-hi hover:text-rail-ink focus-visible:outline-rail-ring aria-[current=page]:bg-rail-hi aria-[current=page]:text-rail-ink'

/** Traço âmbar do item ativo (o mesmo sinal do dock), na borda esquerda. */
const Mark = ({ className }: { className?: string }) => <span aria-hidden="true" className={cn('absolute rounded-[3px] bg-signal', className)} />

/**
 * Menu lateral do desktop (decisão do PO de 18/11/2026, adendo à ADR 0005 D8): as áreas como títulos e os módulos de cada uma
 * embaixo, todos abertos. "Recolher" deixa só os ícones das áreas; o clique abre os módulos num popover (o mesmo AreaMenu do
 * celular). Cadeado, módulos ocultos e rótulos por perfil vêm prontos do useNavigation. No celular continua o Dock.
 */
export function Sidebar({ activeAreaId, activeModuleCode, pathname }: { activeAreaId?: string; activeModuleCode?: string; pathname: string }) {
  const { areas, status, retry } = useNavigation()
  const [collapsed, setCollapsed] = useState(remembered)
  const toggle = () => {
    setCollapsed(!collapsed)
    remember(!collapsed)
  }
  const Toggle = collapsed ? PanelLeftOpen : PanelLeftClose

  return (
    <nav
      aria-label="Navegação principal"
      className={cn('sticky top-0 z-20 flex h-dvh shrink-0 flex-col bg-rail text-rail-ink', collapsed ? 'w-[68px]' : 'w-60')}
    >
      <div className="scrollbar-none flex-1 overflow-y-auto overscroll-contain p-2" onKeyDown={moveFocus}>
        {status === 'loading' ? (
          <div aria-hidden="true" className="space-y-2">
            {Array.from({ length: 7 }, (_, i) => <span key={i} className="block h-9 animate-pulse rounded-[9px] bg-rail-hi" />)}
          </div>
        ) : status === 'error' ? (
          <p role="alert" className="flex flex-col items-start gap-2 px-2 py-2 text-xs">
            Não foi possível carregar o menu.
            <button type="button" onClick={retry} className="font-semibold text-rail-ring">Tentar de novo</button>
          </p>
        ) : collapsed ? (
          <ul className="space-y-1">
            {areas.map((area) => (
              <li key={area.id}>
                <CollapsedArea area={area} active={area.id === activeAreaId} />
              </li>
            ))}
          </ul>
        ) : (
          areas.map((area) => (
            <AreaSection key={area.id} area={area} active={area.id === activeAreaId} activeModuleCode={activeModuleCode} pathname={pathname} />
          ))
        )}
      </div>
      <div className="border-t border-rail-hi p-2">
        <button
          type="button"
          onClick={toggle}
          aria-expanded={!collapsed}
          aria-label={collapsed ? 'Expandir o menu' : undefined}
          title={collapsed ? 'Expandir o menu' : undefined}
          className={cn(link, 'w-full', collapsed && 'justify-center pl-2')}
        >
          <Toggle aria-hidden="true" className="size-[18px]" strokeWidth={1.7} />
          {collapsed ? null : 'Recolher'}
        </button>
      </div>
    </nav>
  )
}

/** Uma área aberta: o título (com o traço quando é a área atual) e os módulos dela. */
function AreaSection({ area, active, activeModuleCode, pathname }: { area: NavArea; active: boolean; activeModuleCode?: string; pathname: string }) {
  const extras = usePeopleExtras(area)
  const titleId = `menu-area-${area.id}`
  return (
    <div className="pb-2.5">
      <p id={titleId} className={cn('relative flex items-center gap-2 px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wide', active ? 'text-rail-ink' : 'text-rail-dim')}>
        {active ? <Mark className="left-0 top-[9px] h-3 w-[3px]" /> : null}
        <area.icon aria-hidden="true" className="size-3.5" strokeWidth={1.8} />
        {area.name}
      </p>
      <ul aria-labelledby={titleId} className="space-y-px">
        {area.modules.map((m) => {
          const current = m.code === activeModuleCode
          return (
            <li key={m.code}>
              <Link
                to={m.path as never}
                data-menu-item=""
                aria-current={current ? 'page' : undefined}
                aria-label={m.locked ? `${m.name}, não liberado` : m.count ? `${m.name}, ${m.count} pendentes` : undefined}
                className={link}
              >
                {current ? <Mark className="left-0 top-2 bottom-2 w-[3px]" /> : null}
                <m.icon aria-hidden="true" className="size-[18px] shrink-0" strokeWidth={1.7} />
                <span className={cn('min-w-0 flex-1 truncate', m.locked && 'opacity-70')}>{m.name}</span>
                {m.locked ? <Lock aria-hidden="true" className="size-3.5 shrink-0" /> : null}
                {m.count ? (
                  <span aria-hidden="true" className="rounded-full bg-signal px-1.5 text-[10px] font-extrabold text-signal-foreground tabular-nums">{m.count}</span>
                ) : null}
              </Link>
            </li>
          )
        })}
        {extras.map((e) => {
          const current = pathname === e.to || pathname.startsWith(`${e.to}/`)
          return (
            <li key={e.to}>
              <Link to={e.to} data-menu-item="" aria-current={current ? 'page' : undefined} className={link}>
                {current ? <Mark className="left-0 top-2 bottom-2 w-[3px]" /> : null}
                <e.icon aria-hidden="true" className="size-[18px] shrink-0" strokeWidth={1.7} />
                <span className="min-w-0 flex-1 truncate">{e.name}</span>
              </Link>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

/** Botão da área no menu recolhido: repassa ref e props, porque é o Trigger (asChild) do Popover. */
function AreaIcon({ area, active, ...props }: { area: NavArea; active: boolean } & ComponentPropsWithRef<'button'>) {
  const [tip, setTip] = useState<number | null>(null)
  const show = (el: HTMLElement) => {
    const box = el.getBoundingClientRect()
    setTip(box.top + box.height / 2)
  }
  return (
    <>
      <button
        type="button"
        {...props}
        data-menu-item=""
        data-active={active}
        aria-label={area.name}
        aria-current={active ? 'page' : undefined}
        onMouseEnter={(e) => show(e.currentTarget)}
        onMouseLeave={() => setTip(null)}
        onFocus={(e) => {
          props.onFocus?.(e)
          show(e.currentTarget)
        }}
        onBlur={(e) => {
          props.onBlur?.(e)
          setTip(null)
        }}
        className="relative grid h-11 w-full place-items-center rounded-[11px] text-rail-dim hover:bg-rail-hi hover:text-rail-ink focus-visible:outline-rail-ring data-[active=true]:bg-rail-hi data-[active=true]:text-rail-ink"
      >
        {active ? <Mark className="left-0 top-3 bottom-3 w-[3px]" /> : null}
        <area.icon aria-hidden="true" className="size-[21px]" strokeWidth={1.7} />
      </button>
      {/* Dica com o nome: fixa, para não ser cortada pela rolagem do menu. É só visual (o nome já está no aria-label). */}
      {tip !== null && props['aria-expanded'] !== true ? (
        <span aria-hidden="true" style={{ top: tip }} className="pointer-events-none fixed left-[76px] z-40 -translate-y-1/2 whitespace-nowrap rounded-md bg-rail px-2 py-1 text-xs font-semibold text-rail-ink shadow-lg">
          {area.name}
        </span>
      ) : null}
    </>
  )
}

/** Área no menu recolhido: o ícone abre os módulos num popover à direita (o mesmo AreaMenu). Esc fecha e o foco volta ao ícone. */
function CollapsedArea({ area, active }: { area: NavArea; active: boolean }) {
  const [open, setOpen] = useState(false)
  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <AreaIcon area={area} active={active} />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content side="right" align="start" sideOffset={10} collisionPadding={8} className="z-40 w-64 rounded-md border border-border bg-popover p-2 text-popover-foreground shadow-lg" aria-label={area.name}>
          <AreaMenu area={area} onNavigate={() => setOpen(false)} />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
