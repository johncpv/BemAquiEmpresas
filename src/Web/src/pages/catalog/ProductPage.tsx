import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { ImageOff, Package } from 'lucide-react'
import { useState } from 'react'
import {
  deleteProduct, getDownloadProductPhotoUrl, getGetProductQueryKey, getListProductsQueryKey, inactivateProduct, reactivateProduct, updateProduct,
  uploadProductPhoto, useGetProduct, useListProductCategories,
} from '@/api/generated/bemaqui'
import type { ProductDetail } from '@/api/generated/model'
import { ApiError, apiBlobUrl } from '@/api/http'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, PRODUCTS } from './catalog'
import { CostPanel } from './CostPanel'
import { BarcodesSection, PriceSection, VariantsSection } from './ProductSections'

/**
 * Ficha do produto (CAT-01, padrão da ADR 0044): Dados (com a categoria e a foto), Variações, Códigos de barras e Preço, mais o painel de
 * custo e margem só com catalog.view_cost. Depois de criar (?novo=sim), os próximos passos: gerar grade, adicionar código, definir preço.
 */
export function ProductPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetProduct(id)
  const client = useQueryClient()
  const navigate = useNavigate()
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const [steps, setSteps] = useState(search.novo === 'sim')
  const [priceOpen, setPriceOpen] = useState(false)
  const [suggested, setSuggested] = useState<number | null>(null)
  const canPrice = useCan('catalog.edit_price')

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Produto não encontrado.' : 'Não foi possível carregar o produto.'} message={notFound ? 'Ele pode ter sido excluído. Volte aos produtos.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const product = query.data
  const refresh = (p: ProductDetail) => { client.setQueryData(getGetProductQueryKey(id), p); void client.invalidateQueries({ queryKey: getListProductsQueryKey() }) }
  const reload = () => { void query.refetch(); void client.invalidateQueries({ queryKey: getListProductsQueryKey() }) }
  const scrollTo = (anchor: string) => document.getElementById(anchor)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  const nextSteps = [
    ...(product.variantOfProductId ? [] : [{ label: 'Gerar grade', onSelect: () => scrollTo('variacoes') }]),
    ...(product.barcodes.length === 0 && !product.hasVariants ? [{ label: 'Adicionar código de barras', onSelect: () => scrollTo('codigos') }] : []),
    ...(product.price == null && canPrice ? [{ label: 'Definir preço', onSelect: () => { setSuggested(null); setPriceOpen(true) } }] : []),
  ]

  return (
    <>
      <PageHeading title={product.saleName}
        meta={
          <>
            <StatusChip tone={product.isActive ? 'ok' : 'neutral'}>{product.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
            {product.hasVariants ? <StatusChip tone="info">{product.variants.length} variações</StatusChip> : null}
            <Link to={PRODUCTS as never} className="ml-2 text-sm font-semibold text-primary">Voltar aos produtos</Link>
          </>
        } />
      {steps ? <div className="mb-4"><NextSteps steps={nextSteps} onDismiss={() => setSteps(false)} /></div> : null}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div className="min-w-0 space-y-8">
          <DataSection key={product.version} product={product} onSaved={refresh} />
          <VariantsSection product={product} onChange={refresh} canPrice={canPrice} />
          <BarcodesSection product={product} onChange={reload} />
          <PriceSection product={product} onChange={refresh} open={priceOpen} onOpenChange={setPriceOpen} initial={suggested} />
          <div className="flex flex-wrap gap-2">
            <Can perm="catalog.edit">
              {product.isActive ? (
                <ConfirmDialog verb="Desativar" done="Desativado" title={`Desativar ${product.saleName}?`}
                  description={product.hasVariants ? 'O produto e as variações saem do caixa e da busca; as vendas antigas continuam.' : 'O produto sai do caixa e da busca; as vendas antigas continuam.'}
                  onConfirm={async () => refresh(await inactivateProduct(id))} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Desativar</button>} />
              ) : (
                <button type="button" onClick={async () => { refresh(await reactivateProduct(id)); notifySuccess('Reativado') }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>
              )}
            </Can>
            <Can perm="catalog.delete">
              <ConfirmDialog verb="Excluir" done="Excluído" destructive title={`Excluir ${product.saleName}?`}
                description="Os códigos de barras ficam livres e o item continua no estoque. Produto já vendido não é excluído: desative-o."
                onConfirm={async () => { await deleteProduct(id); void client.invalidateQueries({ queryKey: getListProductsQueryKey() }); void navigate({ to: PRODUCTS, ignoreBlocker: true } as never) }}
                recovery={{ verb: 'Desativar', done: 'Desativado', when: (code) => code === 'product_in_use', onSelect: async () => refresh(await inactivateProduct(id)) }}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir produto</button>} />
            </Can>
          </div>
          <section aria-labelledby="historico-produto" className="space-y-2">
            <h2 id="historico-produto" className="text-base font-semibold">Histórico de alterações</h2>
            <RecordHistory entity="Product" id={product.id} />
          </section>
        </div>
        <div className="space-y-4">
          <ProductPhoto product={product} onUploaded={reload} />
          {product.cost ? <CostPanel cost={product.cost} price={product.price} onUseSuggested={canPrice ? (p) => { setSuggested(p); setPriceOpen(true) } : undefined} /> : null}
        </div>
      </div>
    </>
  )
}

