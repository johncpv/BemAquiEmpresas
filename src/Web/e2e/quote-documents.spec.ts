import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const quote = { id: 'q1', number: 'ORC-2026-0001', revision: 0, status: 'Draft', partyId: 'c1', partyName: 'Aurora', contactId: null, siteAddressId: null, deliveryDays: null, notes: null,
  pricingMode: 'Markup', items: [{ id: 'i1', sequence: 1, kind: 'Equipment', itemId: null, itemName: null, description: 'Elevador', quantity: 1, unitPrice: 1400, priceOverridden: false, total: 1400 }],
  totalPrice: 1400, sentAt: null, createdAt: '2026-10-20T10:00:00Z', validUntil: '2026-11-04', lostReason: null, lostDetail: null, copiedFromNumber: null, isCurrent: true, parentQuoteId: null, version: 1 }

test.describe('Documentos do orçamento (COM-03)', () => {
  test('enviar por e-mail: destinatário sugerido, confirmação fora dos contatos e envio (CA 7 a 9)', async ({ page }) => {
    const posted: unknown[] = []
    await mockApi(page, {
      'GET /api/v1/quotes/q1': (r: Route) => json(r, quote),
      'GET /api/v1/quotes/q1/documents': (r: Route) => json(r, { generated: [], uploaded: [], dispatches: [], companyComplete: true }),
      'GET /api/v1/quotes/q1/send-draft': (r: Route) => json(r, { to: 'marta@aurora.com.br', subject: 'Orçamento ORC-2026-0001 rev. 0 — Konebras', message: 'Olá,\n\nSegue o orçamento.', customerEmails: ['marta@aurora.com.br'] }),
      'POST /api/v1/quotes/q1/email': (r: Route) => { posted.push(r.request().postDataJSON()); return json(r, { generated: [], uploaded: [], dispatches: [], companyComplete: true }) },
    })
    await page.goto('/app/orcamentos/q1?aba=documentos')
    await page.getByRole('button', { name: 'Enviar por e-mail' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByRole('textbox', { name: 'Para', exact: true })).toHaveValue('marta@aurora.com.br')
    await dialog.getByRole('textbox', { name: /^Cópia/ }).fill('compras@gmail.com')
    await expect(dialog.getByText('Este e-mail não está nos contatos de Aurora. Confirmo o destinatário.')).toBeVisible()
    await dialog.getByRole('checkbox').check()
    await dialog.getByRole('button', { name: 'Enviar orçamento' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Orçamento enviado' }).first()).toBeVisible()
    expect(posted[0]).toMatchObject({ to: 'marta@aurora.com.br', cc: ['compras@gmail.com'], confirmRecipient: true })
  })
})
