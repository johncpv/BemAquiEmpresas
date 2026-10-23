import { Link, useNavigate } from '@tanstack/react-router'
import { LogIn, UserRound } from 'lucide-react'
import { useEffect, useState } from 'react'
import { loginWithPin, useListPinUsers } from '@/api/generated/bemaqui'
import type { PinUser } from '@/api/generated/model'
import { ApiError, OFFLINE_CODE } from '@/api/http'
import { getAccessToken, setAccessToken, setSessionDeadline } from '@/api/session'
import { FloorButton, FloorDica, FloorHeader, FloorMessages, FloorSheet, FloorSkeleton, FloorState, NumericKeypad } from '@/components/shopfloor/kit'

import { registerFloorServiceWorker } from '@/pwa/register'
import { applyTheme } from '@/theme/theme'
import entryHelp from '../../../../../Ajuda/fab-entrar.md?raw'
import { Markdown } from '@/help/markdown'
import { entryHelpSeen, FAB_ENTRY_TEXT, helpSections, isPinComplete, markEntryHelpSeen, NOT_RELEASED, PIN_LENGTH, themeOf } from './device'
import { OfflineState } from './OfflineGate'

const FULL_LOGIN = { to: '/entrar', search: { volta: '/fab', aparelho: '1' } } as const

/**
 * Entrada do tablet da fábrica (/fab/entrar, PRD-03b CA 13): sem sessão; só com o aparelho registrado (sem o cookie a lista é 404 e a
 * tela diz "Este aparelho não está registrado."). Toque no nome, PIN no teclado grande, e a sessão por PIN abre. O último operador do
 * aparelho vem primeiro. A ajuda abre sozinha na primeira visita (padrão 1) e vem no próprio app, porque aqui ainda não há sessão.
 */
export function FabEntryPage() {
  const navigate = useNavigate()
  const users = useListPinUsers({ query: { retry: false, staleTime: 0 } })
  const [selected, setSelected] = useState<PinUser | null>(null)
  const [help, setHelp] = useState(() => !entryHelpSeen())
  const device = users.data?.device

  useEffect(() => { document.title = 'Entrar no app do operador · BemAqui Empresas' }, [])
  useEffect(() => registerFloorServiceWorker(), [])
  useEffect(() => { if (device) applyTheme(themeOf(device.theme)) }, [device])

  const closeHelp = (open: boolean) => {
    setHelp(open)
    if (!open) markEntryHelpSeen()
  }

  const error = users.error instanceof ApiError ? users.error : null
  const header = (
    <FloorHeader onHelp={() => setHelp(true)} title="Entrar" subtitle={device ? device.name : 'App do operador'} />
  )

  let content
  if (users.isPending) {
    content = <FloorSkeleton rows={2} />
  } else if (error?.code === OFFLINE_CODE) {
    content = <OfflineState onRetry={() => void users.refetch()} />
  } else if (error?.status === 404) {
    content = (
      <FloorState title={FAB_ENTRY_TEXT.notRegistered} message="Para registrar o tablet ou criar o seu PIN, entre uma vez com e-mail e senha. Essa entrada não fica salva no tablet."
        action={<Link to={FULL_LOGIN.to} search={FULL_LOGIN.search} data-floor-target="" className="inline-flex min-h-[56px] items-center gap-2 rounded-md bg-primary px-5 text-[18px] font-semibold text-primary-foreground"><LogIn aria-hidden="true" className="size-6" />Entrar com e-mail e senha</Link>} />
    )
  } else if (error?.code === 'module_not_released') {
    content = <FloorState title={NOT_RELEASED} message="O app do operador ainda não faz parte da versão desta empresa." />
  } else if (users.isError || !users.data) {
    content = <FloorState tone="error" title="Não foi possível carregar os nomes." message="Verifique a conexão do tablet e tente de novo." action={<FloorButton onClick={() => void users.refetch()}>Tentar de novo</FloorButton>} />
  } else {
    content = (
      <div className="space-y-4">
        <h1 className="flex items-center gap-1 text-[20px] font-bold">Toque no seu nome e digite o seu PIN<FloorDica nome="PIN" texto={FAB_ENTRY_TEXT.pinDica} /></h1>
        {users.data.users.length === 0 ? (
          <FloorState title={FAB_ENTRY_TEXT.noUsers} message="Entre com e-mail e senha e toque em Criar PIN para entrar mais rápido nas próximas vezes." />
        ) : (
          <ul aria-label="Quem tem PIN neste aparelho" className="grid grid-cols-1 gap-[12px] sm:grid-cols-2">
            {users.data.users.map((u) => (
              <li key={u.userId}>
                <FloorButton tone="secondary" className="w-full justify-start py-3 text-[20px]" onClick={() => setSelected(u)}>
                  <UserRound aria-hidden="true" className="size-7" />{u.displayName}
                </FloorButton>
              </li>
            ))}
          </ul>
        )}
        <div className="border-t border-border pt-4">
          <Link to={FULL_LOGIN.to} search={FULL_LOGIN.search} data-floor-target="" className="inline-flex min-h-[56px] items-center gap-2 rounded-md border-2 border-input bg-card px-5 text-[18px] font-semibold">
            <LogIn aria-hidden="true" className="size-6" />Entrar com e-mail e senha
          </Link>
        </div>
      </div>
    )
  }

  return (
    <main id="conteudo" className="min-h-dvh bg-background pb-8 text-[18px] text-foreground">
      {header}
      <FloorMessages />
      <div className="mx-auto max-w-3xl space-y-4 p-4">
        {getAccessToken() && !selected ? <p className="text-base text-muted-foreground"><Link to="/fab" className="font-semibold underline">Voltar para a fila</Link></p> : null}
        {content}
      </div>
      <PinSheet user={selected} onClose={() => setSelected(null)} onEntered={() => void navigate({ to: '/fab' })} onDeviceGone={() => { setSelected(null); void users.refetch() }} />
      <EntryHelp open={help} onOpenChange={closeHelp} />
    </main>
  )
}

