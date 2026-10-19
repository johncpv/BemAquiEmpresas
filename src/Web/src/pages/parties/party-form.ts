import type { CreatePartyRequest, PartyKind, PartyRoleKind } from '@/api/generated/model'
import { isValidCpf, isValidCnpj } from '@/lib/masks'
import { z } from '@/lib/zod'

const optionalEmail = z.union([z.literal(''), z.email('E-mail inválido.')])
const optionalPhone = z.string().refine((v) => v === '' || v.length === 10 || v.length === 11, 'Telefone com DDD: 10 ou 11 dígitos.')

/**
 * Novo terceiro (CAD-01a CA 2 a 9). O endereço de cobrança e o contato principal são opcionais, mas, começados,
 * pedem os campos que faltam. As mensagens são as mesmas da API.
 */
export const newPartySchema = z
  .object({
    document: z.string(),
    kind: z.enum(['PF', 'PJ']),
    legalName: z.string().trim().min(2, 'Informe o nome ou a razão social.').max(200, 'Use até 200 caracteres.'),
    tradeName: z.string().max(200, 'Use até 200 caracteres.'),
    roles: z.array(z.enum(['Customer', 'Supplier', 'Installer', 'Carrier'])).min(1, 'O terceiro precisa de ao menos um papel.'),
    email: optionalEmail,
    phone: optionalPhone,
    zipCode: z.string(),
    street: z.string(),
    number: z.string(),
    complement: z.string(),
    district: z.string(),
    city: z.string(),
    state: z.string(),
    ibgeCode: z.string(),
    contactName: z.string(),
    contactEmail: optionalEmail,
    contactPhone: optionalPhone,
  })
  .superRefine((v, ctx) => {
    if (v.document) {
      const ok = v.kind === 'PF' ? v.document.length === 11 && isValidCpf(v.document) : v.document.length === 14 && isValidCnpj(v.document)
      if (!ok) ctx.addIssue({ code: 'custom', path: ['document'], message: v.kind === 'PF' ? 'CPF inválido.' : 'CNPJ inválido.' })
    }
    if (hasAddress(v)) {
      if (v.zipCode.length !== 8) ctx.addIssue({ code: 'custom', path: ['zipCode'], message: 'CEP com 8 dígitos.' })
      for (const [field, message] of [['street', 'Informe o logradouro.'], ['number', 'Informe o número (ou S/N).'], ['district', 'Informe o bairro.'], ['city', 'Informe a cidade.'], ['state', 'Escolha a UF.']] as const) {
        if (!v[field].trim()) ctx.addIssue({ code: 'custom', path: [field], message })
      }
    }
    if ((v.contactEmail || v.contactPhone) && !v.contactName.trim()) ctx.addIssue({ code: 'custom', path: ['contactName'], message: 'Informe o nome do contato.' })
  })

export type NewPartyValues = z.input<typeof newPartySchema>

const hasAddress = (v: NewPartyValues) => [v.zipCode, v.street, v.number, v.district, v.city].some((x) => x.trim() !== '')

export const emptyParty = (role: PartyRoleKind = 'Customer'): NewPartyValues => ({
  document: '', kind: 'PJ', legalName: '', tradeName: '', roles: [role], email: '', phone: '',
  zipCode: '', street: '', number: '', complement: '', district: '', city: '', state: '', ibgeCode: '',
  contactName: '', contactEmail: '', contactPhone: '',
})

export function toCreateRequest(v: NewPartyValues): CreatePartyRequest {
  const text = (s: string) => (s.trim() ? s.trim() : null)
  return {
    kind: v.kind as PartyKind,
    document: text(v.document),
    legalName: v.legalName.trim(),
    tradeName: v.kind === 'PF' ? null : text(v.tradeName),
    roles: v.roles as PartyRoleKind[],
    email: text(v.email),
    phone: text(v.phone),
    billingAddress: hasAddress(v)
      ? { kind: 'Billing', zipCode: v.zipCode, street: v.street.trim(), number: v.number.trim(), complement: text(v.complement), district: v.district.trim(), city: v.city.trim(), ibgeCode: text(v.ibgeCode), state: v.state, isDefault: true }
      : null,
    mainContact: text(v.contactName) ? { name: v.contactName.trim(), role: null, email: text(v.contactEmail), phone: text(v.contactPhone), receivesBilling: true } : null,
  } as CreatePartyRequest
}
