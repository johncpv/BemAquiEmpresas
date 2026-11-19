import { FloorButton, FloorSheet } from '@/components/shopfloor/kit'
import { Markdown } from '@/help/markdown'
import { helpSections } from '@/pages/shopfloor/device'
import cashHelp from '../../../../Ajuda/pdv-caixa.md?raw'
import offlineHelp from '../../../../Ajuda/pdv-sem-conexao.md?raw'
import registerHelp from '../../../../Ajuda/pdv-registrar-aparelho.md?raw'
import paymentHelp from '../../../../Ajuda/pdv-pagamento.md?raw'
import saleHelp from '../../../../Ajuda/pdv-venda.md?raw'

/** Título do documento, do campo `titulo:` do cabeçalho do Markdown. */
function helpTitle(markdown: string): string {
  return /^titulo:\s*(.+)$/m.exec(markdown)?.[1]?.trim() ?? 'Ajuda'
}

const DOCS = [cashHelp, saleHelp, paymentHelp, offlineHelp, registerHelp]

/**
 * Ajuda do /pdv, aberta só pelo botão "Ajuda" (padrão do `FloorHelp`: no caixa ela nunca abre sozinha no meio da venda). Os textos vêm de `Ajuda/pdv-caixa.md` (CX-01),
 * `Ajuda/pdv-venda.md`, `Ajuda/pdv-pagamento.md`, `Ajuda/pdv-sem-conexao.md` e `Ajuda/pdv-registrar-aparelho.md` embutidos no app, porque o caixa precisa da ajuda justamente sem rede e sem
 * sessão de usuário (a API de ajuda não responde aí).
 */
export function PdvHelp({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return (
    <FloorSheet open={open} onOpenChange={onOpenChange} title="Ajuda do caixa"
      actions={<FloorButton className="w-full" onClick={() => onOpenChange(false)}>Entendi</FloorButton>}>
      {DOCS.map((doc) => (
        <section key={helpTitle(doc)} className="space-y-3">
          <h2 className="font-display text-[22px] font-bold">{helpTitle(doc)}</h2>
          {helpSections(doc).map((s) => (
            <section key={s.title} className="space-y-1">
              <h3 className="text-[20px] font-bold">{s.title}</h3>
              <Markdown text={s.body} />
            </section>
          ))}
        </section>
      ))}
    </FloorSheet>
  )
}
