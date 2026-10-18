import AxeBuilder from '@axe-core/playwright'
import { expect, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const RUN = '0199f0e0-0000-7000-8000-0000000000aa'
const PARTY = '0199f0e0-0000-7000-8000-0000000000bb'

const run = { id: RUN, startedAt: '2026-10-18T13:00:00Z', finishedAt: '2026-10-18T13:02:00Z', mode: 'Real', status: 'Succeeded', operator: 'operador@konebras.com.br', entities: 'parties', purged: false }
const detail = {
  ...run, snapshotTakenAt: '2026-10-18T12:55:00Z', acceptedAt: null, purgedAt: null, failure: null,
  entities: [{ entity: 'parties', read: 120, imported: 110, updated: 0, unchanged: 0, merged: 0, linked: 3, skipped: 4, conflicts: 1, errors: 2, warnings: 1, total: 120 }],
}
const pending = [
  { id: 'p1', entity: 'parties', sourceTable: 'llx_societe', sourceId: '42', kind: 'conflict', targetType: 'party', targetId: PARTY, message: 'Editado no BemAqui depois da importação e alterado no Dolibarr. O BemAqui não foi sobrescrito.', warning: null, differences: [{ field: 'legalName', source: 'Alfa Ltda', target: 'Alfa Elevadores Ltda' }] },
  { id: 'p2', entity: 'parties', sourceTable: 'llx_societe', sourceId: '77', kind: 'error', targetType: null, targetId: null, message: 'CNPJ inválido.', warning: null, differences: null },
]

/** Tela só de leitura da importação (MIG-01a CA 19); as regras reais estão nos testes de integração .NET. */
test.describe('importação do Dolibarr (MIG-01a)', () => {
  test('sem execuções, o vazio explica que a carga é pela CLI e abre a ajuda', async ({ page }) => {
    await mockApi(page, { 'GET /api/v1/settings/import-runs': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }) })
    await page.goto('/app/configuracoes/importacao')
    await expect(page.getByText('Nenhuma importação feita.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Importação do Dolibarr' })).toHaveAttribute('aria-current', 'page')
    await expect(page.getByRole('button', { name: /Importar|Executar/ })).toHaveCount(0)
    await page.getByRole('button', { name: 'Ver o passo a passo' }).click()
    await expect(page.getByRole('dialog', { name: 'Ajuda' })).toBeVisible()
  })

  test('execução com totais e pendências filtradas na URL, com link para o registro (CA 17 e 19)', async ({ page }) => {
    await mockApi(page, {
      'GET /api/v1/settings/import-runs': (r: Route) => json(r, { items: [run], page: 1, pageSize: 20, total: 1 }),
      'GET /api/v1/settings/import-runs/*/pending': (r: Route) => {
        const kind = new URL(r.request().url()).searchParams.get('kind')
        const items = pending.filter((p) => !kind || p.kind === kind)
        return json(r, { items, page: 1, pageSize: 20, total: items.length })
      },
      'GET /api/v1/settings/import-runs/*': (r: Route) => json(r, detail),
    })
    await page.goto('/app/configuracoes/importacao')
    await page.getByRole('link', { name: /18\/10\/2026/ }).click()

    await expect(page.getByRole('heading', { level: 1, name: /Importação de 18\/10\/2026/ })).toBeVisible()
    const totals = page.getByRole('table', { name: 'Totais por entidade' })
    await expect(totals.getByRole('rowheader', { name: 'Terceiros' })).toBeVisible()
    await expect(totals.getByRole('cell', { name: '110' })).toBeVisible()
    await expect(page.getByText('Alfa Elevadores Ltda', { exact: false })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Abrir registro' })).toHaveAttribute('href', `/app/cadastros/terceiros/${PARTY}`)

    await page.getByRole('combobox', { name: /^Tipo/ }).selectOption('error')
    await expect(page).toHaveURL(/tipo=error/)
    await expect(page.getByText('CNPJ inválido.')).toBeVisible()
    await expect(page.getByText('Alfa Elevadores Ltda', { exact: false })).toHaveCount(0)

    for (const path of ['/app/configuracoes/importacao', `/app/configuracoes/importacao/${RUN}`]) {
      await page.goto(path)
      await page.getByRole('heading', { level: 1 }).waitFor()
      const results = await new AxeBuilder({ page }).analyze()
      expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0)
    }
  })
})
