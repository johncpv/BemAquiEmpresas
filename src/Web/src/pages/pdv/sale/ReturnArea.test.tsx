import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { ApiError } from '@/api/http'
import { lookupBody, RETURN_NOT_FOUND } from '@/pdv/sale/return-model'
import { ReturnArea } from './ReturnArea'

const api = vi.hoisted(() => ({ lookupSaleForReturn: vi.fn(), requestSaleReturn: vi.fn(), approveSaleReturn: vi.fn() }))
vi.mock('@/api/generated/bemaqui', () => api)
vi.mock('@/pdv/print/print-jobs', async (original) => ({ ...(await original<typeof import('@/pdv/print/print-jobs')>()), printDocument: vi.fn(async () => ({ ok: true })) }))

const store = { getMeta: async () => undefined, list: () => [] } as unknown as LocalStore

const candidate = {
  saleId: 's1', number: 'LOJA-01.1-000007', completedAt: '2026-11-20T15:00:00Z', businessDate: '2026-11-20', total: 30, status: 'Completed', deadline: '2026-12-20',
  late: false, lateMessage: null, cardPayments: [],
  lines: [{ saleLineId: 'l1', description: 'Camiseta', unitCode: 'UN', sold: 2, returned: 0, returnable: 2, lineTotal: 30, unitAmount: 15 }],
}

const done = {
  id: 'r1', number: 'DEV-2026-0001', saleId: 's1', saleNumber: candidate.number, status: 'Completed', refundKind: 'StoreCredit', total: 15, late: false, deadline: '2026-12-20',
  lateMessage: null, needsSupervisor: false, isExchange: true, cardNsu: null, requestedAt: '2026-11-24T12:00:00Z', completedAt: '2026-11-24T12:00:01Z',
  lines: [{ id: 'x', saleLineId: 'l1', description: 'Camiseta', quantity: 1, amount: 15, destination: 'Restock', reason: 'Tamanho errado', stockNote: null }],
  storeCredit: { id: 'c1', code: 'VT-ABCD-EFGH-JKMN', codeLast4: 'JKMN', amount: 15, balance: 15, expiresOn: '2027-02-22' },
}

function renderArea(online = true) {
  const onExchange = vi.fn()
  render(<ReturnArea store={store} online={online} place="Loja Centro · Caixa 01" operatorName="Caio" supervisor={null} onBack={vi.fn()} onExchange={onExchange} />)
  return { onExchange }
}

describe('devolução no caixa (PDV-06)', () => {
  beforeEach(() => { api.lookupSaleForReturn.mockReset(); api.requestSaleReturn.mockReset(); api.approveSaleReturn.mockReset() })

  it('número do cupom ou CPF: o CPF vai como documento, no corpo', () => {
    expect(lookupBody('529.982.247-25')).toEqual({ number: null, document: '52998224725' })
    expect(lookupBody(' loja-01.1-000007 ')).toEqual({ number: 'LOJA-01.1-000007', document: null })
    expect(lookupBody('  ')).toBeNull()
  })

  it('localiza, escolhe item, destino e motivo, conclui em vale com troca e oferece a venda nova com o vale', async () => {
    api.lookupSaleForReturn.mockResolvedValue({ sales: [candidate], cashSessionOpen: true })
    api.requestSaleReturn.mockResolvedValue({ ...done, status: 'Requested', storeCredit: null, completedAt: null })
    api.approveSaleReturn.mockResolvedValue(done)
    const { onExchange } = renderArea()
    fireEvent.change(screen.getByLabelText('Número do cupom ou CPF do cliente'), { target: { value: candidate.number } })
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Mais Camiseta' }))
    fireEvent.click(screen.getByRole('button', { name: 'Tamanho errado' }))
    fireEvent.click(screen.getByLabelText(/Troca: o cliente já vai levar outro produto/))
    expect(screen.getByRole('button', { name: 'Vale-troca' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Concluir devolução' }))
    expect(await screen.findByText('Devolução DEV-2026-0001 concluída')).toBeTruthy()
    expect(api.requestSaleReturn).toHaveBeenCalledWith(expect.objectContaining({
      saleId: 's1', refundKind: 'StoreCredit', exchange: true, lines: [{ saleLineId: 'l1', quantity: 1, destination: 'Restock', reason: 'Tamanho errado' }],
    }))
    expect(screen.getByText('VT-ABCD-EFGH-JKMN')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Venda nova com o vale' }))
    expect(onExchange).toHaveBeenCalledWith('VT-ABCD-EFGH-JKMN')
  })

  it('sem venda, a mensagem do vazio', async () => {
    api.lookupSaleForReturn.mockResolvedValue({ sales: [], cashSessionOpen: true })
    renderArea()
    fireEvent.change(screen.getByLabelText('Número do cupom ou CPF do cliente'), { target: { value: '111.444.777-35' } })
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    expect(await screen.findByText(RETURN_NOT_FOUND)).toBeTruthy()
  })

  it('sem rede, a devolução fica indisponível com "Disponível com internet." (CA 11)', () => {
    renderArea(false)
    expect(screen.getByText(/Disponível com internet\./)).toBeTruthy()
    expect(screen.queryByLabelText('Número do cupom ou CPF do cliente')).toBeNull()
    expect(api.lookupSaleForReturn).not.toHaveBeenCalled()
  })

  it('prazo vencido entre o pedido e a conclusão: o servidor pede o supervisor e o caixa chama (revisão de segurança, achado 10)', async () => {
    api.lookupSaleForReturn.mockResolvedValue({ sales: [candidate], cashSessionOpen: true })
    api.requestSaleReturn.mockResolvedValue({ ...done, status: 'Requested', storeCredit: null, completedAt: null })
    api.approveSaleReturn.mockRejectedValue(new ApiError(422, { detail: 'Prazo de devolução encerrado em 20/12/2026. Chame o supervisor.', code: 'supervisor_required' }))
    renderArea()
    fireEvent.change(screen.getByLabelText('Número do cupom ou CPF do cliente'), { target: { value: candidate.number } })
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Mais Camiseta' }))
    fireEvent.click(screen.getByRole('button', { name: 'Tamanho errado' }))
    fireEvent.click(screen.getByRole('button', { name: 'Concluir devolução' }))
    expect(await screen.findByText('Devolução fora do prazo')).toBeTruthy()
    expect(screen.queryByText(/Chame o supervisor\./)).toBeNull()
  })
})
