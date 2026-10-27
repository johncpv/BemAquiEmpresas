import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type Route, test } from '@playwright/test'
import { json, mockApi } from './mock-api'

const PATH = '/app/configuracoes/fiscal'
const CERT = '/app/configuracoes/fiscal/certificado'

type Obj = Record<string, unknown>

const VALUES: Obj = {
  regime: 'LucroPresumido', cnaeMain: '4329103', cnaeSecondary: [], nfeSeries: 2, environment: 'Homologation',
  cfopSaleInStateContributor: '5101', cfopSaleInStateFinal: '5101', cfopSaleInterstateContributor: '6101', cfopSaleInterstateFinal: '6107', cfopResaleInState: '5102', cfopResaleInterstate: '6102',
  mixedSale: 'EquipmentNfeServiceNfse', cstIcms: '00', cstPisCofins: '01', cstIpi: '50',
  icmsInternalRate: 18, icmsInterstateSouthSoutheastRate: 12, icmsInterstateOtherRate: 7, icmsImportedRate: 4, pisRate: 0.65, cofinsRate: 3, ipiFromNcm: true,
  costTaxes: ['ICMS', 'PIS', 'COFINS', 'ISS', 'IBS_CBS'], nfseServiceItem: '7.02', nfseMunicipalCode: null, nfseCtn: null, nfseNbs: null, nfseIssRate: 5, nfseIssLocation: 'WorkSite',
  nfseIssWithheld: false, nfseFederalWithholding: false, ibsCbsCst: '000', ibsCbsClassification: '000001', cbsRate: 0.9, ibsRate: 0.1, ibsCityRate: 0, cancellationWindowHours: 24, freightMode: 0,
  volumeSpecies: 'VOLUME', includeDuplicates: false,
}

const KEYS: [string, string, string, string][] = [
  ['regime', 'regime', 'Regime tributário', 'All'], ['crt', 'regime', 'CRT (Código de Regime Tributário)', 'All'], ['cnaeMain', 'regime', 'CNAE principal', 'All'], ['cnaeSecondary', 'regime', 'CNAEs secundários', 'All'],
  ['nfeSeries', 'serie', 'Série exclusiva da NF-e', 'Nfe'], ['environment', 'serie', 'Ambiente', 'None'],
  ['cfopSaleInStateContributor', 'operacoes', 'Venda de produção própria a contribuinte em SP', 'Nfe'], ['cfopSaleInStateFinal', 'operacoes', 'Venda de produção própria a não contribuinte em SP', 'Nfe'],
  ['cfopSaleInterstateContributor', 'operacoes', 'Venda de produção própria a contribuinte de outro estado', 'Nfe'], ['cfopSaleInterstateFinal', 'operacoes', 'Venda de produção própria a não contribuinte de outro estado', 'Nfe'],
  ['cfopResaleInState', 'operacoes', 'Revenda de peça em SP', 'Nfe'], ['cfopResaleInterstate', 'operacoes', 'Revenda de peça para outro estado', 'Nfe'], ['mixedSale', 'operacoes', 'Venda mista', 'Nfe'],
  ['cstIcms', 'cst', 'CST do ICMS', 'Nfe'], ['cstPisCofins', 'cst', 'CST do PIS e da COFINS', 'Nfe'], ['cstIpi', 'cst', 'CST do IPI', 'Nfe'], ['icmsInternalRate', 'cst', 'ICMS interno SP', 'Nfe'],
  ['icmsInterstateSouthSoutheastRate', 'cst', 'ICMS interestadual (Sul e Sudeste, exceto ES)', 'Nfe'], ['icmsInterstateOtherRate', 'cst', 'ICMS interestadual (Norte, Nordeste, Centro-Oeste e ES)', 'Nfe'],
  ['icmsImportedRate', 'cst', 'ICMS interestadual de mercadoria importada', 'Nfe'], ['pisRate', 'cst', 'PIS', 'All'], ['cofinsRate', 'cst', 'COFINS', 'All'], ['ipiFromNcm', 'cst', 'IPI pela TIPI do NCM do item', 'Nfe'],
  ['costTaxes', 'custo', 'Tributos que entram no custo do projeto', 'None'],
  ['nfseServiceItem', 'nfse', 'Item da LC 116', 'Nfse'], ['nfseMunicipalCode', 'nfse', 'Código municipal do serviço (CodSrv)', 'Nfse'], ['nfseCtn', 'nfse', 'Código de tributação nacional (CTN)', 'Nfse'],
  ['nfseNbs', 'nfse', 'NBS', 'Nfse'], ['nfseIssRate', 'nfse', 'Alíquota do ISS', 'Nfse'], ['nfseIssLocation', 'nfse', 'Local de incidência do ISS', 'Nfse'], ['nfseIssWithheld', 'nfse', 'ISS retido pelo tomador', 'Nfse'],
  ['nfseFederalWithholding', 'nfse', 'Retenções federais', 'Nfse'],
  ['ibsCbsCst', 'ibs-cbs', 'CST do IBS/CBS', 'All'], ['ibsCbsClassification', 'ibs-cbs', 'Classificação tributária (cClassTrib)', 'All'], ['cbsRate', 'ibs-cbs', 'Alíquota da CBS', 'All'], ['ibsRate', 'ibs-cbs', 'Alíquota do IBS estadual', 'All'], ['ibsCityRate', 'ibs-cbs', 'Alíquota do IBS municipal', 'All'],
  ['freightMode', 'transporte', 'Modalidade do frete', 'Nfe'], ['volumeSpecies', 'transporte', 'Espécie dos volumes', 'Nfe'], ['includeDuplicates', 'transporte', 'Duplicatas na NF-e', 'Nfe'],
  ['cancellationWindowHours', 'prazos', 'Prazo de cancelamento da NF-e (horas)', 'Nfe'],
]

