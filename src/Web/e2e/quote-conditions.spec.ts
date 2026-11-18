import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const press = async (page: import('@playwright/test').Page, name: string) => { await page.getByRole('main').getByRole('button', { name }).focus(); await page.keyboard.press('Enter') }

const quote = { id: 'q1', number: 'ORC-2026-0001', revision: 0, status: 'Draft', partyId: 'c1', partyName: 'Aurora', contactId: null, siteAddressId: null, deliveryDays: null, notes: null,
  pricingMode: 'Markup', items: [], totalPrice: 100000, sentAt: null, createdAt: '2026-10-20T10:00:00Z', validUntil: '2026-11-04', lostReason: null, lostDetail: null, copiedFromNumber: null, version: 1 }

test.describe('Condições do orçamento (COM-01b)', () => {
  test('marcos pelo teclado: valor ao lado, o que falta e dividir igualmente (CA 1 e 3)', async ({ page }) => {
    test.setTimeout(60_000)
    const posted: unknown[] = []
    await mockApi(page, {
      'GET /api/v1/quotes/q1': (r: Route) => json(r, quote),
      'GET /api/v1/quotes/q1/conditions': (r: Route) => json(r, { milestones: [], percentSum: 0, warning: null, validUntil: '2026-11-04', defaultPaymentTermId: null, version: 1 }),
      'PUT /api/v1/quotes/q1/conditions': (r: Route) => { posted.push(r.request().postDataJSON()); return json(r, { milestones: [], percentSum: 100, warning: null, validUntil: '2026-11-04', defaultPaymentTermId: null, version: 2 }) },
      'GET /api/v1/quotes/payment-terms': (r: Route) => json(r, []),
    })
    await page.goto('/app/orcamentos/q1?aba=condicoes')
    const main = page.getByRole('main')
    for (const [i, name] of ['Sinal', 'Fabricação', 'Entrega'].entries()) {
      await press(page, 'Adicionar marco')
      await main.getByRole('textbox', { name: `Descrição do marco ${i + 1}` }).fill(name)
    }
    await main.getByRole('textbox', { name: 'Percentual do marco 1' }).fill('30')
    await main.getByRole('textbox', { name: 'Percentual do marco 2' }).fill('40')
    await main.getByRole('textbox', { name: 'Percentual do marco 2' }).blur()
    await expect(main.getByText('Faltam 30,00%')).toBeVisible()
    await press(page, 'Ajustar o último')
    await expect(main.getByText(/R\$\s30\.000,00/).first()).toBeVisible()
    await press(page, 'Dividir igualmente')
    await expect(main.getByText('Faltam')).toHaveCount(0)
    await press(page, 'Salvar condições')
    await expect(page.getByRole('status').filter({ hasText: 'Condições salvas' }).first()).toBeVisible()
    expect(posted[0]).toMatchObject({ milestones: [{ description: 'Sinal', percent: 33.3333 }, { percent: 33.3333 }, { percent: 33.3334 }] })
  })

  test('o evento do marco aparece inteiro no select, e a 400 px a página não rola de lado', async ({ page }) => {
    const milestone = (n: number, triggerEvent: string) => ({ id: 'm' + n, sequence: n, description: 'Marco ' + n, percent: 25, amount: 25000, trigger: 'Event', triggerEvent, daysAfterEvent: 10, dueDate: null })
    const milestones = ['Signature', 'FabricationDone', 'Delivered', 'TechnicalAcceptance'].map((e, i) => milestone(i + 1, e))
    await mockApi(page, {
      'GET /api/v1/quotes/q1': (r: Route) => json(r, quote),
      'GET /api/v1/quotes/q1/conditions': (r: Route) => json(r, { milestones, percentSum: 100, warning: null, validUntil: '2026-11-04', defaultPaymentTermId: null, version: 1 }),
      'GET /api/v1/quotes/payment-terms': (r: Route) => json(r, []),
    })
    await page.goto('/app/orcamentos/q1?aba=condicoes')
    const main = page.getByRole('main')
    for (const [i, label] of ['Assinatura do contrato', 'Fabricação concluída', 'Entrega', 'Aceite técnico'].entries()) {
      const select = main.getByRole('combobox', { name: `Evento do marco ${i + 1}` })
      await expect(select).toHaveValue(milestones[i].triggerEvent)
      // Largura do texto da opção escolhida contra o espaço útil do select (sem o padding e a seta, ~20 px).
      const fit = await select.evaluate((el: HTMLSelectElement) => {
        const style = getComputedStyle(el)
        const ctx = document.createElement('canvas').getContext('2d')!
        ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`
        const text = ctx.measureText(el.selectedOptions[0].text).width
        return { text, room: el.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) - 20 }
      })
      expect(fit.text, label).toBeLessThanOrEqual(fit.room)
      await expect(main.getByRole('textbox', { name: `Prazo após o evento do marco ${i + 1}` })).toHaveValue('10')
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
