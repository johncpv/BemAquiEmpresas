import type { CompanyProfileDto, CompanyProfileInput } from '@/api/generated/model'
import { z } from '@/lib/zod'

const optional = (max: number, message = `Use até ${max} caracteres.`) => z.string().max(max, message)

/** Dados da empresa (CAD-06 CA 2): as mesmas regras da API. */
export const companySchema = z.object({
  legalName: z.string().trim().min(2, 'Informe a razão social (de 2 a 200 caracteres).').max(200, 'Informe a razão social (de 2 a 200 caracteres).'),
  tradeName: optional(200),
  stateRegistration: z.string().refine((v) => v === '' || /^(ISENTO|\d{2,14})$/i.test(v.trim()), 'Informe só os dígitos da inscrição estadual, ou ISENTO.'),
  municipalRegistration: optional(20),
  zipCode: z.string().refine((v) => v === '' || v.length === 8, 'CEP com 8 dígitos.'),
  street: optional(200),
  number: optional(20),
  complement: optional(100),
  district: optional(100),
  city: optional(100),
  cityIbge: z.string().refine((v) => v === '' || /^\d{7}$/.test(v), 'Código IBGE com 7 dígitos.'),
  state: z.string(),
  phone: z.string().refine((v) => v === '' || v.length === 10 || v.length === 11, 'Telefone com DDD: 10 ou 11 dígitos.'),
  email: z.union([z.literal(''), z.email('E-mail inválido.')]),
})

export type CompanyValues = z.input<typeof companySchema>

export const toValues = (p: CompanyProfileDto): CompanyValues => ({
  legalName: p.legalName, tradeName: p.tradeName ?? '', stateRegistration: p.stateRegistration ?? '', municipalRegistration: p.municipalRegistration ?? '',
  zipCode: p.zipCode ?? '', street: p.street ?? '', number: p.number ?? '', complement: p.complement ?? '', district: p.district ?? '', city: p.city ?? '',
  cityIbge: p.cityIbge ?? '', state: p.state ?? '', phone: p.phone ?? '', email: p.email ?? '',
})

export function toInput(v: CompanyValues): CompanyProfileInput {
  const text = (s: string) => (s.trim() ? s.trim() : null)
  return {
    legalName: v.legalName.trim(), tradeName: text(v.tradeName), stateRegistration: text(v.stateRegistration), municipalRegistration: text(v.municipalRegistration),
    zipCode: text(v.zipCode), street: text(v.street), number: text(v.number), complement: text(v.complement), district: text(v.district), city: text(v.city),
    cityIbge: text(v.cityIbge), state: text(v.state), phone: text(v.phone), email: text(v.email),
  }
}
