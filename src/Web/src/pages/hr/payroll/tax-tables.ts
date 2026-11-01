/** Tabelas legais da folha (FLH-01): rótulos, rascunho do editor (decimais em texto, sem perda) e o conteúdo enviado à API. */

export type TaxKind = 'Inss' | 'Irrf' | 'FamilyAllowance' | 'MinimumWage' | 'Fgts' | 'TransportVoucher' | 'EmployerCharges'
export type TaxStatus = 'Unconfirmed' | 'Active' | 'Superseded'
export type Rounding = 'AwayFromZero' | 'ToEven' | 'Truncate'

export const TAX_KINDS: { kind: TaxKind; label: string; description: string }[] = [
  { kind: 'Inss', label: 'INSS', description: 'Faixas progressivas do empregado; a última termina no teto do salário de contribuição.' },
  { kind: 'Irrf', label: 'IRRF', description: 'Faixas com parcela a deduzir, dedução por dependente, desconto simplificado e a redução adicional.' },
  { kind: 'FamilyAllowance', label: 'Salário-família', description: 'Remuneração máxima para ter direito e a cota por filho.' },
  { kind: 'MinimumWage', label: 'Salário mínimo', description: 'Valor mensal nacional.' },
  { kind: 'Fgts', label: 'FGTS', description: 'Alíquota mensal, da multa da rescisão e do aprendiz.' },
  { kind: 'TransportVoucher', label: 'Vale-transporte', description: 'Percentual máximo de desconto sobre o salário base.' },
  { kind: 'EmployerCharges', label: 'Encargos da empresa', description: 'Contribuição patronal, RAT × FAP e terceiros, pelo regime tributário.' },
]

export const kindLabel = (kind: string) => TAX_KINDS.find((k) => k.kind === kind)?.label ?? kind

export const ROUNDINGS: { value: Rounding; label: string }[] = [
  { value: 'AwayFromZero', label: '2 casas, metade para cima (padrão)' },
  { value: 'ToEven', label: '2 casas, metade para o par' },
  { value: 'Truncate', label: '2 casas, truncado' },
]

export const PENDING_LABEL = 'A confirmar com a contadora'

export const TAX_DICAS = {
  valeAPartirDe: 'Primeiro mês em que a tabela vale. Ela vale até a próxima versão do mesmo tipo; a folha de cada competência usa a versão de maior "vale a partir de" até o primeiro dia do mês.',
  aConfirmar: 'Valores pré-cadastrados ou digitados que ainda não foram conferidos. Já servem para simular; a folha oficial calculada só usa tabela conferida. Quem cadastrou ou editou não confere.',
  retificar: 'Corrige uma tabela já conferida sem apagar a original: nasce uma nova versão com a mesma vigência, que passa por nova conferência. Folha fechada nunca é recalculada.',
  reducao: 'Redução do IRRF no formato da norma vigente: para o rendimento tributável do mês até o limite, a redução é o valor fixo menos o percentual sobre o rendimento, sem passar do imposto.',
  arredondamento: 'Como o resultado de cada imposto vira centavos: metade para cima (padrão), metade para o par ou truncado.',
}

/** Status da versão para o chip: texto e tom. */
export function statusChip(status: string, isFuture: boolean, validFrom: string): { label: string; tone: 'warn' | 'ok' | 'neutral' | 'info' }[] {
  const chips: { label: string; tone: 'warn' | 'ok' | 'neutral' | 'info' }[] = []
  if (status === 'Unconfirmed') chips.push({ label: PENDING_LABEL, tone: 'warn' })
  else if (status === 'Active') chips.push({ label: 'Conferida', tone: 'ok' })
  else chips.push({ label: 'Substituída', tone: 'neutral' })
  if (isFuture && status !== 'Superseded') chips.push({ label: `A partir de ${formatDay(validFrom)}`, tone: 'info' })
  return chips
}