const SECTIONS: [string, string][] = [
  ['regime', 'Regime e atividades'], ['serie', 'Série e ambiente'], ['operacoes', 'Operações padrão'], ['cst', 'CST e alíquotas'], ['custo', 'Custo do projeto'],
  ['nfse', 'NFS-e'], ['ibs-cbs', 'Reforma Tributária (IBS/CBS)'], ['transporte', 'Transporte e cobrança'], ['prazos', 'Prazos'],
]

/** Backend em memória da FIS-02a (as regras reais estão nos testes de integração .NET, com o fake do Emissor). */
function fiscalApi() {
  let values: Obj = { ...VALUES }
  const confirmed = new Map<string, string>()
  const calls: { method: string; path: string; body?: unknown }[] = []
  const view = () => {
    const reviews = KEYS.map(([key, section, label, critical]) => ({
      key, section, label, critical, reviewState: confirmed.has(key) ? 'Confirmed' : 'ToConfirm', reviewedByName: confirmed.has(key) ? 'Ana Souza' : null, reviewedAt: confirmed.get(key) ?? null,
    }))
    return {
      id: '019fca35-0000-7000-8000-000000000001', values, crt: values.regime === 'SimplesNacional' ? 1 : 3, reviews,
      sections: SECTIONS.map(([code, title]) => {
        const inSection = reviews.filter((r) => r.section === code)
        const pending = inSection.filter((r) => r.reviewState === 'ToConfirm')
        return { code, title, total: inSection.length, pending: pending.length, firstPendingKey: pending[0]?.key ?? null }
      }),
      confirmedCount: confirmed.size, totalCount: KEYS.length, firstConfiguration: confirmed.size === 0,
      company: { legalName: 'Konebras Elevadores Ltda', document: '47225005000186', stateRegistration: '123456789012', municipalRegistration: '98765', address: 'Rua A, 10 · Caçapava/SP', cityIbge: '3508504' },
      preconditions: [], certificate: { notAfter: null, daysLeft: null, status: 'None' }, allowedEnvironment: 'Homologation', canEdit: true, pendingCriticalNfe: [],
    }
  }
  let certificate: Obj = {
    integrationReady: true, integrationMessage: null, status: 'None', subject: null, document: null, thumbprint: null, notAfter: null, daysLeft: null, uploadedAt: null, uploadedByName: null,
    changedOutsideAt: null, emitterCheckedAt: '2026-10-27T12:00:00Z', emitterUnavailable: false, canEdit: true, history: [],
  }
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/fiscal/settings': (r) => json(r, view()),
    'PUT /api/v1/fiscal/settings': (r) => {
      const body = r.request().postDataJSON() as { values: Obj; code?: string | null }
      calls.push({ method: 'PUT', path: 'settings', body })
      if (String(body.values.cnaeMain).replace(/\D/g, '').length !== 7) {
        return json(r, { status: 422, code: 'validation', title: 'O CNAE tem 7 dígitos (ex.: 4329-1/03).', errors: { cnaeMain: ['O CNAE tem 7 dígitos (ex.: 4329-1/03).'] } }, 422)
      }

      const changed = Object.keys(body.values).filter((k) => JSON.stringify(body.values[k]) !== JSON.stringify(values[k]))
      changed.forEach((k) => confirmed.delete(k))
      values = { ...body.values, cnaeMain: String(body.values.cnaeMain).replace(/\D/g, '') }
      return json(r, { settings: view(), changedKeys: changed, notice: null, rulesToReview: [] })
    },
    'POST /api/v1/fiscal/settings/confirmations': (r) => {
      const body = r.request().postDataJSON() as { keys: string[] }
      calls.push({ method: 'POST', path: 'confirmations', body })
      body.keys.forEach((k) => confirmed.set(k, '2026-10-27T12:00:00Z'))
      return json(r, view())
    },
    'DELETE /api/v1/fiscal/settings/confirmations/*': (r) => {
      confirmed.delete(new URL(r.request().url()).pathname.split('/').pop()!)
      return json(r, view())
    },
    'GET /api/v1/fiscal/settings/certificate': (r) => json(r, certificate),
    'POST /api/v1/fiscal/settings/certificate': (r) => {
      const body = r.request().postData() ?? ''
      calls.push({ method: 'POST', path: 'certificate' })
      if (!body.includes('123456')) return json(r, { status: 403, code: 'two_factor_invalid', title: 'Confirme com o código do aplicativo autenticador para enviar o certificado.' }, 403)
      certificate = { ...certificate, status: 'Valid', subject: 'KONEBRAS ELEVADORES LTDA:47225005000186', document: '47225005000186', thumbprint: 'A1B2C3D4E5F60718293A4B5C6D7E8F9012345678', notAfter: '2027-10-27T12:00:00Z', daysLeft: 365, uploadedAt: '2026-10-27T12:00:00Z', uploadedByName: 'Ana Souza', history: [{ at: '2026-10-27T12:00:00Z', source: 'BemAqui', subject: 'KONEBRAS', thumbprint: 'A1B2', notAfter: '2027-10-27T12:00:00Z', byName: 'Ana Souza' }] }
      return json(r, certificate)
    },
  }
  return { api, calls }
}

