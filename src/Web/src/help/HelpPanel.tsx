import * as Dialog from '@radix-ui/react-dialog'
import { useState } from 'react'
import type { HelpDocument } from '@/api/generated/model'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { renderInline } from './inline'
import { Markdown } from './markdown'
import { searchHelp } from './resolve'

interface HelpPanelProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  docs?: readonly HelpDocument[]
  current?: HelpDocument
  status: 'loading' | 'error' | 'ready'
  retry: () => void
}

/** Painel de ajuda (FND-14 CA 4 e 5): à direita no desktop, tela cheia no celular; ajuda da tela, busca e índice. */
export function HelpPanel({ open, onOpenChange, docs = [], current, status, retry }: HelpPanelProps) {
  const [term, setTerm] = useState('')
  const [selected, setSelected] = useState<HelpDocument | null>(null)
  const shown = selected ?? current
  const hits = searchHelp(docs, term, current?.key)

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { onOpenChange(o); if (!o) { setSelected(null); setTerm('') } }}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/20" />
        <Dialog.Content aria-describedby={undefined} className="fixed inset-y-0 right-0 z-50 flex w-full flex-col border-l border-border bg-popover text-popover-foreground shadow-xl sm:w-[420px]">
          <div className="flex items-center justify-between border-b border-border px-4 py-3">
            <Dialog.Title className="text-base font-semibold">Ajuda</Dialog.Title>
            <Dialog.Close className="rounded-sm px-2 py-1 text-sm font-semibold">Fechar</Dialog.Close>
          </div>
          <div className="border-b border-border p-3">
            <input type="search" value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Buscar na ajuda" aria-label="Buscar na ajuda" className="w-full rounded-md border border-input bg-card px-3 py-2 text-sm" />
          </div>
          <div className="flex-1 space-y-5 overflow-y-auto p-4">
            {status === 'loading' ? <Skeleton className="h-40" /> : status === 'error' ? <ErrorState title="Não foi possível carregar a ajuda." onRetry={retry} /> : term ? (
              hits.length === 0 ? <p className="text-sm text-muted-foreground">Nada encontrado para "{term}". Tente outra palavra.</p> : (
                <ul aria-label="Resultados da busca" className="space-y-3">
                  {hits.map((h) => (
                    <li key={h.doc.key + h.section}>
                      <button type="button" onClick={() => { setSelected(h.doc); setTerm('') }} className="text-left">
                        <b className="block text-sm font-semibold">{h.doc.title} › {h.section}</b>
                        <span className="text-xs text-muted-foreground">…{renderInline(h.excerpt)}…</span>
                      </button>
                    </li>
                  ))}
                </ul>
              )
            ) : (
              <>
                {shown ? (
                  <article aria-label={shown.title} className="space-y-4">
                    <h2 className="text-lg font-bold">{shown.title}</h2>
                    {shown.sections.map((s) => (
                      <section key={s.title} className="space-y-1">
                        <h3 className="text-sm font-semibold">{s.title}</h3>
                        <Markdown text={s.body} />
                      </section>
                    ))}
                  </article>
                ) : null}
                <nav aria-label="Índice da ajuda" className="border-t border-border pt-4">
                  <h2 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">Todas as ajudas</h2>
                  <ul className="space-y-1">
                    {docs.map((d) => (
                      <li key={d.key}>
                        <button type="button" onClick={() => setSelected(d)} aria-current={d.key === shown?.key ? 'true' : undefined} className="text-sm text-primary aria-[current=true]:font-semibold">{d.title}</button>
                      </li>
                    ))}
                  </ul>
                </nav>
              </>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
