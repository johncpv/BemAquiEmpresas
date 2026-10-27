import { readFileSync } from 'node:fs'
import { crc32 } from 'node:zlib'
import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
}

const BOM = '﻿'
const MOVEMENTS = BOM + 'evento;matricula;nome;cpf;nascimento;cargo;cbo;salario;jornada;data;tipo_afastamento;fim_previsto\r\n'
  + 'admissao;001;Ana Souza;123.456.789-09;10/05/1990;Montador;7155-05;3500,00;44 h;05/09/2026;;\r\n'
  + "desligamento;002;'=Rui Prado;111.444.777-35;;;;;;20/09/2026;;\r\n"

/** ZIP sem compressão (STORED), como o servidor entrega o pacote: só para o E2E abrir o CSV baixado. */
function zip(files: Record<string, string>) {
  const locals: Buffer[] = []
  const centrals: Buffer[] = []
  let offset = 0
  for (const [name, text] of Object.entries(files)) {
    const data = Buffer.from(text, 'utf8')
    const fileName = Buffer.from(name, 'utf8')
    const crc = crc32(data)
    const local = Buffer.alloc(30)
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt32LE(crc, 14); local.writeUInt32LE(data.length, 18); local.writeUInt32LE(data.length, 22); local.writeUInt16LE(fileName.length, 26)
    const central = Buffer.alloc(46)
    central.writeUInt32LE(0x02014b50, 0); central.writeUInt16LE(20, 4); central.writeUInt16LE(20, 6); central.writeUInt32LE(crc, 16); central.writeUInt32LE(data.length, 20); central.writeUInt32LE(data.length, 24)
    central.writeUInt16LE(fileName.length, 28); central.writeUInt32LE(offset, 42)
    locals.push(local, fileName, data)
    centrals.push(central, fileName)
    offset += local.length + fileName.length + data.length
  }
  const directory = Buffer.concat(centrals)
  const end = Buffer.alloc(22)
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(Object.keys(files).length, 8); end.writeUInt16LE(Object.keys(files).length, 10); end.writeUInt32LE(directory.length, 12); end.writeUInt32LE(offset, 16)
  return Buffer.concat([...locals, directory, end])
}

/** Lê uma entrada do ZIP sem compressão. */
function entry(content: Buffer, name: string) {
  let i = 0
  while (content.readUInt32LE(i) === 0x04034b50) {
    const size = content.readUInt32LE(i + 18)
    const nameLength = content.readUInt16LE(i + 26)
    const current = content.subarray(i + 30, i + 30 + nameLength).toString('utf8')
    const start = i + 30 + nameLength
    if (current === name) return content.subarray(start, start + size).toString('utf8')
    i = start + size
  }
  return null
}

const sensitive = allModules().map((m) => (m.code === 'hr' ? { ...m, actions: ['view', 'create', 'edit', 'view_sensitive'] } : m))
const asSensitive = { 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '32', modules: sensitive }) }

/** Backend em memória da RH-03 (as regras reais estão nos testes de integração .NET). */
function exportApi() {
  const state = { requested: [] as string[], downloaded: [] as string[], history: [] as { id: string; at: string; userName: string; period: string; layout: string }[] }
  const overview = (period: string) => ({
    period: `${period}-01`, suggestedPeriod: '2026-09-01', layout: 'v1', layoutName: 'Layout padrão do sistema v1', layoutConfirmed: false,
    sections: [
      { key: 'movements', fileName: 'movimentacao.csv', included: true, summary: 'Movimentação: 2 eventos', rows: 2 },
      { key: 'payroll', fileName: null, included: false, summary: 'Folha: nenhuma folha fechada', rows: 0 },
      { key: 'timeclock', fileName: null, included: false, summary: 'Ponto: não incluído', rows: 0 },
    ],
    notIncluded: ['Folha: nenhuma folha fechada de setembro de 2026; o arquivo folha.csv não entrou.', 'Ponto: não incluído; o ponto do sistema ainda não está em uso.'],
    draftRuns: [{ id: 'r9', type: 'Monthly' }],
    previous: state.history.find((h) => h.period === `${period}-01`) ?? null,
    history: state.history,
  })
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/accountant-exports': (r) => {
      const period = new URL(r.request().url()).searchParams.get('period') ?? '2026-09'
      state.requested.push(period)
      return json(r, overview(period))
    },
    'GET /api/v1/hr/accountant-exports/package': (r) => {
      const period = new URL(r.request().url()).searchParams.get('period') ?? ''
      state.downloaded.push(period)
      state.history.unshift({ id: 'h' + state.history.length, at: '2026-10-27T13:30:00Z', userName: 'Ana Souza', period: `${period}-01`, layout: 'v1' })
      return r.fulfill({ status: 200, contentType: 'application/zip', headers: { 'cache-control': 'private, no-store', 'content-disposition': `attachment; filename=contadora-${period}-v1.zip` },
        body: zip({ 'movimentacao.csv': MOVEMENTS, 'leia-me.txt': BOM + 'Layout: padrão do sistema v1 (A confirmar com a contadora)\r\n' }) })
    },
  }
  return { state, api }
}