/** Folha do PIN: teclado grande, visor com bolinhas, "Entrar" só com os 6 números. */
function PinSheet({ user, onClose, onEntered, onDeviceGone }: { user: PinUser | null; onClose: () => void; onEntered: () => void; onDeviceGone: () => void }) {
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const close = () => {
    setPin('')
    setError(null)
    onClose()
  }

  const enter = async () => {
    if (!user) return
    setBusy(true)
    setError(null)
    try {
      const session = await loginWithPin({ userId: user.userId, pin })
      setAccessToken(session.accessToken)
      setSessionDeadline(session.sessionExpiresAt)
      setPin('')
      onEntered()
    } catch (e) {
      setPin('')
      if (e instanceof ApiError && e.status === 404) {
        onDeviceGone()
        return
      }

      setError(e instanceof ApiError ? e.message : 'Não foi possível entrar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <FloorSheet open={!!user} onOpenChange={(o) => { if (!o) close() }} title={user ? `Olá, ${user.displayName}` : 'Entrar'} description="Digite o seu PIN de 6 números."
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={close} disabled={busy}>Voltar</FloorButton>
          <FloorButton className="flex-1" onClick={() => void enter()} disabled={busy || !isPinComplete(pin)}>{busy ? 'Entrando…' : 'Entrar'}</FloorButton>
        </div>
      )}>
      <NumericKeypad label="PIN" mask max={PIN_LENGTH} value={pin} onChange={(v) => { setPin(v); setError(null) }} />
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
    </FloorSheet>
  )
}

/** Ajuda da entrada, do arquivo Ajuda/fab-entrar.md embutido no app (sem sessão, a API de ajuda não responde). */
function EntryHelp({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <FloorSheet open={open} onOpenChange={onOpenChange} title="Ajuda: entrar no app do operador"
      actions={<FloorButton className="w-full" onClick={() => onOpenChange(false)}>Entendi</FloorButton>}>
      {helpSections(entryHelp).map((s) => (
        <section key={s.title} className="space-y-1">
          <h2 className="text-[20px] font-bold">{s.title}</h2>
          <Markdown text={s.body} />
        </section>
      ))}
    </FloorSheet>
  )
}