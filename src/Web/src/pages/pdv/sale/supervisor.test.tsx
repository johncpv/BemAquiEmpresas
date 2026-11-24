import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/api/http'
import { SupervisorDialog, type SupervisorApi, type SupervisorRequest } from './supervisor'

/** PDV-04 CA 10 a 12: "Chamar supervisor" mostra o resumo, só os supervisores que podem aprovar a ação, pede o PIN e mostra o erro com as tentativas. */
const request: SupervisorRequest = {
  action: 'Discount', title: 'Desconto acima do limite da loja', summary: 'Arroz: de R$ 20,00 para R$ 15,00', saleId: 's1', lineId: 'l1',
  beforeCents: 2000, afterCents: 1500, reason: 'Cliente fiel',
}

const supervisors = [
  { userId: 'u1', displayName: 'Sofia Supervisora', actions: ['discount', 'cancel_item'] },
  { userId: 'u2', displayName: 'Caio Caixa', actions: ['cancel_item'] },
]

function renderDialog(api: SupervisorApi, online = true) {
  const onApproved = vi.fn()
  const onCancel = vi.fn()
  render(<SupervisorDialog request={request} api={api} online={online} onApproved={onApproved} onCancel={onCancel} />)
  return { onApproved, onCancel }
}

const typePin = (pin: string) => { for (const d of pin) fireEvent.click(screen.getByRole('button', { name: d })) }

describe('diálogo "Chamar supervisor" (PDV-04)', () => {
  it('lista só quem aprova a ação e manda o pedido com os valores em reais', async () => {
    const approve = vi.fn(async () => ({ approvalId: 'a1', supervisorId: 'u1', supervisorName: 'Sofia Supervisora' }))
    const { onApproved } = renderDialog({ list: async () => supervisors, approve })
    expect(screen.getByText('Arroz: de R$ 20,00 para R$ 15,00')).toBeTruthy()
    fireEvent.click(await screen.findByRole('button', { name: 'Sofia Supervisora' }))
    expect(screen.queryByRole('button', { name: 'Caio Caixa' })).toBeNull()
    const aprovar = screen.getByRole('button', { name: 'Aprovar' })
    expect((aprovar as HTMLButtonElement).disabled).toBe(true)
    typePin('730284')
    fireEvent.click(screen.getByRole('button', { name: 'Aprovar' }))
    await vi.waitFor(() => expect(onApproved).toHaveBeenCalledWith(expect.objectContaining({ approvalId: 'a1' })))
    expect(approve).toHaveBeenCalledWith({
      action: 'Discount', supervisorId: 'u1', pin: '730284', reason: 'Cliente fiel', saleId: 's1', lineId: 'l1', beforeAmount: 20, afterAmount: 15,
    })
  })

  it('PIN errado mostra a mensagem do servidor com as tentativas e limpa o PIN', async () => {
    const approve = vi.fn(async () => { throw new ApiError(422, { code: 'auth.pin_invalid', detail: 'PIN incorreto. Restam 4 tentativas antes do bloqueio.' }) })
    renderDialog({ list: async () => supervisors, approve })
    fireEvent.click(await screen.findByRole('button', { name: 'Sofia Supervisora' }))
    typePin('000001')
    fireEvent.click(screen.getByRole('button', { name: 'Aprovar' }))
    expect(await screen.findByText('PIN incorreto. Restam 4 tentativas antes do bloqueio.')).toBeTruthy()
    expect(screen.getByLabelText('PIN do supervisor: 0 de 6 números')).toBeTruthy()
  })

  it('sem supervisor para a ação, orienta a criar o PIN', async () => {
    renderDialog({ list: async () => [supervisors[1]], approve: vi.fn() })
    expect(await screen.findByText(/Nenhum supervisor desta loja tem PIN do caixa/)).toBeTruthy()
  })

  it('sem rede não busca os supervisores', () => {
    const list = vi.fn(async () => supervisors)
    renderDialog({ list, approve: vi.fn() }, false)
    expect(screen.getByText(/a aprovação do supervisor precisa de rede/)).toBeTruthy()
    expect(list).not.toHaveBeenCalled()
  })
})
