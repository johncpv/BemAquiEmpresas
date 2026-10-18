import { expect, test } from '@playwright/test'
import { aiSettings, json, mockApi } from './mock-api'

const grupos = {
  key: 'acessos-grupos', title: 'Grupos de acesso', route: '/app/acessos/grupos', tab: 'grupos', isDefault: true, module: 'identity',
  sections: [{ title: 'Como usar', body: 'Clique em **Novo grupo**.' }, { title: 'Perguntas frequentes', body: '**Posso ter dois grupos?** Sim.' }],
}
const citation = { key: 'acessos-grupos', documentTitle: 'Grupos de acesso', section: 'Como usar', body: 'Clique em **Novo grupo**.' }

test.describe('Pergunte ao BemAqui (FND-15)', () => {
  test('sem provedor mostra os trechos com link para a fonte; com dado da empresa pede confirmação', async ({ page }) => {
    const asked: unknown[] = []
    await mockApi(page, {
      'GET /api/v1/ajuda': (r) => json(r, [grupos]),
      'GET /api/v1/ajuda/vistas': (r) => json(r, ['acessos-grupos']),
      'POST /api/v1/ajuda/perguntar': (r) => {
        const body = r.request().postDataJSON() as { question: string; confirmed: boolean }
        asked.push(body)
        if (body.question.includes('@') && !body.confirmed) return json(r, { kind: 'confirm', text: 'Sua pergunta parece ter dados da empresa.', citations: [] })
        return json(r, body.confirmed ? { kind: 'answer', text: 'Use Novo grupo.', citations: [citation] } : { kind: 'snippets', text: 'Encontrei estes trechos na ajuda:', citations: [citation] })
      },
    })
    await page.goto('/app/acessos/grupos')
    await page.getByRole('button', { name: 'Ajuda desta tela' }).click()
    const panel = page.getByRole('dialog', { name: 'Ajuda' })
    await panel.getByRole('tab', { name: 'Pergunte ao BemAqui' }).click()
    await expect(panel.getByText('As respostas vêm só da ajuda do sistema.')).toBeVisible()

    await panel.getByRole('button', { name: 'Posso ter dois grupos?' }).click()
    await expect(panel.getByText('Encontrei estes trechos na ajuda:')).toBeVisible()
    await expect(panel.locator('strong', { hasText: 'Novo grupo' })).toBeVisible()

    await panel.getByLabel('Sua pergunta').fill('Como dar acesso para ana@cliente.com.br?')
    await panel.getByRole('button', { name: 'Perguntar' }).click()
    await expect(panel.getByRole('alert')).toContainText('dados da empresa')
    await panel.getByRole('button', { name: 'Enviar assim mesmo' }).click()
    await expect(panel.getByText('Use Novo grupo.')).toBeVisible()
    expect(asked).toHaveLength(3)

    await panel.getByRole('button', { name: 'Grupos de acesso › Como usar' }).click()
    await expect(panel.getByRole('tab', { name: 'Ajuda da tela' })).toHaveAttribute('aria-selected', 'true')
    await expect(panel.getByRole('heading', { name: 'Grupos de acesso' })).toBeVisible()
  })

  test('configurar provedor: testar conexão, salvar e a chave não volta', async ({ page }) => {
    let configured = false
    let saved: Record<string, unknown> | undefined
    await mockApi(page, {
      'GET /api/v1/ajuda/vistas': (r) => json(r, ['configuracoes-ia']),
      'GET /api/v1/settings/ia': (r) => json(r, aiSettings(configured)),
      'POST /api/v1/settings/ia/testar': (r) => json(r, { ok: true, message: 'Conexão OK' }),
      'PUT /api/v1/settings/ia': (r) => { saved = r.request().postDataJSON() as Record<string, unknown>; configured = true; return json(r, aiSettings(true)) },
    })
    await page.goto('/app/configuracoes/ia')
    await expect(page.getByText('Nenhum provedor configurado.')).toBeVisible()
    await page.getByRole('button', { name: 'Configurar provedor' }).click()

    await page.getByRole('button', { name: 'Salvar configuração' }).click()
    await expect(page.getByText('Informe a chave de API.')).toBeVisible()
    await page.getByLabel('Chave de API').fill('sk-ant-e2e-123456')
    await page.getByRole('button', { name: 'Testar conexão' }).click()
    await expect(page.getByText('Conexão OK')).toBeVisible()
    await page.getByRole('button', { name: 'Salvar configuração' }).click()

    await expect(page.getByText('Configuração salva')).toBeVisible()
    expect(saved).toEqual({ provider: 'anthropic', model: 'claude-haiku-5-5', apiKey: 'sk-ant-e2e-123456' })
    await expect(page.getByText(/Chave configurada em/)).toBeVisible()
    await expect(page.getByText('sk-ant-e2e-123456')).toHaveCount(0)
  })
})
