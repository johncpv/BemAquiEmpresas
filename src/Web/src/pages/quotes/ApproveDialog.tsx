import { useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { approveQuote, useGetQuoteApproval, useGetQuoteApprovalCosts } from '@/api/generated/bemaqui'
import type { ApprovalResult, QuoteDetail } from '@/api/generated/model'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { CONTRACT_DICAS } from '../contracts/contracts'
import { formatDate } from '@/lib/format'
import { today } from '../finance/receivables/receivables'

const pct = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

/**
 * "Aprovar e gerar contrato" (COM-04a CA 1 a 3, 11): pré-condições com o caminho para corrigir, o que vai ser criado,
 * data de assinatura e conta de receita; destaques de preço só para quem vê custo. Não bloqueia pela margem.
 */
export function ApproveDialog({ quote, open, onOpenChange, onApproved }: { quote: QuoteDetail; open?: boolean; onOpenChange?: (open: boolean) => void; onApproved: (result: ApprovalResult) => void }) {
  const navigate = useNavigate()
  const canCost = useCan('quotes.view_cost')
  const [shown, setShown] = useState(open ?? false)
  const isOpen = open ?? shown
  const preview = useGetQuoteApproval(quote.id, { query: { enabled: isOpen } })
  const costs = useGetQuoteApprovalCosts(quote.id, { query: { enabled: isOpen && canCost } })
  const [signedAt, setSignedAt] = useState(today())
  const [account, setAccount] = useState('')
  const idempotency = useIdempotencyKey()
  const p = preview.data
  const revenueAccountId = account || p?.suggestedRevenueAccountId || ''
  const fix = (tab: string | null | undefined) => tab ? { label: 'Corrigir', onSelect: () => { setOpen(false); void navigate({ to: '/app/orcamentos/$id', params: { id: quote.id }, search: { aba: tab } } as never) } } : undefined
  const setOpen = (v: boolean) => { setShown(v); onOpenChange?.(v) }
  const preconditions = p
    ? [
        ...p.preconditions.map((c) => ({ ok: c.ok, text: c.text, fix: fix(c.fixTab) })),
        { ok: !!signedAt && signedAt <= today(), text: 'A data de assinatura não pode ser no futuro.' },
        { ok: !!revenueAccountId, text: 'Escolha a conta de receita das parcelas.' },
      ]
    : [{ ok: false, text: preview.isError ? 'Não foi possível carregar as pré-condições. Feche e abra de novo.' : 'Conferindo as pré-condições…' }]
  const promised = p?.deliveryDays && signedAt ? addDays(signedAt, p.deliveryDays) : null

  return (
    <ConfirmDialog size="lg" open={isOpen} onOpenChange={setOpen} verb="Aprovar e gerar contrato" done="Aprovado: contrato e projeto criados. Gerando as parcelas…"
      title={`Aprovar ${quote.number}${quote.revision > 0 ? ` rev. ${quote.revision}` : ''} e gerar o contrato?`}
      description="O orçamento vira contrato congelado e nasce o projeto com os itens e o orçado por natureza. O financeiro gera as parcelas a receber pelos marcos."
      preconditions={preconditions}
      onOpen={() => { setSignedAt(today()); setAccount('') }}
      impact={p ? (
        <div aria-live="polite" className="space-y-1 text-sm">
          <p>Contrato de <Money value={p.totalValue} className="font-semibold" /> · projeto "{p.projectName}" com {p.items} {p.items === 1 ? 'item' : 'itens'}</p>
          <p>Data prometida: {promised ? formatDate(promised) : 'sem prazo de entrega no orçamento'}</p>
          <ul className="list-disc pl-5">
            {p.milestones.map((m) => <li key={m.description + m.when}>{m.description}: {pct(m.percent)}% · <Money value={m.amount} /> {m.when}</li>)}
          </ul>
        </div>
      ) : null}
      onConfirm={async () => {
        const result = await approveQuote(quote.id, { signedAt, revenueAccountId, version: quote.version }, idempotency.options)
        idempotency.renew()
        onApproved(result)
      }}
      trigger={open === undefined ? <button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Aprovar e gerar contrato</button> : undefined}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium"><span className="inline-flex items-center">Data de assinatura<Dica nome="Data de assinatura" texto={CONTRACT_DICAS.assinatura} /></span>
          <DateInput value={signedAt} onChange={setSignedAt} />
        </label>
        <label className="space-y-1 text-sm font-medium"><span className="inline-flex items-center">Conta de receita das parcelas<Dica nome="Conta de receita das parcelas" texto={CONTRACT_DICAS.receita} /></span>
          <select value={revenueAccountId} onChange={(e) => setAccount(e.target.value)} className={inputClass}>
            {(p?.revenueAccounts ?? []).map((a) => <option key={a.id} value={a.id}>{a.label}</option>)}
          </select>
        </label>
      </div>
      {canCost && costs.data ? (
        <div className="mt-3 space-y-1 text-sm" aria-label="Atenção ao preço">
          <p>Margem do orçamento: <b className={(costs.data.marginPct ?? 0) < 0 ? 'text-crit-ink tabular-nums' : 'tabular-nums'}>{costs.data.marginPct === null || costs.data.marginPct === undefined ? '—' : `${pct(costs.data.marginPct)}%`}</b></p>
          {costs.data.alerts.map((a) => (
            <p key={a.sequence} className="flex flex-wrap items-center gap-2">{a.sequence}. {a.description}
              {a.belowCost ? <StatusChip tone="crit">Preço abaixo do custo</StatusChip> : null}
              {a.priceOverridden ? <Tag>Digitado à mão</Tag> : null}
            </p>
          ))}
        </div>
      ) : null}
    </ConfirmDialog>
  )
}

function addDays(iso: string, days: number) {
  const d = new Date(iso + 'T00:00:00Z')
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}
