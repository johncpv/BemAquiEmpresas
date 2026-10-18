import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { mockApi } from './mock-api'

const isMobile = (name: string) => name === 'celular'

test.describe('shell do backoffice (FND-09b)', () => {
  test.beforeEach(async ({ page }) => mockApi(page))

  test('desktop: área abre popover, setas navegam e Esc devolve o foco (CA 2, 5)', async ({ page }, info) => {
    test.skip(isMobile(info.project.name))
    await page.goto('/app')
    const comercial = page.getByRole('button', { name: 'Comercial' })
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
