import { webcrypto } from 'node:crypto'
import { expect, type Locator, type Page, type Route } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

/**
 * API simulada do app do caixa (/pdv) para os E2E do marco MVP do PDV (Fase 11). Mesmo padrão do `mock-api.ts`: nenhum backend real, nenhum tenant
 * real (o servidor é coberto pelos testes de integração .NET). Aqui ficam o aperto de mão, a descida do catálogo e do saldo, a entrada por PIN, a
 * sessão do operador e a subida da fila (com o que subiu guardado em `uploaded`, para o teste conferir).
 */
type Handler = (route: Route) => Promise<void> | void

export const DEVICE_ID = '0193f000-0000-7000-8000-00000000d001'
export const OPERATOR = { userId: '0193f000-0000-7000-8000-0000000000a1', displayName: 'Diego S.' }
export const SUPERVISOR = { userId: '0193f000-0000-7000-8000-0000000000b1', displayName: 'Sofia Supervisora' }
export const OPERATOR_PIN = '730284'

export const COFFEE = { productId: '0193f000-0000-7000-8000-0000000000c1', description: 'Café Pilão 500g', gtin: '7896005800017', priceCents: 1_899 }
export const SHIRT = { productId: '0193f000-0000-7000-8000-0000000000c2', description: 'Camiseta', gtin: '7891000100103', priceCents: 3_000 }

export const handshake = () => ({
  deviceId: DEVICE_ID, storeId: 's1', storeCode: 'CENTRO', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1, epoch: 1,
  uiMode: 'Keyboard', salePrefix: 'C01-', serverTime: new Date().toISOString(),
})

const item = (p: typeof COFFEE, seq: number) => ({
  productId: p.productId, parentProductId: null, description: p.description, unitCode: 'UN', isWeighable: false, plu: null,
  barcodes: [{ gtin: p.gtin, packQuantity: 1 }], priceCents: p.priceCents, priceListVersion: 1, changeSeq: seq,
})

const method = (id: string, name: string, type: string, allowsChange = false, seq = 1) => ({
  id, name, type, allowsChange, nfePaymentCode: '01', confirmed: true, cardBrands: ['Any'], maxInstallments: 1, changeSeq: seq,
})

/** Loja em modo "só cupom não fiscal" já aceito, sem impressão (o E2E não tem impressora), desconto livre até 5%. */
export const snapshot = (verifiers: unknown[] = []) => ({
  schemaVersion: 1,
  collections: ['items', 'barcodes', 'prices', 'paymentMethods', 'operators', 'printers'],
  cursor: 1,
  generatedAt: new Date().toISOString(),
  store: {
    id: 's1', code: 'CENTRO', name: 'Loja Centro', registerId: 'r1', registerNumber: 1, offlineWindowHours: 12, cashBreakToleranceCents: 0, exchangeDays: 30,
    cashDropAlertCents: 100_000, cashCeilingCents: null, companyName: 'Loja Centro Ltda', companyDocument: null, fiscalMode: 'NonFiscal', canSell: true,
    receiptPrintMode: 'Never', freeRemoveLast: true, discountLimitPercent: 5, offlineApprovalMaxCents: 5_000, offlineApprovalSessionCents: 15_000,
    offlineApprovalSessionCount: 5, offlineCancelSaleAllowed: true,
  },
  priceList: { id: 'pl1', version: 1 },
  items: [item(COFFEE, 1), item(SHIRT, 2)],
  paymentMethods: [method('m-cash', 'Dinheiro', 'Cash', true, 1), method('m-pix', 'Pix', 'Pix', false, 2), method('m-debit', 'Débito', 'Debit', false, 3),
    method('m-credit-note', 'Vale-troca', 'StoreCredit', false, 4)],
  operators: [OPERATOR],
  printers: { version: 1, items: [] },
  verifiers,
})

const noChanges = () => ({
  schemaVersion: 1, cursor: 1, hasMore: false, generatedAt: new Date().toISOString(), store: snapshot().store, priceList: { id: 'pl1', version: 1 },
  items: [], removedItemIds: [], paymentMethods: [], removedPaymentMethodIds: [], operators: [], printers: { version: 1, items: [] }, verifiers: [],
})

const b64url = (value: unknown) => Buffer.from(JSON.stringify(value)).toString('base64url')
/** Sessão por PIN no aparelho de caixa: o front só lê o `amr` (pin + hwk) para abrir a tela de venda. */
export const pinToken = () => `${b64url({ alg: 'none', typ: 'JWT' })}.${b64url({ sub: OPERATOR.userId, amr: ['pin', 'hwk'] })}.e2e`

