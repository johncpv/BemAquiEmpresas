import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/api/http'
import { DocumentActions } from './document-actions'
import { exportDay } from './export-day'
import { ExportMenu } from './export-menu'

const http = vi.hoisted(() => ({ apiDownload: vi.fn(), apiBlobUrl: vi.fn() }))
vi.mock('@/api/http', async (original) => ({ ...(await original<typeof import('@/api/http')>()), ...http }))

beforeEach(() => {
  http.apiDownload.mockReset()
  http.apiBlobUrl.mockReset()
})

afterEach(() => vi.restoreAllMocks())

/** Promessa controlada pelo teste, para ver o estado de carregando. */
function deferred() {
  let resolve!: () => void
  let reject!: (e: unknown) => void
  const promise = new Promise<void>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

describe('DocumentActions (EXP-01 CA 9)', () => {
  it('Baixar PDF mostra "Gerando PDF…" com aria-busy e baixa com o nome do documento', async () => {
    const pending = deferred()
    http.apiDownload.mockReturnValue(pending.promise)
    render(<DocumentActions url="/api/v1/finance/payment-orders/1/pdf" fileName="OP-2026-0001.pdf" />)
    fireEvent.click(screen.getByRole('button', { name: 'Baixar PDF' }))
    const busy = await screen.findByRole('button', { name: 'Gerando PDF…' })
    expect(busy.getAttribute('aria-busy')).toBe('true')
    expect((screen.getByRole('button', { name: 'Imprimir' }) as HTMLButtonElement).disabled).toBe(true)
    expect(http.apiDownload).toHaveBeenCalledWith('/api/v1/finance/payment-orders/1/pdf', 'OP-2026-0001.pdf')
    await act(async () => pending.resolve())
    expect(await screen.findByRole('button', { name: 'Baixar PDF' })).toBeTruthy()
  })

  it('erro do servidor aparece abaixo com "Tentar de novo", que repete a ação', async () => {
    http.apiDownload.mockRejectedValueOnce(new ApiError(422, { detail: 'Ordem de pagamento não encontrada.' })).mockResolvedValueOnce(undefined)
    render(<DocumentActions url="/x.pdf" fileName="x.pdf" />)
    fireEvent.click(screen.getByRole('button', { name: 'Baixar PDF' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Ordem de pagamento não encontrada.')
    expect(screen.getByRole('button', { name: 'Baixar PDF' }).getAttribute('aria-describedby')).toBe(alert.id)
    fireEvent.click(screen.getByRole('button', { name: 'Tentar de novo' }))
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull())
    expect(http.apiDownload).toHaveBeenCalledTimes(2)
  })

  it('Imprimir abre a aba no clique e leva o PDF para ela', async () => {
    const tab = { opener: {}, location: { href: '' }, close: vi.fn() }
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    http.apiBlobUrl.mockResolvedValue('blob:pdf')
    render(<DocumentActions url="/x.pdf" fileName="x.pdf" />)
    fireEvent.click(screen.getByRole('button', { name: 'Imprimir' }))
    expect(open).toHaveBeenCalledWith('about:blank', '_blank')
    await waitFor(() => expect(tab.location.href).toBe('blob:pdf'))
    expect(tab.opener).toBeNull()
  })

  it('Imprimir com falha fecha a aba e mostra o erro; sem impressão, só o botão de baixar', async () => {
    const tab = { opener: {}, location: { href: '' }, close: vi.fn() }
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    http.apiBlobUrl.mockRejectedValue(new Error('rede'))
    const { unmount } = render(<DocumentActions url="/x.pdf" fileName="x.pdf" />)
    fireEvent.click(screen.getByRole('button', { name: 'Imprimir' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Não foi possível gerar o PDF')
    expect(tab.close).toHaveBeenCalled()
    unmount()
    render(<DocumentActions url="/x.pdf" fileName="x.pdf" label="Baixar romaneio" print={false} />)
    expect(screen.getByRole('button', { name: 'Baixar romaneio' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Imprimir' })).toBeNull()
  })
})

describe('ExportMenu (EXP-01 CA 9)', () => {
  const openMenu = () => {
    const trigger = screen.getByRole('button', { name: 'Exportar' })
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu')
    fireEvent.keyDown(trigger, { key: 'Enter' })
  }

  it('menu por teclado com Excel e PDF; cada formato baixa com os filtros da tela e o nome do dia', async () => {
    http.apiDownload.mockResolvedValue(undefined)
    const url = vi.fn((format: string) => `/api/v1/finance/payables/export?situation=aberto&format=${format}`)
    render(<ExportMenu url={url} fileBase="contas-a-pagar" />)
    openMenu()
    const items = await screen.findAllByRole('menuitem')
    expect(items.map((i) => i.textContent)).toEqual(['Excel (.xlsx)', 'PDF'])
    fireEvent.click(items[0])
    await waitFor(() => expect(http.apiDownload).toHaveBeenCalledWith('/api/v1/finance/payables/export?situation=aberto&format=xlsx', `contas-a-pagar-${exportDay()}.xlsx`))
    expect(exportDay()).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('acima do teto, a mensagem do 422 aparece abaixo do botão; enquanto exporta, "Exportando…"', async () => {
    const pending = deferred()
    http.apiDownload.mockReturnValue(pending.promise)
    render(<ExportMenu url={(f) => `/e?format=${f}`} fileBase="lista" formats={['pdf']} />)
    openMenu()
    fireEvent.click(await screen.findByRole('menuitem', { name: 'PDF' }))
    const busy = await screen.findByRole('button', { name: 'Exportando…' })
    expect(busy.getAttribute('aria-busy')).toBe('true')
    await act(async () => pending.reject(new ApiError(422, { code: 'export_too_large', detail: 'A lista tem 2.500 linhas e o limite para PDF é 2.000. Refine os filtros.' })))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Refine os filtros')
    expect(screen.getByRole('button', { name: 'Exportar' }).getAttribute('aria-describedby')).toBe(alert.id)
  })
})
