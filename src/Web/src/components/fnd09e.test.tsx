import type { ColumnDef } from '@tanstack/react-table'
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/api/http'
import { renderWithRouter } from '@/test-utils'
import { DataTable, type TableQuery } from './data-table/DataTable'
import { applyServerErrors, toFormPath, useZodForm } from './form/form-utils'
import { RowsEditor } from './form/rows-editor'
import { z } from '@/lib/zod'
import { Chart } from './ui/chart'
import { ConfirmDialog } from './ui/confirm-dialog'
import { MessageRegion, SUCCESS_MS } from './ui/message-region'
import { clearMessages, notifyError, notifySuccess } from './ui/messages'

afterEach(() => {
  act(() => clearMessages())
  vi.useRealTimers()
})

describe('região de mensagens (ADR 0021, CA 1 e 2)', () => {
  it('sucesso em role=status, some depois de 5 s; até 3, a mais nova em cima', () => {
    vi.useFakeTimers()
    render(<MessageRegion />)
    act(() => { notifySuccess('Um'); notifySuccess('Dois'); notifySuccess('Três'); notifySuccess('Quatro') })
    const status = screen.getByRole('status')
    expect(status.textContent).toContain('Quatro')
    expect(status.textContent).not.toContain('Um')
    expect(status.firstElementChild?.textContent).toContain('Quatro')
    act(() => { vi.advanceTimersByTime(SUCCESS_MS - 100) })
    expect(screen.getByRole('status').textContent).toContain('Quatro')
    act(() => { vi.advanceTimersByTime(200) })
    expect(screen.getByRole('status').textContent).toBe('')
  })

  it('erro em role=alert não some e oferece Tentar de novo', () => {
    vi.useFakeTimers()
    const retry = vi.fn()
    render(<MessageRegion />)
    act(() => notifyError('Sem conexão com o servidor.', retry))
    act(() => { vi.advanceTimersByTime(60_000) })
    expect(screen.getByRole('alert').textContent).toContain('Sem conexão com o servidor.')
    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    expect(retry).toHaveBeenCalled()
  })
})

describe('ConfirmDialog grande (CA 4 a 7)', () => {
  it('pré-condição pendente desabilita o botão e diz o que falta', async () => {
    render(<ConfirmDialog verb="Aprovar" done="Aprovado" title="Aprovar o orçamento?" size="lg" onConfirm={vi.fn()} preconditions={[{ ok: true, text: 'Revisão corrente' }, { ok: false, text: 'Os marcos somam 90,00%.' }]} trigger={<button type="button">Abrir</button>} />)
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    const confirm = await screen.findByRole('button', { name: 'Aprovar' })
    expect(confirm.hasAttribute('disabled')).toBe(true)
    expect(document.getElementById(confirm.getAttribute('aria-describedby') ?? '')?.textContent).toContain('Os marcos somam 90,00%.')
  })

  it('erro de regra fica no diálogo com foco; 409 de concorrência oferece Recarregar', async () => {
    const onReload = vi.fn().mockResolvedValue(undefined)
    const onConfirm = vi.fn().mockRejectedValue(new ApiError(409, { detail: 'O título mudou desde que você abriu. Recarregue.', code: 'concurrency_conflict' }))
    render(<ConfirmDialog verb="Dar baixa" done="Baixa registrada" title="Dar baixa?" onConfirm={onConfirm} onReload={onReload} trigger={<button type="button">Abrir</button>} />)
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: 'Dar baixa' })) })
    const alert = screen.getByRole('alert')
    expect(alert.textContent).toContain('mudou desde que você abriu')
    expect(document.activeElement).toBe(alert)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Recarregar' })) })
    expect(onReload).toHaveBeenCalled()
  })

  it('422 com campos vai para os campos pelo onFieldErrors, sem repetir no topo', async () => {
    const onFieldErrors = vi.fn().mockReturnValue(true)
    const onConfirm = vi.fn().mockRejectedValue(new ApiError(422, { detail: 'Dados inválidos.', errors: { amount: ['Informe o valor.'] } }))
    render(<ConfirmDialog verb="Pagar" done="Pago" title="Pagar?" onConfirm={onConfirm} onFieldErrors={onFieldErrors} trigger={<button type="button">Abrir</button>} />)
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: 'Pagar' })) })
    expect(onFieldErrors).toHaveBeenCalled()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('sem 2FA configurado: estado único, sem confirmar (CA 7)', async () => {
    render(<ConfirmDialog verb="Aprovar" done="Aprovada" title="Aprovar a OP?" twoFactorRequired onConfirm={vi.fn()} trigger={<button type="button">Abrir</button>} />)
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    expect(await screen.findByText(/Para aprovar, ative a verificação em duas etapas/)).toBeDefined()
    expect(screen.getByRole('link', { name: 'Ativar agora' })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Aprovar' }).hasAttribute('disabled')).toBe(true)
  })
})

