import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const statement = (name: string) => ({
  accountId: 'b1', accountName: name, from: '2026-10-01', to: '2026-10-31', openingMissing: false, hasMovement: false, startLabel: 'Saldo inicial', startBalance: 0, endBalance: 0,
  lines: { items: [], page: 1, pageSize: 50, total: 0 },
})

const bankLine = (id: string, amount: number, extra: Record<string, unknown> = {}) => ({
  id, date: '2026-10-20', description: 'PIX RECEBIDO ' + id, kind: 'Pix', counterpartyName: 'J***', counterpartyDocument: '529.***.***-25', counterpartyMasked: true, amount,
  source: 'Ofx', bankVerified: true, status: 'Pending', ignoreReason: null, batchId: 'lote1', version: 1, ...extra,
})

const view = (over: Record<string, unknown> = {}) => ({
  accountId: 'b1', accountName: 'Itaú PJ', provider: 'None', apiConnected: false, environment: null, ofxAllowed: true, contingency: null, from: '2026-10-01', to: '2026-10-31',
  latest: null, balances: [], lines: [], total: 0, pendingCount: 0, counterpartyMasked: false, batches: [], lastFetchAt: null, nextFetchAt: null, ...over,
})

/** Backend em memória da BNK-03a (as regras reais estão nos testes de integração .NET com o fake do Inter e os OFX de exemplo). */
function bankApi(start: Record<string, unknown>, name: string) {
  let current = start
  const uploads: string[] = []
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/finance/bank-accounts/*/statement': (r) => json(r, statement(name)),
    'GET /api/v1/finance/bank-accounts/*/statement-lines': (r) => json(r, current),
    'POST /api/v1/finance/bank-accounts/*/statement-lines/import-ofx': (r) => {
      uploads.push(r.request().headers()['content-type'] ?? '')
      if (current.apiConnected) return json(r, { title: 'Conflito', status: 409, code: 'ofx_blocked_api_account', detail: 'Esta conta recebe o extrato direto do Inter. O arquivo só entra como contingência.' }, 409)
      current = view({
        lines: [bankLine('l1', 1500), bankLine('l2', -12.9, { kind: 'Fee', description: 'TARIFA PACOTE', counterpartyName: null, counterpartyDocument: null })], total: 2, pendingCount: 2,
        latest: { date: '2026-10-21', bankBalance: 2000, systemBalance: 1980, difference: 20, source: 'Ofx' },
        batches: [{ id: 'lote1', source: 'Ofx', trigger: 'Upload', periodFrom: '2026-10-20', periodTo: '2026-10-21', fileName: 'extrato.ofx', fileSha256: 'ab'.repeat(32), attachmentId: 'a1', bankVerified: true,
          newCount: 2, duplicateCount: 0, rejectedCount: 0, notes: null, importedAt: '2026-10-27T12:00:00Z', importedByName: 'Ana Souza', undoneAt: null, undoneByName: null, undoReason: null, reconciledCount: 0, version: 1 }],
      })
      return json(r, { batchId: 'lote1', newCount: 2, duplicateCount: 0, rejectedCount: 0, notes: [], periodFrom: '2026-10-20', periodTo: '2026-10-21', pendingCount: 2, bankVerified: true })
    },
  }
  return { api, uploads }
}

const ofx = Buffer.from('OFXHEADER:100\r\nDATA:OFXSGML\r\nVERSION:102\r\n\r\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKACCTFROM><BANKID>341<ACCTID>1234567-8</BANKACCTFROM></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>')

async function noOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
}

test.describe('Extrato do banco (BNK-03a)', () => {
  test('importar um OFX numa conta sem API e ver as linhas pendentes', async ({ page }) => {
    const { api, uploads } = bankApi(view(), 'Itaú PJ')
    await mockApi(page, api)
    await page.goto('/app/financeiro/contas/b1/extrato?aba=extrato-banco')
    await expect(page.getByRole('tab', { name: 'Extrato do banco' })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByText('Nenhuma linha do banco ainda. Conecte o Inter ou importe um arquivo OFX.')).toBeVisible()
    await noOverflow(page)

    await page.getByRole('button', { name: 'Importar OFX' }).first().click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByLabel('Arquivo OFX').setInputFiles({ name: 'extrato.ofx', mimeType: 'application/x-ofx', buffer: ofx })
    await dialog.getByRole('button', { name: 'Importar extrato' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Extrato importado' }).first()).toBeVisible()
    expect(uploads[0]).toContain('multipart/form-data')

    const table = page.getByRole('table', { name: 'Extrato do banco' })
    await expect(table.getByText('Pendente')).toHaveCount(2)
    await expect(page.getByRole('region', { name: 'Próximos passos' })).toContainText('Conciliar 2 linhas pendentes')
    await expect(page.getByText('Saldo no banco')).toBeVisible()
    await expect(page.getByText('Importações')).toBeVisible()
    await noOverflow(page)
    const results = await new AxeBuilder({ page }).analyze()
    expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
  })

  test('OFX numa conta do Inter é recusado com o motivo', async ({ page }) => {
    const { api } = bankApi(view({ accountName: 'Inter PJ', provider: 'Inter', apiConnected: true, environment: 'Sandbox', ofxAllowed: true }), 'Inter PJ')
    await mockApi(page, api)
    await page.goto('/app/financeiro/contas/b1/extrato?aba=extrato-banco')
    await expect(page.getByRole('status').filter({ hasText: 'Ambiente de teste do banco' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Buscar agora' }).first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Liberar OFX nesta conta' }).first()).toBeVisible()
    await page.getByRole('button', { name: 'Importar OFX' }).first().click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByLabel('Arquivo OFX').setInputFiles({ name: 'extrato.ofx', mimeType: 'application/x-ofx', buffer: ofx })
    await dialog.getByRole('button', { name: 'Importar extrato' }).click()
    await expect(dialog).toContainText('Esta conta recebe o extrato direto do Inter. O arquivo só entra como contingência.')
    await noOverflow(page)
  })

  test('a 400 px data, valor e situação visíveis; descrição na linha expansível', async ({ page }, info) => {
    test.skip(info.project.name !== 'celular')
    const { api } = bankApi(view({ lines: [bankLine('l1', 1500)], total: 1, pendingCount: 1 }), 'Itaú PJ')
    await mockApi(page, api)
    await page.goto('/app/financeiro/contas/b1/extrato?aba=extrato-banco')
    const table = page.getByRole('table', { name: 'Extrato do banco' })
    await expect(table).toBeVisible({ timeout: 20_000 })
    await expect(table.getByText('20/10/2026')).toBeVisible()
    await expect(table.getByText('Pendente')).toBeVisible()
    await expect(table.getByText('PIX RECEBIDO l1')).toBeHidden()
    await table.getByRole('button', { name: /Detalhes da linha/ }).click()
    await expect(table.getByText('PIX RECEBIDO l1 · Pix')).toBeVisible()
    await noOverflow(page)
  })
})
