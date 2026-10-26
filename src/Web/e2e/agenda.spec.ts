import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'
import { ordersApi } from './payment-orders-api'

const isMobile = (name: string) => name === 'celular'

const entry = (n: number) => ({ title: `OP-2026-000${n}`, detail: 'Prevista para 25/10', link: `/app/financeiro/ordens-de-pagamento/op${n}?acao=aprovar`, dueDate: '2026-10-25', amount: 950, badge: null })

const awaiting = (entries = [entry(1)]) => ({
  category: 'finance.payment_orders_awaiting', title: entries.length === 1 ? '1 ordem de pagamento aguarda você' : `${entries.length} ordens de pagamento aguardam você`,
  why: 'R$ 950,00 no total; a primeira vence segunda-feira, 25/10.', link: '/app/financeiro/ordens-de-pagamento?filtro=aguardando-voce', dueDate: '2026-10-25', amount: 950,
  tier: 'Unblocks', count: entries.length, action: 'Aprovar', entries,
})

const quality = {
  category: 'production.quality_pending', title: '1 OS aguarda sua decisão de qualidade', why: 'A primeira está prometida para amanhã; 1 seria Autoinspeção.', link: '/app/producao/qualidade',
  dueDate: '2026-10-26', amount: null, tier: 'Unblocks', count: 1, action: 'Decidir qualidade',
  entries: [{ title: 'OS-2026-0007 · Qualidade', detail: 'Elevador 6 paradas · PRJ-2026-0003', link: '/app/producao/qualidade/s1', dueDate: '2026-10-26', amount: null, badge: 'Autoinspeção' }],
}

const overdue = {
  category: 'finance.receivables_overdue', title: '2 títulos a receber vencidos', why: 'R$ 1.500,00 no total de 2 clientes; o mais antigo venceu em 15/10.', link: '/app/financeiro/receber?situacao=vencido',
  dueDate: '2026-10-15', amount: 1500, tier: 'Overdue', count: 2, action: 'Cobrar',
  entries: [{ title: 'CR-2026-00010', detail: 'Alfa Condomínio', link: '/app/financeiro/receber/r1', dueDate: '2026-10-15', amount: 1000, badge: null }],
}

