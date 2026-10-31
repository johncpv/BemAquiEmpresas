import type { RecipientType, TaxOperation, TaxRuleStatus, TaxRuleValues } from '@/api/generated/model'
import type { StatusTone } from '@/components/ui/status-chip'

/** Rótulos das operações (os mesmos do servidor, TaxRuleRules.Label). */
export const OPERATION_LABEL: Record<TaxOperation, string> = {
  OwnProductionSale: 'Venda de produção própria',
  Resale: 'Revenda',
  ServiceProvision: 'Prestação de serviço',
  ShipmentToSite: 'Remessa para obra',
  ReturnFromSite: 'Retorno de obra',
  PurchaseReturn: 'Devolução de compra',
  OtherOutbound: 'Outras saídas',
}

export const OPERATIONS = Object.keys(OPERATION_LABEL) as TaxOperation[]

export const RECIPIENT_LABEL: Record<RecipientType, string> = {
  Contributor: 'Contribuinte',
  Exempt: 'Isento',
  NonContributor: 'Não contribuinte',
}

export const RECIPIENTS = Object.keys(RECIPIENT_LABEL) as RecipientType[]

/** Situação da versão: Rascunho neutro, Ativa ok, Inativa neutro ("Telas e estados"). */
export const STATUS: Record<TaxRuleStatus, { label: string; tone: StatusTone }> = {
  Draft: { label: 'Rascunho', tone: 'neutral' },
  Active: { label: 'Ativa', tone: 'ok' },
  Inactive: { label: 'Inativa', tone: 'neutral' },
}

export const STATES = ['AC', 'AL', 'AM', 'AP', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MG', 'MS', 'MT', 'PA', 'PB', 'PE', 'PI', 'PR', 'RJ', 'RN', 'RO', 'RR', 'RS', 'SC', 'SE', 'SP', 'TO']

export const TAX_LABEL: Record<string, string> = {
  ICMS: 'ICMS',
  ICMS_ST: 'ICMS-ST',
  FCP: 'FCP',
  DIFAL: 'DIFAL',
  IPI: 'IPI',
  PIS: 'PIS',
  COFINS: 'COFINS',
  ISS: 'ISS',
  CBS: 'CBS',
  IBS_UF: 'IBS estadual',
  IBS_MUN: 'IBS municipal',
}

export const SEED_DRAFTS = 'As regras abaixo foram montadas com os valores propostos. Simule, confira com a contadora e ative.'
export const EMPTY_ACTIVE = 'Nenhuma regra ativa. Ative as regras antes de emitir a primeira nota.'
export const DIFAL_NOTICE = 'A nota com DIFAL ainda não é suportada pelo Emissor Fiscal. Fale com a contadora para emitir esta venda.'

export const DICAS = {
  cfop: 'Código Fiscal de Operações e Prestações: diz na nota que tipo de operação é. Começa por 5 dentro do estado e por 6 para outro estado.',
  cst: 'Código de Situação Tributária: diz como o tributo é cobrado no item (tributado, isento, com redução). No Simples Nacional, o ICMS usa o CSOSN.',
  mva: 'Margem de Valor Agregado: o percentual somado ao valor da venda para calcular a base do ICMS cobrado por substituição tributária (ICMS-ST).',
  difal: 'Diferença entre a alíquota interna do estado de destino e a interestadual, devida na venda a não contribuinte de outro estado. O Emissor Fiscal ainda não emite nota com DIFAL.',
  custo: 'Tributos que o sistema soma ao custo do projeto quando a nota é autorizada, para mostrar a margem real. Não muda a nota.',
  ibsCbs: 'Tributos da Reforma Tributária (IBS e CBS). Em 2026 as alíquotas são de teste e aparecem na nota sem cobrança.',
}

/** CFOP fora do estado sugerido a partir do de dentro: 5xxx → 6xxx (o servidor tem a mesma regra). */
export const suggestInterstateCfop = (internal?: string | null) => (internal && /^5\d{3}$/.test(internal) ? '6' + internal.slice(1) : null)

export const isService = (operation: TaxOperation) => operation === 'ServiceProvision'

/** Valores de uma regra nova: começa pela operação mais comum e já vem com os tributos de uma venda. */
export function newRuleValues(today: string): TaxRuleValues {
  return {
    operation: 'OwnProductionSale',
    name: '',
    natureOfOperation: '',
    validFrom: today,
    validTo: null,
    cfopInternal: '',
    cfopInterstate: '',
    icms: { cst: '00', baseModality: 3, internalRate: 18, interstateSouthSoutheastRate: 12, interstateOtherRate: 7, importedRate: 4, baseReduction: 0 },
    icmsSt: null,
    fcp: null,
    ipi: { cst: '50', framework: '999', fromNcm: true, rate: 0 },
    pis: { cst: '01', rate: 0.65 },
    cofins: { cst: '01', rate: 3 },
    iss: null,
    ibsCbs: { cst: '000', classification: '000001', cbsRate: 0.9, ibsStateRate: 0.1, ibsCityRate: 0 },
    difal: null,
    costTaxes: ['ICMS', 'PIS', 'COFINS', 'ISS', 'IBS_CBS'],
    complementaryInfo: null,
    conditions: { states: [], recipientTypes: [], ncmPrefixes: [] },
  }
}

/** Troca a operação: serviço não tem CFOP, ICMS, IPI nem DIFAL; venda não tem ISS. */
export function switchOperation(values: TaxRuleValues, operation: TaxOperation): TaxRuleValues {
  if (isService(operation)) {
    return { ...values, operation, cfopInternal: null, cfopInterstate: null, icms: null, icmsSt: null, fcp: null, ipi: null, difal: null, iss: values.iss ?? { serviceItem: '7.02', rate: 5, location: 'WorkSite', withheld: false } }
  }
  const base = newRuleValues(values.validFrom)
  return { ...values, operation, iss: null, icms: values.icms ?? base.icms, ipi: values.ipi ?? base.ipi, cfopInternal: values.cfopInternal ?? '', cfopInterstate: values.cfopInterstate ?? '' }
}

/** Lista "SP, MG" ↔ ["SP", "MG"]; NCM só com dígitos. */
export const parseList = (text: string, digits = false) =>
  text.split(/[,;\s]+/).map((s) => (digits ? s.replace(/\D/g, '') : s.trim().toUpperCase())).filter((s) => s.length > 0)

/** Erros do servidor por campo ("icms.internalRate") para o mapa da tela. */
export const firstErrors = (fields: Record<string, string[]>) => Object.fromEntries(Object.entries(fields).map(([k, v]) => [k, v[0] ?? '']))

export const percent = (value: number) => `${value.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%`