/** "2026-01-01" vira "01/01/2026". */
export const formatDay = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}`

/** Primeiro dia do mês seguinte (a nova versão abre com a próxima vigência). */
export function nextMonthStart(today = new Date()) {
  const y = today.getMonth() === 11 ? today.getFullYear() + 1 : today.getFullYear()
  const m = today.getMonth() === 11 ? 1 : today.getMonth() + 2
  return `${y}-${String(m).padStart(2, '0')}-01`
}

export interface BracketDraft {
  upTo: string | null
  rate: string | null
  deduction: string | null
}

export interface ReductionDraft {
  upTo: string | null
  fixedAmount: string | null
  rate: string | null
}

/** Campos de valor único dos tipos simples (e os do IRRF fora das faixas). */
export type ValueKey = 'dependentDeduction' | 'simplifiedDiscount' | 'maxRemuneration' | 'quotaPerChild' | 'monthly' | 'monthlyRate' | 'terminationRate' | 'apprenticeRate'
  | 'maxDiscountRate' | 'employerRate' | 'ratRate' | 'fap' | 'thirdPartiesRate'

export interface ContentDraft {
  brackets: BracketDraft[]
  reductions: ReductionDraft[]
  values: Partial<Record<ValueKey, string | null>>
  taxRegime: string
}

export interface ValueField {
  key: ValueKey
  label: string
  /** money: R$ 2 casas; pct: % 4 casas; factor: 4 casas sem unidade. */
  type: 'money' | 'pct' | 'factor'
  hint?: string
}

export const VALUE_FIELDS: Record<TaxKind, ValueField[]> = {
  Inss: [],
  Irrf: [
    { key: 'dependentDeduction', label: 'Dedução por dependente', type: 'money' },
    { key: 'simplifiedDiscount', label: 'Desconto simplificado mensal', type: 'money', hint: 'Usado quando é maior que INSS + dependentes.' },
  ],
  FamilyAllowance: [
    { key: 'maxRemuneration', label: 'Remuneração máxima', type: 'money' },
    { key: 'quotaPerChild', label: 'Cota por filho', type: 'money' },
  ],
  MinimumWage: [{ key: 'monthly', label: 'Salário mínimo mensal', type: 'money' }],
  Fgts: [
    { key: 'monthlyRate', label: 'Alíquota mensal', type: 'pct' },
    { key: 'terminationRate', label: 'Multa da rescisão', type: 'pct' },
    { key: 'apprenticeRate', label: 'Alíquota do aprendiz', type: 'pct' },
  ],
  TransportVoucher: [{ key: 'maxDiscountRate', label: 'Percentual máximo de desconto', type: 'pct' }],
  EmployerCharges: [
    { key: 'employerRate', label: 'Contribuição patronal', type: 'pct' },
    { key: 'ratRate', label: 'RAT (pelo CNAE preponderante)', type: 'pct' },
    { key: 'fap', label: 'FAP', type: 'factor', hint: 'Fator acidentário de prevenção, com 4 casas.' },
    { key: 'thirdPartiesRate', label: 'Terceiros (pelo FPAS)', type: 'pct' },
  ],
}

type Num = number | null | undefined
const text = (v: Num) => (v === null || v === undefined ? null : String(v))
const num = (v: string | null | undefined) => (v === null || v === undefined || v === '' ? 0 : Number(v))

/** Conteúdo como a API devolve (só a seção do tipo vem preenchida). */
export interface TaxContent {
  inss?: { brackets: { upTo?: Num; rate: number }[] } | null
  irrf?: { brackets: { upTo?: Num; rate: number; deduction?: Num }[]; dependentDeduction: number; simplifiedDiscount: number; reductions: { upTo?: Num; fixedAmount: number; rate: number }[] } | null
  familyAllowance?: { maxRemuneration: number; quotaPerChild: number } | null
  minimumWage?: { monthly: number } | null
  fgts?: { monthlyRate: number; terminationRate: number; apprenticeRate: number } | null
  transportVoucher?: { maxDiscountRate: number } | null
  employerCharges?: { taxRegime: string; employerRate: number; ratRate: number; fap: number; thirdPartiesRate: number } | null
}

export const emptyDraft = (kind: TaxKind): ContentDraft => ({
  brackets: kind === 'Inss' || kind === 'Irrf' ? [{ upTo: null, rate: null, deduction: null }] : [],
  reductions: [],
  values: {},
  taxRegime: kind === 'EmployerCharges' ? 'Lucro Presumido' : '',
})

/** Da API para o editor (a nova versão abre preenchida com a vigente). */
export function toDraft(kind: TaxKind, content: TaxContent | null | undefined): ContentDraft {
  if (!content) return emptyDraft(kind)
  const d = emptyDraft(kind)
  if (kind === 'Inss' && content.inss) d.brackets = content.inss.brackets.map((b) => ({ upTo: text(b.upTo), rate: text(b.rate), deduction: null }))
  if (kind === 'Irrf' && content.irrf) {
    d.brackets = content.irrf.brackets.map((b) => ({ upTo: text(b.upTo), rate: text(b.rate), deduction: text(b.deduction ?? 0) }))
    d.reductions = (content.irrf.reductions ?? []).map((r) => ({ upTo: text(r.upTo), fixedAmount: text(r.fixedAmount), rate: text(r.rate) }))
    d.values = { dependentDeduction: text(content.irrf.dependentDeduction), simplifiedDiscount: text(content.irrf.simplifiedDiscount) }
  }
  if (kind === 'FamilyAllowance' && content.familyAllowance) d.values = { maxRemuneration: text(content.familyAllowance.maxRemuneration), quotaPerChild: text(content.familyAllowance.quotaPerChild) }
  if (kind === 'MinimumWage' && content.minimumWage) d.values = { monthly: text(content.minimumWage.monthly) }
  if (kind === 'Fgts' && content.fgts) d.values = { monthlyRate: text(content.fgts.monthlyRate), terminationRate: text(content.fgts.terminationRate), apprenticeRate: text(content.fgts.apprenticeRate) }
  if (kind === 'TransportVoucher' && content.transportVoucher) d.values = { maxDiscountRate: text(content.transportVoucher.maxDiscountRate) }
  if (kind === 'EmployerCharges' && content.employerCharges) {
    const e = content.employerCharges
    d.values = { employerRate: text(e.employerRate), ratRate: text(e.ratRate), fap: text(e.fap), thirdPartiesRate: text(e.thirdPartiesRate) }
    d.taxRegime = e.taxRegime
  }
  return d
}

/** Do editor para a API: só a seção do tipo; campo vazio vai como zero (o servidor valida e diz o que falta). */
export function toContent(kind: TaxKind, d: ContentDraft): TaxContent {
  const v = (k: ValueKey) => num(d.values[k])
  switch (kind) {
    case 'Inss':
      return { inss: { brackets: d.brackets.map((b) => ({ upTo: b.upTo ? Number(b.upTo) : null, rate: num(b.rate) })) } }
    case 'Irrf':
      return {
        irrf: {
          brackets: d.brackets.map((b) => ({ upTo: b.upTo ? Number(b.upTo) : null, rate: num(b.rate), deduction: num(b.deduction) })),
          dependentDeduction: v('dependentDeduction'),
          simplifiedDiscount: v('simplifiedDiscount'),
          reductions: d.reductions.map((r) => ({ upTo: r.upTo ? Number(r.upTo) : null, fixedAmount: num(r.fixedAmount), rate: num(r.rate) })),
        },
      }
    case 'FamilyAllowance':
      return { familyAllowance: { maxRemuneration: v('maxRemuneration'), quotaPerChild: v('quotaPerChild') } }
    case 'MinimumWage':
      return { minimumWage: { monthly: v('monthly') } }
    case 'Fgts':
      return { fgts: { monthlyRate: v('monthlyRate'), terminationRate: v('terminationRate'), apprenticeRate: v('apprenticeRate') } }
    case 'TransportVoucher':
      return { transportVoucher: { maxDiscountRate: v('maxDiscountRate') } }
    case 'EmployerCharges':
      return { employerCharges: { taxRegime: d.taxRegime.trim(), employerRate: v('employerRate'), ratRate: v('ratRate'), fap: v('fap'), thirdPartiesRate: v('thirdPartiesRate') } }
  }
}

/** Nota da conferência como o servidor grava: "Conferido com {contadora} em {data}". */
export const checkNote = (accountant: string, isoDate: string) => `Conferido com ${accountant.trim()} em ${formatDay(isoDate)}`
