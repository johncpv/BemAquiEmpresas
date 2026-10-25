/** Campos do PUT /finance/parameters/dre por chave do parâmetro (FIN-08 CA 16). */
export const DRE_FIELDS = [
  { key: 'finance.dre_interest_income_account_id', field: 'interestIncomeAccountId' },
  { key: 'finance.dre_interest_expense_account_id', field: 'interestExpenseAccountId' },
  { key: 'finance.dre_discount_granted_account_id', field: 'discountGrantedAccountId' },
  { key: 'finance.dre_discount_received_account_id', field: 'discountReceivedAccountId' },
  { key: 'finance.dre_down_payment_as_advance', field: 'downPaymentAsAdvance' },
] as const

export { PENDING_LABEL } from './purchasing-parameters'

/** Alguma coisa mudou em relação ao que está salvo (habilita "Salvar contas do DRE"). */
export const dreChanged = (saved: Record<string, string | boolean>, draft: Record<string, string | boolean>) => DRE_FIELDS.some((f) => saved[f.key] !== draft[f.key])
