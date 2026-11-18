import { RouterProvider } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { FloorButton, FloorSkeleton, FloorState } from '@/components/shopfloor/kit'
import { SECOND_TAB_MESSAGE } from '@/pdv/local-store/writer-lock'
import { SUPPORTED_BROWSERS, UNSUPPORTED_TITLE } from '@/pdv/platform'
import type { Boot } from './boot'
import { Centered } from './Centered'
import { PdvShell } from './PdvShell'
import { pdvRouter } from './router'

/** Navegador fora do PP-09 (CA 4): a mensagem e a lista dos suportados. */
export function UnsupportedBrowser() {
  return (
    <Centered>
      <section className="space-y-4 rounded-md border border-border bg-card p-5">
        <h1 className="font-display text-2xl font-bold">{UNSUPPORTED_TITLE}</h1>
        <p className="text-[18px]">Abra o caixa em um destes:</p>
        <ul aria-label="Navegadores suportados" className="list-disc space-y-1 pl-6 text-[18px]">{SUPPORTED_BROWSERS.map((b) => <li key={b}>{b}</li>)}</ul>
        <p className="text-muted-foreground">iPhone, iPad, Mac e outros navegadores ficam fora do caixa: neles não dá para garantir que as vendas feitas sem internet fiquem guardadas.</p>
      </section>
    </Centered>
  )
}

type View = { kind: 'booting' } | Exclude<Boot, { kind: 'open' }> | (Extract<Boot, { kind: 'open' }> & { ready: boolean })

/** O app do caixa: navegador, aba dona do lock e armazenamento, nessa ordem; depois a casca (faixa de estado, ajuda) e a tela do PDV-01. */
export function PdvApp({ boot }: { boot: Promise<Boot> }) {
  const [view, setView] = useState<View>({ kind: 'booting' })

  useEffect(() => {
    let alive = true
    void boot.then((b) => {
      if (!alive) return
      if (b.kind !== 'open') { setView(b); return }
      setView({ ...b, ready: b.lock.owned })
      void b.owned.then(() => { if (alive) setView({ ...b, ready: true }) })
    })
    return () => { alive = false }
  }, [boot])

  if (view.kind === 'booting') return <Centered><FloorSkeleton rows={3} /></Centered>
  if (view.kind === 'unsupported') return <UnsupportedBrowser />
  if (view.kind === 'storage-error') {
    return (
      <Centered>
        <FloorState tone="error" title="Não foi possível abrir os dados deste caixa."
          message="Feche as outras janelas do navegador e tente de novo. Os dados guardados neste aparelho não foram apagados."
          action={<FloorButton onClick={() => window.location.reload()}>Tentar de novo</FloorButton>} />
      </Centered>
    )
  }

  if (!view.ready) {
    return (
      <Centered>
        <FloorState title={SECOND_TAB_MESSAGE} message="Use a outra janela para vender. Se você fechar a outra, esta assume sozinha." />
      </Centered>
    )
  }

  return (
    <PdvShell store={view.store}>
      <RouterProvider router={pdvRouter as never} />
    </PdvShell>
  )
}
