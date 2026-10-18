import type { AddressKind, PartyRoleKind, TaxRegime } from '@/api/generated/model'

/** Rótulos dos papéis, tipos de endereço e regimes (CAD-01a), na ordem em que aparecem na tela. */
export const ROLE_LABELS: Record<PartyRoleKind, string> = { Customer: 'Cliente', Supplier: 'Fornecedor', Installer: 'Instalador', Carrier: 'Transportadora' }
export const ROLE_OPTIONS = (Object.keys(ROLE_LABELS) as PartyRoleKind[]).map((value) => ({ value, label: ROLE_LABELS[value] }))

export const ADDRESS_LABELS: Record<AddressKind, string> = { Billing: 'Cobrança', Delivery: 'Entrega', Site: 'Obra' }

export const TAX_REGIME_LABELS: Record<TaxRegime, string> = {
  NotInformed: 'Não informado',
  SimplesNacional: 'Simples Nacional',
  Mei: 'MEI',
  LucroPresumido: 'Lucro Presumido',
  LucroReal: 'Lucro Real',
  Exempt: 'Isento / não contribuinte',
}

export const STATES = ['AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA', 'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO'] as const
