import { expect, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const inicio = { key: 'inicio', title: 'Início', route: '/app', tab: null, isDefault: true, module: null, sections: [{ title: 'Para que serve', body: 'Porta de entrada do sistema.' }, { title: 'Como usar', body: '- Clique em **Convidar a equipe**.' }] }
const grupos = { key: 'acessos-grupos', title: 'Grupos de acesso', route: '/app/acessos/grupos', tab: 'grupos', isDefault: true, module: 'identity', sections: [{ title: 'Como usar', body: 'Ligar **Habilitado** marca **Ver**.' }] }

test.describe('ajuda contextual (FND-14)', () => {
  test('abre sozinha na primeira visita, registra e não abre de novo (CA 4, 6)', async ({ page }) => {
    let seen: string[] = []
    await mockApi(page, {
      'GET /api/v1/ajuda': (r) => json(r, [inicio, grupos]),
      'GET /api/v1/ajuda/vistas': (r) => json(r, seen),
      'POST /api/v1/ajuda/inicio/vista': (r) => { seen = ['inicio']; return r.fulfill({ status: 204 }) },
    })
    await page.goto('/app')
    const panel = page.getByRole('dialog', { name: 'Ajuda' })
    await expect(panel.getByRole('heading', { name: 'Início' })).toBeVisible()
    await expect(panel.locator('strong', { hasText: 'Convidar a equipe' })).toBeVisible()
    expect(seen).toEqual(['inicio'])
    await page.keyboard.press('Escape')

    await page.reload() // outro "dispositivo": o registro está no servidor
    await page.getByRole('heading', { level: 1, name: 'Início' }).waitFor()
    await expect(panel).toHaveCount(0)
    await page.getByRole('button', { name: 'Ajuda desta tela' }).click()
    await panel.getByRole('searchbox', { name: 'Buscar na ajuda' }).fill('habilitado')
    await expect(panel.getByRole('list', { name: 'Resultados da busca' })).toContainText('Grupos de acesso')
    await panel.getByRole('searchbox', { name: 'Buscar na ajuda' }).fill('zzz')
    await expect(panel.getByText('Nada encontrado para "zzz". Tente outra palavra.')).toBeVisible()
  })

  test('<Dica> abre por teclado e por toque e fecha com Esc (CA 7)', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/ajuda/vistas': (r) => json(r, ['configuracoes-seguranca']) })
    await page.goto('/app/configuracoes/seguranca')
    const dica = page.getByRole('button', { name: 'Dica', exact: true }) // a de Aparelhos da fábrica (PRD-03b) tem nome próprio
    await dica.focus()
    await expect(page.getByRole('dialog').getByText('Quem ainda não tem será levado à configuração no próximo acesso.')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await dica.click()
    await expect(page.getByRole('dialog').getByText('Quem ainda não tem será levado à configuração no próximo acesso.')).toBeVisible()
  })
})
