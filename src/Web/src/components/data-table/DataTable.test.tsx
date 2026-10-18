import type { ColumnDef } from '@tanstack/react-table'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderWithRouter } from '@/test-utils'
import { DataTable, type TableQuery } from './DataTable'

type Row = { id: string; nome: string }
const columns: ColumnDef<Row>[] = [{ accessorKey: 'nome', header: 'Nome', meta: { sortField: 'nome' } }]
const base: TableQuery<Row> = { isPending: false, isError: false, refetch: () => {} }
const table = (query: TableQuery<Row>) => () => <DataTable label="Teste" columns={columns} query={query} empty={{ title: 'Nada ainda.', action: <button type="button">Começar</button> }} />

describe('DataTable (FND-09c CA 4 e 6)', () => {
  it('carregando mostra linhas fantasmas', async () => {
    renderWithRouter(table({ ...base, isPending: true }))
    expect(await screen.findByLabelText('Carregando')).toBeDefined()
  })

  it('vazio mostra a ação para começar', async () => {
    renderWithRouter(table({ ...base, data: { items: [], page: 1, pageSize: 20, total: 0 } }))
    expect(await screen.findByText('Nada ainda.')).toBeDefined()
    expect(screen.getByRole('button', { name: 'Começar' })).toBeDefined()
  })

  it('filtro sem resultado oferece Limpar filtros', async () => {
    const { router } = renderWithRouter(table({ ...base, data: { items: [], page: 1, pageSize: 20, total: 0 } }), '/?q=zzz')
    expect(await screen.findByText('Nenhum resultado para os filtros.')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Limpar filtros' }))
    await waitFor(() => expect(router.state.location.search).toEqual({}))
  })

  it('erro mostra Tentar de novo ligado ao refetch', async () => {
    const refetch = vi.fn()
    renderWithRouter(table({ ...base, isError: true, refetch }))
    fireEvent.click(await screen.findByRole('button', { name: 'Tentar de novo' }))
    expect(refetch).toHaveBeenCalled()
  })

  it('ordenar e paginar escrevem na URL', async () => {
    const { router } = renderWithRouter(table({ ...base, data: { items: [{ id: '1', nome: 'Ana' }], page: 1, pageSize: 20, total: 45 } }))
    fireEvent.click(await screen.findByRole('button', { name: 'Nome' }))
    await waitFor(() => expect(router.state.location.search).toEqual({ sort: 'nome' }))
    fireEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await waitFor(() => expect(router.state.location.search).toEqual({ sort: 'nome', page: 2 }))
    expect(screen.getByText(/Página 1 de 3 · 45 registros/)).toBeDefined()
  })
})
