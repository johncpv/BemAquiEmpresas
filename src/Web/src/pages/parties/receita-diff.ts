import type { CnpjData, PartyDetail } from '@/api/generated/model'
import { phoneMask } from '@/lib/masks'

export interface ReceitaRow { key: 'legalName' | 'tradeName' | 'email' | 'phone' | 'address'; label: string; current: string; receita: string }

type AddressLike = { street?: string | null; number?: string | null; district?: string | null; city?: string | null; state?: string | null; zipCode?: string | null }

const addressText = (a: AddressLike | null | undefined) =>
  a ? [a.street, a.number, a.district, a.city && a.state ? `${a.city}/${a.state}` : a.city, a.zipCode].filter(Boolean).join(', ') : ''

/** Endereço da Receita com tudo o que o cadastro exige (CEP, logradouro, número, bairro, cidade e UF). */
export const completeAddress = (a: CnpjData['address']) => !!(a?.zipCode && a.street && a.number && a.district && a.city && a.state)

/** Linhas que diferem entre o cadastro e a Receita (CAD-01b CA 8); o endereço só entra se a Receita trouxe um completo. */
export function differences(party: PartyDetail, data: CnpjData): ReceitaRow[] {
  const billing = party.addresses.find((a) => a.kind === 'Billing' && a.isDefault)
  const rows: ReceitaRow[] = [
    { key: 'legalName', label: 'Razão social', current: party.legalName, receita: data.legalName },
    { key: 'tradeName', label: 'Nome fantasia', current: party.tradeName ?? '', receita: data.tradeName ?? '' },
    { key: 'email', label: 'E-mail', current: party.email ?? '', receita: data.email ?? '' },
    { key: 'phone', label: 'Telefone', current: party.phone ? phoneMask.format(party.phone) : '', receita: data.phone ? phoneMask.format(data.phone) : '' },
    ...(completeAddress(data.address) ? [{ key: 'address' as const, label: 'Endereço de cobrança', current: addressText(billing), receita: addressText(data.address) }] : []),
  ]
  return rows.filter((r) => r.receita && r.receita !== r.current)
}
