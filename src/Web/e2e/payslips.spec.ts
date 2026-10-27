import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
}

const rh = allModules().map((m) => (m.code === 'hr' ? { ...m, actions: ['view', 'create', 'edit', 'approve', 'view_sensitive'] } : m))
/** Funcionário sem grupo: o RH liberado no tenant, sem acesso ao módulo (o "Meus holerites" vem do vínculo, ADR 0035). */
const employeeOnly = allModules().map((m) => ({ ...m, enabled: false, actions: [] as string[] }))
const pdf = (r: Route) => r.fulfill({ status: 200, contentType: 'application/pdf', headers: { 'content-disposition': 'attachment; filename="holerite-2026-10-101.pdf"' }, body: '%PDF-1.7 holerite' })

/** Backend em memória da RH-02b (as regras reais estão nos testes de integração .NET). */
function payslipApi() {
  const state = { notified: false, notifyBody: null as Record<string, unknown> | null, idempotency: null as string | null, downloads: [] as string[] }
  const run = {
    id: 'r1', period: '2026-10-01', type: 'Monthly', mode: 'Entered', status: 'Closed', payslipCount: 3, createdAt: '2026-10-27T12:00:00Z', closedAt: '2026-10-30T15:00:00Z',
    closedByName: 'Ana Souza', version: 2, paymentDate: '2026-11-06', paidAt: null, reopenReason: null, payableCount: 3,
  }
  const slip = (id: string, name: string, registration: string, net: number) => ({ id, employeeId: 'e-' + id, employeeName: name, registration, gross: net, deductions: 0, net, lineCount: 2, version: 1 })
  const noticeRow = (id: string, name: string, registration: string, status: string) => ({
    payslipId: id, employeeId: 'e-' + id, employeeName: name, registration, status, notifiedAt: status === 'Queued' ? '2026-10-30T16:00:00Z' : null, sentAt: null, error: null,
    firstViewedAt: null, changedAfterNotice: false,
  })
  const notices = () => ({
    runClosed: true, pending: state.notified ? 0 : 2, alreadyNotified: state.notified ? 2 : 0, changed: 0,
    withoutUser: [{ employeeId: 'e-s3', name: 'Caio Prado', registration: '103', suggestedEmail: 'caio@exemplo.com' }],
    terminated: [],
    rows: [
      noticeRow('s1', 'Joana Lima', '101', state.notified ? 'Queued' : 'NotNotified'),
      noticeRow('s2', 'Rui Prado', '102', state.notified ? 'Queued' : 'NotNotified'),
      noticeRow('s3', 'Caio Prado', '103', 'NoUser'),
    ],
  })
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/payroll-runs/*': (r) => json(r, run),
    'GET /api/v1/hr/payroll-runs/*/payslips/sensitive': (r) => json(r, [slip('s1', 'Joana Lima', '101', 3300), slip('s2', 'Rui Prado', '102', 2800), slip('s3', 'Caio Prado', '103', 2100)]),
    'GET /api/v1/hr/payroll-runs/*/notices/sensitive': (r) => json(r, notices()),
    'POST /api/v1/hr/payroll-runs/*/notify': (r) => {
      state.notifyBody = r.request().postDataJSON()
      state.idempotency = r.request().headers()['idempotency-key'] ?? null
      state.notified = true
      return json(r, { queued: 2, message: '2 avisos na fila de envio' })
    },
    'GET /api/v1/hr/payroll-runs/*/payslips/*/pdf': (r) => { state.downloads.push(new URL(r.request().url()).pathname); return pdf(r) },
    'GET /api/v1/me/hr/link': (r) => json(r, { name: 'Joana Lima', situation: 'Active' }),
    'GET /api/v1/me/hr/payslips': (r) => json(r, [
      { id: 'm2', period: '2026-10-01', type: 'Monthly', net: 3300, paymentDate: '2026-11-06', firstViewedAt: null },
      { id: 'm1', period: '2026-09-01', type: 'Monthly', net: 3250, paymentDate: '2026-10-06', firstViewedAt: '2026-10-07T12:00:00Z' },
    ]),
    'GET /api/v1/me/hr/payslips/*/pdf': (r) => { state.downloads.push(new URL(r.request().url()).pathname); return pdf(r) },
  }
  return { state, api }
}

