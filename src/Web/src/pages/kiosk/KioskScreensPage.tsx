import * as Dialog from '@radix-ui/react-dialog'
import { useQueryClient } from '@tanstack/react-query'
import { Copy, MonitorPlay, Plus, X } from 'lucide-react'
import { useState } from 'react'
import { getListKioskScreensQueryKey, regenerateKioskScreenLink, revokeKioskScreen, useListKioskScreens } from '@/api/generated/bemaqui'
import type { KioskScreenLink, KioskScreenRow } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { KioskScreenPanel } from './KioskScreenPanel'
import { awaitingTooLong, LINK_HINT, revokeImpact, seenText, STATUS, viewsText } from './kiosk-screens'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold'

/**
 * Telas dedicadas → Painel da TV (/app/painel-tv, PNL-01a CA 2 a 4, 6, 14 e 20): as telas com o último acesso e a situação; "Nova tela"
 * e "Editar" com kiosk.create e production.view; "Gerar novo link" (mostrado uma vez) e "Revogar" com kiosk.delete. A 400 px, cartões.
 */
export function KioskScreensPage() {
  const client = useQueryClient()
  const list = useListKioskScreens()
  const canCreate = useCan('kiosk.create')
  const canViewProduction = useCan('production.view')
  const canWrite = canCreate && canViewProduction // quem não vê a produção não a publica numa TV (ADR 0030 §4)
  const canRevoke = useCan('kiosk.delete')
  const [panel, setPanel] = useState<{ screen: KioskScreenRow | null } | null>(null)
  const [link, setLink] = useState<{ value: string; name: string; created: boolean } | null>(null)
  const [next, setNext] = useState<{ value: string; name: string } | null>(null)
  const reload = () => client.invalidateQueries({ queryKey: getListKioskScreensQueryKey() })

  const created = (result: KioskScreenLink) => {
    setPanel(null)
    void reload()
    setLink({ value: result.link, name: result.screen.name, created: true })
    setNext({ value: result.link, name: result.screen.name })
  }

  const newLink = async (screen: KioskScreenRow) => {
    const result = await regenerateKioskScreenLink(screen.id, { version: screen.version })
    await reload()
    setLink({ value: result.link, name: screen.name, created: false })
  }

  const revoke = async (screen: KioskScreenRow) => {
    await revokeKioskScreen(screen.id, { version: screen.version })
    await reload()
  }

  return (
    <>
      <PageHeading title="Painel da TV">
        <p className="mt-2 max-w-3xl text-muted-foreground">Cada TV da fábrica tem um link próprio, que abre o painel sem login e pode ser desligado a qualquer momento.</p>
      </PageHeading>
      <div className="space-y-4">
        {next ? (
          <NextSteps steps={[{ label: 'Copiar link', icon: <Copy aria-hidden="true" className="size-3.5" />, onSelect: () => void copy(next.value) }]} onDismiss={() => setNext(null)} />
        ) : null}
        {canWrite ? (
          <div className="flex justify-end">
            <button type="button" onClick={() => setPanel({ screen: null })} className="inline-flex min-h-11 items-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
              <Plus aria-hidden="true" className="size-4" /> Nova tela
            </button>
          </div>
        ) : null}
        {list.isPending ? <Skeleton className="h-40" /> : list.isError ? <ErrorState onRetry={() => list.refetch()} /> : list.data.screens.length === 0 ? (
          <EmptyState title="Nenhuma tela de TV cadastrada."
            description={canWrite ? 'Crie a primeira tela, copie o link e abra-o no navegador da TV.' : 'Peça a quem cuida da produção para criar a tela da TV.'}
            action={canWrite ? <button type="button" onClick={() => setPanel({ screen: null })} className={button}><Plus aria-hidden="true" className="size-4" /> Nova tela</button> : undefined} />
        ) : (
          <ul aria-label="Telas de TV" className="grid gap-3">
            {list.data.screens.map((s) => (
              <ScreenCard key={s.id} screen={s} canWrite={canWrite} canRevoke={canRevoke}
                onEdit={() => setPanel({ screen: s })} onNewLink={() => newLink(s)} onRevoke={() => revoke(s)} onReload={reload} />
            ))}
          </ul>
        )}
      </div>
      {panel && list.data ? (
        <KioskScreenPanel screen={panel.screen} lines={list.data.lines} views={list.data.views} onClose={() => setPanel(null)} onCreated={created}
          onSaved={() => { setPanel(null); void reload() }} />
      ) : null}
      {link ? <LinkDialog link={link} onClose={() => setLink(null)} /> : null}
    </>
  )
}

