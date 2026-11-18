/**
 * Regras do Catálogo no front (CAT-01): as mesmas do servidor (GtinRules, MarginRules), para avisar enquanto digita. Quem decide é o
 * servidor: 422 "Código de barras inválido. Confira os dígitos." e 409 com o nome do produto que já usa o código.
 */

export const PRODUCTS = '/app/catalogo/produtos'
export const CATEGORIES = '/app/catalogo/categorias'

export const GTIN_INVALID = 'Código de barras inválido. Confira os dígitos.'
const GTIN_LENGTHS = [8, 12, 13, 14]

export const DICAS = {
  pack: 'Quantas unidades vêm nesse código. A unidade avulsa é 1; a caixa com 12 leva 12. No caixa, ler o código da caixa lança as 12 unidades.',
  plu: 'Código curto da balança (até 6 dígitos). A balança imprime a etiqueta com ele, e o caixa acha o produto pesado pelo PLU.',
  variacao: 'Grade: o mesmo produto em cores, tamanhos ou sabores. Cada combinação vira uma variação com código de barras, preço e saldo próprios; nome, categoria e foto vêm do produto pai.',
  edicao: 'Quem cadastra produto não muda preço sem a permissão "Alterar preço". A versão da tabela sobe a cada mudança e o histórico guarda quem mudou e quando.',
  margem: 'Margem: quanto do preço sobra depois do custo médio do item. Markup: quanto o preço está acima do custo.',
}

/** Só os dígitos que importam: tira espaços e hífens que o leitor ou a digitação trazem. */
export const normalizeGtin = (value: string) => value.replace(/[\s-]/g, '')

/** GTIN-8, 12, 13 ou 14 com o dígito verificador do módulo 10 (pesos 3 e 1 a partir da direita). */
export function isValidGtin(value: string): boolean {
  const gtin = normalizeGtin(value)
  if (!GTIN_LENGTHS.includes(gtin.length) || !/^\d+$/.test(gtin)) return false
  const body = gtin.slice(0, -1)
  let sum = 0
  for (let i = 0; i < body.length; i++) {
    const digit = Number(body[body.length - 1 - i])
    sum += i % 2 === 0 ? digit * 3 : digit
  }
  return (10 - (sum % 10)) % 10 === Number(gtin[gtin.length - 1])
}

/** Erro do GTIN enquanto digita: vazio não é erro (o campo é opcional). */
export const gtinError = (value: string) => (normalizeGtin(value) === '' || isValidGtin(value) ? undefined : GTIN_INVALID)

/** Margem em % sobre o preço, com 2 casas; null sem preço. */
export function marginPercent(price: number | null | undefined, cost: number): number | null {
  if (!price || price <= 0) return null
  return Math.round(((price - cost) / price) * 10000) / 100
}

/** Markup em % sobre o custo, com 2 casas; null sem preço ou sem custo. */
export function markupPercent(price: number | null | undefined, cost: number): number | null {
  if (price === null || price === undefined || cost <= 0) return null
  return Math.round(((price - cost) / cost) * 10000) / 100
}

/** Tom da margem no painel: negativa é crítica, abaixo de 10% é alerta. */
export const marginTone = (margin: number | null): 'crit' | 'warn' | 'ok' | 'neutral' =>
  margin === null ? 'neutral' : margin < 0 ? 'crit' : margin < 10 ? 'warn' : 'ok'

export const formatPercent = (value: number | null | undefined) =>
  value === null || value === undefined ? '—' : `${value.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}%`

/** "Caixa com 12" para a embalagem; a unidade avulsa é "Unidade". */
export const describePack = (quantity: number) =>
  quantity === 1 ? 'Unidade' : `Embalagem com ${quantity.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}`

/** Atributos da grade digitados ("Azul, Preto"): valores sem repetição (vale a primeira grafia) e sem vazio. */
export const parseValues = (text: string) => {
  const seen = new Map<string, string>()
  for (const v of text.split(',').map((s) => s.trim()).filter(Boolean)) if (!seen.has(v.toLowerCase())) seen.set(v.toLowerCase(), v)
  return [...seen.values()]
}

/** Quantas variações a grade gera (produto das quantidades de valores). */
export const combinationCount = (attributes: { name: string; values: string[] }[]) => {
  const used = attributes.filter((a) => a.name.trim() && a.values.length > 0)
  return used.length === 0 ? 0 : used.reduce((n, a) => n * a.values.length, 1)
}

/** "Cor: Azul · Tamanho: M" */
export const describeAttributes = (attributes: Record<string, string> | null | undefined) =>
  Object.entries(attributes ?? {}).map(([k, v]) => `${k}: ${v}`).join(' · ')

/** Número da API (string normalizada do campo) ou null. */
export const toNumber = (value: string | null | undefined) => (value === null || value === undefined || value === '' ? null : Number(value))

/** Importação (CAT-02): quantas linhas vão entrar (criam + atualizam). */
export const validRows = (view: { toCreate: number; toUpdate: number }) => view.toCreate + view.toUpdate

/** Importação (CAT-02): progresso do job em %, de 0 a 100. */
export const progressPercent = (view: { processed: number; toProcess: number }) =>
  view.toProcess <= 0 ? 100 : Math.min(100, Math.round((view.processed / view.toProcess) * 100))
