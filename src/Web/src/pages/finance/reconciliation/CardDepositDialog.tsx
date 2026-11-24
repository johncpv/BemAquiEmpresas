import { useState } from 'react'
import { listCardReceivablesToSettle, settleCardsFromLine } from '@/api/generated/bemaqui'
import type { CardReceivableCandidate, ReconLine } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { cardDeposit, CARD_DEPOSIT_DICA } from './reconciliation'

const secondary = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

/**
 * CX-03 CA 8: "Baixar recebíveis de cartão" a partir do depósito da maquininha. A adquirente deposita um valor só para vários dias, bandeiras e
 * formas: escolha os recebíveis (já vêm marcados os que vencem até a data da linha, da mesma maquininha, até fechar o valor); cada um é baixado
 * pelo líquido previsto e a diferença fica apontada no último.
 */
export function CardDepositDialog({ accountId, line, onDone }: { accountId: string; line: ReconLine; onDone: () => Promise<void> }) {
  const [items, setItems] = useState<CardReceivableCandidate[] | null>(null)
  const [chosen, setChosen] = useState<string[]>([])
  const [backdate, setBackdate] = useState('')
  const [loadError, setLoadError] = useState<string | null>(null)
  const totals = cardDeposit(items ?? [], chosen, line.amount)
  const open = async () => {
    setItems(null)
    setChosen([])
    setBackdate('')
    setLoadError(null)
    try {
      const list = await listCardReceivablesToSettle(accountId, { until: line.date })
      setItems(list)
      setChosen(cardDeposit(list, [], line.amount).suggested)
    } catch {
      setLoadError('Não foi possível carregar os recebíveis de cartão. Feche e tente de novo.')
    }
  }

  const toggle = (id: string) => setChosen((c) => (c.includes(id) ? c.filter((x) => x !== id) : [...c, id]))
  return (
    <ConfirmDialog size="lg" verb="Baixar recebíveis" done="Recebíveis baixados" title="Depósito da maquininha"
      description="Data e conta vêm da linha do banco. Cada recebível é baixado pelo líquido previsto (o bruto menos a taxa)."
      onOpen={() => void open()}
      preconditions={[
        { ok: !line.blocked, text: line.blockedReason ?? 'Linha bloqueada para você.' },
        { ok: totals.count > 0 && totals.count <= 50, text: 'Escolha de 1 a 50 recebíveis.' },
        { ok: totals.sameParty, text: 'Escolha recebíveis de uma maquininha só.' },
        { ok: totals.expected >= line.amount, text: 'O depósito passa do líquido previsto dos escolhidos: inclua os que faltam.' },
      ]}
      impact={(
        <dl className="grid grid-cols-2 gap-1">
          <dt>Líquido previsto</dt><dd className="text-right"><Money value={totals.expected} /></dd>
          <dt className="font-semibold">Depósito ({formatDate(line.date)})</dt><dd className="text-right font-semibold"><Money value={line.amount} /></dd>
          <dt>Diferença<Dica nome="Diferença do depósito" texto={CARD_DEPOSIT_DICA} /></dt><dd className="text-right"><Money value={totals.difference} /></dd>
        </dl>
      )}
      onConfirm={async () => {
        const refs = (items ?? []).filter((i) => chosen.includes(i.id)).map((i) => ({ id: i.id, version: i.version }))
        await settleCardsFromLine(accountId, line.id, { receivables: refs, lineVersion: line.version, backdateReason: backdate.trim() || null })
        await onDone()
      }}
      trigger={<button type="button" disabled={line.blocked} className={secondary}>Baixar recebíveis de cartão</button>}>
      {loadError ? <p role="alert" className="mt-3 text-sm text-crit-ink">{loadError}</p> : items === null ? <p className="mt-3 text-sm text-muted-foreground">Carregando os recebíveis…</p> : items.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">Nenhum recebível de cartão em aberto até {formatDate(line.date)}. Confira o fechamento do caixa ou as vendas da loja virtual.</p>
      ) : (
        <ul className="mt-3 max-h-72 divide-y divide-border overflow-y-auto rounded-md border border-border text-sm">
          {items.map((i) => (
            <li key={i.id}>
              <label className="flex min-h-11 items-center gap-2 p-2">
                <input type="checkbox" checked={chosen.includes(i.id)} onChange={() => toggle(i.id)} className="size-4" />
                <span className="flex-1">{i.number} · {i.description}{i.dueDate ? ` · crédito em ${formatDate(i.dueDate)}` : ''}</span>
                <Money value={i.balance} />
              </label>
            </li>
          ))}
        </ul>
      )}
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo da data (data antiga ou período fechado)<input value={backdate} maxLength={200} onChange={(e) => setBackdate(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}
