import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const iso = (offset: number) => { const d = new Date(Date.now() + offset * 86_400_000); return d.toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' }) }

/** FIN-07: base com folga e ajustado negativo no dia 5 (o recebimento do dia 2 entra no dia 12). */
function flow(view: string) {
  const days = Array.from({ length: 14 }, (_, i) => i - 2)
  let base = 1000
  let adjusted = 1000
  const period = (o: number, inflow: number, outflow: number, balance: number) => ({ start: iso(o), end: iso(o), inflow, outflow, balance, projected: o >= 0, containsToday: o === 0, overdueInflow: 0, overdueOutflow: 0 })
  const baseDays = days.map((o) => { const i = o === 2 ? 3000 : 0; const out = o === 5 ? 2000 : 0; base += i - out; return period(o, i, out, base) })
  const adjustedDays = days.map((o) => { const i = o === 11 ? 3000 : 0; const out = o === 5 ? 2000 : 0; adjusted += i - out; return period(o, i, out, adjusted) })
  const pick = (list: typeof baseDays) => (view === 'daily' ? list : [{ ...list[0], end: list[13].end, inflow: 3000, outflow: 2000, balance: list[13].balance, containsToday: true }])
  return {
    today: iso(0), from: iso(-2), to: iso(11), view, bankAccountId: null, currentBalance: 1000, consolidated: true, periods: pick(baseDays),
    lowest: { date: iso(0), balance: 1000 }, firstNegative: null, undatedReceivables: { count: 0, total: 0 },
    adjusted: {
      periods: pick(adjustedDays), lowest: { date: iso(5), balance: -1000 }, firstNegative: { date: iso(5), balance: -1000 }, lowestDifference: 2000,
      shiftedBeyond: { count: 1, total: 1500 }, atRiskTotal: 1600, atRisk: [{ partyId: 'c1', name: 'Gama Construtora', defaultPct: 40, atRisk: 1600, count: 1 }],
      tenantHasHistory: true, tenantAvgDelayDays: 6, thresholdPct: 20,
    },
  }
}

const day = (scenario: string) => ({
  date: iso(5), projected: true, scenario, total: scenario === 'adjusted' ? -2000 : 1000,
  items: [
    { kind: 'Payable', id: 'p1', number: 'CP-2026-00007', description: 'Chapas de aço', partyName: 'Gama Aços', amount: -2000, dueDate: iso(5), overdue: false, estimate: false, link: '/app/financeiro/pagar/p1' },
    scenario === 'adjusted'
      ? { kind: 'Receivable', id: 'r1', number: 'CR-2026-00010', description: 'Parcela 2', partyName: 'Alfa Elevadores', amount: 3000, dueDate: iso(5), overdue: false, estimate: false, link: '/app/financeiro/receber/r1', adjustedDate: iso(15), reason: 'Vai para depois. Atraso médio de 10 dias de Alfa Elevadores.', shiftedOut: true, originalAmount: null }
      : { kind: 'Receivable', id: 'r1', number: 'CR-2026-00010', description: 'Parcela 2', partyName: 'Alfa Elevadores', amount: 3000, dueDate: iso(5), overdue: false, estimate: false, link: '/app/financeiro/receber/r1' },
  ],
})

const scenarios: string[] = []
const api: Record<string, (r: Route) => Promise<void> | void> = {
  'GET /api/v1/finance/cash-flow': (r) => {
    const params = new URL(r.request().url()).searchParams
    scenarios.push(params.get('scenario') ?? '')
    const body = flow(params.get('view') ?? 'daily')
    return json(r, params.get('scenario') === 'base' ? { ...body, adjusted: null } : body)
  },
  'GET /api/v1/finance/cash-flow/day': (r) => json(r, day(new URL(r.request().url()).searchParams.get('scenario') ?? 'base')),
  'GET /api/v1/finance/bank-accounts': (r) => json(r, { items: [{ id: 'b1', name: 'Banco Inter', kind: 'Bank', isActive: true }], page: 1, pageSize: 100, total: 1 }),
}

test.describe('Fluxo de caixa ajustado (FIN-07)', () => {
  test('os dois cenários, destaques, alerta do ajustado, dia ajustado e troca de cenário (CA 7 a 9, 11)', async ({ page }) => {
    await mockApi(page, api)
    await page.goto('/app/financeiro/fluxo-de-caixa')
    const main = page.getByRole('main')
    await expect(main.getByRole('alert')).toContainText('Considerando os atrasos de sempre, o caixa fica negativo em')
    await expect(main.getByText('Menor saldo previsto (ajustado)')).toBeVisible()
    await expect(main.getByText('Diferença causada por atrasos')).toBeVisible()
    await expect(main.getByText(/^Menor saldo ajustado em .*; na base, em /)).toBeVisible()
    await expect(main.getByRole('region', { name: 'Efeito dos atrasos' }).getByText('Gama Construtora')).toBeVisible()
    await expect(main.getByRole('link', { name: 'Ver títulos' })).toHaveAttribute('href', /cliente=c1/)
    await expect(main.getByRole('columnheader', { name: /Saldo ajustado/ })).toBeVisible()
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])

    // O alerta abre o dia no ajustado, com o deslocado à parte e o total da célula.
    await main.getByRole('button', { name: 'Ver o que entra e sai nesse dia' }).click()
    const panel = page.getByRole('dialog')
    await expect(panel.getByRole('region', { name: 'Saíram deste dia' }).getByRole('link', { name: /CR-2026-00010/ })).toBeVisible()
    await expect(panel.getByText('Atraso médio de 10 dias de Alfa Elevadores', { exact: false })).toBeVisible()
    await expect(panel.getByText('Total do dia')).toBeVisible()
    await panel.getByRole('radio', { name: 'Base' }).click()
    await expect(panel.getByRole('region', { name: 'Saíram deste dia' })).toHaveCount(0)
    await page.keyboard.press('Escape')

    // "Ver como tabela" pelo teclado: o foco vai para a tabela equivalente com as duas colunas de saldo.
    const toggle = main.getByRole('button', { name: 'Ver como tabela' })
    await toggle.focus()
    await page.keyboard.press('Enter')
    const table = main.getByRole('region', { name: /tabela$/ })
    await expect(table).toBeFocused()
    await expect(table.getByRole('columnheader', { name: 'Base' })).toBeVisible()
    await expect(table.getByRole('columnheader', { name: 'Ajustado' })).toBeVisible()

    await main.getByRole('radio', { name: 'Base' }).click()
    await expect(page).toHaveURL(/cenario=base/)
    await expect(main.getByRole('columnheader', { name: /Saldo ajustado/ })).toHaveCount(0)
    await main.getByRole('radio', { name: 'Ajustado' }).click()
    await expect(page).toHaveURL(/cenario=ajustado/)
    expect(scenarios).toEqual(expect.arrayContaining(['both', 'base', 'adjusted']))
  })

  test('sem histórico na empresa, a nota explica que o ajustado é igual à base (CA 2)', async ({ page }) => {
    await mockApi(page, { ...api, 'GET /api/v1/finance/cash-flow': (r) => { const b = flow('daily'); return json(r, { ...b, adjusted: { ...b.adjusted, tenantHasHistory: false, firstNegative: null } }) } })
    await page.goto('/app/financeiro/fluxo-de-caixa')
    await expect(page.getByRole('note').filter({ hasText: 'Ainda não há histórico de pagamento: o ajustado é igual à base.' })).toBeVisible()
  })

  test('a 400 px: período e os dois saldos, entradas e saídas na linha expansível, nada transborda', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    await mockApi(page, api)
    await page.goto('/app/financeiro/fluxo-de-caixa')
    const table = page.getByRole('table', { name: 'Fluxo de caixa por período' })
    await expect(table.getByRole('columnheader', { name: 'Saldo base' })).toBeVisible()
    await expect(table.getByRole('columnheader', { name: 'Entradas' })).toBeHidden()
    await table.getByRole('button', { name: /^Entradas e saídas de/ }).first().click()
    await expect(table.getByText('Entradas (ajustado)').first()).toBeVisible()
    const radio = page.getByRole('radiogroup', { name: 'Cenário' })
    const chart = page.getByRole('img', { name: 'Saldo do realizado ao projetado' })
    expect((await radio.boundingBox())!.y).toBeLessThan((await chart.boundingBox())!.y)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
