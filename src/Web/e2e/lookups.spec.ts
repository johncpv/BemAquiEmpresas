import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const CNPJ = '47960950000121'

/** Consulta de CNPJ e CEP (CAD-01b) com a API simulada; as regras do provedor estão nos testes de integração .NET. */
test.describe('consulta de CNPJ e CEP (CAD-01b)', () => {
  test('CNPJ novo traz os dados da Receita, "Usar estes dados" preenche e salva com o endereço (CA 1, 2 e 3)', async ({ page }) => {
    let body: Record<string, unknown> | null = null
    await mockApi(page, {
      'GET /api/v1/parties/document-check': (r: Route) => json(r, { valid: true, exists: false, id: null, name: null, deleted: false, roles: [] }),
      'GET /api/v1/parties/cnpj/*': (r: Route) => json(r, {
        data: { document: CNPJ, legalName: 'ALFA ELEVADORES LTDA', tradeName: 'ALFA', situation: 'INAPTA', activeAtReceita: false, phone: '1236531000', email: 'contato@alfa.com.br',
          address: { zipCode: '12280000', street: 'Rua das Flores', number: '120', complement: null, district: 'Centro', city: 'Caçapava', state: 'SP', ibgeCode: '3508504' } },
        lookedUpAt: '2026-10-19T12:00:00Z', fromCache: false,
      }),
      'POST /api/v1/parties': (r: Route) => { body = r.request().postDataJSON(); return json(r, { id: '0199f0e0-0000-7000-8000-0000000000d1' }, 201) },
      'GET /api/v1/parties/*': (r: Route) => json(r, { code: 'not_found' }, 404),
    })
    await page.goto('/app/cadastros/terceiros/novo')
    await page.getByRole('textbox', { name: 'CPF ou CNPJ' }).fill('47.960.950/0001-21')
    await page.getByRole('textbox', { name: /Razão social/ }).focus()

    const receita = page.getByRole('region', { name: 'Dados da Receita' })
    await expect(receita).toContainText('ALFA ELEVADORES LTDA')
    await expect(receita).toContainText('Situação na Receita: INAPTA')
    await receita.getByRole('button', { name: 'Usar estes dados' }).click()
    await expect(page.getByRole('textbox', { name: /Razão social/ })).toHaveValue('ALFA ELEVADORES LTDA')
    await expect(page.getByLabel('Logradouro')).toHaveValue('Rua das Flores')
    await expect(page.getByLabel('Bairro')).toHaveValue('Centro')
    await page.getByRole('button', { name: 'Salvar terceiro' }).first().click()
    await expect.poll(() => body).not.toBeNull()
    expect(body!.billingAddress).toMatchObject({ zipCode: '12280000', ibgeCode: '3508504', state: 'SP' })
  })

  test('CEP completo preenche os campos vazios e avisa quando não acha (CA 10)', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/parties/cep/12280000': (r: Route) => json(r, { zipCode: '12280000', street: 'Rua das Flores', district: 'Centro', city: 'Caçapava', state: 'SP', ibgeCode: '3508504' }),
      'GET /api/v1/parties/cep/99999999': (r: Route) => json(r, { code: 'cep_not_found', detail: 'CEP não encontrado. Preencha o endereço.' }, 404),
    })
    await page.goto('/app/cadastros/terceiros/novo')
    await page.getByLabel('CEP').fill('12280000')
    await expect(page.getByLabel('Cidade')).toHaveValue('Caçapava')
    await expect(page.getByText('Endereço preenchido pelo CEP. Confira o número.')).toBeVisible()
    await page.getByLabel('CEP').fill('99999999')
    await expect(page.getByText('CEP não encontrado. Preencha o endereço.')).toBeVisible()
  })
})