test.describe('RH-03: exportação para a contadora', () => {
  test('gera o pacote da competência sugerida, abre o CSV e sugere avisar a contadora (CA 1, 2, 5 e 6)', async ({ page }) => {
    const { state, api } = exportApi()
    await mockApi(page, { ...api, ...asSensitive })
    await page.goto('/app/rh/exportar-contadora')
    await expect(page.getByRole('heading', { name: 'RH: Exportar para a contadora' })).toBeVisible()
    await expect(page.getByLabel('Competência')).toHaveValue('2026-09')
    await expect(page.getByText('A confirmar com a contadora', { exact: true })).toBeVisible()
    await expect(page.getByRole('list', { name: 'O que vai entrar no pacote' })).toContainText('Movimentação: 2 eventos')
    await expect(page.getByText('A folha Mensal de setembro de 2026 ainda está em rascunho: ela não entra no pacote.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Abrir a folha' })).toHaveAttribute('href', '/app/rh/folha/r9')
    await expect(page.getByText('Nenhuma exportação ainda. Escolha o mês e gere o pacote.')).toBeVisible()
    await noHorizontalScroll(page)

    const download = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Gerar pacote (CSV)' }).click()
    const file = await download
    expect(file.suggestedFilename()).toBe('contadora-2026-09-v1.zip')
    const csv = entry(readFileSync((await file.path())!), 'movimentacao.csv')
    expect(csv?.startsWith(BOM + 'evento;matricula;nome;cpf')).toBe(true)
    expect(csv).toContain("'=Rui Prado")
    expect(state.downloaded).toEqual(['2026-09'])
    await expect(page.getByText('Pacote de setembro de 2026 gerado.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Avisar a contadora' })).toBeVisible()
    await expect(page.getByText('setembro de 2026 · layout v1').or(page.getByRole('cell', { name: 'setembro de 2026' })).filter({ visible: true })).toBeVisible()

    // Nova exportação da mesma competência (CA 6): a tela diz o que substitui.
    await expect(page.getByText(/Já exportada em .*: o novo pacote substitui essa exportação\./)).toBeVisible()
    await page.getByRole('button', { name: 'Gerar pacote (CSV)' }).click()
    await expect(page.getByText(/Pacote de setembro de 2026 gerado\. Substitui a exportação de /)).toBeVisible()
    await noHorizontalScroll(page)
  })

  test('escolhe outra competência e não aceita mês futuro', async ({ page }) => {
    const { state, api } = exportApi()
    await mockApi(page, { ...api, ...asSensitive })
    await page.goto('/app/rh/exportar-contadora')
    await expect(page.getByLabel('Competência')).toHaveValue('2026-09')
    await page.getByLabel('Competência').fill('2026-08')
    await expect(page.getByRole('heading', { name: 'O que vai entrar no pacote de agosto de 2026' })).toBeVisible()
    expect(state.requested).toContain('2026-08')
    await page.getByLabel('Competência').fill('2099-01')
    await expect(page.getByText('Escolha o mês atual ou um mês anterior.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Gerar pacote (CSV)' })).toHaveCount(0)
  })

  test('sem hr.view_sensitive: a aba some e a tela mostra Dados protegidos sem chamar o servidor (CA 8)', async ({ page }) => {
    const { state, api } = exportApi()
    const viewer = allModules().map((m) => (m.code === 'hr' ? { ...m, actions: ['view', 'create', 'edit'] } : m))
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '31', modules: viewer }), 'GET /api/v1/hr/employees': (r: Route) => json(r, { items: [], page: 1, pageSize: 20, total: 0 }) })
    await page.goto('/app/rh/exportar-contadora')
    await expect(page.getByText('Dados protegidos')).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'RH' }).getByRole('link', { name: 'Exportar para a contadora' })).toHaveCount(0)
    expect(state.requested).toEqual([])
  })

  test('erro ao montar o pacote mostra o motivo e Tentar de novo', async ({ page }) => {
    await mockApi(page, { ...asSensitive, 'GET /api/v1/hr/accountant-exports': (r: Route) => json(r, { status: 500, title: 'Erro' }, 500) })
    await page.goto('/app/rh/exportar-contadora')
    await expect(page.getByText('Não foi possível montar o pacote.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Tentar de novo' })).toBeVisible()
  })
})
