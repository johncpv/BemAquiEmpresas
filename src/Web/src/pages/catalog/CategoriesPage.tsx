import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  createProductCategory, deleteProductCategory, getListProductCategoriesQueryKey, inactivateProductCategory, reactivateProductCategory, updateProductCategory,
  useListProductCategories,
} from '@/api/generated/bemaqui'
import type { ProductCategoryDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { PercentInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { TreeTable, type TreeNode } from '@/components/ui/tree-table'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { formatPercent, toNumber } from './catalog'

type Node = TreeNode & { category: ProductCategoryDetail; children?: Node[] }

const toTree = (all: ProductCategoryDetail[], parent: string | null = null): Node[] =>
  all.filter((c) => (c.parentId ?? null) === parent).map((c) => ({ id: c.id, label: c.name, category: c, children: toTree(all, c.id) }))

const MARGIN_DICA = 'Usada para sugerir o preço quando o custo do item muda. Margem é sobre o preço (30% de margem: custo de R$ 7 vira R$ 10); markup é sobre o custo (30% de markup: R$ 7 vira R$ 9,10).'

/**
 * Catálogo → Categorias (CAT-01 CA 14): árvore simples com ordem e margem ou markup padrão. A "Geral" é a padrão de todo produto e não sai;
 * categoria com produto não é excluída, só desativada.
 */
export function CategoriesPage() {
  const query = useListProductCategories({ includeInactive: true })
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ category: ProductCategoryDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListProductCategoriesQueryKey() })
  const newButton = <Can perm="catalog.create"><button type="button" onClick={() => setEditing({ category: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova categoria</button></Can>
  const all = query.data ?? []

  return (
    <>
      <PageHeading title="Catálogo: Categorias" description="Organize os produtos para o caixa, os relatórios e a margem padrão, como Bebidas › Refrigerantes." actions={newButton} />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState title="Não foi possível carregar as categorias." onRetry={() => query.refetch()} /> : (
        <>
          <TreeTable<Node> label="Categorias de produtos" nodes={toTree(all)}
            renderLabel={(n) => (
              <span className="inline-flex flex-wrap items-center gap-1.5">
                <button type="button" onClick={() => setEditing({ category: n.category })} className="font-semibold text-primary hover:underline">{n.label}</button>
                {n.category.isDefault ? <StatusChip tone="info">Padrão</StatusChip> : null}
              </span>
            )}
            columns={[
              { header: 'Produtos', numeric: true, cell: (n) => n.category.productCount },
              {
                header: 'Margem ou markup', cell: (n) => n.category.defaultMarginPercent != null ? `Margem ${formatPercent(n.category.defaultMarginPercent)}`
                  : n.category.defaultMarkupPercent != null ? `Markup ${formatPercent(n.category.defaultMarkupPercent)}` : '—',
              },
              { header: 'Situação', cell: (n) => <StatusChip tone={n.category.isActive ? 'ok' : 'neutral'}>{n.category.isActive ? 'Ativa' : 'Inativa'}</StatusChip> },
            ]} />
          {all.length <= 1 ? <EmptyState className="mt-4" title="Só a categoria Geral, por enquanto." description="Todo produto novo cai na Geral. Crie categorias como Bebidas ou Mercearia para organizar o caixa e os relatórios." action={newButton} /> : null}
        </>
      )}
      {editing ? <CategoryPanel category={editing.category} all={all} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
    </>
  )
}

/** A própria categoria e as descendentes não podem virar pai dela (sem ciclo). */
function descendants(all: ProductCategoryDetail[], id: string) {
  const out = new Set<string>([id])
  let grew = true
  while (grew) {
    grew = false
    for (const c of all) {
      if (c.parentId && out.has(c.parentId) && !out.has(c.id)) { out.add(c.id); grew = true }
    }
  }
  return out
}

type Rule = 'none' | 'margin' | 'markup'

function CategoryPanel({ category, all, onClose, onSaved }: { category: ProductCategoryDetail | null; all: ProductCategoryDetail[]; onClose: () => void; onSaved: () => void }) {
  const initialRule: Rule = category?.defaultMarginPercent != null ? 'margin' : category?.defaultMarkupPercent != null ? 'markup' : 'none'
  const initial = {
    name: category?.name ?? '', parentId: category?.parentId ?? '', sortOrder: String(category?.sortOrder ?? 0), rule: initialRule,
    percent: category?.defaultMarginPercent != null ? String(category.defaultMarginPercent) : category?.defaultMarkupPercent != null ? String(category.defaultMarkupPercent) : null,
  }
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const canWrite = useCan(category ? 'catalog.edit' : 'catalog.create')
  const blocked = category ? descendants(all, category.id) : new Set<string>()
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)

  const save = async () => {
    if (!draft.name.trim()) { setError('Informe o nome da categoria.'); return false }
    const percent = toNumber(draft.percent)
    const input = {
      name: draft.name.trim(), parentId: draft.parentId || null, sortOrder: Number(draft.sortOrder || 0),
      defaultMarginPercent: draft.rule === 'margin' ? percent : null, defaultMarkupPercent: draft.rule === 'markup' ? percent : null,
    }
    try {
      if (category) await updateProductCategory(category.id, input)
      else await createProductCategory(input)
      notifySuccess('Categoria salva')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar a categoria.')
      return false
    }
  }

  const actions = category && !category.isDefault ? (
    <span className="mr-auto flex flex-wrap gap-2">
      <Can perm="catalog.edit">
        {category.isActive ? (
          <ConfirmDialog verb="Desativar" done="Categoria desativada" title={`Desativar a categoria ${category.name}?`} description="Ela some do formulário do produto e continua nos produtos que já a usam."
            onConfirm={async () => { await inactivateProductCategory(category.id); onSaved() }} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Desativar</button>} />
        ) : <button type="button" onClick={async () => { await reactivateProductCategory(category.id); notifySuccess('Categoria reativada'); onSaved() }} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>}
      </Can>
      <Can perm="catalog.delete">
        <ConfirmDialog verb="Excluir" done="Categoria excluída" destructive title={`Excluir a categoria ${category.name}?`} description="Só dá para excluir categoria sem produtos e sem subcategorias."
          onConfirm={async () => { await deleteProductCategory(category.id); onSaved() }}
          recovery={{ verb: 'Desativar', done: 'Categoria desativada', when: (code) => code === 'category_in_use', onSelect: async () => { await inactivateProductCategory(category.id); onSaved() } }}
          trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold text-crit-ink">Excluir</button>} />
      </Can>
    </span>
  ) : null

  return (
    <SidePanel open onOpenChange={(o) => { if (!o) onClose() }} title={category ? category.name : 'Nova categoria'} dirty={dirty && canWrite}
      onSave={save}
      footer={(
        <>
          {actions}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar categoria</button> : null}
        </>
      )}>
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error ?? undefined} />
        <FormGrid>
          <Field label="Nome" required size="lg">{(p) => <input {...p} value={draft.name} maxLength={80} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
          <Field label="Categoria pai" size="lg" hint="Vazio: categoria principal.">
            {(p) => (
              <select {...p} value={draft.parentId} disabled={category?.isDefault} onChange={(e) => setDraft({ ...draft, parentId: e.target.value })} className={inputClass}>
                <option value="">Nenhuma</option>
                {all.filter((c) => c.isActive && !blocked.has(c.id)).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
            )}
          </Field>
          <Field label="Ordem" size="sm" hint="Menor aparece antes no caixa.">
            {(p) => <input {...p} value={draft.sortOrder} inputMode="numeric" maxLength={4} onChange={(e) => setDraft({ ...draft, sortOrder: e.target.value.replace(/\D/g, '') })} className={inputClass} />}
          </Field>
          <Field label="Preço sugerido por" size="md" dica={MARGIN_DICA}>
            {(p) => (
              <select {...p} value={draft.rule} onChange={(e) => setDraft({ ...draft, rule: e.target.value as Rule })} className={inputClass}>
                <option value="none">Sem padrão</option>
                <option value="margin">Margem padrão</option>
                <option value="markup">Markup padrão</option>
              </select>
            )}
          </Field>
          {draft.rule === 'none' ? null : (
            <Field label={draft.rule === 'margin' ? 'Margem padrão' : 'Markup padrão'} size="sm">
              {(p) => <PercentInput {...p} value={draft.percent} onChange={(v) => setDraft({ ...draft, percent: v })} />}
            </Field>
          )}
        </FormGrid>
      </fieldset>
    </SidePanel>
  )
}
