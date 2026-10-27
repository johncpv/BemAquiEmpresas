import * as Dialog from '@radix-ui/react-dialog'
import { ShieldAlert } from 'lucide-react'
import { type ReactNode, useEffect, useId, useRef, useState } from 'react'
import { ApiError } from '@/api/http'
import { cn } from '@/lib/utils'
import { DialogMessage } from './dialog-message'
import { notifySuccess } from './messages'
import { remainingText } from './remaining-text'

/** Pré-condição de uma ação (FND-09e CA 5): o que falta aparece antes de confirmar, com o caminho para corrigir. */
export interface Precondition {
  ok: boolean
  text: string
  /** `keepOpen`: a correção acontece no próprio diálogo (ex.: "Usar os valores padrão"). */
  fix?: { label: string; onSelect: () => void; keepOpen?: boolean }
}

/** Item do impacto em lista (D18, padrão 7): o que a ação afeta, com o detalhe numa segunda linha. */
export interface ImpactItem {
  key: string
  primary: ReactNode
  secondary?: ReactNode
}

/** Item recusado do lote, com o motivo. */
export interface RefusedItem {
  key: string
  primary: ReactNode
  reason: string
}

/** Impacto em lista (ações em lote): afetados, recusados com o motivo e o total. */
export interface ImpactList {
  items: ImpactItem[]
  refused?: RefusedItem[]
  total?: ReactNode
  /** Rótulo dos afetados (ex.: "Vão receber boleto"). */
  label?: string
}

/** Confirmação digitada (LGPD-01b): o botão só habilita depois de digitar exatamente `word`. */
export interface TypedConfirmation {
  word: string
  label?: string
}

/** Contagem de prazo (FIS-03b): "Restam 3 h 12 min para cancelar", atualizada a cada minuto; zerado, bloqueia. */
export interface Deadline {
  until: Date | string
  /** Fim da frase (ex.: "para cancelar"). */
  purpose: string
}

function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!active) return
    const timer = window.setInterval(() => setNow(Date.now()), 60_000)
    return () => window.clearInterval(timer)
  }, [active])
  return now
}

interface ConfirmDialogProps {
  /** Verbo da ação, repetido no botão de confirmar (ex.: "Desativar"). */
  verb: string
  /** Particípio da mensagem de sucesso (ex.: "Desativado"), na região do topo (ADR 0021). */
  done: string
  title: string
  description?: ReactNode
  /** Só para "Estornar…" e "Cancelar…" (padrão 1 da Fase 2). */
  destructive?: boolean
  onConfirm: () => Promise<unknown>
  /** Sem trigger, o diálogo é aberto de fora por open/onOpenChange. */
  trigger?: ReactNode
  open?: boolean
  onOpenChange?: (open: boolean) => void
  /**
   * Ação alternativa quando o servidor recusa (FND-09d CA 17), ex.: excluir recusado por uso oferece "Inativar".
   * `when` filtra pelo código do erro; sem ele, a alternativa aparece em qualquer recusa.
   */
  recovery?: { verb: string; done?: string; onSelect: () => Promise<unknown>; when?: (code: string | undefined) => boolean }
  /** Campos do diálogo (ex.: vigência e motivo do reajuste), entre a descrição e o erro. */
  children?: ReactNode
  /** Chamado ao abrir: o chamador reinicia os campos. */
  onOpen?: () => void
  /** "lg": cerca de 640 px e tela cheia a 400 px, para ações com efeito financeiro (FND-09e CA 4). */
  size?: 'md' | 'lg'
  /** Impacto da ação (valores, conta, títulos afetados, saldo antes e depois), acima do botão. */
  impact?: ReactNode
  /** O que falta para confirmar; com alguma pendente, o botão fica desabilitado e diz por quê (CA 5). */
  preconditions?: Precondition[]
  /** Erro com campos (422): o chamador põe cada erro no seu campo (applyServerErrors) e devolve true (CA 6). */
  onFieldErrors?: (error: ApiError) => boolean
  /** 409 concurrency_conflict: "Recarregar" busca a versão atual (ADR 0019). */
  onReload?: () => Promise<unknown>
  /** Quem aprova ou paga sem o 2FA configurado (regra da FND-06c CA 12): estado único, sem confirmar (CA 7). */
  twoFactorRequired?: boolean
  /** Impacto em lista (D18): itens afetados e recusados com o motivo, e o total. */
  impactList?: ImpactList
  /** Confirmação digitada (D18): a palavra ou o número a digitar. */
  typed?: TypedConfirmation
  /** Contagem de prazo (D18): bloqueia ao zerar. */
  deadline?: Deadline
}

