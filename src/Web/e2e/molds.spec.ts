import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { mockApi } from './mock-api'

const SHOTS = process.env.BQ_SHOTS

/** Moldes e componentes da FND-09d (CA 2, 7, 10, 17, 19, 21, 22 e 23), na galeria de aprovação dentro do shell. */
test.describe('moldes de tela (FND-09d)', () => {
  test.beforeEach(async ({ page }) => mockApi(page))

  for (const [path, heading] of [['/app/dev/moldes/cadastro', 'Novo terceiro'], ['/app/dev/moldes/detalhe', 'WSP Equipamentos por Elevação Ltda'], ['/app/dev/moldes/painel', 'Unidades de medida'], ['/app/dev/componentes', 'Componentes de formulário']] as const) {
    test(`${heading}: sem violação séria (axe) e sem rolagem horizontal (CA 22, 23)`, async ({ page }, info) => {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible()
      const results = await new AxeBuilder({ page }).analyze()
      const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
      expect(serious.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([])
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
      expect(overflow).toBeLessThanOrEqual(0)
      if (SHOTS) await page.screenshot({ path: `${SHOTS}/${info.project.name}-${path.split('/').pop()}.png`, fullPage: true })
    })
  }

  test('cadastro: documento define PF/PJ, erros por campo e criação rápida no combobox (CA 3, 7, 15)', async ({ page }) => {
    await page.goto('/app/dev/moldes/cadastro')
    await page.getByRole('textbox', { name: 'CPF ou CNPJ' }).fill('123.456.789-09')
    await expect(page.getByRole('radio', { name: 'Pessoa física' })).toHaveAttribute('aria-checked', 'true')
    await expect(page.getByLabel('Nome', { exact: false }).first()).toHaveAttribute('aria-required', 'true')
    await page.getByRole('button', { name: 'Salvar terceiro' }).first().click()
    await expect(page.getByText('Informe o nome ou a razão social.')).toBeVisible()

    await page.getByRole('combobox', { name: 'Condição de pagamento padrão' }).click()
    await page.getByPlaceholder('Digite para buscar').fill('45 dias')
    await page.getByRole('option', { name: 'Criar condição "45 dias"' }).click()
    await expect(page.getByRole('dialog', { name: 'Nova condição de pagamento' })).toBeVisible()
    await page.getByRole('button', { name: 'Salvar condição' }).click()
    await expect(page.getByRole('combobox', { name: 'Condição de pagamento padrão' })).toContainText('45 dias')
  })

  test('detalhe: erro em aba escondida abre a aba e mostra o contador; excluir recusado oferece inativar (CA 10, 17)', async ({ page }) => {
    await page.goto('/app/dev/moldes/detalhe?aba=contatos')
    await expect(page.getByRole('tab', { name: 'Contatos' })).toHaveAttribute('aria-selected', 'true')
    await page.getByRole('tab', { name: 'Dados' }).click()
    await page.getByLabel('Razão social').fill('')
    await page.getByRole('tab', { name: 'Contatos' }).click()
    await page.getByRole('button', { name: 'Salvar terceiro' }).click()
    await expect(page.getByRole('tab', { name: /Dados/ })).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByRole('tab', { name: /Dados 1 erro/ })).toBeVisible()
    await expect(page.getByLabel('Razão social')).toBeFocused()

    await page.getByRole('button', { name: 'Excluir' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Excluir' }).click()
    await expect(page.getByText('Este terceiro tem orçamentos e não pode ser excluído.')).toBeVisible()
    await page.getByRole('button', { name: 'Inativar' }).click()
    await expect(page.getByText('Terceiro inativado')).toBeVisible()
  })

  test('painel: filtro de inativos na URL e guarda ao fechar com alteração (CA 19, 21)', async ({ page }) => {
    await page.goto('/app/dev/moldes/painel')
    await expect(page.getByRole('button', { name: 'CX' })).toHaveCount(0)
    await page.getByLabel('Mostrar inativos').check()
    await expect(page).toHaveURL(/inativos=true/)
    await expect(page.getByRole('button', { name: 'CX' })).toBeVisible()

    await page.getByRole('button', { name: 'KG' }).click()
    await page.getByRole('dialog').getByRole('textbox', { name: 'Nome (obrigatório)' }).fill('Quilo')
    await page.keyboard.press('Escape')
    await expect(page.getByText('Há alterações não salvas')).toBeVisible()
    await page.getByRole('button', { name: 'Descartar' }).click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
  })
})
