import { expect, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const STEP_NAMES = ['Projeto', 'Corte', 'Montagem', 'Qualidade', 'Expedição']
const step = (name: string, i: number, extra: Record<string, unknown> = {}) => ({
  id: `s${i + 1}`, sequence: i + 1, code: name[0], name, workCenterId: 'c1', workCenterName: 'Fábrica', workCenterActive: true, standardHours: 1.5,
  isQualityStep: name === 'Qualidade', requiresApproval: false, checklist: [], ...extra,
})
const detail = (extra: Record<string, unknown> = {}) => ({
  id: 'rt1', name: 'Cabine padrão', productType: 'Elevador', isActive: true, isExample: false, totalStandardHours: 7.5, itemCount: 0, usageCount: 0, usage: null,
  steps: STEP_NAMES.map((n, i) => step(n, i)), version: 7, createdAt: '2026-10-21T12:00:00Z', ...extra,
})

/** API do módulo engineering com um centro só (todas as etapas já vêm com ele, "Sugerido"). */
async function routingApi(page: import('@playwright/test').Page, overrides: Record<string, (r: Route) => unknown> = {}) {
  await mockApi(page, {
    'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules() }),
    'GET /api/v1/engineering/routing-templates': (r: Route) => json(r, { items: [{ id: 'rt1', name: 'Cabine padrão', productType: 'Elevador', isActive: true, codes: ['P', 'C', 'M', 'Q', 'E'], stepNames: STEP_NAMES, totalStandardHours: 7.5, itemCount: 2, usageCount: 3, usage: '3 OS' }], page: 1, pageSize: 20, total: 1 }),
    'GET /api/v1/engineering/routing-templates/product-types': (r: Route) => json(r, ['Elevador']),
    'GET /api/v1/engineering/routing-templates/work-centers': (r: Route) => json(r, [{ id: 'c1', name: 'Fábrica', line: null }]),
    'GET /api/v1/engineering/routing-templates/suggested-work-centers': (r: Route) => json(r, []),
    'GET /api/v1/engineering/routing-templates/*': (r: Route) => json(r, detail()),
    'GET /api/v1/engineering/routing-templates/*/items': (r: Route) => json(r, []),
    'GET /api/v1/engineering/routing-templates/*/history': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
    ...overrides,
  })
}

test.describe('Roteiros de fabricação (ENG-01)', () => {
  test('cria a partir do exemplo, reordena pelo teclado, troca a letra, preenche o checklist e vincula produto', async ({ page }) => {
    let body: Record<string, unknown> | undefined
    let linked: unknown
    await routingApi(page, {
      'POST /api/v1/engineering/routing-templates': (r: Route) => { body = r.request().postDataJSON(); return json(r, detail(), 201) },
      'GET /api/v1/engineering/routing-templates/products': (r: Route) => json(r, [{ id: 'i1', code: 'CAB-01', name: 'Cabine 6 pessoas', type: 'Finished', routingTemplateId: null, routingTemplateName: null }]),
      'POST /api/v1/engineering/routing-templates/*/items': (r: Route) => { linked = r.request().postDataJSON(); return json(r, { id: 'l1', itemId: 'i1', code: 'CAB-01', name: 'Cabine 6 pessoas', type: 'Finished', isActive: true, createdAt: '2026-10-21T12:00:00Z' }, 201) },
    })
    await page.goto('/app/engenharia/roteiros/novo')
    await expect(page.getByRole('heading', { name: 'Novo roteiro' })).toBeVisible()
    await expect(page.getByLabel('Letra da etapa 5')).toHaveValue('E')
    await expect(page.getByText('Começa sem BOM')).toBeVisible()
    await expect(page.getByText('Sugerido').first()).toBeVisible()
    await page.getByLabel('Nome', { exact: true }).fill('Cabine padrão')

    // Corte desce para a 3ª posição pelo teclado; a letra da Montagem (agora 2ª) vira N.
    await page.getByRole('button', { name: 'Mover etapa 2 para baixo' }).focus()
    await page.keyboard.press('Enter')
    await expect(page.getByLabel('Nome da etapa 3')).toHaveValue('Corte')
    await page.getByLabel('Letra da etapa 2').fill('N')

    await page.getByRole('button', { name: /Checklist da etapa 3/ }).click()
    await page.getByRole('button', { name: 'Adicionar item' }).click()
    await page.getByLabel('Item 1 do checklist').fill('Conferir a medida da chapa')
    await page.getByRole('button', { name: 'Aplicar checklist' }).click()
    await expect(page.getByRole('button', { name: 'Checklist da etapa 3 (1 itens)' })).toBeVisible()
    await page.getByLabel('Horas padrão da etapa 1').fill('2,5')
    await expect(page.getByText('Total de horas padrão: 2,50 h')).toBeVisible()

    await page.getByRole('button', { name: 'Salvar roteiro' }).click()
    await expect(page.getByText('Roteiro salvo')).toBeVisible()
    const steps = body?.steps as { name: string; code: string; workCenterId: string; checklist: string[]; standardHours: number }[]
    expect(steps.map((s) => `${s.code}:${s.name}`)).toEqual(['P:Projeto', 'N:Montagem', 'C:Corte', 'Q:Qualidade', 'E:Expedição'])
    expect(steps[2].checklist).toEqual(['Conferir a medida da chapa'])
    expect(steps[0].standardHours).toBe(2.5)
    expect(steps.every((s) => s.workCenterId === 'c1')).toBe(true)

    await page.getByRole('button', { name: 'Vincular produtos' }).click()
    await page.getByRole('button', { name: 'Vincular produto' }).click()
    await page.getByRole('combobox', { name: 'Produto para vincular' }).click()
    await page.getByRole('option', { name: /CAB-01/ }).click()
    await expect(page.getByText('Produto vinculado')).toBeVisible()
    expect(linked).toEqual({ itemId: 'i1' })
  })

  test('erro do servidor no campo da linha e vínculo recusado com "Abrir roteiro"', async ({ page }) => {
    await routingApi(page, {
      'PUT /api/v1/engineering/routing-templates/*': (r: Route) => json(r, { code: 'validation', detail: 'A letra C já está na etapa 2.', errors: { 'steps[2].code': ['A letra C já está na etapa 2.'] } }, 422),
      'GET /api/v1/engineering/routing-templates/products': (r: Route) => json(r, [{ id: 'i1', code: 'CAB-01', name: 'Cabine', type: 'Finished', routingTemplateId: 'rt9', routingTemplateName: 'Porta' }]),
      'POST /api/v1/engineering/routing-templates/*/items': (r: Route) => json(r, { code: 'routing_item_taken', detail: 'O produto CAB-01 já usa o roteiro Porta.', errors: { itemId: ['O produto CAB-01 já usa o roteiro Porta.'] }, routingTemplateId: 'rt9', routingTemplateName: 'Porta' }, 422),
    })
    await page.goto('/app/engenharia/roteiros/rt1')
    await page.getByRole('button', { name: 'Salvar roteiro' }).click()
    await expect(page.getByRole('alertdialog')).toContainText('As OS já criadas não mudam. Só as próximas usam o roteiro novo.')
    await page.getByRole('alertdialog').getByRole('button', { name: 'Salvar roteiro' }).click()
    await expect(page.getByLabel('Letra da etapa 3')).toHaveAttribute('aria-invalid', 'true')
    await expect(page.getByText('A letra C já está na etapa 2.')).toBeVisible()

    await page.getByRole('tab', { name: 'Produtos' }).click()
    await expect(page.getByText('Nenhum produto usa este roteiro. Vincule para a OS nascer com ele.')).toBeVisible()
    await page.getByRole('button', { name: 'Vincular produto' }).click()
    await page.getByRole('combobox', { name: 'Produto para vincular' }).click()
    await page.getByRole('option', { name: /CAB-01/ }).click()
    await expect(page.getByRole('alert').filter({ hasText: 'O produto CAB-01 já usa o roteiro Porta.' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Abrir roteiro Porta' })).toBeVisible()
  })

  test('lista com letras e uso, e a 400 px sem rolagem lateral', async ({ page }) => {
    await routingApi(page)
    await page.setViewportSize({ width: 400, height: 820 })
    await page.goto('/app/engenharia/roteiros')
    await expect(page.getByRole('heading', { name: 'Roteiros de fabricação' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Cabine padrão' })).toBeVisible()
    await expect(page.getByRole('img', { name: 'Etapa Projeto, 1 de 5' })).toBeVisible()
    await expect(page.getByText('Ativo', { exact: true })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)

    await page.goto('/app/engenharia/roteiros/rt1')
    await expect(page.getByRole('button', { name: 'Remover etapa 1' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Mover etapa 1 para baixo' })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
  })

  test('sem engineering.edit: só leitura com o motivo e sem os botões de alterar', async ({ page }) => {
    await routingApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ engineering: { actions: ['view'] } as never }) }),
    })
    await page.goto('/app/engenharia/roteiros/rt1')
    await expect(page.getByText('Você pode consultar este roteiro.', { exact: false })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Salvar roteiro' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Excluir roteiro' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Duplicar' })).toHaveCount(0)
  })

  test('módulo não liberado mostra a tela padrão', async ({ page }) => {
    await routingApi(page, {
      'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '1', modules: allModules({ engineering: { released: false } }) }),
    })
    await page.goto('/app/engenharia/roteiros')
    await expect(page.getByText('Funcionalidade não liberada nessa versão do sistema.')).toBeVisible()
  })
})