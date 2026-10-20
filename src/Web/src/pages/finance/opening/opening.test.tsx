import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import type { OpeningStatus } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { RowsEditor } from '@/components/form/rows-editor'
import { toNumber } from '../receivables/receivables'
import { currentStep, emptyLine, lineErrors, localLineErrors, reconciliationCsv } from './opening'

const base: OpeningStatus = {
  cutoverDate: '2026-09-30', suggestedCutover: '2026-09-30', completedAt: null, notes: null, hasMovement: false, version: 1,
  accounts: [{ id: 'a', name: 'Inter; PJ', openingBalance: 1500.25, openingDate: '2026-09-30', hasMovement: false, systemBalanceAtCutover: 1500.25 }],
  receivables: [{ partyId: 'c', partyName: 'Cliente A', count: 2, total: 2800.5 }],
  payables: [],
}

describe('implantação financeira (FIN-00)', () => {
  it('o passo atual é o primeiro que falta', () => {
    expect(currentStep({ ...base, cutoverDate: null })).toBe(0)
    expect(currentStep({ ...base, accounts: [{ ...base.accounts[0], openingBalance: null }] })).toBe(1)
    expect(currentStep(base)).toBe(3)
    expect(currentStep({ ...base, completedAt: '2026-10-20T10:00:00Z' })).toBe(4)
  })

  it('o 422 do lote vai para a linha certa (CA 7)', () => {
    const parsed = lineErrors(new ApiError(422, { detail: 'Dados inválidos.', errors: { 'lines[3].balance': ['Informe um saldo maior que zero.'] } }))
    expect(parsed.byLine).toEqual({ 3: 'Informe um saldo maior que zero.' })
    expect(parsed.general).toBeUndefined()
    expect(lineErrors(new ApiError(409, { detail: 'A implantação foi concluída em 20/10/2026.' })).general).toBe('A implantação foi concluída em 20/10/2026.')
  })

  it('confere as linhas antes de enviar', () => {
    const ok = { ...emptyLine(), party: { id: 'c', label: 'Cliente' }, description: 'Fatura', dueDate: '2026-10-10', balance: '10.00' }
    expect(localLineErrors([ok, { ...ok, balance: '0' }], toNumber)).toEqual({ 1: 'Informe um saldo maior que zero.' })
  })

  it('CSV da conferência com ";" e diferença arredondada (CA 9)', () => {
    const csv = reconciliationCsv(base, { a: '1500.20' }).split('\r\n')
    expect(csv[0]).toBe('Tipo;Nome;Quantidade;Saldo do sistema;Saldo no extrato;Diferença')
    expect(csv[1]).toBe('Conta;"Inter; PJ";;1.500,25;1.500,20;-0,05')
    expect(csv[2]).toBe('A receber;Cliente A;2;2.800,50;;')
  })
})

function Lines() {
  const [rows, setRows] = useState([{ name: 'Fatura 1' }])
  return <RowsEditor noun="título" rows={rows} onChange={setRows} newRow={() => ({ name: '' })} duplicate columns={[{ label: 'Nome', render: (r, i, set) => <input aria-label={`Nome ${i + 1}`} value={r.name} onChange={(e) => set({ name: e.target.value })} /> }]} />
}

describe('RowsEditor: duplicar', () => {
  it('a cópia entra logo abaixo', () => {
    render(<Lines />)
    fireEvent.click(screen.getByRole('button', { name: 'Duplicar título 1' }))
    expect((screen.getByLabelText('Nome 2') as HTMLInputElement).value).toBe('Fatura 1')
  })
})