const TWO_FACTOR_SETUP = '/app/conta/seguranca'

const contentClass = (size: 'md' | 'lg', centered: boolean) => cn(
  'fixed left-1/2 z-50 flex max-h-[calc(100dvh-32px)] -translate-x-1/2 flex-col overflow-hidden rounded-md border border-border bg-popover text-popover-foreground shadow-lg',
  size === 'lg'
    ? 'top-1/2 w-[min(640px,calc(100vw-32px))] -translate-y-1/2 max-sm:inset-0 max-sm:max-h-none max-sm:w-full max-sm:translate-x-0 max-sm:translate-y-0 max-sm:rounded-none'
    : cn('w-[min(420px,calc(100vw-32px))]', centered ? 'top-1/2 -translate-y-1/2' : 'top-1/3'),
)

/** Confirmação com o mesmo verbo da ação (FND-09c CA 11); erro do servidor fica no diálogo, com o motivo e o foco. */
export function ConfirmDialog(props: ConfirmDialogProps) {
  const { verb, done, title, description, destructive, onConfirm, trigger, recovery, children, onOpen, open: controlled, onOpenChange, size = 'md', impact, preconditions = [], onFieldErrors, onReload, twoFactorRequired, impactList, typed, deadline } = props
  const [own, setOwn] = useState(false)
  const open = controlled ?? own
  const setOpen = (o: boolean) => { setOwn(o); onOpenChange?.(o) }
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<{ message: string; code?: string } | null>(null)
  const alertRef = useRef<HTMLDivElement>(null)
  const pendingId = useId()
  const [typedValue, setTypedValue] = useState('')
  const typedId = useId()
  const now = useNow(open && !!deadline)
  const remaining = deadline ? new Date(deadline.until).getTime() - now : Infinity
  const expired = remaining <= 0
  const pending = preconditions.filter((p) => !p.ok)
  const typedMissing = !!typed && typedValue.trim() !== typed.word
  const blocked = pending.length > 0 || !!twoFactorRequired || typedMissing || expired

  useEffect(() => {
    if (error) alertRef.current?.focus()
  }, [error])

  const fail = (e: unknown) => {
    if (e instanceof ApiError && Object.keys(e.fields).length > 0 && onFieldErrors?.(e)) {
      setError(null)
      return
    }

    setError({ message: e instanceof ApiError || e instanceof DialogMessage ? e.message : 'Não foi possível concluir. Tente de novo.', code: e instanceof ApiError ? e.code : undefined })
  }

  const run = async (action: () => Promise<unknown>, message?: string) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      setOpen(false)
      if (message) notifySuccess(message)
    } catch (e) {
      fail(e)
    } finally {
      setBusy(false)
    }
  }

  const reload = async () => {
    if (!onReload) return
    setBusy(true)
    try {
      await onReload()
      setError(null)
    } finally {
      setBusy(false)
    }
  }

  const button = 'rounded-md border border-border bg-card px-3 py-1.5 font-semibold text-foreground'
  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); setError(null); setTypedValue(''); if (o) onOpen?.() }}>
      {trigger ? <Dialog.Trigger asChild>{trigger}</Dialog.Trigger> : null}
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content role="alertdialog" className={contentClass(size, !!children || !!impact)}>
          <div className="flex-1 overflow-y-auto p-5">
            <Dialog.Title className="text-base font-semibold">{title}</Dialog.Title>
            {description ? <Dialog.Description className="mt-1 text-sm text-muted-foreground">{description}</Dialog.Description> : null}
            {twoFactorRequired ? (
              <div className="mt-4 flex items-start gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
                <ShieldAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                <p className="flex-1">
                  Para {verb.toLowerCase()}, ative a verificação em duas etapas.{' '}
                  <a href={TWO_FACTOR_SETUP} target="_blank" rel="noopener" className="font-semibold underline">Ativar agora</a>
                </p>
              </div>
            ) : null}
            {children ? <div className="mt-4 space-y-3">{children}</div> : null}
            {deadline ? (
              <p role="timer" aria-live="polite" className={cn('mt-4 rounded-md border px-3 py-2 text-sm font-semibold tabular-nums', expired ? 'border-crit-soft bg-crit-soft text-crit-ink' : 'border-warn-soft bg-warn-soft text-warn-ink')}>
                {expired ? `O prazo ${deadline.purpose} terminou.` : `Restam ${remainingText(remaining)} ${deadline.purpose}`}
              </p>
            ) : null}
            {impactList ? <ImpactListView list={impactList} /> : null}
            {impact ? <div role="group" aria-label="Impacto" className="mt-4 rounded-md border border-border bg-muted px-3 py-2 text-sm tabular-nums">{impact}</div> : null}
            {typed ? (
              <div className="mt-4 space-y-1 text-sm">
                <label htmlFor={typedId} className="font-semibold">{typed.label ?? `Para confirmar, digite ${typed.word}`}</label>
                <input id={typedId} value={typedValue} onChange={(e) => setTypedValue(e.target.value)} autoComplete="off" spellCheck={false} className="min-h-11 w-full rounded-md border border-input bg-background px-3 py-2" />
              </div>
            ) : null}
            {pending.length > 0 ? (
              <div id={pendingId} className="mt-4 space-y-1 text-sm">
                <p className="font-semibold">Antes de {verb.toLowerCase()}:</p>
                <ul className="list-disc space-y-1 pl-5">
                  {pending.map((p) => (
                    <li key={p.text}>
                      {p.text}
                      {p.fix ? <> <button type="button" onClick={() => { if (!p.fix?.keepOpen) setOpen(false); p.fix?.onSelect() }} className="font-semibold text-primary underline">{p.fix.label}</button></> : null}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            {error ? (
              <div ref={alertRef} tabIndex={-1} role="alert" className="mt-4 space-y-2 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink outline-none">
                <p>{error.message}</p>
                {(error.code === 'concurrency_conflict' && onReload) || (recovery && (recovery.when?.(error.code) ?? true)) ? (
                  <div className="flex flex-wrap gap-2">
                    {error.code === 'concurrency_conflict' && onReload ? <button type="button" disabled={busy} onClick={() => void reload()} className={button}>Recarregar</button> : null}
                    {recovery && (recovery.when?.(error.code) ?? true) ? <button type="button" disabled={busy} onClick={() => void run(recovery.onSelect, recovery.done)} className={button}>{recovery.verb}</button> : null}
                  </div>
                ) : null}
              </div>
            ) : null}
          </div>
          <div className="flex justify-end gap-2 border-t border-border px-5 py-3">
            <Dialog.Close className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Dialog.Close>
            <button type="button" disabled={busy || blocked} aria-describedby={pending.length > 0 ? pendingId : undefined} onClick={() => { if (!blocked) void run(onConfirm, done) }}
              className={cn('rounded-md px-3 py-2 text-sm font-semibold disabled:opacity-60', destructive ? 'bg-crit text-critical-foreground' : 'bg-primary text-primary-foreground')}>
              {verb}
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

/** Impacto em lista (D18, padrão 7): afetados, recusados com o motivo e o total, numa lista legível por leitor de tela. */
function ImpactListView({ list }: { list: ImpactList }) {
  const refused = list.refused ?? []
  return (
    <div className="mt-4 space-y-3 text-sm">
      <section aria-label={list.label ?? 'Itens afetados'} className="rounded-md border border-border bg-muted">
        <p className="border-b border-border px-3 py-2 font-semibold">{list.label ?? 'Itens afetados'} ({list.items.length})</p>
        <ul className="max-h-64 divide-y divide-border overflow-y-auto">
          {list.items.map((item) => (
            <li key={item.key} className="px-3 py-2 tabular-nums">
              <span className="block font-medium">{item.primary}</span>
              {item.secondary ? <span className="block text-muted-foreground">{item.secondary}</span> : null}
            </li>
          ))}
        </ul>
        {list.total ? <p className="border-t border-border px-3 py-2 font-semibold tabular-nums">Total: {list.total}</p> : null}
      </section>
      {refused.length > 0 ? (
        <section aria-label="Recusados" className="rounded-md border border-warn-soft bg-warn-soft text-warn-ink">
          <p className="border-b border-warn-soft px-3 py-2 font-semibold">Ficam de fora ({refused.length})</p>
          <ul className="max-h-48 divide-y divide-warn-soft overflow-y-auto">
            {refused.map((item) => (
              <li key={item.key} className="px-3 py-2">
                <span className="block font-medium">{item.primary}</span>
                <span className="block">{item.reason}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  )
}