function ScreenCard({ screen: s, canWrite, canRevoke, onEdit, onNewLink, onRevoke, onReload }: {
  screen: KioskScreenRow
  canWrite: boolean
  canRevoke: boolean
  onEdit: () => void
  onNewLink: () => Promise<unknown>
  onRevoke: () => Promise<unknown>
  onReload: () => Promise<unknown>
}) {
  const status = STATUS[s.status] ?? { label: s.status, tone: 'neutral' as const }
  const active = s.status !== 'Revoked'
  return (
    <li className="rounded-md border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 space-y-1">
          <p className="flex flex-wrap items-center gap-2 font-semibold">
            <MonitorPlay aria-hidden="true" className="size-4 text-muted-foreground" />
            {s.name}
            <StatusChip tone={status.tone}>{status.label}</StatusChip>
            {s.needsNewLink ? <StatusChip tone="warn">Precisa de novo link</StatusChip> : null}
          </p>
          <p className="text-sm text-muted-foreground">
            {s.line ?? 'Todas as linhas'} · {viewsText(s.views)} · <span className="tabular-nums">{s.rotationSeconds} s</span>
          </p>
          <p className="text-sm text-muted-foreground tabular-nums">
            {seenText(s.lastSeenAt)} · {s.activeSessions === 1 ? '1 sessão ativa' : `${s.activeSessions} sessões ativas`} · link {s.tokenPrefix}…
          </p>
          <p className="text-xs text-muted-foreground tabular-nums">
            Criada por {s.createdByName} em {formatInstantDate(s.createdAt)}
            {s.allowedNetworks.length > 0 ? ` · redes ${s.allowedNetworks.join(', ')}` : ' · qualquer rede'}
            {s.revokedAt ? ` · revogada por ${s.revokedByName ?? 'um usuário'} em ${formatInstantDate(s.revokedAt)}` : ''}
          </p>
          {awaitingTooLong(s) ? <p className="text-sm font-medium text-info-ink">Abra o link na TV: a tela ainda não foi conectada.</p> : null}
        </div>
        {active ? (
          <div className="flex flex-wrap gap-2">
            {canWrite ? <button type="button" onClick={onEdit} className={button}>Editar</button> : null}
            {canWrite ? (
              <span className="inline-flex items-center">
                <ConfirmDialog verb="Gerar novo link" done="Novo link gerado" title={`Gerar novo link para ${s.name}?`}
                  description="O link anterior deixa de valer e a TV conectada sai do painel até abrir o link novo."
                  onConfirm={onNewLink} onReload={onReload}
                  trigger={<button type="button" className={s.needsNewLink ? button + ' border-primary text-primary' : button}>Gerar novo link</button>} />
                <Dica nome="Gerar novo link" texto="Para conectar outra vez (TV nova ou navegador limpo). O link vale para uma conexão só e derruba a sessão anterior da tela." />
              </span>
            ) : null}
            {canRevoke ? (
              <span className="inline-flex items-center">
                <ConfirmDialog verb="Revogar tela" done="Tela revogada" destructive title={`Revogar a tela ${s.name}?`}
                  description="A tela deixa de mostrar o painel. Não dá para desfazer."
                  impact={<p className="font-semibold">{revokeImpact(s.name)}</p>}
                  onConfirm={onRevoke} onReload={onReload}
                  trigger={<button type="button" className={button}>Revogar</button>} />
                <Dica nome="Revogar" texto="Desliga a TV na hora: a conexão aberta cai e o link e a sessão deixam de valer. O Proprietário é avisado." />
              </span>
            ) : null}
          </div>
        ) : null}
      </div>
    </li>
  )
}

async function copy(value: string) {
  try {
    await navigator.clipboard.writeText(value)
    notifySuccess('Link copiado')
  } catch {
    notifySuccess('Selecione o link e copie')
  }
}

/** O link aparece uma única vez (CA 3 e 6): copiar agora; depois, só gerando um novo. */
function LinkDialog({ link, onClose }: { link: { value: string; name: string; created: boolean }; onClose: () => void }) {
  return (
    <Dialog.Root open onOpenChange={(o) => !o && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-50 w-[min(560px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2 space-y-4 rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg">
          <div className="flex items-start justify-between gap-3">
            <Dialog.Title className="text-base font-semibold">{link.created ? `Link da tela ${link.name}` : `Novo link da tela ${link.name}`}</Dialog.Title>
            <Dialog.Close aria-label="Fechar" className="grid size-8 place-items-center rounded-md hover:bg-muted"><X aria-hidden="true" className="size-4" /></Dialog.Close>
          </div>
          <Dialog.Description className="text-sm text-muted-foreground">{LINK_HINT}</Dialog.Description>
          <input aria-label="Link da TV" readOnly value={link.value} onFocus={(e) => e.currentTarget.select()}
            className="w-full rounded-md border border-input bg-card px-3 py-2 font-mono text-sm" />
          <div className="flex flex-wrap justify-end gap-2">
            <button type="button" onClick={onClose} className={button}>Fechar</button>
            <button type="button" onClick={() => void copy(link.value)} className="inline-flex min-h-11 items-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
              <Copy aria-hidden="true" className="size-4" /> Copiar link
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
