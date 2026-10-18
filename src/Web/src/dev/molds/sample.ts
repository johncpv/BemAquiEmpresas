import type { ComboOption } from '@/components/form/combobox'
import type { TreeNode } from '@/components/ui/tree-table'

/** Dados de exemplo dos moldes (FND-09d CA 2). Nada aqui vai para a API. */
export const PAYMENT_TERMS: ComboOption[] = [
  { id: 'avista', label: 'À vista' },
  { id: '30', label: '30 dias' },
  { id: '30-60', label: '30/60 dias', hint: '2 parcelas iguais' },
  { id: '30-60-90', label: '30/60/90 dias', hint: '3 parcelas iguais' },
]

export const searchTerms = async (term: string): Promise<ComboOption[]> => {
  await new Promise((r) => setTimeout(r, 300))
  const t = term.toLowerCase()
  return PAYMENT_TERMS.filter((o) => o.label.toLowerCase().includes(t))
}

export const ROLES = [
  { value: 'cliente', label: 'Cliente' },
  { value: 'fornecedor', label: 'Fornecedor' },
  { value: 'instalador', label: 'Instalador' },
  { value: 'transportadora', label: 'Transportadora' },
] as const

export interface Account extends TreeNode {
  code: string
  kind: string
  children?: Account[]
}

export const ACCOUNTS: Account[] = [
  { id: '1', code: '1', label: 'Receitas', kind: 'Receita', children: [
    { id: '1.1', code: '1.1', label: 'Receita de vendas', kind: 'Receita', children: [
      { id: '1.1.01', code: '1.1.01', label: 'Venda de elevadores', kind: 'Receita' },
      { id: '1.1.02', code: '1.1.02', label: 'Venda de componentes', kind: 'Receita' },
    ] },
    { id: '1.2', code: '1.2', label: 'Receita de serviços', kind: 'Receita', children: [{ id: '1.2.01', code: '1.2.01', label: 'Instalação', kind: 'Receita' }] },
  ] },
  { id: '2', code: '2', label: 'Custos', kind: 'Despesa', children: [
    { id: '2.1', code: '2.1', label: 'Matéria-prima', kind: 'Despesa', children: [{ id: '2.1.01', code: '2.1.01', label: 'Aço e chapas', kind: 'Despesa' }] },
    { id: '2.2', code: '2.2', label: 'Mão de obra direta', kind: 'Despesa' },
  ] },
]

export interface Unit {
  id: string
  code: string
  name: string
  decimals: number
  active: boolean
}

export const UNITS: Unit[] = [
  { id: 'un', code: 'UN', name: 'Unidade', decimals: 0, active: true },
  { id: 'kg', code: 'KG', name: 'Quilograma', decimals: 3, active: true },
  { id: 'm', code: 'M', name: 'Metro', decimals: 2, active: true },
  { id: 'm2', code: 'M2', name: 'Metro quadrado', decimals: 2, active: true },
  { id: 'cx', code: 'CX', name: 'Caixa', decimals: 0, active: false },
]