async function noSeriousViolations(page: Page) {
  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)).toEqual([])
}

test.describe('Configurações → Fiscal (FIS-02a)', () => {
  test('abre pré-configurada, salva, marca uma seção como conferida e desfaz', async ({ page }) => {
    const { api, calls } = fiscalApi()
    await mockApi(page, api)
    await page.goto(PATH)
    await expect(page.getByRole('heading', { level: 1, name: 'Fiscal' })).toBeVisible()
    await expect(page.getByText('Valores sugeridos pelo sistema. Confira cada um com a contadora antes de emitir em produção.')).toBeVisible()
    await expect(page.getByText('0 de 41 campos conferidos')).toBeVisible()
    await expect(page.getByText('Ambiente de homologação da SEFAZ: as notas não têm valor fiscal.')).toBeVisible()
    await expect(page.getByLabel('Regime tributário')).toHaveValue('LucroPresumido')
    await expect(page.getByLabel(/^Série exclusiva da NF-e/)).toHaveValue('2')
    await expect(page.getByLabel(/^CNAE principal/)).toHaveValue('4329-1/03')
    await expect(page.getByText('Konebras Elevadores Ltda')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Editar dados da empresa' })).toBeVisible()
    await expect(page.getByText('A confirmar com a contadora').first()).toBeVisible()
    await noSeriousViolations(page)

    await page.getByLabel(/^CNAE principal/).fill('4329')
    await page.getByRole('button', { name: 'Salvar configuração fiscal' }).click()
    await expect(page.getByText('O CNAE tem 7 dígitos (ex.: 4329-1/03).')).toBeVisible()
    await page.getByLabel(/^CNAE principal/).fill('4329-1/03')
    await page.getByLabel(/^Espécie dos volumes/).fill('CAIXA')
    await page.getByRole('button', { name: 'Salvar configuração fiscal' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Configuração fiscal salva' }).first()).toBeVisible()
    expect(calls.filter((c) => c.path === 'settings')).toHaveLength(2)

    const cst = page.getByRole('region', { name: 'CST e alíquotas' })
    await cst.getByRole('button', { name: 'Marcar seção como conferida' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByText('ICMS interno SP')).toBeVisible()
    await dialog.getByRole('button', { name: 'Marcar seção como conferida' }).click()
    await expect(page.getByText('10 de 41 campos conferidos')).toBeVisible()
    await expect(cst.getByText(/Conferido por Ana Souza/).first()).toBeVisible()
    expect((calls.find((c) => c.path === 'confirmations')?.body as { keys: string[] }).keys).toHaveLength(10)

    await page.getByRole('button', { name: 'Desfazer conferência: PIS' }).click()
    await expect(page.getByText('9 de 41 campos conferidos')).toBeVisible()

    await page.getByRole('button', { name: 'Marcar como conferido: Regime tributário' }).click()
    await expect(page.getByText('10 de 41 campos conferidos')).toBeVisible()
  })

  test('troca para produção pede o diálogo com o código do autenticador', async ({ page }) => {
    const { api, calls } = fiscalApi()
    await mockApi(page, api)
    await page.goto(PATH)
    await page.getByLabel(/^Ambiente/).selectOption('Production')
    await page.getByRole('button', { name: 'Salvar configuração fiscal' }).click()
    const dialog = page.getByRole('alertdialog')
    await expect(dialog.getByRole('heading', { name: 'Ligar a emissão com valor fiscal' })).toBeVisible()
    await expect(dialog.getByText('Notas em produção têm valor fiscal e só podem ser canceladas no prazo legal.')).toBeVisible()
    await dialog.getByRole('button', { name: 'Ligar a emissão com valor fiscal' }).click()
    await expect(dialog.getByText('Digite os 6 números do aplicativo autenticador.')).toBeVisible()
    expect(calls.filter((c) => c.path === 'settings')).toHaveLength(0)
  })

  test('certificado: sem certificado, envia ao Emissor com 2FA e mostra a validade', async ({ page }) => {
    const { api } = fiscalApi()
    await mockApi(page, api)
    await page.goto(CERT)
    await expect(page.getByRole('heading', { level: 1, name: 'Certificado digital' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Fiscal' }).getByRole('link', { name: 'Certificado digital' })).toHaveAttribute('aria-current', 'page')
    await expect(page.getByText('Nenhum certificado enviado.')).toBeVisible()
    await noSeriousViolations(page)

    await page.getByRole('button', { name: 'Enviar certificado' }).click()
    const dialog = page.getByRole('alertdialog')
    await dialog.getByLabel(/^Arquivo do certificado/).setInputFiles({ name: 'empresa.pfx', mimeType: 'application/x-pkcs12', buffer: Buffer.from('pfx de teste gerado no e2e') })
    await dialog.getByLabel(/^Senha do certificado/).fill('senha-do-teste')
    await dialog.getByLabel(/^Código do aplicativo autenticador/).fill('000000')
    await dialog.getByRole('button', { name: 'Enviar certificado' }).click()
    await expect(dialog.getByText('Confirme com o código do aplicativo autenticador para enviar o certificado.')).toBeVisible()
    await dialog.getByLabel(/^Senha do certificado/).fill('senha-do-teste')
    await dialog.getByLabel(/^Código do aplicativo autenticador/).fill('123456')
    await dialog.getByRole('button', { name: 'Enviar certificado' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Certificado enviado' }).first()).toBeVisible()
    await expect(page.getByText('Válido', { exact: true })).toBeVisible()
    await expect(page.getByText('27/10/2027')).toBeVisible()
    await expect(page.getByText('Histórico de trocas')).toBeVisible()
  })

  test('a 400 px as seções empilham sem rolagem horizontal', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 860 })
    const { api } = fiscalApi()
    await mockApi(page, api)
    for (const path of [PATH, CERT]) {
      await page.goto(path)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
      expect(overflow).toBeLessThanOrEqual(0)
    }
  })
})
