import type { EmployeeBankAccountType, DependentKinship, EmployeePixKeyType } from '@/api/generated/model'
import { isValidCpf, onlyDigits } from '@/lib/masks'

/** Complemento do cadastro (CAD-04c2): rótulos, validações do formulário e máscaras. O servidor confere tudo de novo. */

type Kinship = NonNullable<DependentKinship>
type AccountType = NonNullable<EmployeeBankAccountType>
type PixType = NonNullable<EmployeePixKeyType>

export const KINSHIP_LABELS: Record<Kinship, string> = {
  Child: 'Filho ou filha',
  Stepchild: 'Enteado ou enteada',
  Spouse: 'Cônjuge',
  Partner: 'Companheiro ou companheira',
  Parent: 'Pai ou mãe',
  Ward: 'Menor sob guarda ou tutela',
  Other: 'Outro',
}

export const ACCOUNT_TYPE_LABELS: Record<AccountType, string> = { Checking: 'Conta corrente', Savings: 'Poupança', Salary: 'Conta salário', Payment: 'Conta de pagamento' }

export const PIX_TYPE_LABELS: Record<PixType, string> = { Cpf: 'CPF', Email: 'E-mail', Phone: 'Celular', Random: 'Chave aleatória' }

export const INSALUBRITY_GRADES = [10, 20, 40] as const

export const MISMATCH_REASON_MIN = 10
export const MISMATCH_REASON_MAX = 200
export const REVIEW_NOTE_MAX = 200

export const COMPLEMENT_DICAS = {
  ir: 'Dependente que entra no cálculo do Imposto de Renda do funcionário. Para ele, o CPF é obrigatório; para os demais, o CPF não é pedido.',
  salarioFamilia: 'Marca o dependente que dá direito ao salário-família. Quem confere a idade, os documentos e o valor é a folha (a confirmar com a contadora).',
  titular: 'Quem é o dono da conta. Vem preenchido com o funcionário; se a conta ou a chave Pix for de outra pessoa, o sistema pede uma segunda confirmação.',
  quarentena: 'Conta trocada fica 30 dias fora do pagamento automático da folha, até alguém que não fez a troca revisar. É uma proteção contra desvio de salário.',
  revisar: 'Quem não fez a troca confere a conta com o funcionário (por telefone ou pessoalmente) e libera a conta antes do fim da quarentena.',
  documentosSalarioFamilia: 'Marque quando o funcionário entregar a certidão de nascimento e, conforme a idade, a carteira de vacinação ou a frequência escolar. Sem a marca, a folha calculada não paga a cota e avisa "documentos pendentes".',
  valeTransporte: 'Só "usa" ou "não usa". O valor do vale e o desconto do funcionário são calculados na folha.',
}

/** Chave Pix válida pelo tipo (mesma regra do servidor, CA 4); devolve a mensagem do erro ou undefined. */
export function pixKeyError(type: PixType, key: string): string | undefined {
  const text = key.trim()
  if (!text) return 'Informe a chave Pix.'
  switch (type) {
    case 'Cpf':
      return isValidCpf(onlyDigits(text)) ? undefined : 'Chave Pix inválida: informe um CPF válido.'
    case 'Email':
      return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(text) && text.length <= 77 ? undefined : 'Chave Pix inválida: informe um e-mail válido.'
    case 'Phone': {
      let digits = onlyDigits(text)
      if ((digits.length === 12 || digits.length === 13) && digits.startsWith('55')) digits = digits.slice(2)
      return (digits.length === 10 || digits.length === 11) && digits[0] !== '0' ? undefined : 'Chave Pix inválida: informe o celular com DDD.'
    }
    default:
      return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(text) ? undefined : 'Chave Pix inválida: copie a chave aleatória do app do banco (36 caracteres, com hifens).'
  }
}

export interface BankDraft {
  bankCode: string
  agency: string
  account: string
  accountType: AccountType | ''
  holderName: string
  holderCpf: string
  pixKeyType: PixType | ''
  pixKey: string
}

/** Propositivo (CAD-04c2): o titular vem com o nome e o CPF do funcionário, e a chave Pix como CPF, com o CPF dele. */
export const emptyBankDraft = (name: string, cpf: string): BankDraft => ({ bankCode: '', agency: '', account: '', accountType: 'Checking', holderName: name, holderCpf: cpf, pixKeyType: 'Cpf', pixKey: cpf })

