import { listFinanceCustomers, listFinanceSuppliers } from '@/api/generated/bemaqui'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import type { RowsColumn } from '@/components/form/rows-editor'
import type { OpeningLineDraft } from './opening'

export type OpeningKind = 'receivables' | 'payables'

interface Option { id: string; label: string }

const searchCustomers = async (term: string): Promise<ComboOption[]> => (await listFinanceCustomers({ q: term })).map((c) => ({ id: c.id, label: c.name, hint: c.document ?? undefined }))
const searchSuppliers = async (term: string): Promise<ComboOption[]> => (await listFinanceSuppliers({ q: term })).map((s) => ({ id: s.id, label: s.name, hint: s.document ?? undefined }))

/** Colunas do lote (CA 5 e 6): a pagar não tem emissão (vale a data de corte) e a conta vazia usa a padrão do servidor. */
export function openingColumns(kind: OpeningKind, accounts: Option[], centers: Option[], disabled: boolean): RowsColumn<OpeningLineDraft>[] {
  const receivables = kind === 'receivables'
  const party = receivables ? 'Cliente' : 'Fornecedor'
  const account = receivables ? 'Conta de receita' : 'Conta de despesa'
  const columns: (RowsColumn<OpeningLineDraft> | null)[] = [
    { label: party, width: 'minmax(10rem, 1.4fr)', render: (r, i, set) => (
      <Combobox aria-label={`${party} da linha ${i + 1}`} queryKey={[receivables ? 'finance-customers' : 'finance-suppliers']} search={receivables ? searchCustomers : searchSuppliers} value={r.party?.id ?? null} selected={r.party} disabled={disabled} placeholder="Busque pelo nome" onChange={(p) => set({ party: p })} />
    ) },
    { label: 'Descrição', width: 'minmax(8rem, 1.2fr)', render: (r, i, set) => <input aria-label={`Descrição da linha ${i + 1}`} value={r.description} maxLength={200} disabled={disabled} onChange={(e) => set({ description: e.target.value })} className={inputClass} /> },
    { label: 'Documento de origem', width: '7rem', render: (r, i, set) => <input aria-label={`Documento de origem da linha ${i + 1}`} value={r.reference} maxLength={40} disabled={disabled} placeholder="Ex.: FA2409-0012" onChange={(e) => set({ reference: e.target.value })} className={inputClass} /> },
    receivables ? { label: 'Emissão', width: '7.5rem', render: (r, i, set) => <DateInput aria-label={`Emissão da linha ${i + 1}`} value={r.issueDate} disabled={disabled} onChange={(issueDate) => set({ issueDate })} /> } : null,
    { label: 'Vencimento', width: '7.5rem', render: (r, i, set) => <DateInput aria-label={`Vencimento da linha ${i + 1}`} value={r.dueDate} disabled={disabled} onChange={(dueDate) => set({ dueDate })} /> },
    { label: receivables ? 'Saldo em aberto' : 'Saldo líquido', width: '8rem', render: (r, i, set) => <MoneyInput aria-label={`Saldo da linha ${i + 1}`} value={r.balance} disabled={disabled} onChange={(balance) => set({ balance })} /> },
    { label: account, width: 'minmax(8rem, 1fr)', render: (r, i, set) => (
      <select aria-label={`${account} da linha ${i + 1}`} value={r.accountId} disabled={disabled} onChange={(e) => set({ accountId: e.target.value })} className={inputClass}>
        {receivables ? null : <option value="">Última do fornecedor, ou 3.9</option>}
        {accounts.map((a) => <option key={a.id} value={a.id}>{a.label}</option>)}
      </select>
    ) },
    { label: 'Centro de custo', width: 'minmax(7rem, 0.8fr)', render: (r, i, set) => (
      <select aria-label={`Centro de custo da linha ${i + 1}`} value={r.costCenterId} disabled={disabled} onChange={(e) => set({ costCenterId: e.target.value })} className={inputClass}>
        <option value="">Sem centro</option>
        {centers.map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
      </select>
    ) },
  ]
  return columns.filter((c): c is RowsColumn<OpeningLineDraft> => c !== null)
}
