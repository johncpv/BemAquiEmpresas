import { expect, type Page, type Route, test } from '@playwright/test'
import { allModules, json, mockApi } from './mock-api'

const CPF = '52998224725'
const OTHER = '11144477735'
const EMPLOYEE = { id: 'e1', name: 'Joana Lima', registration: null, positionId: 'p1', positionName: 'Montador', workCenterId: 'wc1', workCenterName: 'Montagem', workScheduleId: 's44', unionId: null, admissionDate: '2026-10-19', terminationDate: null, status: 'Active', version: 3 }
const NO_ACCOUNT = { hasAccount: false, isFirstAccount: false, changedAt: null, changedByName: null, quarantineUntil: null, inQuarantine: false, reviewedAt: null, reviewedByName: null, selfReviewed: false, accountVersion: 0, canReview: false, cannotReviewReason: null }

const withHr = (actions: string[]) => allModules().map((m) => (m.code === 'hr' ? { ...m, actions } : m))

async function press(page: Page, name: string, scope = page.locator('body')) {
  await scope.getByRole('button', { name, exact: true }).first().focus()
  await page.keyboard.press('Enter')
}

async function noHorizontalScroll(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)
}

/** Backend em memória da CAD-04c2 (as regras reais estão nos testes de integração .NET). */
function complementApi() {
  const state = {
    account: null as null | Record<string, unknown>,
    clear: null as null | Record<string, unknown>,
    status: { ...NO_ACCOUNT } as Record<string, unknown>,
    reviewer: false,
    dependents: [] as Record<string, unknown>[],
    notifications: [] as Record<string, unknown>[],
    posted: [] as { url: string; body: Record<string, unknown> }[],
  }
  const status = () => ({ ...state.status, canReview: state.reviewer && !!state.status.inQuarantine, cannotReviewReason: !state.reviewer && state.status.inQuarantine ? 'Você não pode revisar uma troca feita por você.' : null })
  const payment = () => ({ employeeId: 'e1', account: state.account, status: status(), insalubrityPct: null, hazardPay: false, usesTransportVoucher: false, version: 3 })
  const post = (r: Route) => { const body = r.request().postDataJSON(); state.posted.push({ url: r.request().url(), body }); return body }
  const api: Record<string, (r: Route) => Promise<void> | void> = {
    'GET /api/v1/hr/employees/*': (r) => json(r, EMPLOYEE),
    'GET /api/v1/hr/employees/*/user': (r) => json(r, { user: null }),
    'GET /api/v1/hr/employees/*/timeline': (r) => json(r, []),
    'GET /api/v1/hr/positions': (r) => json(r, { items: [], page: 1, pageSize: 100, total: 0 }),
    'GET /api/v1/hr/schedules': (r) => json(r, []),
    'GET /api/v1/hr/unions': (r) => json(r, []),
    'GET /api/v1/hr/employees/linkable-users': (r) => json(r, []),
    'GET /api/v1/me/2fa': (r) => json(r, { enabled: true, required: false, recoveryCodesLeft: 8 }),
    'GET /api/v1/notifications/': (r) => json(r, { page: { items: state.notifications, page: 1, pageSize: 10, total: state.notifications.length }, unread: state.notifications.length }),
    'GET /api/v1/hr/employees/*/dependents/sensitive': (r) => json(r, state.dependents),
    'POST /api/v1/hr/employees/*/dependents': (r) => {
      const b = post(r)
      const d = { id: 'd' + (state.dependents.length + 1), name: b.name, birthDate: b.birthDate, kinship: b.kinship, irDependent: b.irDependent, familyAllowance: b.familyAllowance, cpfMasked: b.cpf ? '***.982.247-**' : null, version: 1 }
      state.dependents.push(d)
      return json(r, d, 201)
    },
    'GET /api/v1/hr/employees/*/payment/sensitive': (r) => json(r, payment()),
    'GET /api/v1/hr/employees/*/bank-account/status': (r) => json(r, status()),
    'GET /api/v1/hr/employees/*/bank-account/sensitive': (r) => json(r, { ...(state.clear ?? {}), employeeName: 'Joana Lima', employeeCpf: CPF }),
    'PUT /api/v1/hr/employees/*/bank-account': (r) => {
      const b = post(r)
      if (b.twoFactorCode !== '123456') return json(r, { status: 403, code: 'mfa_step_up_required', detail: 'Confirme com o código do aplicativo autenticador para salvar a conta para pagamento.' }, 403)
      const first = !state.account
      const mismatch = b.holderCpf !== CPF || (b.pixKeyType === 'Cpf' && b.pixKey !== CPF)
      if (mismatch && !b.checkedWithEmployee) return json(r, { status: 422, code: 'bank_owner_mismatch_unconfirmed', detail: 'Confirme.' }, 422)
      state.clear = { bankCode: b.bankCode, agency: b.agency, account: b.account, accountType: b.accountType, holderName: b.holderName, holderCpf: b.holderCpf, pixKeyType: b.pixKeyType, pixKey: b.pixKey }
      state.account = { bankCode: b.bankCode, agency: '**01', account: '*******-3', accountType: b.accountType, holderName: 'J**** L***', holderCpf: '***.982.247-**', pixKeyType: b.pixKeyType, pixKey: mismatch ? '***.444.777-**' : '***.982.247-**', mismatches: mismatch ? ['PixKey'] : [] }
      state.status = first
        ? { ...NO_ACCOUNT, hasAccount: true, isFirstAccount: true, changedAt: '2026-10-26T12:00:00Z', changedByName: 'Ana Souza', accountVersion: 1 }
        : { ...NO_ACCOUNT, hasAccount: true, changedAt: '2026-10-26T13:00:00Z', changedByName: 'Ana Souza', quarantineUntil: '2026-11-25T13:00:00Z', inQuarantine: true, accountVersion: 2 }
      if (!first) state.notifications.push({ id: 'n1', type: 'hr.bank_account_changed', title: 'Conta para pagamento de Joana Lima alterada por Ana Souza, com chave ou titular diferente do CPF.', link: '/app/rh/funcionarios/e1?aba=pagamento', createdAt: '2026-10-26T13:00:00Z', readAt: null })
      return json(r, payment())
    },
    'POST /api/v1/hr/employees/*/bank-account/review': (r) => {
      post(r)
      state.status = { ...state.status, inQuarantine: false, reviewedAt: '2026-10-27T12:00:00Z', reviewedByName: 'Bruno Dono' }
      return json(r, payment())
    },
  }
  return { api, state }
}

