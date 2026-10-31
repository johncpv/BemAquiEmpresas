import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const base = {
  id: 'p1', number: 'LGPD-2026-0001', subjectType: 'Employee', subjectId: 'e1', subjectName: 'Maria Souza', subjectContact: null, kind: 'Access', requestedOn: '2026-10-30',
  channel: 'InPerson', notes: null, dueOn: '2026-11-14', dueChip: null, status: 'Open', requiresIdentity: true, identityVerified: false, identityVerifiedAt: null,
  identityVerifiedByName: null, identityMethod: null, identityCheckedOn: null, identityNotes: null, response: null, respondedOn: null, fileSent: null, refusalReason: null,
  closedAt: null, correctionLink: null, canExport: false, exportStatus: 'None', exportedAt: null, exportError: null, exportExpiresAt: null, createdAt: '2026-10-30T12:00:00Z', version: 1,
}

test.describe('Privacidade (LGPD-01a)', () => {
  test('registra um pedido de acesso, confere a identidade e baixa a exportação', async ({ page }) => {
    let detail: Record<string, unknown> = { ...base }
    let downloaded = false
    await mockApi(page, {
      'GET /api/v1/settings/privacy-requests': (r: Route) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
      'GET /api/v1/settings/privacy-requests/settings': (r: Route) => json(r, { responseDays: 15, settingId: 's1' }),
      'GET /api/v1/settings/privacy-requests/subjects': (r: Route) => json(r, [{ kind: 'Employee', id: 'e1', name: 'Maria Souza', detail: '***.982.247-**' }]),
      'POST /api/v1/settings/privacy-requests': (r: Route) => json(r, detail, 201),
      'GET /api/v1/settings/privacy-requests/p1': (r: Route) => json(r, detail),
      'GET /api/v1/settings/privacy-requests/p1/history': (r: Route) => json(r, [{ id: 'h1', at: '2026-10-30T12:00:00Z', action: 'Created', userName: 'Ana Souza', ip: '10.0.0.1', changes: '{}' }]),
      'POST /api/v1/settings/privacy-requests/p1/identity': (r: Route) => {
        detail = { ...detail, identityVerified: true, identityVerifiedAt: '2026-10-30T12:05:00Z', identityMethod: 'DocumentPresented', identityCheckedOn: '2026-10-30', canExport: true, version: 2 }
        return json(r, detail)
      },
      'POST /api/v1/settings/privacy-requests/p1/export': (r: Route) => {
        detail = { ...detail, exportStatus: 'Ready', exportedAt: '2026-10-30T12:06:00Z', version: 3 }
        return json(r, { request: detail, message: 'Gerando a exportação…' }, 202)
      },
      'POST /api/v1/settings/privacy-requests/p1/export/link': (r: Route) => json(r, { url: '/api/v1/settings/privacy-requests/p1/export/download?token=abc', expiresAt: '2026-10-30T12:21:00Z', fileName: 'dados-do-titular-LGPD-2026-0001.zip' }),
      'GET /api/v1/settings/privacy-requests/p1/export/download': (r: Route) => { downloaded = true; return r.fulfill({ status: 200, contentType: 'application/zip', body: 'PK' }) },
    })
    await page.goto('/app/configuracoes/privacidade')
    await expect(page.getByText('Nenhum pedido de titular registrado.')).toBeVisible()
    await page.getByRole('button', { name: 'Registrar pedido' }).first().click()
    await page.getByRole('combobox', { name: 'Quem pediu' }).selectOption('Employee')
    await page.getByLabel('Buscar titular').fill('Maria')
    await page.getByRole('button', { name: /Maria Souza/ }).click()
    await page.getByRole('button', { name: 'Registrar pedido' }).last().click()
    await expect(page.getByRole('heading', { name: 'Pedido LGPD-2026-0001' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Exportar dados do titular' })).toBeDisabled()
    await page.getByRole('button', { name: 'Conferir identidade' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Conferir identidade' }).click()
    await expect(page.getByText('Identidade conferida')).toBeVisible()
    await page.getByRole('button', { name: 'Exportar dados do titular' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Exportar' }).click()
    await page.getByRole('button', { name: 'Baixar exportação' }).first().click()
    await expect.poll(() => downloaded).toBe(true)
  })

  test('quem não é Proprietário não vê a aba nem os pedidos', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: [{ code: 'settings', name: 'Configurações', area: 'Sistema', order: 1, description: 'x', released: true, enabled: true, actions: ['view', 'edit'] }] }),
    })
    await page.goto('/app/configuracoes/privacidade')
    await expect(page.getByText('Só o Proprietário atende os pedidos do titular.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Privacidade (LGPD)' })).toHaveCount(0)
  })
})
