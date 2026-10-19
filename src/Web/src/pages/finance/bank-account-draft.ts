import type { BankAccountDetail } from '@/api/generated/model'

export interface Draft { name: string; kind: 'Bank' | 'Cash'; bankCode: string | null; bankName: string | null; agency: string; account: string; balance: string | null; date: string; isDefault: boolean }

/** Hoje em São Paulo, no formato da data de negócio. */
export const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

export const toDraft = (a: BankAccountDetail | null): Draft => ({
  name: a?.name ?? '', kind: a?.kind ?? 'Bank', bankCode: a?.bankCode ?? null, bankName: a?.bankName ?? null, agency: a?.agency ?? '', account: a?.account ?? '',
  balance: a?.openingBalance == null ? null : a.openingBalance.toFixed(2), date: a?.openingDate ?? '', isDefault: a?.isDefault ?? false,
})