test.describe('RH-02b: holerite em PDF, aviso e Meus holerites', () => {
  test('RH avisa os funcionários da folha fechada: quem recebe, sem usuário e a situação (CA 7 a 9)', async ({ page }) => {
    const { state, api } = payslipApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '22', modules: rh }) })
    await page.goto('/app/rh/folha/r1')
    await expect(page.getByRole('list', { name: 'Holerites' })).toContainText('Joana Lima')
    await expect(page.getByText('Sem usuário').first()).toBeVisible()

    await page.getByRole('button', { name: 'Avisar funcionários' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByRole('button', { name: 'Avisar 2 funcionários' })).toBeEnabled()
    await expect(dialog.getByRole('region', { name: 'Sem usuário' })).toContainText('Caio Prado')
    await expect(dialog.getByRole('button', { name: 'Convidar usuário' })).toBeVisible()
    await noHorizontalScroll(page)
    await dialog.getByRole('button', { name: 'Avisar 2 funcionários' }).click()
    await expect(page.getByRole('status').filter({ hasText: '2 avisos na fila de envio' }).first()).toBeVisible()
    expect(state.notifyBody).toEqual({ onlyChanged: false })
    expect(state.idempotency).toMatch(/^[0-9a-f-]{36}$/)
    await expect(page.getByText('Na fila').first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Avisar de novo' }).first()).toBeVisible()

    await page.getByRole('list', { name: 'Holerites' }).getByRole('button', { name: 'Baixar' }).first().click()
    await expect.poll(() => state.downloads).toContain('/api/v1/hr/payroll-runs/r1/payslips/s1/pdf')
    await noHorizontalScroll(page)
  })

  test('"Convidar usuário" abre o convite com o e-mail pessoal sugerido (CA 7)', async ({ page }) => {
    const { api } = payslipApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '22', modules: rh.map((m) => (m.code === 'identity' ? { ...m, actions: ['view', 'create', 'edit'] } : m)) }) })
    await page.goto('/app/rh/folha/r1')
    await page.getByRole('button', { name: 'Avisar funcionários' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Convidar usuário' }).click()
    await expect(page).toHaveURL(/acessos\/usuarios/)
    const invite = page.getByRole('dialog', { name: 'Convidar usuário' })
    await expect(invite.getByLabel('E-mail')).toHaveValue('caio@exemplo.com')
    await expect(invite.getByLabel('Nome')).toHaveValue('Caio Prado')
  })

  test('funcionário vê e baixa o próprio holerite em "Meus holerites", sem grupo (CA 5)', async ({ page }) => {
    const { state, api } = payslipApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '23', modules: employeeOnly }) })
    await page.goto('/app/meus-holerites')
    await expect(page.getByRole('heading', { name: 'Meus holerites' })).toBeVisible()
    const list = page.getByRole('list', { name: 'Meus holerites' })
    await expect(list.getByRole('listitem').first()).toContainText('Outubro de 2026')
    await expect(list.getByRole('listitem').first()).toContainText('R$ 3.300,00')
    await expect(list.getByRole('listitem').nth(1)).toContainText('Visto em 07/10/2026')
    await list.getByRole('button', { name: 'Baixar PDF' }).first().click()
    await expect.poll(() => state.downloads).toContain('/api/v1/me/hr/payslips/m2/pdf')
    const box = await list.getByRole('button', { name: 'Baixar PDF' }).first().boundingBox()
    expect(box!.height).toBeGreaterThanOrEqual(56)
    await noHorizontalScroll(page)
  })

  test('sem vínculo com funcionário ativo e sem holerite fechado: os estados da tela (CA 5 e 6)', async ({ page }) => {
    const { api } = payslipApi()
    let linked = false
    await mockApi(page, {
      ...api,
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '23', modules: employeeOnly }),
      'GET /api/v1/me/hr/payslips': (r: Route) => (linked ? json(r, [])
        : json(r, { status: 403, code: 'employee_not_active', detail: 'Seu usuário não está ligado a um funcionário ativo. Fale com o RH.' }, 403)),
    })
    await page.goto('/app/meus-holerites')
    await expect(page.getByText('Seu usuário não está ligado a um funcionário ativo. Fale com o RH.')).toBeVisible()
    linked = true
    await page.reload()
    await expect(page.getByText('Nenhum holerite disponível ainda.')).toBeVisible()
    await expect(page.getByText('Ele aparece aqui quando a folha do mês for fechada.')).toBeVisible()
    await noHorizontalScroll(page)
  })
})
