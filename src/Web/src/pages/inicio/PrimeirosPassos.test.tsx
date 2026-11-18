import { cleanup, fireEvent, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { FirstStep, FirstStepsCard } from '@/api/generated/model'
import type { NavModule } from '@/navigation/modules'
import { renderWithRouter } from '@/test-utils'
import { ASK_ADMIN, PrimeirosPassosCard } from './PrimeirosPassos'

afterEach(cleanup)

const mod = (code: string, actions: string[]) => ({ code, locked: false, actions }) as unknown as NavModule

const step = (key: string, over: Partial<FirstStep> = {}): FirstStep => ({
  key, title: key, text: 'texto de ' + key, done: false, available: true, url: null, permission: null, comingWith: null, ...over,
})

const steps: FirstStep[] = [
  step('company', { title: 'Empresa e regime tributário', url: '/app/configuracoes/empresa', permission: 'settings.edit' }),
  step('products', { title: 'Importar produtos', available: false, comingWith: 'CAT-02' }),
  step('payments', { title: 'Formas de pagamento e taxas', url: '/app/financeiro/formas-de-pagamento', permission: 'finance.edit', done: true }),
  step('printer', { title: 'Impressora', available: false, comingWith: 'IMP-01' }),
  step('first_sale', { title: 'Abrir o caixa e fazer uma venda de teste', available: false, comingWith: 'PDV-02' }),
]

const card = (over: Partial<FirstStepsCard> = {}): FirstStepsCard => ({
  applies: true, steps, done: 1, total: 5, allDone: false, nextTitle: null, nextUrl: null, ...over,
})

function renderCard(value: FirstStepsCard, all: NavModule[], onDismiss = () => undefined) {
  return renderWithRouter(() => <PrimeirosPassosCard card={value} all={all} onDismiss={onDismiss} />)
}

describe('Primeiros passos (PERF-04)', () => {
  it('mostra os cinco passos, o progresso e o link do passo com permissão (CA 6 e 7)', async () => {
    renderCard(card(), [mod('settings', ['view', 'edit']), mod('finance', ['view', 'edit'])])
    expect(await screen.findByRole('heading', { name: /Primeiros passos/ })).toBeTruthy()
    expect(screen.getByText('1 de 5 concluídos')).toBeTruthy()
    expect(screen.getAllByRole('listitem')).toHaveLength(5)
    expect(screen.getByRole('link', { name: /Empresa e regime tributário/ }).getAttribute('href')).toBe('/app/configuracoes/empresa')
    expect(screen.getByText('Concluído')).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Formas de pagamento/ })).toBeNull()
    expect(screen.getAllByText('Em breve')).toHaveLength(3)
    expect(screen.queryByRole('link', { name: /Importar produtos/ })).toBeNull()
  })

  it('sem a permissão da tela, o passo aparece sem link e pede a quem administra (CA 9)', async () => {
    renderCard(card({ steps: steps.map((s) => (s.key === 'payments' ? { ...s, done: false } : s)), done: 0 }), [mod('settings', ['view']), mod('finance', ['view'])])
    await screen.findByRole('heading', { name: /Primeiros passos/ })
    expect(screen.queryAllByRole('link')).toHaveLength(0)
    expect(screen.getAllByText(ASK_ADMIN)).toHaveLength(2)
  })

  it('com tudo pronto, mostra "Tudo pronto", o próximo passo e pode ser dispensado (CA 8)', async () => {
    const onDismiss = vi.fn()
    renderCard(card({ allDone: true, done: 5, nextTitle: 'Convide a equipe da loja', nextUrl: '/app/acessos/usuarios' }), [], onDismiss)
    expect(await screen.findByRole('heading', { name: /Tudo pronto/ })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Convide a equipe da loja' }).getAttribute('href')).toBe('/app/acessos/usuarios')
    fireEvent.click(screen.getByRole('button', { name: 'Dispensar' }))
    expect(onDismiss).toHaveBeenCalledOnce()
  })
})
