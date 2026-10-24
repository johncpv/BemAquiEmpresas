import { useState } from 'react'
import { createApprovalRule, updateApprovalRule, useListAccounts, useListApprovalApprovers } from '@/api/generated/bemaqui'
import type { ApprovalRuleView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { toNumber } from '../receivables/receivables'

/** Painel da regra de alçada (FIN-03b CA 2 a 5; CMP-03a): do tipo escolhido na tela; faixa, conta opcional com teto, aprovador (com a permissão do tipo) e nível. */
export function ApprovalRulePanel({ rule, documentType = 'PaymentOrder', open, onOpenChange, onSaved }: { rule?: ApprovalRuleView; documentType?: 'PaymentOrder' | 'PurchaseOrder'; open: boolean; onOpenChange: (o: boolean) => void; onSaved: () => Promise<void> }) {
  const approvers = useListApprovalApprovers({ documentType }, { query: { enabled: open } })
  const accounts = useListAccounts()
  const [min, setMin] = useState<string | null>(rule ? String(rule.minAmount) : '0')
  const [max, setMax] = useState<string | null>(rule?.maxAmount != null ? String(rule.maxAmount) : null)
  const [account, setAccount] = useState(rule?.accountId ?? '')
  const [approver, setApprover] = useState(rule?.approverGroupId ? `g:${rule.approverGroupId}` : rule?.approverUserId ? `u:${rule.approverUserId}` : '')
  const [level, setLevel] = useState(String(rule?.level ?? 1))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const expense = (accounts.data ?? []).filter((a) => a.type === 'Expense' && a.acceptsEntries && a.isActive)
  const save = async () => {
    const [kind, id] = approver.split(':')
    const body = { minAmount: toNumber(min), maxAmount: max ? toNumber(max) : null, accountId: account || null, approverGroupId: kind === 'g' ? id : null, approverUserId: kind === 'u' ? id : null, level: Number(level), documentType }
    try {
      await (rule ? updateApprovalRule(rule.id, body) : createApprovalRule(body))
      notifySuccess('Regra de alçada salva')
      await onSaved()
      onOpenChange(false)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a regra.', fields: {} })
      return false
    }
  }

  const field = (k: string) => error?.fields[k]?.[0]
  return (
    <SidePanel open={open} onOpenChange={onOpenChange} title={`${rule ? 'Editar regra de alçada' : 'Nova regra de alçada'}: ${documentType === 'PurchaseOrder' ? 'pedido de compra' : 'ordem de pagamento'}`}
      footer={<button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar regra de alçada</button>}>
      <div className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Nível" dica="Ordem da aprovação. O documento passa pelo nível 1 e, se houver regra no nível 2 para o valor, por ele também." error={field('level')}>
          {(p) => <select {...p} value={level} onChange={(e) => setLevel(e.target.value)} className={inputClass}><option value="1">1</option><option value="2">2</option><option value="3">3</option></select>}
        </Field>
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="A partir de" error={field('minAmount')}>{(p) => <MoneyInput {...p} value={min} onChange={setMin} />}</Field>
          <Field label="Até (teto)" hint="Vazio = sem teto." error={field('maxAmount')}>{(p) => <MoneyInput {...p} value={max} onChange={setMax} />}</Field>
        </div>
        <Field label="Conta (opcional)" dica="Regra por conta vale só quando o documento é todo dessa conta (na OP, todos os títulos; no pedido, a conta dos títulos previstos) e o valor está dentro do teto. Ela é mais específica e vence a faixa." error={field('accountId')}>
          {(p) => <select {...p} value={account} onChange={(e) => setAccount(e.target.value)} className={inputClass}><option value="">Qualquer conta</option>{expense.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>}
        </Field>
        <Field label="Aprovador" required error={field('approverGroupId') ?? field('approverUserId')}>
          {(p) => (
            <select {...p} value={approver} onChange={(e) => setApprover(e.target.value)} className={inputClass}>
              <option value="">Escolha</option>
              {(approvers.data ?? []).map((a) => <option key={a.id} value={`${a.isGroup ? 'g' : 'u'}:${a.id}`}>{a.isGroup ? `Grupo ${a.name}` : a.name}</option>)}
            </select>
          )}
        </Field>
      </div>
    </SidePanel>
  )
}
