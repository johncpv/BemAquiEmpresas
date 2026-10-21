import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { extendQuoteValidity, newQuoteRevision } from '@/api/generated/bemaqui'
import type { QuoteDetail } from '@/api/generated/model'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'

/** "Nova revisão", "Reabrir com nova revisão" e "Prorrogar validade" (COM-02 CA 1, 2 e 6). */
/** `action` abre o diálogo direto, vindo do quadro "Vencendo" do funil (COM-08 CA 6). */
export function RevisionActions({ quote, refresh, action }: { quote: QuoteDetail; refresh: () => Promise<void>; action?: 'prorrogar' | 'revisar' }) {
  const navigate = useNavigate()
  const [date, setDate] = useState(quote.validUntil ?? '')
  const [reviseOpen, setReviseOpen] = useState(action === 'revisar')
  const [extendOpen, setExtendOpen] = useState(action === 'prorrogar' && quote.status === 'Sent')
  const expired = quote.status === 'Expired'
  return (
    <>
      <ConfirmDialog open={reviseOpen} onOpenChange={setReviseOpen} verb={expired ? 'Reabrir com nova revisão' : 'Nova revisão'} done={expired ? 'Orçamento reaberto' : 'Revisão criada'}
        title={expired ? 'Reabrir com uma nova revisão?' : 'Criar uma nova revisão?'}
        description={`Nasce a revisão ${quote.revision + 1} em rascunho, com os mesmos itens, custos, preços e marcos. ${expired ? 'Esta continua expirada.' : 'Esta passa a substituída e fica só para consulta.'}`}
        onConfirm={async () => { const created = await newQuoteRevision(quote.id, { version: quote.version }); await navigate({ to: '/app/orcamentos/$id', params: { id: created.id } } as never) }}
        trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{expired ? 'Reabrir com nova revisão' : 'Nova revisão'}</button>} />
      {quote.status === 'Sent' ? (
        <ConfirmDialog open={extendOpen} onOpenChange={setExtendOpen} verb="Prorrogar validade" done="Validade prorrogada" title="Prorrogar a validade?" description="Muda só a data; para mudar preço ou itens, crie uma nova revisão."
          preconditions={[{ ok: !!date && date > (quote.validUntil ?? ''), text: 'Nova data depois da validade atual.' }]}
          onConfirm={async () => { await extendQuoteValidity(quote.id, { validUntil: date, version: quote.version }); await refresh() }}
          trigger={<button type="button" className="inline-flex items-center rounded-md border border-border px-3 py-2 text-sm font-semibold">Prorrogar validade</button>}>
          <label className="mt-3 block space-y-1 text-sm font-medium"><span className="inline-flex items-center">Nova validade<Dica nome="Prorrogar validade" texto="Muda só a data; para mudar preço ou itens, crie uma nova revisão." /></span><DateInput value={date} onChange={setDate} /></label>
        </ConfirmDialog>
      ) : null}
    </>
  )
}