export type UploadedEvent = { eventId: string; kind: string; seq: number; payload: string }

/**
 * Caixa registrado com rede. `actions` são as ações do operador na sessão (sem `discount`, o desconto acima do limite chama o supervisor).
 * Devolve `uploaded`, com os eventos que a fila subiu (OFF-02), na ordem.
 */
export async function mockPdv(page: Page, options: { actions?: string[]; verifiers?: unknown[]; overrides?: Record<string, Handler> } = {}) {
  const uploaded: UploadedEvent[] = []
  const actions = options.actions ?? ['cash_out', 'cash_in', 'return']
  await mockApi(page, {
    'POST /api/v1/auth/refresh': (r) => json(r, { code: 'auth.session_expired' }, 422),
    'POST /api/v1/pos/sync/handshake': (r) => json(r, handshake()),
    'GET /api/v1/pos/sync/snapshot': (r) => json(r, snapshot(options.verifiers)),
    'GET /api/v1/pos/sync/changes': (r) => json(r, noChanges()),
    'GET /api/v1/pos/sync/stock': (r) => json(r, { sellWithoutStock: true, full: true, cursor: 1, generatedAt: new Date().toISOString(), levels: [] }),
    'GET /api/v1/pos/sync/operators': (r) => json(r, { deviceName: 'Caixa da frente', storeName: 'Loja Centro', registerNumber: 1, operators: [OPERATOR] }),
    'POST /api/v1/pos/sync/pin': (r) => json(r, { accessToken: pinToken(), expiresAt: '2030-01-01T00:00:00Z', sessionExpiresAt: new Date(Date.now() + 8 * 3_600_000).toISOString() }),
    'GET /api/v1/pos/session': (r) => json(r, {
      userId: OPERATOR.userId, name: OPERATOR.displayName, isOperationUser: true, storeId: 's1', storeName: 'Loja Centro', registerId: 'r1', registerNumber: 1,
      deviceName: 'Caixa da frente', sessionExpiresAt: new Date(Date.now() + 8 * 3_600_000).toISOString(), actions,
    }),
    'POST /api/v1/pos/session/end': (r) => r.fulfill({ status: 204 }),
    'POST /api/v1/pos/sync/events': (r) => {
      const { events } = r.request().postDataJSON() as { events: (UploadedEvent & { deviceEpoch: number })[] }
      uploaded.push(...events.map(({ eventId, kind, seq, payload }) => ({ eventId, kind, seq, payload })))
      return json(r, {
        results: events.map((e) => ({ eventId: e.eventId, deviceEpoch: e.deviceEpoch, seq: e.seq, outcome: 'Accepted', replayed: false })),
        acks: events.length ? [{ deviceEpoch: events[0].deviceEpoch, seq: Math.max(...events.map((e) => e.seq)) }] : [],
        serverTime: new Date().toISOString(),
      })
    },
    ...options.overrides,
  })
  return { uploaded }
}

/** Registro do aparelho como o PDV-01 deixa: a chave ECDSA não extraível no banco do aparelho (o mesmo do `pdv-pwa.spec.ts`). */
export async function registerDevice(page: Page) {
  await page.goto('/pdv/')
  await expect(page.getByRole('heading', { name: 'Este aparelho ainda não é um caixa.' })).toBeVisible()
  await page.evaluate(async () => {
    const keys = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign', 'verify'])
    await new Promise<void>((resolve, reject) => {
      const open = indexedDB.open('bemaqui-pdv', 1)
      open.onupgradeneeded = () => open.result.createObjectStore('device')
      open.onerror = () => reject(open.error)
      open.onsuccess = () => {
        const tx = open.result.transaction('device', 'readwrite')
        tx.objectStore('device').put(keys, 'key')
        tx.oncomplete = () => { open.result.close(); resolve() }
        tx.onerror = () => reject(tx.error)
      }
    })
  })
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Caixa 01 · Loja Centro' })).toBeVisible()
}

/** Toca os números no teclado numérico da tela (PIN, fundo de troco, sangria, contagem). */
export async function tapDigits(scope: Page | Locator, digits: string) {
  const pad = scope.getByRole('group', { name: 'Teclado numérico' })
  for (const d of digits) await pad.getByRole('button', { name: d, exact: true }).click()
}

