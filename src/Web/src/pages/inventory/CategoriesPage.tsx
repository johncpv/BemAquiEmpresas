import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListItemCategoriesQueryKey, useListItemCategories } from '@/api/generated/bemaqui'
import type { CategoryDetail } from '@/api/generated/model'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { TreeTable, type TreeNode } from '@/components/ui/tree-table'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { CategoryPanel } from './CategoryPanel'
import { InventoryTabs } from './InventoryTabs'

type Node = TreeNode & { category: CategoryDetail; children?: Node[] }

const toTree = (all: CategoryDetail[], parent: string | null = null): Node[] =>
  all.filter((c) => (c.parentId ?? null) === parent).map((c) => ({ id: c.id, label: c.name, category: c, children: toTree(all, c.id) }))

/** Estoque → Categorias (CAD-02a CA 3): árvore com nome único entre as irmãs; edição no painel lateral. */
export function CategoriesPage() {
  const query = useListItemCategories()
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ category: CategoryDetail | null } | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: getListItemCategoriesQueryKey() })
  const newButton = <Can perm="inventory.create"><button type="button" onClick={() => setEditing({ category: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova categoria</button></Can>

  return (
    <>
      <PageHeading title="Estoque: Categorias">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Agrupe os itens para achar e analisar mais rápido, como Chapas › Aço carbono.</p>
          {newButton}
        </div>
      </PageHeading>
      <InventoryTabs />
      {query.isPending ? <Skeleton className="h-64" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Nenhuma categoria cadastrada." description="As categorias são opcionais e podem ter subcategorias." action={newButton} />
      ) : (
        <TreeTable<Node> label="Categorias de itens" nodes={toTree(query.data)}
          renderLabel={(n) => <button type="button" onClick={() => setEditing({ category: n.category })} className="font-semibold text-primary hover:underline">{n.label}</button>}
          columns={[
            { header: 'Itens', numeric: true, cell: (n) => n.category.itemCount },
            { header: 'Situação', cell: (n) => <StatusChip tone={n.category.isActive ? 'ok' : 'neutral'}>{n.category.isActive ? 'Ativa' : 'Inativa'}</StatusChip> },
          ]} />
      )}
      {editing ? <CategoryPanel category={editing.category} all={query.data ?? []} onClose={() => setEditing(null)} onSaved={() => { refresh(); setEditing(null) }} /> : null}
    </>
  )
}
