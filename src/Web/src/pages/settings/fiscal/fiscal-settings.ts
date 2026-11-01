import type { FiscalSettingsValues } from '@/api/generated/model'

/** Tipo de controle de cada campo de Configurações → Fiscal. */
export type FieldKind = 'select' | 'text' | 'percent' | 'int' | 'bool' | 'cnaes' | 'costTaxes' | 'inputTaxCredit' | 'fixed' | 'derived'

export interface FieldOption { value: string; label: string }

export interface FieldConfig {
  key: keyof FiscalSettingsValues | 'crt'
  kind: FieldKind
  options?: FieldOption[]
  hint?: string
  dica?: string
  maxLength?: number
  /** Texto do campo fixo (sem alternativa até a contadora responder). */
  text?: string
}

export const REGIME_OPTIONS: FieldOption[] = [
  { value: 'LucroPresumido', label: 'Lucro Presumido' },
  { value: 'LucroReal', label: 'Lucro Real' },
  { value: 'SimplesNacional', label: 'Simples Nacional' },
  { value: 'SimplesNacionalExcess', label: 'Simples Nacional, excesso de sublimite' },
  { value: 'Mei', label: 'MEI' },
]

export const CRT_LABEL: Record<number, string> = { 1: '1 – Simples Nacional', 2: '2 – Simples Nacional, excesso de sublimite', 3: '3 – Regime Normal', 4: '4 – MEI' }

/** CRT pelo regime (o mesmo cálculo do servidor). */
export const crtOf = (regime: string) => (regime === 'SimplesNacional' ? 1 : regime === 'SimplesNacionalExcess' ? 2 : regime === 'Mei' ? 4 : 3)

export const FREIGHT_OPTIONS: FieldOption[] = [
  { value: '0', label: '0 – Por conta do remetente (CIF)' },
  { value: '1', label: '1 – Por conta do destinatário (FOB)' },
  { value: '2', label: '2 – Por conta de terceiros' },
  { value: '3', label: '3 – Próprio, por conta do remetente' },
  { value: '4', label: '4 – Próprio, por conta do destinatário' },
  { value: '9', label: '9 – Sem transporte' },
]

export const COST_TAX_OPTIONS: FieldOption[] = [
  { value: 'ICMS', label: 'ICMS' },
  { value: 'IPI', label: 'IPI' },
  { value: 'PIS', label: 'PIS' },
  { value: 'COFINS', label: 'COFINS' },
  { value: 'ISS', label: 'ISS' },
  { value: 'IBS_CBS', label: 'IBS/CBS' },
]

/** Tributos da NF-e de entrada que podem ser recuperáveis (FIS-04b CA 5); nenhum marcado = todos ficam no custo. */
export const INPUT_TAX_CREDIT_OPTIONS: FieldOption[] = [
  { value: 'ICMS', label: 'ICMS' },
  { value: 'IPI', label: 'IPI' },
  { value: 'PIS', label: 'PIS' },
  { value: 'COFINS', label: 'COFINS' },
]

export const FIRST_CONFIGURATION = 'Valores sugeridos pelo sistema. Confira cada um com a contadora antes de emitir em produção.'
export const PRODUCTION_TEXT = 'Notas em produção têm valor fiscal e só podem ser canceladas no prazo legal.'

