import { expect, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const actions = (codes: string[]) => codes.map((c) => ({ code: c, label: { view: 'Ver', create: 'Criar', edit: 'Editar', approve: 'Aprovar', delete: 'Excluir' }[c] ?? c, description: c === 'approve' ? 'Aprovar aqui significa: ordem de pagamento.' : '', isStandard: true }))
const detail = (system: boolean) => ({
  group: { id: system ? 'g0' : 'g1', name: system ? 'Proprietário' : 'Comercial', description: 'Orçamentos.', isSystem: system, isActive: true, requiresTwoFactor: system, memberCount: 2 },
  members: [{ userId: 'u1', name: 'Ana Souza', email: 'ana@konebras.com.br' }, { userId: 'u2', name: 'Caio Lima', email: 'caio@konebras.com.br' }],
  modules: [
    { code: 'quotes', name: 'Orçamentos', area: 'Comercial', released: true, enabled: true, actions: ['view'], available: actions(['view', 'create', 'edit', 'approve', 'delete']), approveMeaning: 'gerar o contrato' },
    { code: 'finance', name: 'Caixa e pagamentos', area: 'Financeiro', released: true, enabled: false, actions: [], available: actions(['view', 'create', 'edit', 'approve', 'delete']), approveMeaning: 'ordem de pagamento' },
    { code: 'fiscal', name: 'Fiscal', area: 'Financeiro', released: false, enabled: false, actions: [], available: actions(['view']), approveMeaning: null },
  ],
})
const groups = [detail(true).group, detail(false).group]

test.describe('Usuários e acessos e Segurança (FND-06b/06c)', () => {
  test('matriz: habilitar marca Ver, alteração não salva e Salvar grupo com o toast (CA 1, 3)', async ({ page }) => {
    let saved: unknown
    await mockApi(page, {
      'GET /api/v1/identity/groups': (r) => json(r, groups),
      'GET /api/v1/identity/groups/g1': (r) => json(r, detail(false)),
      'GET /api/v1/identity/groups/g0': (r) => json(r, detail(true)),
      'PUT /api/v1/identity/groups/g1': (r) => { saved = r.request().postDataJSON(); return json(r, { id: 'g1', affectedUsers: 2 }) },
    })
    await page.goto('/app/acessos/grupos?grupo=g1')
    await page.getByRole('switch', { name: 'Habilitar Caixa e pagamentos' }).click()
    await expect(page.getByRole('checkbox', { name: 'Ver em Caixa e pagamentos' })).toBeChecked()
    await expect(page.getByText('Alterações não salvas')).toBeVisible()
    await expect(page.getByRole('checkbox', { name: 'Aprovar em Caixa e pagamentos' })).toHaveAttribute('title', /ordem de pagamento/)
    await page.getByRole('button', { name: 'Salvar grupo' }).click()
    await expect(page.getByText('Grupo Comercial salvo. 2 usuários recebem as novas permissões no próximo acesso.')).toBeVisible()
    expect(JSON.stringify(saved)).toContain('"moduleCode":"finance"')
  })

  test('Proprietário é do sistema e módulo não liberado aparece desabilitado (CA 4, 10)', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/identity/groups': (r) => json(r, groups), 'GET /api/v1/identity/groups/g0': (r) => json(r, detail(true)) })
    await page.goto('/app/acessos/grupos?grupo=g0')
    await expect(page.getByText('Grupo do sistema: acesso total, não pode ser restringido')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Salvar grupo' })).toHaveCount(0)
    await expect(page.getByText('Não liberado')).toBeVisible()
  })

  test('convidar: e-mail existente oferece abrir o usuário; sucesso oferece convidar outra pessoa (CA 5, 8)', async ({ page }) => {
    let calls = 0
    await mockApi(page, {
      'GET /api/v1/identity/users': (r) => json(r, [{ id: 'u1', name: 'Ana Souza', email: 'ana@konebras.com.br', groups: [{ id: 'g0', name: 'Proprietário' }], twoFactor: 'active', lastLoginAt: '2026-10-18T12:00:00Z', status: 'Active', inviteOpen: false }]),
      'GET /api/v1/identity/groups': (r) => json(r, groups),
      'POST /api/v1/identity/users/invite': (r) => (++calls === 1 ? json(r, { code: 'user_exists', detail: 'Já existe.' }, 409) : json(r, { id: 'u9', email: 'bia@konebras.com.br' }, 201)),
    })
    await page.goto('/app/acessos/usuarios')
    await expect(page.getByText('ana@konebras.com.br')).toBeVisible()
    await page.getByRole('button', { name: 'Convidar usuário' }).click()
    const dialog = page.getByRole('dialog', { name: 'Convidar usuário' })
    await dialog.getByLabel('Nome').fill('Bia Lima')
    await dialog.getByLabel('E-mail').fill('bia@konebras.com.br')
    await dialog.getByRole('button', { name: 'Enviar convite' }).click()
    await expect(dialog.getByRole('button', { name: 'Abrir o usuário' })).toBeVisible()
    await dialog.getByRole('button', { name: 'Enviar convite' }).click()
    await expect(page.getByText('Convite enviado para bia@konebras.com.br')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Convidar outra pessoa' })).toBeVisible()
  })

  test('Segurança: Exigir confirma com o mesmo verbo (CA 13)', async ({ page }) => {
    let required = false
    await mockApi(page, {
      'GET /api/v1/settings/security': (r) => json(r, { requireTwoFactor: required }),
      'PUT /api/v1/settings/security': (r) => { required = true; return json(r, { requireTwoFactor: true }) },
    })
    await page.goto('/app/configuracoes/seguranca')
    await page.getByRole('button', { name: 'Exigir' }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Exigir' }).click()
    await expect(page.getByText('Exigência ligada')).toBeVisible()
    await expect(page.getByText('Exigida', { exact: true })).toBeVisible()
  })
})
