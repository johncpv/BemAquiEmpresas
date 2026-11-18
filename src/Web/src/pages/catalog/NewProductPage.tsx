import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { useState } from 'react'
import { createProduct, getListProductsQueryKey, searchCatalogItems, useListCatalogUnits, useListProductCategories } from '@/api/generated/bemaqui'
import type { ProductCreateInput } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { Field, FormActions, FormError, FormGrid, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, NcmInput, QuantityInput } from '@/components/form/inputs'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { notifySuccess } from '@/components/ui/messages'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, gtinError, normalizeGtin, PRODUCTS, toNumber } from './catalog'

type ItemMode = 'new' | 'existing'

interface Draft {
  mode: ItemMode
  item: (ComboOption & { name?: string }) | null
  unitId: string
  ncm: string
  code: string
  saleName: string
  categoryId: string
  plu: string
  weighable: boolean
  sellablePos: boolean
  gtin: string
  pack: string | null
  price: string | null
}

const EMPTY: Draft = { mode: 'new', item: null, unitId: '', ncm: '', code: '', saleName: '', categoryId: '', plu: '', weighable: false, sellablePos: true, gtin: '', pack: '1', price: null }

/**
 * Novo produto (CAT-01 CA 3): o item do estoque nasce no mesmo formulário (Mercadoria, com o nome de venda, a unidade e o NCM digitados uma
 * vez só) ou vem de um item que ainda não tem produto. Categoria "Geral" por padrão, PLU, o primeiro código de barras e, com "Alterar preço",
 * o preço. Depois de salvar, a ficha oferece o próximo passo (grade, mais códigos ou o preço).
 */