/** Dados (CA 14 e 16): nome de venda, categoria, PLU e onde vende. Na variação, nome e categoria vêm do pai. */
function DataSection({ product, onSaved }: { product: ProductDetail; onSaved: (p: ProductDetail) => void }) {
  const categories = useListProductCategories({})
  const canEdit = useCan('catalog.edit')
  const initial = { saleName: product.saleName, categoryId: product.categoryId, plu: product.plu ?? '', weighable: product.isWeighable, pos: product.sellablePos }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const variant = !!product.variantOfProductId
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)
  const options = (categories.data ?? []).filter((c) => c.isActive || c.id === product.categoryId)

  const save = async () => {
    if (!draft.saleName.trim()) { setError('Informe o nome de venda.'); return false }
    try {
      onSaved(await updateProduct(product.id, {
        saleName: draft.saleName.trim(), categoryId: draft.categoryId, plu: draft.plu || null, isWeighable: draft.weighable, sellablePos: draft.pos,
        sellableOnline: product.sellableOnline, price: null, version: product.version,
      }))
      notifySuccess('Produto salvo')
      setError(null)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o produto.')
      return false
    }
  }

  return (
    <section aria-labelledby="dados" className="space-y-3">
      <h2 id="dados" className="text-base font-semibold">Dados</h2>
      <form id="produto" onSubmit={(e) => { e.preventDefault(); void save() }} noValidate>
        <UnsavedChangesGuard when={dirty && canEdit} onSave={save} />
        <fieldset disabled={!canEdit} className="space-y-3">
          <FormError message={error ?? undefined} />
          <FormGrid>
            <Field label="Nome de venda" required size="lg" hint={variant ? 'Vem do produto pai, com os atributos da variação.' : product.hasVariants ? 'As variações recebem o nome novo.' : undefined}>
              {(p) => <input {...p} value={draft.saleName} disabled={variant} maxLength={150} onChange={(e) => setDraft({ ...draft, saleName: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Categoria" size="md">
              {(p) => (
                <select {...p} value={draft.categoryId} disabled={variant} onChange={(e) => setDraft({ ...draft, categoryId: e.target.value })} className={inputClass}>
                  {options.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              )}
            </Field>
            <Field label="PLU" size="sm" dica={DICAS.plu}>
              {(p) => <input {...p} value={draft.plu} inputMode="numeric" maxLength={6} onChange={(e) => setDraft({ ...draft, plu: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
            </Field>
          </FormGrid>
          <div className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
            <label className="inline-flex min-h-11 items-center gap-2"><input type="checkbox" checked={draft.weighable} onChange={(e) => setDraft({ ...draft, weighable: e.target.checked })} />Vendido por peso (balança)</label>
            <label className="inline-flex min-h-11 items-center gap-2"><input type="checkbox" checked={draft.pos} onChange={(e) => setDraft({ ...draft, pos: e.target.checked })} />Vende no caixa</label>
          </div>
          <p className="text-sm text-muted-foreground">
            Item do estoque: <Link to="/app/estoque/itens/$id" params={{ id: product.itemId }} className="font-semibold text-primary">{product.itemCode}</Link>
            {' '}· unidade {product.unitCode}{product.ncm ? ` · NCM ${product.ncm}` : ' · sem NCM'}
          </p>
          {canEdit ? <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar produto</button> : null}
        </fieldset>
      </form>
    </section>
  )
}

/** Foto (CA 16): só a limpa, lida pela rota do produto; a variação mostra a do pai. Envio com catalog.edit. */
function ProductPhoto({ product, onUploaded }: { product: ProductDetail; onUploaded: () => void }) {
  const photo = useQuery({
    queryKey: ['product-photo', product.id, product.photoId],
    queryFn: () => apiBlobUrl(getDownloadProductPhotoUrl(product.id, product.photoId!)),
    enabled: !!product.photoId,
    staleTime: Infinity,
  })
  const [error, setError] = useState<string | null>(null)
  const send = async (file: File) => {
    try {
      await uploadProductPhoto(product.id, { file })
      notifySuccess('Foto enviada')
      setError(null)
      onUploaded()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar a foto.')
    }
  }
  return (
    <section aria-labelledby="foto" className="space-y-2 rounded-md border border-border bg-card p-4">
      <h2 id="foto" className="text-base font-semibold">Foto</h2>
      {product.photoId && photo.data ? <img src={photo.data} alt={`Foto de ${product.saleName}`} className="aspect-square w-full rounded-md border border-border object-cover" /> : (
        <span className="grid aspect-square w-full place-items-center rounded-md border border-border bg-muted text-muted-foreground">
          {photo.isError ? <ImageOff aria-hidden="true" className="size-8" /> : <Package aria-hidden="true" className="size-8" />}
          <span className="sr-only">{photo.isError ? 'Não foi possível carregar a foto.' : 'Sem foto.'}</span>
        </span>
      )}
      {product.variantOfProductId ? <p className="text-xs text-muted-foreground">A foto vem do produto pai.</p> : (
        <Can perm="catalog.edit">
          <label htmlFor="foto-produto" className="inline-flex min-h-11 cursor-pointer items-center text-sm font-semibold text-primary">Enviar foto</label>
          <input id="foto-produto" type="file" accept=".png,.jpg,.jpeg,.webp" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; if (f) void send(f); e.target.value = '' }} />
          <p className="text-xs text-muted-foreground">PNG, JPG ou WEBP até 5 MB. As variações usam a mesma foto.</p>
        </Can>
      )}
      {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
    </section>
  )
}
