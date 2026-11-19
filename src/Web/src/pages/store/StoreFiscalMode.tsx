import { useState } from 'react'
import { setStoreNonFiscalMode, useGetNonFiscalTerms } from '@/api/generated/bemaqui'
import type { StoreView } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useHelpTab } from '@/help/help-context'
import { formatInstantDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'

const MODE_DICA = 'O documento que o cliente recebe na venda do balcão. Enquanto a NFC-e não chega ao sistema, a loja vende só no modo "só cupom não fiscal", que o Proprietário liga com um aceite.'

/**
 * Loja → Lojas e caixas → Documento fiscal (IMP-01 CA 10, 11 e 13): o modo da loja e o aceite. Só o Proprietário vê os botões (o servidor
 * recusa os outros com 403). Ligar abre o texto do aceite, versionado; o aceite fica no histórico da loja.
 */
export function StoreFiscalMode({ store, onChanged }: { store: StoreView; onChanged: () => void }) {
  const { isOwner } = useNavigation()
  const [open, setOpen] = useState<'on' | 'off' | null>(null)
  const nonFiscal = store.fiscalMode === 'NonFiscal'
  const needsNew = !!store.needsNewAcceptance

  return (
    <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border p-3 text-sm">
      <div className="min-w-0 space-y-0.5">
        <p className="flex flex-wrap items-center gap-2 font-semibold">
          Documento fiscal<Dica nome="Documento fiscal" texto={MODE_DICA} />
          {nonFiscal ? <StatusChip tone="ok">Só cupom não fiscal</StatusChip> : <StatusChip tone="warn">Não vende: falta o documento fiscal</StatusChip>}
          {needsNew ? <StatusChip tone="warn">Aceite o texto novo</StatusChip> : null}
        </p>
        {nonFiscal && store.nonFiscalAcceptedAt ? (
          <p className="text-muted-foreground tabular-nums">Aceito por {store.nonFiscalAcceptedByName ?? 'usuário removido'} em {formatInstantDate(store.nonFiscalAcceptedAt)} (texto versão {store.nonFiscalTermsVersion}).</p>
        ) : !nonFiscal ? <p className="text-muted-foreground">A NFC-e chega numa próxima versão. Até lá, os caixas desta loja só vendem com o modo "só cupom não fiscal".</p> : null}
      </div>
      {isOwner ? (
        <div className="flex flex-wrap gap-2">
          {!nonFiscal || needsNew ? (
            <button type="button" onClick={() => setOpen('on')} className="rounded-md bg-primary px-3 py-2 font-semibold text-primary-foreground">
              {needsNew ? 'Ler e aceitar o texto novo' : 'Ligar só cupom não fiscal'}
            </button>
          ) : null}
          {nonFiscal ? <button type="button" onClick={() => setOpen('off')} className="rounded-md border border-border px-3 py-2 font-semibold">Desligar</button> : null}
        </div>
      ) : <p className="text-muted-foreground">Só o Proprietário muda.</p>}
      {open === 'on' ? <AcceptDialog store={store} onClose={() => setOpen(null)} onDone={onChanged} /> : null}
      {open === 'off' ? (
        <ConfirmDialog open onOpenChange={(o) => { if (!o) setOpen(null) }} verb="Desligar" done="Desligado" title={`Desligar o modo não fiscal em ${store.name}?`}
          description="Os caixas desta loja param de vender na próxima sincronização, até a loja ter um documento fiscal."
          onConfirm={async () => { await setStoreNonFiscalMode(store.id, { enabled: false, accepted: false, termsVersion: null, version: store.version }); onChanged() }} />
      ) : null}
    </div>
  )
}

function AcceptDialog({ store, onClose, onDone }: { store: StoreView; onClose: () => void; onDone: () => void }) {
  useHelpTab('documento-fiscal')
  const terms = useGetNonFiscalTerms()
  const [accepted, setAccepted] = useState(false)
  return (
    <ConfirmDialog open onOpenChange={(o) => { if (!o) onClose() }} verb="Ligar" done="Ligado" size="lg"
      title={terms.data?.title ?? 'Vender só com cupom não fiscal'}
      description={`Loja ${store.name}. Leia o texto inteiro antes de aceitar.`}
      preconditions={[{ ok: !!terms.data, text: 'Carregando o texto do aceite…' }, { ok: accepted, text: 'Marque "Li e aceito" para ligar.' }]}
      onConfirm={async () => {
        await setStoreNonFiscalMode(store.id, { enabled: true, accepted: true, termsVersion: terms.data?.version ?? null, version: store.version })
        onDone()
      }}>
      {terms.isError ? <p role="alert" className="text-crit-ink">Não foi possível carregar o texto do aceite. Feche e tente de novo.</p> : null}
      {terms.data ? (
        <div className="space-y-3">
          <div className="max-h-64 space-y-2 overflow-y-auto rounded-md border border-border bg-muted p-3 text-sm">
            {terms.data.text.split('\n\n').map((p) => <p key={p}>{p}</p>)}
          </div>
          <p className="text-xs text-muted-foreground">Versão {terms.data.version} do texto.</p>
          <label className="flex items-start gap-2 text-sm font-semibold">
            <input type="checkbox" checked={accepted} onChange={(e) => setAccepted(e.target.checked)} className="mt-0.5" />
            Li e aceito. A obrigação de emitir documento fiscal é da minha empresa.
          </label>
        </div>
      ) : null}
    </ConfirmDialog>
  )
}
