import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { isSafeReturn, safeReturn } from '@/auth/pending-challenge'
import { QrScanner } from '@/components/shopfloor/QrScanner'
import { exportLabel } from '@/components/history/describe-changes'
import { CAMERA_FALLBACK, cardRouteFor, labelSizeError, tagFrom } from './labels'

describe('etiqueta e leitura (PAT-01b)', () => {
  it('a leitura vira sempre a etiqueta, de qualquer host, nunca o endereço lido (CA 5 e 9)', () => {
    expect(tagFrom('https://app.konebras.com.br/b/PAT-00012')).toBe('PAT-00012')
    expect(tagFrom('https://outro.exemplo/b/pat-00012?x=1')).toBe('PAT-00012')
    expect(tagFrom('/b/PAT-00012')).toBe('PAT-00012')
    expect(tagFrom(' pat-00012 ')).toBe('PAT-00012')
    expect(tagFrom('12')).toBe('PAT-00012')
    expect(tagFrom('00012')).toBe('PAT-00012')
    expect(tagFrom('https://evil.example/login')).toBeNull()
    expect(tagFrom('javascript:alert(1)')).toBeNull()
    expect(tagFrom('https://x/b/PAT-1/../../app')).toBeNull()
    expect(tagFrom('')).toBeNull()
  })

  it('o tamanho aceita largura de 30 a 150 e altura de 15 a 150 mm (CA 1)', () => {
    expect(labelSizeError('50', '25')).toBeUndefined()
    expect(labelSizeError('29', '25')).toMatch(/largura de 30 a 150/)
    expect(labelSizeError('50', '151')).toBeDefined()
    expect(labelSizeError('', '25')).toBeDefined()
  })

  it('no toque abre a ficha resumida do /fab; no computador, a ficha completa', () => {
    expect(cardRouteFor('a1', true)).toBe('/fab/bens/a1')
    expect(cardRouteFor('a1', false)).toBe('/app/patrimonio/bens/a1')
  })

  it('o login só volta para caminho relativo do app: /b, /fab ou /app (CA 8 [Segurança])', () => {
    expect(safeReturn('/b/PAT-00001')).toBe('/b/PAT-00001')
    expect(safeReturn('/fab/bens/a1')).toBe('/fab/bens/a1')
    expect(safeReturn('/app/patrimonio/bens?lote=1')).toBe('/app/patrimonio/bens?lote=1')
    for (const bad of ['https://evil.example/b/PAT-1', '//evil.example', '/\\evil.example', '/b//evil.example', 'javascript:alert(1)', '/entrar', '/bx', 'b/PAT-1', '/b/PAT-1\n', '/app\\..\\x']) {
      expect(isSafeReturn(bad), bad).toBe(false)
      expect(safeReturn(bad), bad).toBe('/app')
    }
  })

  it('a impressão e a reimpressão aparecem com nome próprio no histórico (CA 12)', () => {
    expect(exportLabel(JSON.stringify({ format: { old: null, new: 'etiqueta' }, rows: { old: null, new: 1 } }))).toBe('Imprimiu etiqueta')
    expect(exportLabel(JSON.stringify({ format: { old: null, new: 'etiqueta-reimpressao' } }))).toBe('Reimprimiu etiqueta')
    expect(exportLabel(JSON.stringify({ format: { old: null, new: 'csv' } }))).toBeNull()
  })

  it('sem BarcodeDetector, a mensagem da câmera e o campo para digitar a etiqueta (CA 9)', async () => {
    const onResult = vi.fn()
    render(<QrScanner open onOpenChange={() => {}} title="Ler etiqueta" fallbackMessage={CAMERA_FALLBACK} invalidMessage="Código inválido."
      manualLabel="Número da etiqueta" manualPrefix="PAT-" parse={tagFrom} onResult={onResult} />)
    expect(await screen.findByText(CAMERA_FALLBACK)).toBeTruthy()
    const input = screen.getByLabelText('Número da etiqueta')
    fireEvent.change(input, { target: { value: 'abc' } })
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    expect(await screen.findByText('Código inválido.')).toBeTruthy()
    fireEvent.change(input, { target: { value: '12' } })
    fireEvent.click(screen.getByRole('button', { name: 'Abrir' }))
    expect(onResult).toHaveBeenCalledWith('PAT-00012')
  })
})
