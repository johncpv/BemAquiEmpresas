import { act, fireEvent, render, renderHook, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/api/http'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { toastDone } from '@/components/ui/toast'
import { z } from '@/lib/zod'
import { applyServerErrors, useZodForm } from './form-utils'

vi.mock('@/components/ui/toast', () => ({ toastDone: vi.fn() }))

describe('formulário (FND-09c CA 8 e 9)', () => {
  it('mensagens do Zod em português', () => {
    const result = z.object({ email: z.email() }).safeParse({ email: 'x' })
    expect(result.success).toBe(false)
    expect(result.error?.issues[0].message.toLowerCase()).toContain('inválid')
  })

  it('erro de campo do servidor vai para o campo; o resto vai para o topo', () => {
    const { result } = renderHook(() => useZodForm(z.object({ email: z.string() }), { defaultValues: { email: '' } }))
    act(() => {
      applyServerErrors(result.current, new ApiError(422, { detail: 'Dados inválidos.', errors: { email: ['Já existe.'], outro: ['x'] } }))
    })
    expect(result.current.getFieldState('email').error?.message).toBe('Já existe.')
    expect(result.current.getFieldState('root.server' as 'email').error?.message).toBe('Dados inválidos.')
  })
})

describe('ConfirmDialog (CA 11)', () => {
  it('confirma com o mesmo verbo e avisa com o particípio', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    render(<ConfirmDialog verb="Desativar" done="Desativado" title="Desativar Ana?" onConfirm={onConfirm} trigger={<button type="button">Abrir</button>} />)
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    const confirm = await screen.findByRole('button', { name: 'Desativar' })
    await act(async () => {
      fireEvent.click(confirm)
    })
    expect(onConfirm).toHaveBeenCalled()
    expect(toastDone).toHaveBeenCalledWith('Desativado', undefined)
  })
})

describe('histórico (CA 13)', () => {
  it('descreve antes → depois e esconde segredo', () => {
    expect(describeChanges('{"name":{"old":"A","new":"B"},"passwordHash":{"changed":true}}')).toEqual(['name: A → B', 'passwordHash: alterado'])
    expect(describeChanges('{"before":["x.view"],"after":["x.view","x.edit"]}')).toEqual(['["x.view"] → ["x.view","x.edit"]'])
  })
})