export const DICAS = {
  crt: 'Código de Regime Tributário que vai em toda NF-e. Sai do regime: 3 para Lucro Presumido ou Real, 1 para o Simples Nacional.',
  serie: 'A série 1 costuma ser a do emissor atual. Confirme com a contadora a série reservada para o sistema.',
  ambiente: 'Homologação é o ambiente de teste da SEFAZ: a nota não tem valor fiscal. Produção só liga na virada, com tudo conferido.',
  vendaMista: 'Contrato com equipamento e instalação: o equipamento sai em NF-e na expedição e a instalação em NFS-e no aceite técnico.',
  lc116: 'Alternativa: 14.06 (instalação e montagem de aparelhos). Com 7.02, o ISS é devido no município da obra; com 14.06, no da empresa.',
  ibsCbs: 'Tributos da Reforma Tributária (IBS e CBS). Em 2026 as alíquotas são de teste e aparecem na nota sem cobrança.',
  custo: 'Tributos que o sistema soma ao custo do projeto para mostrar a margem real. Não muda a nota.',
  credito: 'Crédito de impostos é o tributo da nota de entrada que a empresa recupera na apuração. Marcado aqui, ele sai do custo de entrada do material e fica guardado na nota. Nenhum marcado (o padrão): ICMS, IPI, PIS e COFINS ficam no custo, até a contadora confirmar.',
  difal: 'Para não contribuinte de outro estado, o DIFAL ainda não é calculado: a emissão recusa até a contadora orientar.',
  a1: 'Certificado digital do tipo A1 (arquivo .pfx com senha) do CNPJ da empresa. Assina as notas; fica guardado só no cofre do Emissor Fiscal.',
}

/** Campos de cada seção, na ordem da tela (as chaves são as mesmas do servidor). */
export const SECTION_FIELDS: Record<string, FieldConfig[]> = {
  regime: [
    { key: 'regime', kind: 'select', options: REGIME_OPTIONS },
    { key: 'crt', kind: 'derived', dica: DICAS.crt },
    { key: 'cnaeMain', kind: 'text', maxLength: 9, hint: 'Ex.: 4329-1/03.' },
    { key: 'cnaeSecondary', kind: 'cnaes', hint: 'Separe por vírgula. Ex.: 2822-4/01.' },
  ],
  serie: [
    { key: 'nfeSeries', kind: 'int', maxLength: 3, dica: DICAS.serie, hint: 'De 1 a 999. Obrigatória para emitir.' },
    { key: 'environment', kind: 'select', dica: DICAS.ambiente, options: [{ value: 'Homologation', label: 'Homologação (sem valor fiscal)' }, { value: 'Production', label: 'Produção (com valor fiscal)' }] },
  ],
  operacoes: [
    { key: 'cfopSaleInStateContributor', kind: 'text', maxLength: 4, hint: 'CFOP de 4 dígitos começando por 5.' },
    { key: 'cfopSaleInStateFinal', kind: 'text', maxLength: 4, hint: 'Consumidor final.' },
    { key: 'cfopSaleInterstateContributor', kind: 'text', maxLength: 4, hint: 'CFOP de 4 dígitos começando por 6.' },
    { key: 'cfopSaleInterstateFinal', kind: 'text', maxLength: 4, dica: DICAS.difal, hint: 'DIFAL bloqueado: a emissão recusa.' },
    { key: 'cfopResaleInState', kind: 'text', maxLength: 4 },
    { key: 'cfopResaleInterstate', kind: 'text', maxLength: 4 },
    { key: 'mixedSale', kind: 'fixed', dica: DICAS.vendaMista, text: 'NF-e de venda do equipamento na expedição, sem nota de remessa para a obra, com o endereço da obra nas informações complementares; instalação em NFS-e no aceite técnico.' },
  ],
  cst: [
    { key: 'cstIcms', kind: 'text', maxLength: 3, hint: 'Regime Normal: 00. Simples Nacional: CSOSN (ex.: 101).' },
    { key: 'cstPisCofins', kind: 'text', maxLength: 2, hint: '01 no regime cumulativo.' },
    { key: 'cstIpi', kind: 'text', maxLength: 2, hint: 'Saída: 50 a 55 ou 99.' },
    { key: 'icmsInternalRate', kind: 'percent' },
    { key: 'icmsInterstateSouthSoutheastRate', kind: 'percent' },
    { key: 'icmsInterstateOtherRate', kind: 'percent' },
    { key: 'icmsImportedRate', kind: 'percent' },
    { key: 'pisRate', kind: 'percent' },
    { key: 'cofinsRate', kind: 'percent' },
    { key: 'ipiFromNcm', kind: 'bool', hint: 'A alíquota do IPI vem da TIPI pelo NCM de cada item.' },
  ],
  custo: [{ key: 'costTaxes', kind: 'costTaxes', dica: DICAS.custo }],
  entrada: [{ key: 'inputTaxCredit', kind: 'inputTaxCredit', dica: DICAS.credito }],
  nfse: [
    { key: 'nfseServiceItem', kind: 'text', maxLength: 5, dica: DICAS.lc116, hint: 'Ex.: 7.02.' },
    { key: 'nfseMunicipalCode', kind: 'text', maxLength: 8, hint: 'Formato NN.NN.NN, da lista da prefeitura. Vazio bloqueia só a NFS-e em produção.' },
    { key: 'nfseCtn', kind: 'text', maxLength: 20 },
    { key: 'nfseNbs', kind: 'text', maxLength: 20 },
    { key: 'nfseIssRate', kind: 'percent', hint: '5% é o teto da LC 116.' },
    { key: 'nfseIssLocation', kind: 'select', options: [{ value: 'WorkSite', label: 'Município da obra' }, { value: 'Company', label: 'Município da empresa' }] },
    { key: 'nfseIssWithheld', kind: 'bool' },
    { key: 'nfseFederalWithholding', kind: 'bool' },
  ],
  'ibs-cbs': [
    { key: 'ibsCbsCst', kind: 'text', maxLength: 3, dica: DICAS.ibsCbs },
    { key: 'ibsCbsClassification', kind: 'text', maxLength: 6 },
    { key: 'cbsRate', kind: 'percent' },
    { key: 'ibsRate', kind: 'percent', hint: 'Parcela da UF.' },
    { key: 'ibsCityRate', kind: 'percent', hint: 'Parcela do município.' },
  ],
  transporte: [
    { key: 'freightMode', kind: 'select', options: FREIGHT_OPTIONS },
    { key: 'volumeSpecies', kind: 'text', maxLength: 60, hint: 'Quantidade, peso bruto e transportadora vêm do romaneio.' },
    { key: 'includeDuplicates', kind: 'bool', hint: 'A parcela do contrato é só cobrança.' },
  ],
  prazos: [{ key: 'cancellationWindowHours', kind: 'int', maxLength: 3, hint: 'De 1 a 168 horas depois da autorização.' }],
}

