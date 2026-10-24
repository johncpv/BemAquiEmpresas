import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deleteApprovalRule, getListApprovalRulesQueryKey, useListApprovalRules, useSimulateApproval } from '@/api/generated/bemaqui'
import type { ApprovalRuleView } from '@/api/generated/model'
import { MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { cn } from '@/lib/utils'
import { FinanceTabs } from '../FinanceTabs'
import { toNumber } from '../receivables/receivables'
import { ApprovalRulePanel } from './ApprovalRulePanel'

const range = (r: ApprovalRuleView) => (r.maxAmount != null ? <><Money value={r.minAmount} /> a <Money value={r.maxAmount} /></> : <>a partir de <Money value={r.minAmount} /></>)

/** Tipos de documento do motor de alçadas (FIN-03b; CMP-03a, ADR 0024 §4), na URL (?tipo=pedido). */
const DOCUMENT_TYPES = [
  { id: 'PaymentOrder', slug: 'ordem', label: 'Ordem de pagamento', noun: 'ordem de pagamento', short: 'OP', permission: 'Aprovar em Caixa e pagamentos' },
  { id: 'PurchaseOrder', slug: 'pedido', label: 'Pedido de compra', noun: 'pedido de compra', short: 'pedido', permission: 'Aprovar em Compras' },
] as const

/** Financeiro → Alçadas (FIN-03b; CMP-03a): quem aprova cada ordem de pagamento e cada pedido de compra; só o Proprietário edita; simulador. */
export function ApprovalRulesPage() {
  const { tipo } = useSearch({ strict: false }) as { tipo?: string }
  const navigate = useNavigate()
  const type = DOCUMENT_TYPES.find((t) => t.slug === tipo) ?? DOCUMENT_TYPES[0]
  const query = useListApprovalRules({ documentType: type.id })
  const client = useQueryClient()
  // Só o Proprietário altera (o servidor confere); approve é reservado a ele por padrão (ADR 0008).
  const isOwner = useCan('finance.approve')
  const [editing, setEditing] = useState<ApprovalRuleView | undefined>()
  const [open, setOpen] = useState(false)
  const [amount, setAmount] = useState<string | null>(null)
  const simulation = useSimulateApproval({ amount: toNumber(amount), documentType: type.id }, { query: { enabled: toNumber(amount) > 0 } })
  const refresh = () => client.invalidateQueries({ queryKey: getListApprovalRulesQueryKey({ documentType: type.id }) })
  const rules = query.data ?? []
  return (
    <>
      <PageHeading title="Financeiro: Alçadas">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Quem aprova cada ordem de pagamento e cada pedido de compra.</p>
          {isOwner ? <button type="button" onClick={() => { setEditing(undefined); setOpen(true) }} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova regra de alçada</button> : null}
        </div>
      </PageHeading>
      <FinanceTabs />
      <div role="group" aria-label="Tipo de documento" className="mb-4 flex flex-wrap gap-2">
        {DOCUMENT_TYPES.map((t) => (
          <button key={t.id} type="button" aria-pressed={t.id === type.id}
            onClick={() => { setAmount(null); void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, tipo: t.slug === 'ordem' ? undefined : t.slug }) } as never) }}
            className={cn('rounded-md border px-3 py-1.5 text-sm font-semibold', t.id === type.id ? 'border-primary bg-primary-soft text-foreground' : 'border-border bg-card text-muted-foreground')}>{t.label}</button>
        ))}
      </div>
      <div className="space-y-6">
        {query.isPending ? <Skeleton className="h-40" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : rules.length === 0 ? <EmptyState title="Nenhuma regra de alçada." description={`Sem regra, nenhum ${type.noun} pode ser enviado para aprovação. Crie uma regra.`} /> : (
          <>
            {rules.length === 1 && rules[0].isDefault ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Hoje o Proprietário aprova tudo. Crie faixas para delegar quando houver outro aprovador.</p> : null}
            <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm" aria-label={`Regras de alçada: ${type.label}`}>
              {rules.map((r) => (
                <li key={r.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
                  <span className="flex flex-wrap items-center gap-2"><b>Nível {r.level}</b> {range(r)}{r.accountLabel ? <span className="text-muted-foreground">· conta {r.accountLabel}</span> : null}{r.isDefault ? <Tag>Padrão</Tag> : null}</span>
                  <span className="flex items-center gap-2">{r.approverName}
                    {isOwner ? <button type="button" onClick={() => { setEditing(r); setOpen(true) }} className="text-xs font-semibold text-primary">Editar</button> : null}
                    {isOwner ? <ConfirmDialog destructive verb="Excluir regra" done="Regra excluída" title="Excluir esta regra de alçada?" description={`Os documentos já enviados mantêm a alçada do envio (${type.noun}).`}
                      onConfirm={async () => { await deleteApprovalRule(r.id); await refresh() }} trigger={<button type="button" className="text-xs font-semibold text-crit-ink">Excluir</button>} /> : null}
                  </span>
                </li>
              ))}
            </ul>
          </>
        )}
        <section aria-label="Simulador" className="space-y-2 rounded-md border border-border bg-card p-4 text-sm">
          <label className="flex flex-wrap items-center gap-2 font-medium">Quem aprovaria {type.id === 'PurchaseOrder' ? 'um pedido' : 'uma OP'} de <span className="w-40"><MoneyInput aria-label={type.id === 'PurchaseOrder' ? 'Valor do pedido simulado' : 'Valor da OP simulada'} value={amount} onChange={setAmount} /></span>?</label>
          {simulation.data ? (
            <ul className="space-y-1">
              {simulation.data.levels.map((l) => <li key={l.level}>Nível {l.level}: {l.approverName}</li>)}
              <li className="text-xs text-muted-foreground">Valor de referência <Money value={simulation.data.referenceAmount} /></li>
            </ul>
          ) : simulation.isError ? <p className="text-crit-ink">Nenhuma regra cobre esse valor.</p> : null}
        </section>
      </div>
      {open ? <ApprovalRulePanel key={editing?.id ?? 'new'} rule={editing} documentType={type.id} open={open} onOpenChange={setOpen} onSaved={refresh} /> : null}
    </>
  )
}
