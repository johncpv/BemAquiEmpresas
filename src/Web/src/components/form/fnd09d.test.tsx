import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { TreeTable } from '@/components/ui/tree-table'
import { ChipGroup, ToggleGroup } from './choice'
import { Field } from './form'
import { DateInput, MoneyInput } from './inputs'
import { RowsEditor } from './rows-editor'

const attr = (el: Element | null, name: string) => el?.getAttribute(name) ?? null

function Toggle() {
  const [v, setV] = useState<'PJ' | 'PF'>('PJ')
  return <ToggleGroup label="Tipo de pessoa" value={v} onChange={setV} options={[{ value: 'PJ', label: 'Jurídica' }, { value: 'PF', label: 'Física' }]} />
}

function Chips() {
  const [v, setV] = useState<string[]>([])
  return <ChipGroup legend="Papéis" value={v} onChange={setV} error={v.length ? undefined : 'Escolha ao menos um papel.'} options={[{ value: 'c', label: 'Cliente' }, { value: 'f', label: 'Fornecedor' }]} />
}

function Rows() {
  const [rows, setRows] = useState([{ d: '30' }, { d: '60' }])
  return <RowsEditor noun="parcela" rows={rows} onChange={setRows} newRow={() => ({ d: '' })} headers={['Dias']} summary={`${rows.length} parcelas`} renderRow={(r) => [<span key="d">{r.d}</span>]} />
}

describe('componentes da FND-09d', () => {
  it('ToggleGroup é um grupo de rádio: setas trocam e selecionam (CA 9)', () => {
    render(<Toggle />)
    const pj = screen.getByRole('radio', { name: 'Jurídica' })
    expect(attr(pj, 'aria-checked')).toBe('true')
    expect(attr(screen.getByRole('radio', { name: 'Física' }), 'tabindex')).toBe('-1')
    fireEvent.keyDown(pj, { key: 'ArrowRight' })
    expect(attr(screen.getByRole('radio', { name: 'Física' }), 'aria-checked')).toBe('true')
  })

  it('ChipGroup: fieldset com legenda, checkbox por chip e erro do grupo (CA 8)', () => {
    render(<Chips />)
    expect(attr(screen.getByRole('group', { name: 'Papéis' }), 'aria-invalid')).toBe('true')
    expect(screen.queryByText('Escolha ao menos um papel.')).not.toBeNull()
    fireEvent.click(screen.getByRole('checkbox', { name: /Cliente/ }))
    expect((screen.getByRole('checkbox', { name: /Cliente/ }) as HTMLInputElement).checked).toBe(true)
    expect(screen.queryByText('Escolha ao menos um papel.')).toBeNull()
  })

  it('RowsEditor nomeia cada remoção e anuncia o total (CA 12)', () => {
    render(<Rows />)
    fireEvent.click(screen.getByRole('button', { name: 'Remover parcela 1' }))
    expect(screen.queryByRole('button', { name: 'Remover parcela 2' })).toBeNull()
    expect(attr(screen.getByText('1 parcelas'), 'aria-live')).toBe('polite')
    fireEvent.click(screen.getByRole('button', { name: 'Adicionar parcela' }))
    expect(screen.queryByRole('button', { name: 'Remover parcela 2' })).not.toBeNull()
  })

  it('TreeTable: treegrid com aria-level e setas (CA 11)', () => {
    render(<TreeTable label="Plano de contas" nodes={[{ id: '1', label: 'Receitas', children: [{ id: '1.1', label: 'Vendas', children: [{ id: '1.1.1', label: 'Elevadores' }] }] }]} />)
    expect(screen.queryByRole('treegrid', { name: 'Plano de contas' })).not.toBeNull()
    const root = screen.getByText('Receitas').closest('tr')
    expect(attr(root, 'aria-level')).toBe('1')
    expect(attr(root, 'aria-expanded')).toBe('true')
    const child = screen.getByText('Vendas').closest('tr')
    expect(attr(child, 'aria-expanded')).toBe('false')
    fireEvent.keyDown(child!, { key: 'ArrowRight' })
    expect(attr(screen.getByText('Elevadores').closest('tr'), 'aria-level')).toBe('3')
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'eleva' } })
    expect(screen.queryByText('Receitas › Vendas')).not.toBeNull()
  })

  it('DateInput entrega aaaa-mm-dd e recusa data impossível (CA 13)', () => {
    const values: string[] = []
    render(<DateInput aria-label="Data" onChange={(v) => values.push(v)} />)
    fireEvent.change(screen.getByLabelText('Data'), { target: { value: '18102026' } })
    fireEvent.change(screen.getByLabelText('Data'), { target: { value: '31022026' } })
    expect(values).toEqual(['2026-10-18', ''])
  })

  it('MoneyInput formata ao sair e entrega string normalizada (CA 5)', () => {
    const values: (string | null)[] = []
    render(<MoneyInput aria-label="Valor" onChange={(v) => values.push(v)} />)
    const input = screen.getByLabelText('Valor') as HTMLInputElement
    fireEvent.focus(input)
    fireEvent.change(input, { target: { value: '1234,5' } })
    fireEvent.blur(input)
    expect(values.at(-1)).toBe('1234.50')
  })

  it('Field: obrigatório em texto e aria-required; aviso não é erro; Dica com o nome do campo (CA 14, 15, 20)', () => {
    render(<Field label="Saldo inicial" required warning="Saldo inicial negativo." dica="Saldo na data de abertura.">{(p) => <input {...p} />}</Field>)
    const input = screen.getByRole('textbox')
    expect(attr(input, 'aria-required')).toBe('true')
    expect(attr(input, 'aria-invalid')).toBe('false')
    expect(screen.getByText('(obrigatório)', { exact: false })).not.toBeNull()
    expect(screen.getByRole('status').textContent).toContain('Saldo inicial negativo.')
    expect(screen.getByRole('button', { name: 'Dica: Saldo inicial' }).className).toContain('size-6')
  })

  it('Inativo usa o tom neutro e Tag não carrega gravidade (CA 18)', () => {
    render(<><StatusChip tone="neutral">Inativo</StatusChip><Tag>Sem documento</Tag></>)
    expect(attr(screen.getByText('Inativo').closest('[data-tone]'), 'data-tone')).toBe('neutral')
    expect(screen.getByText('Sem documento').className).not.toMatch(/crit/)
  })
})