/** Contador do topo (CA 3). */
export const confirmedText = (confirmed: number, total: number) => `${confirmed} de ${total} campos conferidos`

/** CNAE para mostrar: 4329103 → 4329-1/03. */
export const formatCnae = (digits: string) => (digits.length === 7 ? `${digits.slice(0, 4)}-${digits[4]}/${digits.slice(5)}` : digits)

/** CNAEs digitados separados por vírgula (ou espaço). */
export const parseCnaes = (text: string) => text.split(/[,;\s]+/).map((c) => c.replace(/\D/g, '')).filter((c) => c.length > 0)

/** A troca liga a produção? (diálogo "Ligar a emissão com valor fiscal", CA 8). */
export const turnsOnProduction = (before: FiscalSettingsValues, after: FiscalSettingsValues) => before.environment !== 'Production' && after.environment === 'Production'

/** Situação do certificado (CA 13): rótulo e tom do chip. */
export function certificateStatus(status: string, days: number | null | undefined): { label: string; tone: 'ok' | 'warn' | 'crit' | 'info' } {
  if (status === 'Expired') return { label: 'Vencido', tone: 'crit' }
  if (status === 'Expiring') return { label: days === 0 ? 'Vence hoje' : days === 1 ? 'Vence em 1 dia' : `Vence em ${days ?? 0} dias`, tone: 'warn' }
  if (status === 'Valid') return { label: 'Válido', tone: 'ok' }
  return { label: 'Não enviado', tone: 'info' }
}

/** Primeiro campo pendente de uma lista de chaves, para o índice levar direto a ele. */
export const fieldAnchor = (key: string) => `campo-${key}`
