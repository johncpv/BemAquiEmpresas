import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { addProductBarcode, createProductVariants, removeProductBarcode, setProductPrice, useListProductPriceHistory } from '@/api/generated/bemaqui'
import type { ProductDetail } from '@/api/generated/model'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput, QuantityInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatMoney } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { combinationCount, describeAttributes, describePack, DICAS, gtinError, normalizeGtin, parseValues, PRODUCTS, toNumber } from './catalog'

type Refresh = (product: ProductDetail) => void

/**
 * Preço (CA 8, 9 e 16): o vigente na tabela Balcão, "Alterar preço" só com catalog.edit_price (no pai de grade, pode valer para todas as
 * variações) e o histórico com o anterior, o novo, a versão da tabela, quem e quando.
 */
export function PriceSection({ product, onChange, open, onOpenChange, initial }: {
  product: ProductDetail; onChange: Refresh; open: boolean; onOpenChange: (open: boolean) => void; initial: number | null
}) {
  const history = useListProductPriceHistory(product.id)
  const [price, setPrice] = useState<string | null>(null)
  const [all, setAll] = useState(true)
  const value = toNumber(price)
  return (
    <section aria-labelledby="preco" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="preco" className="flex items-center text-base font-semibold">Preço<Dica nome="Alterar preço" texto={DICAS.edicao} /></h2>
        <Can perm="catalog.edit_price">
          <ConfirmDialog verb="Alterar preço" done="Preço alterado" title={`Alterar o preço de ${product.saleName}`} open={open} onOpenChange={onOpenChange}
            description={`Tabela ${product.priceListName}. A versão da tabela sobe e o histórico guarda o preço anterior, quem mudou e quando.`}
            onOpen={() => { setPrice(initial != null ? String(initial) : product.price != null ? String(product.price) : null); setAll(true) }}
            preconditions={[{ ok: value !== null && value > 0, text: 'Informe um preço maior que zero.' }]}
            onConfirm={async () => {
              onChange(await setProductPrice(product.id, { price: value!, priceListId: null, applyToVariants: product.hasVariants && all }))
              void history.refetch()
            }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Alterar preço</button>}>
            <FormGrid>
              <Field label="Preço de venda" size="sm">{(p) => <MoneyInput {...p} value={price} onChange={setPrice} />}</Field>
            </FormGrid>
            {product.hasVariants ? (
              <label className="mt-3 inline-flex min-h-11 items-center gap-2 text-sm"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} />Aplicar a todas as variações</label>
            ) : null}
          </ConfirmDialog>
        </Can>
      </div>
      <p className="text-2xl font-semibold tabular-nums">{product.price == null ? <StatusChip tone="warn">Sem preço</StatusChip> : formatMoney(product.price)}</p>
      <p className="text-xs text-muted-foreground">Tabela {product.priceListName}, versão {product.priceListVersion}.{product.price == null ? ' Sem preço, o produto não vende no caixa.' : ''}</p>
      <h3 className="text-sm font-semibold">Histórico do preço</h3>
      {history.isPending ? <Skeleton className="h-12" /> : history.isError ? <ErrorState title="Não foi possível carregar o histórico do preço." onRetry={() => history.refetch()} /> : (history.data ?? []).length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhuma mudança de preço ainda.</p>
      ) : (
        <ol className="space-y-1 text-sm">
          {(history.data ?? []).map((c) => (
            <li key={c.id} className="flex flex-wrap gap-x-2">
              <span className="tabular-nums">{c.previousPrice == null ? 'Sem preço' : formatMoney(c.previousPrice)} → <b>{formatMoney(c.newPrice)}</b></span>
              <span className="text-muted-foreground">· versão {c.listVersion} · por {c.changedByName ?? 'sistema'} em <time dateTime={c.changedAt} className="tabular-nums">{formatDateTime(c.changedAt)}</time></span>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

/** Códigos de barras (CA 5 a 7): vários por produto, cada um com a quantidade da embalagem; o GTIN é conferido enquanto digita. */
export function BarcodesSection({ product, onChange }: { product: ProductDetail; onChange: () => void }) {
  const [gtin, setGtin] = useState('')
  const [pack, setPack] = useState<string | null>('1')
  const [tried, setTried] = useState(false)
  if (product.hasVariants) {
    return (
      <section aria-labelledby="codigos" className="space-y-2">
        <h2 id="codigos" className="text-base font-semibold">Códigos de barras</h2>
        <p className="text-sm text-muted-foreground">Este produto tem variações: cada código de barras vai na sua variação.</p>
      </section>
    )
  }

  const error = gtinError(gtin) ?? (tried && !normalizeGtin(gtin) ? 'Informe o código de barras.' : undefined)
  return (
    <section aria-labelledby="codigos" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="codigos" className="text-base font-semibold">Códigos de barras</h2>
        <Can perm="catalog.edit">
          <ConfirmDialog verb="Adicionar código" done="Código adicionado" title="Adicionar código de barras"
            description="Leia com o leitor ou digite. A caixa ou o fardo têm código próprio: informe quantas unidades vêm nele."
            onOpen={() => { setGtin(''); setPack('1'); setTried(false) }}
            preconditions={[{ ok: !!normalizeGtin(gtin) && !gtinError(gtin), text: 'Informe um código de barras válido.' }, { ok: (toNumber(pack) ?? 0) > 0, text: 'A quantidade da embalagem é maior que zero.' }]}
            onConfirm={async () => { setTried(true); await addProductBarcode(product.id, { gtin: normalizeGtin(gtin), packQuantity: toNumber(pack) ?? 1 }); onChange() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Adicionar código</button>}>
            <FormGrid>
              <Field label="Código de barras (GTIN)" size="md" error={error}>
                {(p) => <input {...p} value={gtin} inputMode="numeric" maxLength={18} onChange={(e) => setGtin(e.target.value)} className={inputClass} />}
              </Field>
              <Field label="Quantidade da embalagem" size="sm" dica={DICAS.pack}>{(p) => <QuantityInput {...p} value={pack} onChange={setPack} />}</Field>
            </FormGrid>
          </ConfirmDialog>
        </Can>
      </div>
      {product.barcodes.length === 0 ? (
        <EmptyState title="Sem código de barras." description="Sem código, o caixa acha o produto pelo nome ou pelo PLU. Adicione o código da etiqueta." />
      ) : (
        <ul aria-label="Códigos de barras" className="divide-y divide-border rounded-md border border-border">
          {product.barcodes.map((b) => (
            <li key={b.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <span className="tabular-nums">{b.gtin}</span>
              <span className="text-muted-foreground">{describePack(b.packQuantity)}</span>
              <Can perm="catalog.edit">
                <ConfirmDialog verb="Remover" done="Código removido" destructive title={`Remover o código ${b.gtin}?`} description="O código fica livre para outro produto."
                  onConfirm={async () => { await removeProductBarcode(product.id, b.id); onChange() }}
                  trigger={<button type="button" className="text-sm font-semibold text-crit-ink">Remover</button>} />
              </Can>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

interface AttributeDraft { name: string; values: string }

/**
 * Variações (CA 15 e 16): a grade do pai (cada combinação com item, código e preço próprios) e "Gerar grade", com a prévia de quantas
 * variações saem. Nome, categoria e foto vêm do pai.
 */
export function VariantsSection({ product, onChange, canPrice }: { product: ProductDetail; onChange: (p: ProductDetail) => void; canPrice: boolean }) {
  const [attributes, setAttributes] = useState<AttributeDraft[]>([{ name: 'Cor', values: '' }, { name: 'Tamanho', values: '' }])
  const [price, setPrice] = useState<string | null>(null)
  if (product.variantOfProductId) {
    return (
      <section aria-labelledby="variacoes" className="space-y-2">
        <h2 id="variacoes" className="text-base font-semibold">Variação</h2>
        <p className="text-sm">{describeAttributes(product.variantAttributes)} de{' '}
          <Link to="/app/catalogo/produtos/$id" params={{ id: product.variantOfProductId }} className="font-semibold text-primary">{product.parentName}</Link>.
          Nome, categoria e foto vêm do produto pai.</p>
      </section>
    )
  }

  const parsed = attributes.map((a) => ({ name: a.name.trim(), values: parseValues(a.values) })).filter((a) => a.name && a.values.length > 0)
  const count = combinationCount(parsed)
  return (
    <section aria-labelledby="variacoes" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="variacoes" className="flex items-center text-base font-semibold">Variações<Dica nome="Variação" texto={DICAS.variacao} /></h2>
        <Can perm="catalog.create">
          <ConfirmDialog verb="Gerar variações" done="Variações geradas" size="lg" title="Gerar grade"
            description="Cada combinação vira uma variação com item no estoque, código de barras e preço próprios. Combinações que já existem ficam como estão."
            onOpen={() => { setPrice(null); setAttributes([{ name: 'Cor', values: '' }, { name: 'Tamanho', values: '' }]) }}
            preconditions={[{ ok: count > 0, text: 'Informe ao menos um atributo com valores (ex.: Cor: Azul, Preto).' }, { ok: count <= 200, text: 'A grade passa de 200 variações. Divida em mais de um produto.' }]}
            onConfirm={async () => onChange(await createProductVariants(product.id, { attributes: parsed, price: canPrice ? toNumber(price) : null }))}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-1.5 text-sm font-semibold">Gerar grade</button>}>
            <div className="space-y-3">
              {attributes.map((a, i) => (
                <FormGrid key={i}>
                  <Field label={`Atributo ${i + 1}`} size="sm">
                    {(p) => <input {...p} value={a.name} maxLength={30} onChange={(e) => setAttributes(attributes.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} className={inputClass} />}
                  </Field>
                  <Field label={`Valores de ${a.name || `atributo ${i + 1}`}`} size="lg" hint="Separe por vírgula. Ex.: P, M, G, GG.">
                    {(p) => <input {...p} value={a.values} onChange={(e) => setAttributes(attributes.map((x, j) => (j === i ? { ...x, values: e.target.value } : x)))} className={inputClass} />}
                  </Field>
                </FormGrid>
              ))}
              {attributes.length < 3 ? (
                <button type="button" onClick={() => setAttributes([...attributes, { name: '', values: '' }])} className="text-sm font-semibold text-primary">Adicionar atributo</button>
              ) : null}
              {canPrice ? (
                <FormGrid>
                  <Field label="Preço das variações" size="sm" hint={product.price == null ? 'Vazio: ficam sem preço.' : 'Vazio: usam o preço do produto pai.'}>
                    {(p) => <MoneyInput {...p} value={price} onChange={setPrice} />}
                  </Field>
                </FormGrid>
              ) : null}
              <p role="status" className="text-sm font-semibold">{count === 0 ? 'Nenhuma variação ainda.' : count === 1 ? 'Sai 1 variação.' : `Saem ${count} variações.`}</p>
            </div>
          </ConfirmDialog>
        </Can>
      </div>
      {product.variants.length === 0 ? (
        <p className="text-sm text-muted-foreground">Sem variações. Use "Gerar grade" para vender o mesmo produto em cores, tamanhos ou sabores.</p>
      ) : (
        <div className="overflow-x-auto rounded-md border border-border">
          <table className="w-full text-sm">
            <thead className="bg-muted text-left"><tr><th className="px-3 py-2">Variação</th><th className="hidden px-3 py-2 sm:table-cell">Código de barras</th><th className="px-3 py-2 text-right">Preço</th><th className="hidden px-3 py-2 sm:table-cell">Situação</th></tr></thead>
            <tbody>
              {product.variants.map((v) => (
                <tr key={v.id} className="border-t border-border">
                  <td className="px-3 py-2"><Link to="/app/catalogo/produtos/$id" params={{ id: v.id }} className="font-semibold text-primary">{describeAttributes(v.attributes) || v.saleName}</Link></td>
                  <td className="hidden px-3 py-2 tabular-nums sm:table-cell">{v.firstGtin ?? '—'}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{v.price == null ? <StatusChip tone="warn">Sem preço</StatusChip> : formatMoney(v.price)}</td>
                  <td className="hidden px-3 py-2 sm:table-cell"><StatusChip tone={v.isActive ? 'ok' : 'neutral'}>{v.isActive ? 'Ativa' : 'Inativa'}</StatusChip></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="text-xs text-muted-foreground">A grade aparece agrupada na lista de <Link to={PRODUCTS as never} className="font-semibold text-primary">produtos</Link>; o caixa acha cada variação pelo código dela.</p>
    </section>
  )
}
