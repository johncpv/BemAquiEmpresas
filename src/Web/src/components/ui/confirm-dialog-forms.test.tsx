import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ConfirmDialog } from './confirm-dialog'
import { remainingText } from './remaining-text'
import { clearMessages } from './messages'
import { ToConfirmChip } from './to-confirm-chip'

afterEach(() => {
  cleanup()
  act(() => clearMessages())
  vi.useRealTimers()
})

const open = () => fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))

describe('ConfirmDialog nas três formas da D18 (BNK-01b CA 20)', () => {
  it('impacto em lista: afetados, recusados com o motivo e o total; o verbo é o da ação', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    render(<ConfirmDialog verb="Emitir 2 boletos" done="2 boletos solicitados" title="Emitir boletos" size="lg" onConfirm={onConfirm} trigger={<button type="button">Abrir</button>}
      impactList={{ label: 'Vão receber boleto', items: [{ key: 'a', primary: 'CR-1 · Alfa' }, { key: 'b', primary: 'CR-2 · Beta', secondary: 'vence 10/11/2026' }], refused: [{ key: 'c', primary: 'CR-3 · Gama', reason: 'O cliente está sem CPF/CNPJ.' }], total: 'R$ 300,00' }} />)
    open()
    const affected = await screen.findByRole('region', { name: 'Vão receber boleto' })
    expect(affected.textContent).toContain('CR-1 · Alfa')
    expect(affected.textContent).toContain('vence 10/11/2026')
    expect(affected.textContent).toContain('Total: R$ 300,00')
    expect(screen.getByRole('region', { name: 'Recusados' }).textContent).toContain('O cliente está sem CPF/CNPJ.')
    const confirm = screen.getByRole('button', { name: 'Emitir 2 boletos' })
    confirm.focus()
    expect(document.activeElement).toBe(confirm)
    fireEvent.click(confirm)
    await vi.waitFor(() => expect(onConfirm).toHaveBeenCalled())
  })

  it('confirmação digitada: o botão só habilita com a palavra exata, pelo teclado', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    render(<ConfirmDialog verb="Excluir dados" done="Dados excluídos" title="Excluir os dados pessoais?" destructive onConfirm={onConfirm} typed={{ word: 'EXCLUIR' }} trigger={<button type="button">Abrir</button>} />)
    open()
    const input = await screen.findByLabelText('Para confirmar, digite EXCLUIR')
    const confirm = screen.getByRole('button', { name: 'Excluir dados' })
    expect(confirm.hasAttribute('disabled')).toBe(true)
    fireEvent.change(input, { target: { value: 'excluir' } })
    expect(confirm.hasAttribute('disabled')).toBe(true)
    fireEvent.change(input, { target: { value: 'EXCLUIR' } })
    expect(confirm.hasAttribute('disabled')).toBe(false)
    fireEvent.click(confirm)
    await vi.waitFor(() => expect(onConfirm).toHaveBeenCalled())
  })

  it('contagem de prazo: mostra quanto falta, atualiza a cada minuto e bloqueia ao zerar', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const until = new Date(Date.now() + 2 * 60_000 + 5_000)
    render(<ConfirmDialog verb="Cancelar nota" done="Nota cancelada" title="Cancelar a nota?" destructive onConfirm={vi.fn()} deadline={{ until, purpose: 'para cancelar' }} trigger={<button type="button">Abrir</button>} />)
    open()
    const timer = await screen.findByRole('timer')
    expect(timer.textContent).toBe('Restam 2 min para cancelar')
    act(() => { vi.advanceTimersByTime(60_000) })
    expect(screen.getByRole('timer').textContent).toBe('Restam 1 min para cancelar')
    expect(screen.getByRole('button', { name: 'Cancelar nota' }).hasAttribute('disabled')).toBe(false)
    act(() => { vi.advanceTimersByTime(120_000) })
    expect(screen.getByRole('timer').textContent).toBe('O prazo para cancelar terminou.')
    expect(screen.getByRole('button', { name: 'Cancelar nota' }).hasAttribute('disabled')).toBe(true)
  })

  it('texto do prazo', () => {
    expect(remainingText(3 * 3_600_000 + 12 * 60_000)).toBe('3 h 12 min')
    expect(remainingText(45 * 60_000 + 30_000)).toBe('45 min')
    expect(remainingText(30_000)).toBe('menos de 1 min')
  })

  it('o foco entra no diálogo e o Esc fecha', async () => {
    render(<ConfirmDialog verb="Emitir boleto" done="" title="Emitir boleto" onConfirm={vi.fn()} trigger={<button type="button">Abrir</button>} />)
    open()
    const dialog = await screen.findByRole('alertdialog')
    await vi.waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true))
    fireEvent.keyDown(dialog, { key: 'Escape' })
    await vi.waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
  })
})

describe('ToConfirmChip (ADR 0042, BNK-01b CA 2)', () => {
  it('a confirmar: selo e "Marcar como conferido" só para quem edita', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    const { rerender } = render(<ToConfirmChip state="ToConfirm" canEdit onConfirm={onConfirm} field="Multa" />)
    expect(screen.getByText('A confirmar com a contadora')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Marcar como conferido: Multa' }))
    await vi.waitFor(() => expect(onConfirm).toHaveBeenCalled())
    rerender(<ToConfirmChip state="ToConfirm" canEdit={false} onConfirm={onConfirm} field="Multa" />)
    expect(screen.queryByRole('button', { name: /Marcar como conferido/ })).toBeNull()
  })

  it('conferido: mostra quem conferiu, sem o selo', () => {
    render(<ToConfirmChip state="Confirmed" reviewedByName="Ana" canEdit onConfirm={vi.fn()} />)
    expect(screen.getByText(/Conferido por Ana/)).toBeTruthy()
    expect(screen.queryByText('A confirmar com a contadora')).toBeNull()
  })
})
