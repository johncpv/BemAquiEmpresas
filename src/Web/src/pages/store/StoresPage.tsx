import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import {
  createRegister, createStore, getListStoresQueryKey, updateRegister, updateStore, useGetStoreFormOptions, useListStores,
} from '@/api/generated/bemaqui'
import type { ReceiptPrintMode, RegisterUiMode, RegisterView, StoreView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatInstantDate } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { StoreFiscalMode } from './StoreFiscalMode'
import { parsePercent } from '@/pages/pdv/sale/discount-input'
import { contactText, isValidStoreCode, moneyText, nextRegisterNumber, normalizeStoreCode, parseAmount, parseOfflineLimits, registerLabel, UI_MODE_LABEL } from './stores'
import { DICAS } from './sales'

const CODE_DICA = 'Entra no número de cada venda (LOJA-01.1-000123): código da loja, caixa, época do aparelho e sequência. Escolha um curto e estável.'
const EPOCH_DICA = 'Sobe a cada aparelho novo no caixa. As vendas do aparelho anterior que ainda não subiram não se misturam com as do novo.'
const ALERT_DICA = 'Quando o dinheiro na gaveta passa deste valor, o caixa avisa "Faça uma sangria." depois da venda em dinheiro. O valor da gaveta nunca aparece para o operador.'
const CEILING_DICA = 'Acima deste valor, o caixa não recebe mais em dinheiro até a sangria (cartão e Pix continuam). Deixe em branco para não ter teto.'

const FREE_REMOVE_DICA = 'Ligado, o operador tira o último item que lançou sem chamar ninguém. Outro item, ou com isto desligado, pede quem tem a permissão de cancelar item. O servidor confere cada remoção por esta regra.'
const SYNC_ALERT_DICA = 'Quem aprova vendas recebe "Caixa 02 sem enviar vendas há N horas." quando um caixa passa deste tempo com venda esperando para subir, ou aberto e sem contato.'
const DISCOUNT_LIMIT_DICA = 'Desconto máximo, em % do item ou da venda, que o operador dá sem supervisor (sempre com motivo). Acima dele, só quem tem a permissão de desconto ou o PIN de um supervisor. Padrão 0: todo desconto pede autorização.'
const BREAK_TOLERANCE_DICA = 'Quebra de caixa é a diferença entre o contado e o esperado no fechamento. Até este valor, por forma de pagamento, o caixa fecha sozinho; acima, vai para a conferência do gerente. Sobra numa forma não compensa falta em outra.'
const OFFLINE_WINDOW_DICA = 'Quanto tempo o caixa pode ficar sem enviar as vendas. Passou disso, ele não abre turno novo até conectar (o turno aberto pode ser fechado). A conta parte do último horário recebido do servidor; atrasar o relógio do aparelho não estende o prazo. De 1 a 168 horas (7 dias).'
const OFFLINE_LIMITS_DICA = 'Sem internet, o supervisor aprova desconto e cancelamento com o código sem internet, conferido no aparelho e de novo quando as vendas sobem. Cada aprovação vai até este valor; o turno tem um número e uma soma máximos. Zero desliga a aprovação sem internet.'

const PRINT_DICA = 'O que o caixa faz com o cupom quando a venda termina. "Perguntar" mostra Imprimir e Não imprimir; nos outros, o botão Imprimir cupom continua lá.'

type Editing = { kind: 'store'; store: StoreView | null } | { kind: 'register'; store: StoreView; register: RegisterView | null }

/**
 * Loja → Lojas e caixas (PDV-01 CA 2 e 3): lojas com os caixas e o aparelho de cada um (último contato, "Sem contato há N h"). Telas de
 * retaguarda com sales.*; o aparelho é registrado no próprio aparelho, em /pdv.
 */