export function NewProductPage() {
  const navigate = useNavigate()
  const client = useQueryClient()
  const units = useListCatalogUnits()
  const categories = useListProductCategories({})
  const canPrice = useCan('catalog.edit_price')
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [tried, setTried] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const activeCategories = (categories.data ?? []).filter((c) => c.isActive)
  // Propositivo: a categoria "Geral" e a unidade "UN" vêm escolhidas.
  const categoryId = draft.categoryId || activeCategories.find((c) => c.isDefault)?.id || ''
  const unitId = draft.unitId || (units.data ?? []).find((u) => u.code.toUpperCase() === 'UN')?.id || (units.data ?? [])[0]?.id || ''
  const dirty = JSON.stringify(draft) !== JSON.stringify(EMPTY)
  const set = (patch: Partial<Draft>) => setDraft((d) => ({ ...d, ...patch }))

  const errors = {
    saleName: draft.saleName.trim() ? undefined : 'Informe o nome de venda.',
    item: draft.mode === 'existing' && !draft.item ? 'Escolha o item do estoque.' : undefined,
    unit: draft.mode === 'new' && !unitId ? 'Escolha a unidade.' : undefined,
    plu: draft.plu && !/^\d{1,6}$/.test(draft.plu) ? 'O PLU tem de 1 a 6 dígitos.' : draft.weighable && !draft.plu ? 'Produto vendido por peso precisa do PLU.' : undefined,
    gtin: gtinError(draft.gtin),
    pack: normalizeGtin(draft.gtin) && !(Number(draft.pack) > 0) ? 'A quantidade da embalagem é maior que zero.' : undefined,
  }

  const save = async () => {
    setTried(true)
    if (Object.values(errors).some(Boolean)) return false
    const gtin = normalizeGtin(draft.gtin)
    const input: ProductCreateInput = {
      saleName: draft.saleName.trim(),
      categoryId: categoryId || null,
      itemId: draft.mode === 'existing' ? draft.item!.id : null,
      newItem: draft.mode === 'new' ? { code: draft.code.trim() || null, unitId, ncm: draft.ncm || null } : null,
      plu: draft.plu || null,
      isWeighable: draft.weighable,
      sellablePos: draft.sellablePos,
      sellableOnline: false,
      price: canPrice ? toNumber(draft.price) : null,
      barcodes: gtin ? [{ gtin, packQuantity: toNumber(draft.pack) ?? 1 }] : [],
    }
    setBusy(true)
    try {
      const saved = await createProduct(input)
      notifySuccess(`Produto ${saved.saleName} salvo`)
      void client.invalidateQueries({ queryKey: getListProductsQueryKey() })
      void navigate({ to: `${PRODUCTS}/${saved.id}`, search: { novo: 'sim' }, ignoreBlocker: true } as never)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o produto. Tente de novo.')
      return false
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeading title="Novo produto" meta={<Link to={PRODUCTS as never} className="text-sm font-semibold text-primary">Voltar aos produtos</Link>} />
      <form id="produto-novo" onSubmit={(e) => { e.preventDefault(); void save() }} noValidate className="space-y-6">
        <UnsavedChangesGuard when={dirty && !busy} onSave={save} />
        <FormError message={error ?? undefined} />
        <FormSection grid="none" title="Dados" description="Como o produto aparece no caixa, na etiqueta e nos relatórios.">
          <FormGrid>
            <Field label="Nome de venda" required size="lg" error={tried ? errors.saleName : undefined} hint="Ex.: Refrigerante cola 2 L. É também o nome do item novo no estoque.">
              {(p) => <input {...p} value={draft.saleName} maxLength={150} onChange={(e) => set({ saleName: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Categoria" size="md">
              {(p) => (
                <select {...p} value={categoryId} onChange={(e) => set({ categoryId: e.target.value })} className={inputClass}>
                  {activeCategories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="PLU" size="sm" dica={DICAS.plu} error={tried ? errors.plu : undefined}>
              {(p) => <input {...p} value={draft.plu} inputMode="numeric" maxLength={6} onChange={(e) => set({ plu: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
            </Field>
          </FormGrid>
          <div className="mt-3 flex flex-wrap gap-x-6 gap-y-2 text-sm">
            <label className="inline-flex min-h-11 items-center gap-2"><input type="checkbox" checked={draft.weighable} onChange={(e) => set({ weighable: e.target.checked })} />Vendido por peso (balança)</label>
            <label className="inline-flex min-h-11 items-center gap-2"><input type="checkbox" checked={draft.sellablePos} onChange={(e) => set({ sellablePos: e.target.checked })} />Vende no caixa</label>
          </div>
        </FormSection>

        <FormSection grid="none" title="Item do estoque" description="O estoque guarda a unidade, o NCM, o custo e o saldo; o catálogo, o nome de venda, o código de barras e o preço.">
          <fieldset className="mb-3 flex flex-wrap gap-x-6 gap-y-2 text-sm">
            <legend className="sr-only">De onde vem o item</legend>
            <label className="inline-flex min-h-11 items-center gap-2"><input type="radio" name="modo-item" checked={draft.mode === 'new'} onChange={() => set({ mode: 'new' })} />Criar o item agora</label>
            <label className="inline-flex min-h-11 items-center gap-2"><input type="radio" name="modo-item" checked={draft.mode === 'existing'} onChange={() => set({ mode: 'existing' })} />Usar um item que já existe</label>
          </fieldset>
          {draft.mode === 'new' ? (
            <FormGrid>
              <Field label="Unidade" required size="sm" error={tried ? errors.unit : undefined}>
                {(p) => (
                  <select {...p} value={unitId} onChange={(e) => set({ unitId: e.target.value })} className={inputClass}>
                    {(units.data ?? []).map((u) => <option key={u.id} value={u.id}>{u.code} — {u.name}</option>)}
                  </select>
                )}
              </Field>
              <Field label="NCM" size="sm" hint="Opcional no cupom não fiscal; a nota fiscal pede.">
                {(p) => <NcmInput {...p} value={draft.ncm} onChange={(v: string) => set({ ncm: v })} />}
              </Field>
              <Field label="Código interno" size="sm" hint="Vazio: o sistema gera (MER-000001).">
                {(p) => <input {...p} value={draft.code} maxLength={30} onChange={(e) => set({ code: e.target.value })} className={inputClass} />}
              </Field>
            </FormGrid>
          ) : (
            <FormGrid>
              <Field label="Item do estoque" required size="lg" error={tried ? errors.item : undefined} hint="Só aparecem itens que ainda não têm produto.">
                {(p) => (
                  <Combobox {...p} queryKey={['catalog-items']} placeholder="Busque pelo código ou nome" selected={draft.item} value={draft.item?.id}
                    search={async (q) => (await searchCatalogItems({ q })).map((i) => ({ id: i.id, label: `${i.code} — ${i.name}`, hint: i.unitCode, name: i.name }))}
                    onChange={(o) => set({ item: o, saleName: draft.saleName || (o as { name?: string } | null)?.name || '' })} />
                )}
              </Field>
            </FormGrid>
          )}
        </FormSection>

        <FormSection grid="none" title="Código de barras" description="O primeiro código; outros (a caixa, o fardo) entram na ficha do produto.">
          <FormGrid>
            <Field label="Código de barras (GTIN)" size="md" error={errors.gtin} hint="Leia com o leitor ou digite os números da etiqueta.">
              {(p) => <input {...p} value={draft.gtin} inputMode="numeric" maxLength={18} onChange={(e) => set({ gtin: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Quantidade da embalagem" size="sm" dica={DICAS.pack} error={tried ? errors.pack : undefined}>
              {(p) => <QuantityInput {...p} value={draft.pack} onChange={(v) => set({ pack: v })} />}
            </Field>
          </FormGrid>
        </FormSection>

        {canPrice ? (
          <FormSection grid="none" title="Preço" description="Preço da tabela Balcão. Pode ficar para depois: o produto sem preço não vende no caixa.">
            <FormGrid>
              <Field label="Preço de venda" size="sm">{(p) => <MoneyInput {...p} value={draft.price} onChange={(v) => set({ price: v })} />}</Field>
            </FormGrid>
          </FormSection>
        ) : null}

        <FormActions sticky dirty={dirty}><button type="submit" disabled={busy} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar produto</button></FormActions>
      </form>
    </>
  )
}
