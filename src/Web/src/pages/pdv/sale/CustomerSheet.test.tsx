import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DOCUMENT_SEARCH_MESSAGE } from '@/pdv/sale/customer-document'
import { CustomerSheet } from './sale-sheets'

/** PDV-02, revisão de segurança (achado 2): a busca do cliente é só por nome; termo com cara de CPF/CNPJ não vai ao servidor (iria na URL). */
function renderSheet(searchCustomers: (term: string) => Promise<{ id: string; name: string; document: string | null }[]>) {
  render(<CustomerSheet open online current={{ name: null, document: null }} searchCustomers={searchCustomers} onCancel={() => {}} onSave={async () => null} />)
  return screen.getByLabelText('Buscar cliente cadastrado')
}

describe('busca do cliente cadastrado (PDV-02 CA 15)', () => {
  it('CPF com ou sem pontuação não chama o servidor e orienta a buscar pelo nome', async () => {
    const search = vi.fn(async () => [])
    const field = renderSheet(search)
    expect(field.getAttribute('placeholder')).toBe('Nome do cliente (3 letras ou mais)')
    fireEvent.change(field, { target: { value: '529.982.247-25' } })
    fireEvent.click(screen.getByRole('button', { name: 'Buscar cliente' }))
    expect(await screen.findByText(DOCUMENT_SEARCH_MESSAGE)).toBeTruthy()
    fireEvent.change(field, { target: { value: '52998224725' } })
    fireEvent.keyDown(field, { key: 'Enter' })
    expect(search).not.toHaveBeenCalled()
  })

  it('nome vai ao servidor e mostra o documento já mascarado', async () => {
    const search = vi.fn(async () => [{ id: 'c1', name: 'Maria Souza', document: '***.***.247-**' }])
    const field = renderSheet(search)
    fireEvent.change(field, { target: { value: 'Maria' } })
    fireEvent.click(screen.getByRole('button', { name: 'Buscar cliente' }))
    expect(await screen.findByText('***.***.247-**')).toBeTruthy()
    expect(search).toHaveBeenCalledWith('Maria')
  })
})