export function StoresPage() {
  const query = useListStores()
  const client = useQueryClient()
  const [editing, setEditing] = useState<Editing | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListStoresQueryKey() })
  const newStore = <Can perm="sales.create"><button type="button" onClick={() => setEditing({ kind: 'store', store: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova loja</button></Can>
  const actions = (
    <div className="flex flex-wrap gap-2">
      <Link to="/app/loja/vendas" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Vendas</Link>
      <Link to="/app/loja/caixas" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Caixas</Link>
      <Link to="/app/loja/pendencias" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Pendências do PDV</Link>
      <Link to="/app/loja/vendas-sem-documento-fiscal" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Vendas sem documento fiscal</Link>
      <Link to="/app/loja/aprovacoes" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Aprovações de supervisor</Link>
      <Link to="/app/loja/devolucoes" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Devoluções</Link>
      {newStore}
    </div>
  )
  const stores = query.data ?? []

  return (
    <>
      <PageHeading title="Lojas e caixas" description="Cada venda do balcão sai de um caixa conhecido, num aparelho registrado." actions={actions} />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState title="Não foi possível carregar as lojas." onRetry={() => query.refetch()} /> : stores.length === 0 ? (
        <EmptyState title="Nenhuma loja ainda." description="Cadastre a loja com o local de estoque e a tabela de preço do balcão; depois, os caixas." action={newStore} />
      ) : (
        <div className="space-y-4">
          {stores.map((store) => (
            <section key={store.id} aria-labelledby={`loja-${store.id}`} className="space-y-3 rounded-md border border-border bg-card p-4">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <h2 id={`loja-${store.id}`} className="flex flex-wrap items-center gap-2 font-semibold">
                    <button type="button" onClick={() => setEditing({ kind: 'store', store })} className="text-primary hover:underline">{store.name}</button>
                    <span className="font-mono text-sm text-muted-foreground">{store.code}</span>
                    {store.isActive ? null : <StatusChip tone="neutral">Inativa</StatusChip>}
                  </h2>
                  <p className="text-sm text-muted-foreground">Estoque: {store.stockLocationName} · Tabela: {store.defaultPriceListName}</p>
                </div>
                <Can perm="sales.create">
                  <button type="button" onClick={() => setEditing({ kind: 'register', store, register: null })} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Novo caixa</button>
                </Can>
              </div>
              <StoreFiscalMode store={store} onChanged={refresh} />
              {store.registers.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nenhum caixa nesta loja. Clique em <strong>Novo caixa</strong> para criar o primeiro.</p>
              ) : (
                <ul className="divide-y divide-border">
                  {store.registers.map((r) => <RegisterRow key={r.id} register={r} onOpen={() => setEditing({ kind: 'register', store, register: r })} />)}
                </ul>
              )}
            </section>
          ))}
        </div>
      )}
      {editing?.kind === 'store' ? <StorePanel store={editing.store} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
      {editing?.kind === 'register' ? <RegisterPanel store={editing.store} register={editing.register} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
    </>
  )
}

function RegisterRow({ register, onOpen }: { register: RegisterView; onOpen: () => void }) {
  const device = register.device ?? null
  const warning = device ? contactText(device.hoursWithoutContact) : null
  return (
    <li className="flex flex-wrap items-center justify-between gap-3 py-3">
      <div className="min-w-0 space-y-0.5">
        <p className="flex flex-wrap items-center gap-2 font-semibold">
          <button type="button" onClick={onOpen} className="text-primary hover:underline">Caixa {registerLabel(register.number)}</button>
          <span className="text-sm font-normal text-muted-foreground">{UI_MODE_LABEL[register.uiMode] ?? register.uiMode}</span>
          {register.isActive ? null : <StatusChip tone="neutral">Inativo</StatusChip>}
        </p>
        <p className="text-sm text-muted-foreground tabular-nums">
          Conta: {register.cashAccountName}
          {register.deviceEpoch > 0 ? <> · época {register.deviceEpoch}<Dica nome="Época do caixa" texto={EPOCH_DICA} /></> : null}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-2 text-sm">
        {device ? (
          <>
            <span className="font-medium">{device.name}</span>
            <span className="text-muted-foreground tabular-nums">último contato {formatInstantDate(device.lastContactAt)}</span>
            {warning ? <StatusChip tone="warn">{warning}</StatusChip> : <StatusChip tone="ok">Em contato</StatusChip>}
          </>
        ) : <StatusChip tone="info">Sem aparelho: registre em /pdv</StatusChip>}
      </div>
    </li>
  )
}

function StorePanel({ store, onClose, onSaved }: { store: StoreView | null; onClose: () => void; onSaved: () => void }) {
  const options = useGetStoreFormOptions()
  const initial = {
    code: store?.code ?? '', name: store?.name ?? '', stockLocationId: store?.stockLocationId ?? '', defaultPriceListId: store?.defaultPriceListId ?? '',
    isActive: store?.isActive ?? true,
    cashDropAlert: moneyText(store?.cashDropAlertAmount ?? 1000), cashCeiling: store?.cashCeilingAmount == null ? '' : moneyText(store.cashCeilingAmount),
    receiptPrintMode: (store?.receiptPrintMode ?? 'Ask') as ReceiptPrintMode,
    freeRemoveLast: store?.freeRemoveLast ?? true, syncAlertHours: String(store?.syncAlertHours ?? 4),
    cashBreakTolerance: moneyText(store?.cashBreakTolerance ?? 5),
    discountLimit: String(store?.discountLimitPercent ?? 0).replace('.', ','),
    // PDV-07 (PP-22): alerta de cancelamentos do turno, padrão 3%.
    cancelAlert: String(store?.cancelAlertPercent ?? 3).replace('.', ','),
    // OFF-03 (PP-10 e PP-22): tempo máximo sem internet e limites da aprovação sem rede.
    offlineWindowHours: String(store?.offlineWindowHours ?? 168),
    offlineMax: moneyText(store?.offlineApprovalMaxAmount ?? 50), offlineSession: moneyText(store?.offlineApprovalSessionAmount ?? 150),
    offlineCount: String(store?.offlineApprovalSessionCount ?? 5), offlineReview: moneyText(store?.offlineApprovalReviewAmount ?? 100),
    offlineCancelSale: store?.offlineCancelSaleAllowed ?? true,
  }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const canWrite = useCan(store ? 'sales.edit' : 'sales.create')
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const priceListId = draft.defaultPriceListId || options.data?.defaultPriceListId || ''
  const locationId = draft.stockLocationId || (store ? '' : options.data?.locations[0]?.id ?? '')

  const save = async () => {
    if (!isValidStoreCode(draft.code)) { setError('Informe o código da loja com 2 a 6 letras ou números (ex.: LOJA, CENTRO).'); return false }
    if (!draft.name.trim()) { setError('Informe o nome da loja.'); return false }
    if (!locationId || !priceListId) { setError('Escolha o local de estoque e a tabela de preço.'); return false }
    const alert = parseAmount(draft.cashDropAlert)
    const ceiling = draft.cashCeiling.trim() ? parseAmount(draft.cashCeiling) : null
    if (alert === undefined) { setError('Informe o aviso de sangria em reais (ex.: 1.000,00).'); return false }
    if (ceiling === undefined || (ceiling !== null && ceiling <= alert)) { setError('O teto de dinheiro no caixa tem de ser maior que o aviso de sangria, ou fique em branco.'); return false }
    const syncAlertHours = Number(draft.syncAlertHours)
    if (!Number.isInteger(syncAlertHours) || syncAlertHours < 1 || syncAlertHours > 72) { setError('Informe o aviso de caixa sem enviar vendas entre 1 e 72 horas.'); return false }
    const cashBreakTolerance = parseAmount(draft.cashBreakTolerance)
    if (cashBreakTolerance === undefined || cashBreakTolerance > 9999.99) { setError('Informe a tolerância de quebra em reais (ex.: 5,00).'); return false }
    const discountLimitPercent = parsePercent(draft.discountLimit)
    if (discountLimitPercent === null) { setError('Informe o limite de desconto em %, de 0 a 100 (ex.: 5).'); return false }
    const cancelAlertPercent = parsePercent(draft.cancelAlert)
    if (cancelAlertPercent === null) { setError('Informe o alerta de cancelamentos em %, de 0 a 100 (ex.: 3).'); return false }
    const offline = parseOfflineLimits(draft)
    if (typeof offline === 'string') { setError(offline); return false }
    const input = {
      code: draft.code, name: draft.name.trim(), stockLocationId: locationId, defaultPriceListId: priceListId, isActive: draft.isActive,
      cashDropAlertAmount: alert, cashCeilingAmount: ceiling, receiptPrintMode: draft.receiptPrintMode, freeRemoveLast: draft.freeRemoveLast, syncAlertHours, cashBreakTolerance,
      discountLimitPercent, cancelAlertPercent, ...offline,
    }
    try {
      if (store) await updateStore(store.id, input)
      else await createStore(input)
      notifySuccess(store ? 'Loja salva' : 'Loja criada. Agora crie os caixas.')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a loja.')
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={store ? store.name : 'Nova loja'} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar loja</button> : null}
        </>
      )}>
      {options.isError ? <ErrorState title="Não foi possível carregar os locais e as tabelas." onRetry={() => options.refetch()} /> : (
        <fieldset disabled={!canWrite} className="space-y-3">
          <FormError message={error ?? undefined} />
          <FormGrid>
            <Field label="Código" required size="sm" dica={CODE_DICA}>
              {(p) => <input {...p} value={draft.code} maxLength={6} onChange={(e) => setDraft({ ...draft, code: normalizeStoreCode(e.target.value) })} className={inputClass} />}
            </Field>
            <Field label="Nome" required size="lg">{(p) => <input {...p} value={draft.name} maxLength={80} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
            <Field label="Local de estoque" required size="lg" hint="De onde sai a mercadoria vendida nesta loja.">
              {(p) => (
                <select {...p} value={locationId} onChange={(e) => setDraft({ ...draft, stockLocationId: e.target.value })} className={inputClass}>
                  {store && !options.data?.locations.some((l) => l.id === store.stockLocationId) ? <option value={store.stockLocationId}>{store.stockLocationName}</option> : null}
                  {(options.data?.locations ?? []).map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="Tabela de preço" required size="lg">
              {(p) => (
                <select {...p} value={priceListId} onChange={(e) => setDraft({ ...draft, defaultPriceListId: e.target.value })} className={inputClass}>
                  {(options.data?.priceLists ?? []).map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
                </select>
              )}
            </Field>
            {store ? (
              <Field label="Situação" size="md">
                {(p) => (
                  <label className="flex items-center gap-2 text-sm"><input {...p} type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} /> Loja ativa</label>
                )}
              </Field>
            ) : null}
            <Field label="Aviso de sangria a partir de (R$)" required size="md" dica={ALERT_DICA}>
              {(p) => <input {...p} value={draft.cashDropAlert} inputMode="decimal" onChange={(e) => setDraft({ ...draft, cashDropAlert: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Teto de dinheiro no caixa (R$)" size="md" dica={CEILING_DICA} hint="Em branco: sem teto.">
              {(p) => <input {...p} value={draft.cashCeiling} inputMode="decimal" onChange={(e) => setDraft({ ...draft, cashCeiling: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Cupom ao concluir a venda" size="md" dica={PRINT_DICA}>
              {(p) => (
                <select {...p} value={draft.receiptPrintMode} onChange={(e) => setDraft({ ...draft, receiptPrintMode: e.target.value as ReceiptPrintMode })} className={inputClass}>
                  <option value="Ask">Perguntar</option>
                  <option value="Always">Imprimir sempre</option>
                  <option value="Never">Não imprimir</option>
                </select>
              )}
            </Field>
            <Field label="Último item sem supervisor" size="md" dica={FREE_REMOVE_DICA}>
              {(p) => (
                <label className="flex items-center gap-2 text-sm"><input {...p} type="checkbox" checked={draft.freeRemoveLast} onChange={(e) => setDraft({ ...draft, freeRemoveLast: e.target.checked })} /> O operador tira o último item lançado</label>
              )}
            </Field>
            <Field label="Limite de desconto sem supervisor (%)" required size="sm" dica={DISCOUNT_LIMIT_DICA}>
              {(p) => <input {...p} value={draft.discountLimit} inputMode="decimal" maxLength={6} onChange={(e) => setDraft({ ...draft, discountLimit: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Alerta de cancelamentos no turno (%)" required size="sm" dica={DICAS.cancelamentos} hint="Zero desliga.">
              {(p) => <input {...p} value={draft.cancelAlert} inputMode="decimal" maxLength={6} onChange={(e) => setDraft({ ...draft, cancelAlert: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Tolerância de quebra por forma (R$)" required size="md" dica={BREAK_TOLERANCE_DICA}>
              {(p) => <input {...p} value={draft.cashBreakTolerance} inputMode="decimal" onChange={(e) => setDraft({ ...draft, cashBreakTolerance: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Aviso de caixa sem enviar vendas (horas)" required size="sm" dica={SYNC_ALERT_DICA}>
              {(p) => <input {...p} value={draft.syncAlertHours} inputMode="numeric" maxLength={2} onChange={(e) => setDraft({ ...draft, syncAlertHours: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
            </Field>
            <Field label="Tempo máximo sem internet (horas)" required size="sm" dica={OFFLINE_WINDOW_DICA}>
              {(p) => <input {...p} value={draft.offlineWindowHours} inputMode="numeric" maxLength={3} onChange={(e) => setDraft({ ...draft, offlineWindowHours: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
            </Field>
            <Field label="Aprovação sem internet: até (R$ cada)" required size="md" dica={OFFLINE_LIMITS_DICA}>
              {(p) => <input {...p} value={draft.offlineMax} inputMode="decimal" onChange={(e) => setDraft({ ...draft, offlineMax: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Aprovações sem internet por turno" required size="sm">
              {(p) => <input {...p} value={draft.offlineCount} inputMode="numeric" maxLength={2} onChange={(e) => setDraft({ ...draft, offlineCount: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
            </Field>
            <Field label="Soma sem internet por turno (R$)" required size="md">
              {(p) => <input {...p} value={draft.offlineSession} inputMode="decimal" onChange={(e) => setDraft({ ...draft, offlineSession: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Fechamento em conferência acima de (R$ aprovados sem internet)" required size="md">
              {(p) => <input {...p} value={draft.offlineReview} inputMode="decimal" onChange={(e) => setDraft({ ...draft, offlineReview: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Cancelar venda sem internet" size="lg" hint="Só a venda paga toda em dinheiro, com o código sem internet do supervisor.">
              {(p) => (
                <label className="flex min-h-10 items-center gap-2">
                  <input {...p} type="checkbox" checked={draft.offlineCancelSale} onChange={(e) => setDraft({ ...draft, offlineCancelSale: e.target.checked })} />
                  Permitir
                </label>
              )}
            </Field>
          </FormGrid>
        </fieldset>
      )}
    </SidePanel>
  )
}

function RegisterPanel({ store, register, onClose, onSaved }: { store: StoreView; register: RegisterView | null; onClose: () => void; onSaved: () => void }) {
  const options = useGetStoreFormOptions()
  const initial = {
    number: String(register?.number ?? nextRegisterNumber(store.registers.map((r) => r.number))), cashAccountId: register?.cashAccountId ?? '',
    uiMode: (register?.uiMode ?? 'Keyboard') as RegisterUiMode, isActive: register?.isActive ?? true,
  }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const canWrite = useCan(register ? 'sales.edit' : 'sales.create')
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const accounts = [...(register ? [{ id: register.cashAccountId, name: register.cashAccountName }] : []), ...(options.data?.cashAccounts ?? [])]
  const accountId = draft.cashAccountId || accounts[0]?.id || ''

  const save = async () => {
    const number = Number(draft.number)
    if (!Number.isInteger(number) || number < 1 || number > 99) { setError('Informe o número do caixa, de 1 a 99.'); return false }
    if (!accountId) { setError('Crie uma conta do tipo Caixa no Financeiro (Contas e caixas) e volte aqui.'); return false }
    const input = { number, cashAccountId: accountId, uiMode: draft.uiMode, isActive: draft.isActive }
    try {
      if (register) await updateRegister(store.id, register.id, input)
      else await createRegister(store.id, input)
      notifySuccess(register ? 'Caixa salvo' : 'Caixa criado. Registre o aparelho em /pdv.')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o caixa.')
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={register ? `Caixa ${registerLabel(register.number)} · ${store.name}` : `Novo caixa · ${store.name}`} dirty={dirty && canWrite} onSave={save}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar caixa</button> : null}
        </>
      )}>
      {options.isError ? <ErrorState title="Não foi possível carregar as contas do tipo Caixa." onRetry={() => options.refetch()} /> : (
        <fieldset disabled={!canWrite} className="space-y-3">
          <FormError message={error ?? undefined} />
          <FormGrid>
            <Field label="Número" required size="sm">
              {(p) => <input {...p} value={draft.number} inputMode="numeric" maxLength={2} onChange={(e) => setDraft({ ...draft, number: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
            </Field>
            <Field label="Conta do tipo Caixa" required size="lg" hint="Onde entra o dinheiro deste caixa. Cada caixa tem a sua.">
              {(p) => (
                <select {...p} value={accountId} onChange={(e) => setDraft({ ...draft, cashAccountId: e.target.value })} className={inputClass}>
                  {accounts.length === 0 ? <option value="">Nenhuma conta Caixa livre</option> : null}
                  {accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="Modo de tela" required size="md" hint="Toque: grade de produtos. Teclado e leitor: código de barras.">
              {(p) => (
                <select {...p} value={draft.uiMode} onChange={(e) => setDraft({ ...draft, uiMode: e.target.value as RegisterUiMode })} className={inputClass}>
                  <option value="Keyboard">Teclado e leitor</option>
                  <option value="Touch">Toque</option>
                </select>
              )}
            </Field>
            {register ? (
              <Field label="Situação" size="md" hint={register.device ? 'Bloqueie o aparelho antes de desativar o caixa.' : undefined}>
                {(p) => (
                  <label className="flex items-center gap-2 text-sm"><input {...p} type="checkbox" checked={draft.isActive} onChange={(e) => setDraft({ ...draft, isActive: e.target.checked })} /> Caixa ativo</label>
                )}
              </Field>
            ) : null}
          </FormGrid>
          {register?.device ? <p className="text-sm text-muted-foreground">Aparelho: {register.device.name}. Para bloquear, use Configurações → Segurança → Aparelhos.</p> : null}
        </fieldset>
      )}
    </SidePanel>
  )
}
