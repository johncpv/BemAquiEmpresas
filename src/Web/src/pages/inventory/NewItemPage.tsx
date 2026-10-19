import { Link, useNavigate, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { createItem, useGetItem, useListWarrantyDefaults } from '@/api/generated/bemaqui'
import type { ItemDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { UnsavedChangesGuard } from '@/components/form/UnsavedChangesGuard'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { toastDone } from '@/components/ui/toast'
import { PageHeading } from '@/shell/PageHeading'
import { draftErrors, emptyDraft, fromItem, type ItemDraft, toInput } from './item-draft'
import { ItemFields } from './ItemFields'

/** Novo item (CAD-02a CA 6 a 12, molde "cadastro em seções"); com ?duplicar=id, vem com os dados de outro item, sem o código (CA 18). */
export function NewItemPage() {
  const { duplicar } = useSearch({ strict: false }) as { duplicar?: string }
  const source = useGetItem(duplicar ?? '', { query: { enabled: !!duplicar } })
  // A garantia padrão precisa estar carregada antes da escolha do tipo, que a usa como sugestão (CA 12).
  const warranties = useListWarrantyDefaults()
  if ((duplicar && source.isPending) || warranties.isPending) return <Skeleton className="h-96 max-w-3xl" />
  if (warranties.isError) return <ErrorState onRetry={() => warranties.refetch()} />
  if (duplicar && (source.isError || !source.data)) return <ErrorState title="Não foi possível carregar o item a duplicar." onRetry={() => source.refetch()} />
  return <NewItemForm key={duplicar ?? 'novo'} source={duplicar ? source.data : undefined} />
}

function NewItemForm({ source }: { source?: ItemDetail }) {
  const navigate = useNavigate()
  const [initial] = useState<ItemDraft>(() => (source ? fromItem(source, true) : emptyDraft()))
  const [draft, setDraft] = useState(initial)
  const [submitted, setSubmitted] = useState(false)
  const [server, setServer] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const local = draftErrors(draft, true)
  const errors: Partial<Record<string, string>> = { ...(submitted ? local : {}), ...Object.fromEntries(Object.entries(server?.fields ?? {}).map(([k, v]) => [k, v[0]])) }
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial)

  const save = async () => {
    setSubmitted(true)
    if (Object.keys(local).length) return false
    try {
      const saved = await createItem(toInput(draft))
      toastDone('Item salvo')
      await navigate({ to: '/app/estoque/itens/$id', params: { id: saved.id }, search: { novo: '1' }, ignoreBlocker: true } as never)
      return true
    } catch (e) {
      setServer(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o item.', fields: {} })
      return false
    }
  }

  return (
    <>
      <PageHeading title={source ? `Novo item (cópia de ${source.code})` : 'Novo item'}>
        <p className="mt-2 text-muted-foreground">Comece pelo tipo: ele sugere o lote, a série e a garantia.</p>
      </PageHeading>
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="max-w-3xl space-y-4">
        <UnsavedChangesGuard when={dirty} onSave={save} />
        <FormError message={server && Object.keys(server.fields).length === 0 ? server.message : undefined} />
        <ItemFields draft={draft} onChange={(d) => { setServer(null); setDraft(d) }} errors={errors} readOnly={false} active />
        <div className="flex justify-end gap-2">
          <Link to="/app/estoque/itens" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</Link>
          <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar item</button>
        </div>
      </form>
    </>
  )
}