test.describe('Complemento do funcionário (CAD-04c2)', () => {
  // Fluxo longo (três diálogos, recarga e troca de usuário): mais tempo que o padrão com a máquina carregada.
  test.describe.configure({ timeout: 120_000 })

  test('troca a conta com chave Pix de outro CPF, passa pela segunda confirmação e pelo 2FA, vê a quarentena e o sino, e outro usuário revisa (CA 4 a 8)', async ({ page }) => {
    const { api, state } = complementApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '5', modules: withHr(['view', 'create', 'edit', 'approve', 'view_sensitive']) }) })
    await page.goto('/app/rh/funcionarios/e1?aba=pagamento')
    const panel = page.getByRole('tabpanel', { name: 'Pagamento e adicionais' })
    await expect(panel.getByText('Nenhuma conta para pagamento.')).toBeVisible()
    await noHorizontalScroll(page)

    // Primeira conta: o titular e a chave Pix vêm do funcionário (propositivo); entra direto na folha.
    await press(page, 'Registrar conta para pagamento', panel)
    const form = page.getByRole('alertdialog', { name: /Registrar conta para pagamento de Joana Lima/ })
    await expect(form.getByRole('textbox', { name: /^Titular da conta/ })).toHaveValue('Joana Lima')
    await expect(form.getByRole('textbox', { name: /^CPF do titular/ })).toHaveValue('529.982.247-25')
    await form.getByRole('textbox', { name: /^Banco/ }).fill('077')
    await form.getByRole('textbox', { name: /^Agência/ }).fill('0001')
    await form.getByRole('textbox', { name: /^Conta com dígito/ }).fill('7766554-3')
    await press(page, 'Salvar conta para pagamento', form)
    const code = page.getByRole('alertdialog', { name: 'Confirme com o 2FA' })
    await code.getByRole('textbox', { name: /^Código do aplicativo/ }).fill('000000')
    await press(page, 'Confirmar', code)
    await expect(code.getByRole('alert')).toContainText('Confirme com o código do aplicativo autenticador')
    await code.getByRole('textbox', { name: /^Código do aplicativo/ }).fill('123456')
    await press(page, 'Confirmar', code)
    await expect(page.getByText('Conta para pagamento registrada')).toBeVisible()
    await expect(panel.getByText('Novo na folha')).toBeVisible()
    await expect(panel.getByText('*******-3')).toBeVisible()

    // Troca com a chave Pix de outro CPF: aviso, segunda confirmação com motivo e 2FA de novo.
    await press(page, 'Trocar conta', panel)
    const change = page.getByRole('alertdialog', { name: /Trocar a conta para pagamento de Joana Lima/ })
    await change.getByRole('textbox', { name: /^Chave Pix/ }).fill('111.444.777-35')
    await expect(change.getByText('A chave Pix (ou o titular) não é o CPF do funcionário. Confira antes de salvar.')).toBeVisible()
    await press(page, 'Salvar conta para pagamento', change)
    const second = page.getByRole('alertdialog', { name: 'Conferir a conta com o funcionário' })
    await expect(second.getByText('Chave Pix: CPF ***.444.777-**')).toBeVisible()
    await expect(second.getByRole('button', { name: 'Salvar mesmo assim' })).toBeDisabled()
    if ((page.viewportSize()?.width ?? 0) <= 400) expect((await second.boundingBox())?.width).toBeGreaterThanOrEqual(399)
    await second.getByRole('checkbox', { name: 'Conferi com o funcionário' }).check()
    await second.getByRole('textbox', { name: /^Motivo/ }).fill('Conta da esposa, conferida por telefone')
    await press(page, 'Salvar mesmo assim', second)
    const again = page.getByRole('alertdialog', { name: 'Confirme com o 2FA' })
    await again.getByRole('textbox', { name: /^Código do aplicativo/ }).fill('123456')
    await press(page, 'Confirmar', again)
    await expect(page.getByText('Conta para pagamento trocada')).toBeVisible()
    const sent = state.posted.at(-1)!.body
    expect(sent).toMatchObject({ pixKeyType: 'Cpf', pixKey: OTHER, checkedWithEmployee: true, mismatchReason: 'Conta da esposa, conferida por telefone', twoFactorCode: '123456', version: 3 })
    await expect(panel.getByText('Conta em quarentena até 25/11/2026')).toBeVisible()
    await expect(page.getByRole('main').locator('[data-tone="warn"]', { hasText: 'Conta em quarentena até 25/11/2026' }).first()).toBeVisible()
    await expect(panel.getByText('Você não pode revisar uma troca feita por você.', { exact: false })).toBeVisible()
    await expect(panel.getByRole('button', { name: 'Revisar conta' })).toHaveCount(0)
    await noHorizontalScroll(page)

    // O sino do Proprietário mostra a troca.
    await page.reload()
    await page.getByRole('button', { name: /notificações/i }).click()
    await expect(page.getByText('Conta para pagamento de Joana Lima alterada por Ana Souza, com chave ou titular diferente do CPF.')).toBeVisible()
    await page.keyboard.press('Escape')

    // Outro usuário revisa: confere, informa como conferiu e libera com o 2FA.
    state.reviewer = true
    await page.goto('/app/rh/funcionarios/e1?aba=pagamento')
    await press(page, 'Revisar conta', panel)
    const review = page.getByRole('alertdialog', { name: /Revisar a conta de Joana Lima/ })
    await expect(review.getByRole('button', { name: 'Liberar conta' })).toBeDisabled()
    await review.getByRole('textbox', { name: /^Como conferiu/ }).fill('Liguei para a Joana')
    await review.getByRole('textbox', { name: /^Código do aplicativo/ }).fill('123456')
    await press(page, 'Liberar conta', review)
    await expect(page.getByText('Conta liberada')).toBeVisible()
    expect(state.posted.at(-1)!.body).toMatchObject({ note: 'Liguei para a Joana', twoFactorCode: '123456', version: 3 })
    await expect(panel.getByText('Conta revisada em 27/10/2026')).toBeVisible()
  })

  test('dependente de IR pede o CPF e o próximo passo é a conta para pagamento; sem a permissão, dados protegidos (CA 1, 2 e 14)', async ({ page }) => {
    const { api, state } = complementApi()
    await mockApi(page, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: true, version: '6', modules: withHr(['view', 'edit', 'view_sensitive']) }) })
    await page.goto('/app/rh/funcionarios/e1?aba=dependentes')
    const panel = page.getByRole('tabpanel', { name: 'Dependentes' })
    await expect(panel.getByText('Nenhum dependente cadastrado.')).toBeVisible()
    await press(page, 'Novo dependente', panel)
    const dialog = page.getByRole('alertdialog', { name: /Novo dependente de Joana Lima/ })
    await dialog.getByRole('textbox', { name: /^Nome/ }).fill('Ana Lima')
    await dialog.getByRole('textbox', { name: /^Data de nascimento/ }).focus()
    await page.keyboard.type('03022015')
    await dialog.getByRole('combobox', { name: /^Parentesco/ }).selectOption('Child')
    await expect(dialog.getByRole('textbox', { name: /^CPF do dependente/ })).toHaveCount(0)
    await dialog.getByRole('checkbox', { name: 'Dependente de IR' }).check()
    await press(page, 'Salvar dependente', dialog)
    await expect(dialog.getByText('Informe o CPF do dependente de IR.')).toBeVisible()
    await dialog.getByRole('textbox', { name: /^CPF do dependente/ }).fill('529.982.247-25')
    await dialog.getByRole('checkbox', { name: 'Salário-família' }).check()
    await press(page, 'Salvar dependente', dialog)
    await expect(page.getByText('Dependente salvo')).toBeVisible()
    expect(state.posted.at(-1)!.body).toMatchObject({ name: 'Ana Lima', birthDate: '2015-02-03', kinship: 'Child', irDependent: true, familyAllowance: true, cpf: CPF })
    await expect(panel.getByText('CPF ***.982.247-**')).toBeVisible()
    await noHorizontalScroll(page)
    await page.getByRole('region', { name: 'Próximos passos' }).getByRole('button', { name: 'Registrar conta para pagamento' }).click()
    await expect(page.getByRole('tabpanel', { name: 'Pagamento e adicionais' })).toBeVisible()

    // Sem hr.view_sensitive: as três abas mostram "Dados protegidos" e nada sensível é pedido.
    const requests: string[] = []
    const other = await page.context().newPage()
    other.on('request', (r) => requests.push(r.url()))
    await mockApi(other, { ...api, 'GET /api/v1/me/permissions': (r: Route) => json(r, { permissions: [], isOwner: false, version: '7', modules: withHr(['view', 'edit']) }) })
    for (const [tab, name] of [['dependentes', 'Dependentes'], ['pagamento', 'Pagamento e adicionais'], ['documentos', 'Documentos']]) {
      await other.goto('/app/rh/funcionarios/e1?aba=' + tab)
      await expect(other.getByRole('tabpanel', { name }).getByText('Dados protegidos')).toBeVisible()
    }

    expect(requests.filter((u) => u.includes('/api/v1/') && (u.includes('/sensitive') || u.includes('/bank-account') || u.includes('/documents')))).toEqual([])
  })
})