/** Erros do formulário por campo: banco completo, Pix completo ou os dois; titular sempre. */
export function bankDraftErrors(d: BankDraft): Record<string, string> {
  const errors: Record<string, string> = {}
  const anyBank = !!(d.bankCode || d.agency.trim() || d.account.trim())
  if (anyBank) {
    if (onlyDigits(d.bankCode).length !== 3) errors.bankCode = 'Informe o código do banco (COMPE), com 3 dígitos.'
    if (!/^[0-9-]{1,10}$/.test(d.agency.trim())) errors.agency = 'Informe a agência, só com números (e o dígito, se houver).'
    if (!/^[0-9xX-]{1,20}$/.test(d.account.trim())) errors.account = 'Informe a conta com o dígito, só com números.'
    if (!d.accountType) errors.accountType = 'Escolha o tipo da conta.'
  }
  const pix = d.pixKeyType ? pixKeyError(d.pixKeyType, d.pixKey) : d.pixKey.trim() ? 'Escolha o tipo da chave Pix.' : undefined
  if (pix && (d.pixKeyType || d.pixKey.trim())) errors.pixKey = pix
  if (!anyBank && !d.pixKeyType) errors.bankCode = 'Informe o banco, a agência e a conta, ou uma chave Pix.'
  if (!d.holderName.trim()) errors.holderName = 'Informe o nome do titular da conta.'
  if (!isValidCpf(onlyDigits(d.holderCpf))) errors.holderCpf = 'Informe um CPF válido para o titular da conta.'
  return errors
}

export type Mismatch = { kind: 'Holder' | 'PixKey'; text: string }

/** O que não bate com o CPF do funcionário (CA 5), já mascarado para a segunda confirmação. */
export function bankMismatches(d: BankDraft, employeeCpf: string): Mismatch[] {
  const list: Mismatch[] = []
  const holder = onlyDigits(d.holderCpf)
  if (holder && holder !== employeeCpf) list.push({ kind: 'Holder', text: `Titular: ${maskName(d.holderName)}, CPF ${maskCpf(holder)}` })
  const key = onlyDigits(d.pixKey)
  if (d.pixKeyType === 'Cpf' && key && key !== employeeCpf) list.push({ kind: 'PixKey', text: `Chave Pix: CPF ${maskCpf(key)}` })
  return list
}

export const MISMATCH_WARNING = 'A chave Pix (ou o titular) não é o CPF do funcionário. Confira antes de salvar.'

/** Motivo da segunda confirmação (CA 5): de 10 a 200 caracteres, depois de marcar "Conferi com o funcionário". */
export const mismatchConfirmed = (checked: boolean, reason: string) => checked && reason.trim().length >= MISMATCH_REASON_MIN && reason.trim().length <= MISMATCH_REASON_MAX

/** CPF mascarado no padrão "***.456.789-**". */
export function maskCpf(cpf: string) {
  const d = onlyDigits(cpf)
  return d.length === 11 ? `***.${d.slice(3, 6)}.${d.slice(6, 9)}-**` : '***'
}

/** Nome mascarado: a inicial de cada palavra ("Maria Silva" vira "M**** S****"). */
export const maskName = (name: string) => name.trim().split(/\s+/).filter(Boolean).map((w) => w[0] + '*'.repeat(Math.max(w.length - 1, 1))).join(' ')

/** Dependente (CA 1 e 2): CPF só no de IR. */
export interface DependentDraft { name: string; birthDate: string; kinship: Kinship | ''; irDependent: boolean; familyAllowance: boolean; familyAllowanceDocsOk?: boolean; cpf: string }

export const emptyDependentDraft = (): DependentDraft => ({ name: '', birthDate: '', kinship: '', irDependent: false, familyAllowance: false, cpf: '' })

/** Erros do dependente; `keepsCpf`: na edição do dependente de IR com CPF, o campo vazio mantém o gravado. */
export function dependentErrors(d: DependentDraft, today: string, keepsCpf = false): Record<string, string> {
  const errors: Record<string, string> = {}
  if (!d.name.trim()) errors.name = 'Informe o nome do dependente.'
  if (!d.birthDate || d.birthDate > today) errors.birthDate = 'Informe a data de nascimento do dependente.'
  if (!d.kinship) errors.kinship = 'Escolha o parentesco.'
  const cpf = onlyDigits(d.cpf)
  if (d.irDependent && !cpf && !keepsCpf) errors.cpf = 'Informe o CPF do dependente de IR.'
  else if (d.irDependent && cpf && !isValidCpf(cpf)) errors.cpf = 'CPF inválido.'
  return errors
}

/** Quarentena (CA 7): chip de alerta até o fim do prazo; revisada vira informativo. */
export function bankChip(status: { inQuarantine: boolean; quarantineUntil?: string | null; reviewedAt?: string | null; selfReviewed: boolean }, formatDate: (iso: string) => string) {
  if (status.inQuarantine && status.quarantineUntil) return { tone: 'warn' as const, label: `Conta em quarentena até ${formatDate(status.quarantineUntil)}` }
  if (status.reviewedAt) return { tone: 'info' as const, label: `Conta revisada em ${formatDate(status.reviewedAt)}${status.selfReviewed ? ' (autorrevisado)' : ''}` }
  return null
}