describe('applyServerErrors aninhado (CA 8)', () => {
  it('erro de linha vai para o campo da linha', () => {
    expect(toFormPath('lines[2].unitCost')).toBe('lines.2.unitCost')
    const { result } = renderHook(() => useZodForm(z.object({ milestones: z.array(z.object({ percent: z.string() })) }), { defaultValues: { milestones: [{ percent: '50' }, { percent: '40' }] } }))
    act(() => { applyServerErrors(result.current, new ApiError(422, { detail: 'Dados inválidos.', errors: { 'milestones[1].percent': ['A soma precisa dar 100,00%.'] } })) })
    expect(result.current.getFieldState('milestones.1.percent').error?.message).toBe('A soma precisa dar 100,00%.')
    expect(result.current.getFieldState('root.server' as 'milestones').error).toBeUndefined()
  })
})

function Lines() {
  const [rows, setRows] = useState([{ name: 'A' }, { name: 'B' }, { name: 'C' }])
  return (
    <RowsEditor noun="linha" rows={rows} onChange={setRows} newRow={() => ({ name: '' })} rowError={(i) => (i === 1 ? 'Custo obrigatório.' : undefined)}
      columns={[{ label: 'Nome', width: '2fr', render: (r, i, update) => <input aria-label={`Nome ${i + 1}`} value={r.name} onChange={(e) => update({ name: e.target.value })} /> }]} />
  )
}

describe('RowsEditor com modelo de colunas (CA 9)', () => {
  it('erro por linha e foco na linha seguinte depois de remover', async () => {
    render(<Lines />)
    expect(screen.getByText('Custo obrigatório.')).toBeDefined()
    const typed = screen.getByLabelText('Nome 3') as HTMLInputElement
    fireEvent.change(typed, { target: { value: 'C editado' } })
    fireEvent.click(screen.getByRole('button', { name: 'Remover linha 2' }))
    await waitFor(() => expect(document.activeElement).toBe(screen.getByLabelText('Nome 2')))
    expect((screen.getByLabelText('Nome 2') as HTMLInputElement).value).toBe('C editado')
    fireEvent.click(screen.getByRole('button', { name: 'Remover linha 2' }))
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: /Adicionar linha/ })))
  })
})

type Row = { id: string; nome: string; valor: number }
const columns: ColumnDef<Row>[] = [{ id: 'nome', header: 'Nome', cell: ({ row }) => row.original.nome }, { id: 'valor', header: 'Valor', meta: { numeric: true }, cell: ({ row }) => row.original.valor }]
const query: TableQuery<Row> = { isPending: false, isError: false, refetch: () => {}, data: { items: [{ id: 'a', nome: 'Ana', valor: 10 }, { id: 'b', nome: 'Bia', valor: 20 }], page: 1, pageSize: 20, total: 2 } }

function Selectable() {
  const [selected, setSelected] = useState<string[]>([])
  return <DataTable label="Títulos" columns={columns} query={query} getRowId={(r) => r.id} empty={{ title: 'Nada.' }} selectable={{ selected, onChange: setSelected, rowLabel: (r) => r.nome }} totals={{ valor: 'R$ 30,00' }} expandable={{ label: (r) => r.nome, render: (r) => <p>Detalhe de {r.nome}</p> }} />
}

describe('DataTable com seleção, totais e linha expansível (CA 10)', () => {
  it('seleciona a página, anuncia o total e expande por teclado', async () => {
    renderWithRouter(() => <Selectable />)
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Selecionar todos desta página' }))
    expect(screen.getByText('2 selecionados')).toBeDefined()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Selecionar Ana' }))
    expect(screen.getByText('1 selecionado')).toBeDefined()
    expect(screen.getByText('Total do filtro')).toBeDefined()
    expect(screen.getByText('R$ 30,00')).toBeDefined()
    const toggle = screen.getByRole('button', { name: 'Detalhes de Bia' })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByText('Detalhe de Bia')).toBeDefined()
  })
})

describe('Chart (CA 11)', () => {
  it('traz a tabela equivalente ligada ao gráfico', () => {
    render(<Chart title="Saldo projetado" xKey="dia" xLabel="Dia" data={[{ dia: '01/11', real: 100, proj: 90 }]} series={[{ key: 'real', label: 'Realizado' }, { key: 'proj', label: 'Projetado', kind: 'projected' }]} format={(v) => `R$ ${v}`} />)
    const img = screen.getByRole('img', { name: 'Saldo projetado' })
    const table = document.getElementById(img.getAttribute('aria-describedby') ?? '')
    expect(table?.tagName).toBe('TABLE')
    expect(table?.textContent).toContain('R$ 90')
    expect(screen.getAllByText('Projetado').length).toBeGreaterThan(0)
  })
})

describe('menu do Financeiro em dois grupos (CA 12)', () => {
  it('operação primeiro, configuração depois, cada item no seu grupo', async () => {
    const { FinanceTabs } = await import('@/pages/finance/FinanceTabs')
    renderWithRouter(() => <FinanceTabs />)
    const cash = await screen.findByRole('group', { name: 'Caixa e pagamentos' })
    const setup = screen.getByRole('group', { name: 'Configuração financeira' })
    expect(cash.textContent).toContain('Contas e caixas')
    expect(setup.textContent).toContain('Plano de contas')
    expect(setup.textContent).toContain('Condições de pagamento')
    expect(cash.compareDocumentPosition(setup) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