test.describe('Minha pauta no Início (DSH-04)', () => {
  test('o Proprietário vê a OP aguardando, aprova pelo link e a linha some (CA 5 a 7)', async ({ page }) => {
    const { api, calls } = ordersApi('Pending')
    let approved = false
    await mockApi(page, {
      ...api,
      'POST /api/v1/finance/payment-orders/*/approve': async (r: Route) => { approved = true; await api['POST /api/v1/finance/payment-orders/*/approve'](r) },
      'GET /api/v1/me/agenda': (r: Route) => json(r, approved ? { items: [], failures: [], total: 0 } : { items: [awaiting(), overdue], failures: [], total: 3 }),
    })
    await page.goto('/app')

    const pauta = page.getByRole('region', { name: 'Minha pauta' })
    await expect(pauta.getByRole('heading', { name: '1 ordem de pagamento aguarda você' })).toBeVisible()
    await expect(pauta.getByText('R$ 950,00 no total; a primeira vence segunda-feira, 25/10.')).toBeVisible()
    await expect(pauta.getByRole('group', { name: 'Destrava outra pessoa ou dinheiro' })).toBeVisible()
    await expect(pauta.getByRole('group', { name: 'Já passou do prazo' })).toContainText('2 títulos a receber vencidos')
    await expect(page).toHaveTitle('(3) Início · Konebras · BemAqui Empresas')
    // A pauta vem antes dos próximos passos e dos atalhos (CA 7).
    const order = await page.locator('main h2').allTextContents()
    expect(order.indexOf('Minha pauta')).toBeLessThan(order.indexOf('Próximos passos'))
    await expect(page.getByRole('navigation', { name: 'Navegação principal' })).not.toContainText('Minha pauta')
    const results = await new AxeBuilder({ page }).include('main').analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    await pauta.getByRole('link', { name: 'Aprovar: OP-2026-0001' }).click()
    await expect(page).toHaveURL(/\/app\/financeiro\/ordens-de-pagamento\/op1\?acao=aprovar$/)
    const dialog = page.getByRole('alertdialog')
    await dialog.getByRole('button', { name: 'Aprovar' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Aprovada' }).first()).toBeVisible()
    expect(calls[0].url).toContain('/approve')

    await page.goto('/app')
    await expect(page.getByText('Nada pendente para você hoje.')).toBeVisible()
    await expect(page.getByRole('heading', { name: '1 ordem de pagamento aguarda você' })).toHaveCount(0)
    await expect(page).toHaveTitle('Início · Konebras · BemAqui Empresas')
  })

  test('"Ver todos" abre a lista filtrada e a Autoinspeção aparece com a dica', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/me/agenda': (r: Route) => json(r, { items: [quality, awaiting()], failures: [], total: 2 }),
      'GET /api/v1/finance/payment-orders': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    })
    await page.goto('/app')
    const pauta = page.getByRole('region', { name: 'Minha pauta' })
    await expect(pauta.getByText('Autoinspeção', { exact: true })).toBeVisible()
    await expect(pauta.getByRole('button', { name: 'Dica: Autoinspeção' })).toBeVisible()
    await expect(pauta.getByRole('link', { name: 'Decidir qualidade: OS-2026-0007 · Qualidade' })).toHaveAttribute('href', '/app/producao/qualidade/s1')
    await pauta.getByRole('link', { name: 'Ver todos (1)' }).last().click()
    await expect(page).toHaveURL(/\/app\/financeiro\/ordens-de-pagamento\?filtro=aguardando-voce$/)
  })

  test('o Operador sem pendência vê o vazio com os atalhos (CA 9)', async ({ page }) => {
    await mockApi(page)
    await page.goto('/app')
    await expect(page.getByText('Nada pendente para você hoje.')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Seus módulos' })).toBeVisible()
  })

  test('uma categoria que falhou mostra a mensagem e "Tentar de novo"; as outras aparecem (CA 10)', async ({ page }) => {
    let healthy = false
    await mockApi(page, {
      'GET /api/v1/me/agenda': (r: Route) => json(r, !healthy
        ? { items: [overdue], failures: [{ category: 'finance.payment_orders_awaiting', label: 'as ordens de pagamento aguardando você', tier: 'Unblocks', message: 'Não foi possível carregar as ordens de pagamento aguardando você.' }], total: 2 }
        : { items: [awaiting(), overdue], failures: [], total: 3 }),
    })
    await page.goto('/app')
    const pauta = page.getByRole('region', { name: 'Minha pauta' })
    await expect(pauta.getByRole('heading', { name: '2 títulos a receber vencidos' })).toBeVisible()
    const failure = pauta.getByRole('alert')
    await expect(failure).toContainText('Não foi possível carregar as ordens de pagamento aguardando você.')
    healthy = true
    await failure.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(pauta.getByRole('heading', { name: '1 ordem de pagamento aguarda você' })).toBeVisible()
  })

  test('erro geral com "Tentar de novo"', async ({ page }) => {
    let healthy = false
    await mockApi(page, { 'GET /api/v1/me/agenda': (r: Route) => (healthy ? json(r, { items: [overdue], failures: [], total: 2 }) : json(r, { title: 'Erro' }, 500)) })
    await page.goto('/app')
    await expect(page.getByText('Não foi possível carregar a sua pauta.')).toBeVisible()
    healthy = true
    await page.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(page.getByRole('heading', { name: '2 títulos a receber vencidos' })).toBeVisible()
  })

  test('entrar sem rota de volta cai no Início (CA 8; o Proprietário vai ao painel, DSH-01a)', async ({ page }) => {
    await mockApi(page, {
      'POST /api/v1/auth/login': (r: Route) => json(r, { accessToken: 'tok-final', expiresAt: '2030-01-01T00:00:00Z', twoFactor: null }),
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: ['dashboard.view'], isOwner: false, version: '1', modules: allModules() }),
    }, { session: false })
    await page.goto('/entrar')
    await page.getByLabel('E-mail').fill('ana@konebras.com.br')
    await page.getByLabel('Senha').fill('uma frase longa de teste')
    await page.getByRole('button', { name: 'Entrar' }).click()
    await expect(page).toHaveURL(/\/app$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Início' })).toBeVisible()
  })

  test('a 400 px os cartões empilham, mostram os 3 primeiros itens e os botões têm 44 px', async ({ page }, info) => {
    test.skip(!isMobile(info.project.name), 'só no celular')
    await mockApi(page, { 'GET /api/v1/me/agenda': (r: Route) => json(r, { items: [awaiting([1, 2, 3, 4, 5].map(entry)), overdue], failures: [], total: 7 }) })
    await page.goto('/app')
    const pauta = page.getByRole('region', { name: 'Minha pauta' })
    const card = pauta.getByRole('article').filter({ hasText: '5 ordens de pagamento aguardam você' })
    await expect(card.locator('li')).toHaveCount(5)
    await expect(card.getByRole('link', { name: /^Aprovar: / })).toHaveCount(3)
    const box = await card.getByRole('link', { name: 'Aprovar: OP-2026-0001' }).boundingBox()
    expect(box!.height).toBeGreaterThanOrEqual(44)
    const [a, b] = await pauta.getByRole('article').evaluateAll((cards) => cards.map((c) => c.getBoundingClientRect().top))
    expect(b).toBeGreaterThan(a)
    const width = await page.evaluate(() => document.documentElement.scrollWidth)
    expect(width).toBeLessThanOrEqual(400)
  })
})
