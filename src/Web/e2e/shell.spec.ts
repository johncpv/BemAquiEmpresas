import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const isMobile = (name: string) => name === 'celular'

test.describe('shell do backoffice (FND-09b)', () => {
  test.beforeEach(async ({ page }) => mockApi(page))

  test('desktop: menu lateral com os módulos abertos, setas navegam e o módulo atual fica marcado (CA 2, 5, adendo D8)', async ({ page }, info) => {
    test.skip(isMobile(info.project.name))
    // Lista de Projetos vazia e válida: o "{}" genérico do mock derruba a tela de destino.
    await mockApi(page, {
      'GET /api/v1/projects': (r) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }),
      'GET /api/v1/projects/margins': (r) => json(r, []),
      'GET /api/v1/projects/received': (r) => json(r, []),
    })
    await page.goto('/app')
    const nav = page.getByRole('navigation', { name: 'Navegação principal' })
    const orcamentos = nav.getByRole('link', { name: 'Orçamentos' })
    await expect(orcamentos).toBeVisible()
    await expect(nav.getByRole('list', { name: 'Comercial' })).toBeVisible()
    // O menu fica à esquerda e o conteúdo ao lado, sem o espaço do dock.
    const side = (await nav.boundingBox())!
    const main = (await page.getByRole('main').boundingBox())!
    expect(side.x).toBe(0)
    expect(main.x).toBeGreaterThanOrEqual(side.x + side.width)
    await orcamentos.focus()
    await page.keyboard.press('ArrowDown')
    await expect(nav.getByRole('link', { name: 'Projetos' })).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/projetos/)
    await expect(nav.getByRole('link', { name: 'Projetos' })).toHaveAttribute('aria-current', 'page')
    await expect(nav.locator('[aria-current="page"]')).toHaveCount(1)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
  })

  test('desktop: Recolher deixa só os ícones, o ícone abre o popover, Esc devolve o foco e a escolha fica lembrada (adendo D8)', async ({ page }, info) => {
    test.skip(isMobile(info.project.name))
    await page.goto('/app')
    const nav = page.getByRole('navigation', { name: 'Navegação principal' })
    await nav.getByRole('button', { name: 'Recolher' }).click()
    await expect(nav.getByRole('link')).toHaveCount(0)
    expect((await nav.boundingBox())!.width).toBeLessThan(80)
    const comercial = nav.getByRole('button', { name: 'Comercial' })
    await comercial.click()
    const menu = page.getByRole('dialog', { name: 'Comercial' })
    const orcamentos = menu.getByRole('link', { name: 'Orçamentos' })
    await expect(orcamentos).toBeVisible()
    await orcamentos.focus()
    await page.keyboard.press('ArrowDown')
    await expect(menu.getByRole('link', { name: 'Projetos' })).toBeFocused()
    await page.keyboard.press('Escape')
    await expect(orcamentos).toBeHidden()
    await expect(comercial).toBeFocused()
    await page.reload()
    await expect(nav.getByRole('button', { name: 'Expandir o menu' })).toBeVisible()
    await nav.getByRole('button', { name: 'Expandir o menu' }).click()
    await expect(nav.getByRole('link', { name: 'Orçamentos' })).toBeVisible()
  })

  test('desktop: erro ao carregar o menu mostra a mensagem e Tentar de novo', async ({ page }, info) => {
    test.skip(isMobile(info.project.name))
    let fail = true
    await mockApi(page, {
      'GET /api/v1/me/permissions': (r) => (fail
        ? r.fulfill({ status: 500, contentType: 'application/json', body: '{}' })
        : r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ permissions: [], isOwner: true, version: '1', modules: allModules() }) })),
    })
    await page.goto('/app')
    const nav = page.getByRole('navigation', { name: 'Navegação principal' })
    await expect(nav.getByRole('alert')).toHaveText(/Não foi possível carregar o menu\./, { timeout: 15_000 })
    fail = false
    await nav.getByRole('button', { name: 'Tentar de novo' }).click()
    await expect(nav.getByRole('link', { name: 'Orçamentos' })).toBeVisible()
  })

  test('celular: área abre sheet e nada transborda na horizontal (CA 2, 4, 7)', async ({ page }, info) => {
    test.skip(!isMobile(info.project.name))
    await page.goto('/app')
    await page.getByRole('button', { name: 'Fábrica' }).tap()
    await expect(page.getByRole('dialog', { name: 'Fábrica' }).getByRole('link', { name: 'Estoque' })).toBeVisible()
    await page.keyboard.press('Escape')
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(0)
    await expect(page.getByRole('button', { name: 'Buscar tela (Ctrl K)' })).toBeVisible()
    await expect(page.getByRole('radiogroup', { name: 'Tema' })).toHaveCount(0)
  })

  test('Ctrl K abre a paleta e Enter navega (CA 9)', async ({ page }) => {
    await page.goto('/app')
    await page.getByRole('heading', { level: 1 }).waitFor()
    await page.keyboard.press('Control+k')
    await page.getByPlaceholder('Buscar tela ou módulo…').fill('Segurança')
    await page.keyboard.press('Enter')
    await expect(page).toHaveURL(/\/app\/configuracoes\/seguranca$/)
    await expect(page).toHaveTitle('Configurações: Segurança · Konebras · BemAqui Empresas')
  })

  test('primeiro Tab é "Pular para o conteúdo" (CA 12)', async ({ page }) => {
    await page.goto('/app')
    await page.getByRole('heading', { level: 1 }).waitFor()
    await page.keyboard.press('Tab')
    await expect(page.getByRole('link', { name: 'Pular para o conteúdo' })).toBeFocused()
  })

  test('sem violações sérias de acessibilidade no shell (axe)', async ({ page }) => {
    await page.goto('/app')
    await page.getByRole('heading', { level: 1 }).waitFor()
    const results = await new AxeBuilder({ page }).analyze()
    const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
    expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([])
  })
})

test.describe('tema (FND-09a)', () => {
  test.beforeEach(async ({ page }) => mockApi(page))

  test('sem flash: o tema guardado vale antes da primeira pintura (CA 8)', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/me/preferences': (r) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ theme: 'dark' }) }) })
    await page.addInitScript(() => localStorage.setItem('bq-theme', 'dark'))
    // O script inline roda antes de qualquer CSS ou JS do app: no DOMContentLoaded o atributo já está lá.
    await page.goto('/app', { waitUntil: 'domcontentloaded' })
    expect(await page.evaluate(() => document.documentElement.getAttribute('data-theme'))).toBe('dark')
    await page.waitForLoadState('load')
    await page.getByRole('heading', { level: 1 }).waitFor()
    const background = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--background').trim())
    expect(background).toBe('#0e1419')
  })

  test('Automático acompanha o sistema sem recarregar (CA 10)', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' })
    await page.goto('/app')
    const bg = () => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--background').trim())
    expect(await bg()).toBe('#e9edf1')
    await page.emulateMedia({ colorScheme: 'dark' })
    expect(await bg()).toBe('#0e1419')
  })

  test('galeria nos dois temas (CA 16, screenshots para comparar com o protótipo)', async ({ page }, info) => {
    for (const scheme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: scheme })
      await page.goto('/dev/galeria')
      await page.getByRole('heading', { name: 'Galeria de componentes' }).waitFor()
      await info.attach(`galeria-${scheme}-${info.project.name}`, { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' })
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      expect(overflow).toBeLessThanOrEqual(0)
    }
  })
})