/** Entrada do operador por PIN, com rede (PDV-01 CA 11). */
export async function enterWithPin(page: Page) {
  await page.getByRole('list', { name: 'Quem pode entrar neste caixa' }).getByRole('button', { name: OPERATOR.displayName }).click()
  await tapDigits(page, OPERATOR_PIN)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

/** Abertura do caixa com o fundo de troco em centavos digitados (CX-01). */
export async function openRegister(page: Page, digits: string) {
  const open = page.getByRole('region', { name: 'Abrir caixa' })
  await expect(open).toBeVisible()
  await tapDigits(open, digits)
  await open.getByRole('button', { name: 'Abrir caixa' }).click()
  await expect(page.getByText('Bipe ou busque um produto para começar.')).toBeVisible()
}

/** Leitor de código de barras: o leitor "digita" o código e manda Enter no campo de código (PDV-02 CA 1). */
export async function scan(page: Page, code: string) {
  const field = page.getByLabel('Código, quantidade (3*código) ou nome')
  await field.fill(code)
  await field.press('Enter')
}

/**
 * Verificador sem rede como o servidor manda na descida (OFF-03): PBKDF2 do PIN + HKDF "bq-offline-proof" + HMAC da mensagem do verificador.
 * Mesmo cálculo de `src/pdv/offline/offline-crypto.ts`; iterações baixas só para o teste ser rápido.
 */
export async function offlineVerifier(person: { userId: string; displayName: string }, pin: string, kind: 'operator' | 'supervisor', actionsList: string[]) {
  const subtle = webcrypto.subtle
  const salt = webcrypto.getRandomValues(new Uint8Array(16))
  const nonce = Buffer.from(webcrypto.getRandomValues(new Uint8Array(16))).toString('base64')
  const iterations = 1_000
  const keyLength = 32
  const base = await subtle.importKey('raw', new TextEncoder().encode(pin), 'PBKDF2', false, ['deriveBits'])
  const bits = await subtle.deriveBits({ name: 'PBKDF2', salt, iterations, hash: 'SHA-256' }, base, keyLength * 8)
  const ikm = await subtle.importKey('raw', bits, 'HKDF', false, ['deriveKey'])
  const key = await subtle.deriveKey({ name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(0), info: new TextEncoder().encode('bq-offline-proof') }, ikm,
    { name: 'HMAC', hash: 'SHA-256', length: 256 }, false, ['sign'])
  const message = `bq-pos-offline-verifier|v1|${kind}|${DEVICE_ID.toLowerCase()}|${person.userId.toLowerCase()}|${nonce}`
  const verifier = Buffer.from(await subtle.sign('HMAC', key, new TextEncoder().encode(message))).toString('base64')
  return {
    userId: person.userId, displayName: person.displayName, kind, algorithm: 'PBKDF2-SHA256', iterations, salt: Buffer.from(salt).toString('base64'), keyLength,
    nonce, verifier, actions: actionsList,
  }
}

/** /me/permissions com a área Loja (módulo `sales`) liberada, para as telas da retaguarda do PDV. */
export const storeModules = () => [...allModules(), { code: 'sales', name: 'Loja', area: 'Loja', order: 200, description: 'Lojas, caixas e vendas.', released: true, enabled: true, actions: ['view', 'create', 'edit', 'approve', 'delete', 'view_customer_document'] }]

/** Venda como a retaguarda lista (`GET /api/v1/sales/sales`, PDV-07). */
export const saleListItem = (over: Partial<{ id: string; number: string; total: number; offline: boolean; paymentMethods: string[]; customerDocument: string | null }> = {}) => ({
  id: 'sale-1', number: 'C01-000001', at: new Date().toISOString(), businessDate: new Date().toISOString().slice(0, 10), channel: 'Pos', storeId: 's1', storeName: 'Loja Centro',
  registerId: 'r1', registerLabel: 'Caixa 01', operatorId: OPERATOR.userId, operatorName: OPERATOR.displayName, total: 18.99, paymentMethods: ['Dinheiro'], status: 'Completed',
  stockStatus: 'Posted', hasDivergence: false, offline: false, customerDocument: null as string | null, ...over,
})

export const salePage = (items: ReturnType<typeof saleListItem>[]) => ({
  items, page: 1, pageSize: 20, total: items.length,
  totals: { count: items.length, total: items.reduce((sum, s) => sum + s.total, 0), cancelledCount: 0, cancelledTotal: 0 },
})
