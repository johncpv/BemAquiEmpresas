import { ItemType } from '@/api/generated/model'

export const TYPE_LABELS: Record<ItemType, string> = {
  RawMaterial: 'Matéria-prima', Component: 'Componente', Finished: 'Produto acabado', Consumable: 'Material de consumo', Service: 'Serviço',
}

export const TYPE_OPTIONS = (Object.keys(TYPE_LABELS) as ItemType[]).map((value) => ({ value, label: TYPE_LABELS[value] }))

/** Tipos que exigem NCM quando o item está ativo (CAD-02a CA 7). */
export const requiresNcm = (type: ItemType) => type === ItemType.RawMaterial || type === ItemType.Component || type === ItemType.Finished

/** Lote e série sugeridos pelo tipo (CA 10, R-PRP-1). */
export const suggestedTracking = (type: ItemType) => ({ lot: type === ItemType.RawMaterial, serial: type === ItemType.Finished })

/** Origem da mercadoria (tabela A do CST do ICMS), com a descrição oficial (CA 8). */
export const ORIGINS = [
  'Nacional, exceto as indicadas nos códigos 3, 4, 5 e 8',
  'Estrangeira, importação direta, exceto a indicada no código 6',
  'Estrangeira, adquirida no mercado interno, exceto a indicada no código 7',
  'Nacional, com Conteúdo de Importação superior a 40% e inferior ou igual a 70%',
  'Nacional, produzida conforme os processos produtivos básicos (PPB)',
  'Nacional, com Conteúdo de Importação inferior ou igual a 40%',
  'Estrangeira, importação direta, sem similar nacional, constante em lista da CAMEX e gás natural',
  'Estrangeira, adquirida no mercado interno, sem similar nacional, constante em lista da CAMEX e gás natural',
  'Nacional, com Conteúdo de Importação superior a 70%',
].map((label, code) => ({ code, label: `${code} — ${label}` }))

export const formatNcm = (ncm: string | null | undefined) => (ncm && ncm.length === 8 ? `${ncm.slice(0, 4)}.${ncm.slice(4, 6)}.${ncm.slice(6)}` : ncm ?? '')

export const DICAS = {
  ncm: 'Nomenclatura Comum do Mercosul: o código de 8 dígitos que classifica a mercadoria na nota fiscal. Obrigatório para matéria-prima, componente e produto acabado ativos.',
  cest: 'Código Especificador da Substituição Tributária, de 7 dígitos. Só preencha se a mercadoria estiver sujeita à substituição tributária.',
  origem: 'De onde vem a mercadoria, para o ICMS. Use 0 para item nacional; a contadora confirma os casos de importação.',
  lote: 'Cada entrada recebe um número de lote, e a saída diz de qual lote saiu. Útil para chapas, tintas e insumos com validade.',
  serie: 'Cada unidade tem um número de série próprio. Útil para o elevador e os componentes que têm garantia individual.',
  garantia: 'Meses de garantia dados ao cliente. Vem do padrão do tipo e pode ser trocado no item. Vazio = sem garantia.',
}
