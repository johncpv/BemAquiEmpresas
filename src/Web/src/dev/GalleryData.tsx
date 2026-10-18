import type { ColumnDef } from '@tanstack/react-table'
import { useMemo, useState } from 'react'
import { ApiError } from '@/api/http'
import { DataTable, type Page, type TableQuery } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { z } from '@/lib/zod'

interface Projeto {
  id: string
  numero: string
  cliente: string
  valor: number
  situacao: 'ok' | 'warn' | 'crit'
}

const CLIENTES = ['Edifício Aurora', 'Condomínio Mirante', 'Hotel Atlântico', 'Clínica Vida', 'Shopping Leste', 'Residencial Serra']
const ROWS: Projeto[] = Array.from({ length: 57 }, (_, i) => ({
  id: String(i + 1),
  numero: `PRJ-2026-${String(i + 1).padStart(4, '0')}`,
  cliente: CLIENTES[i % CLIENTES.length],
  valor: 48_000 + ((i * 7919) % 90_000),
  situacao: (['ok', 'warn', 'crit'] as const)[i % 3],
}))

const LABEL = { ok: 'Em dia', warn: 'Em risco', crit: 'Atrasado' } as const

/** "Servidor" em memória, com a mesma semântica do PageRequest (página, ordenação "campo"/"-campo" e busca). */
function useFakeServer(): TableQuery<Projeto> {
  const [search] = useTableSearch()
  return useMemo(() => {
    let rows = ROWS.filter((r) => !search.q || `${r.numero} ${r.cliente}`.toLowerCase().includes(search.q.toLowerCase()))
    if (search.sort) {
      const desc = search.sort.startsWith('-')
      const field = search.sort.replace('-', '') as keyof Projeto
      rows = [...rows].sort((a, b) => (a[field] < b[field] ? -1 : a[field] > b[field] ? 1 : 0) * (desc ? -1 : 1))
    }

    const data: Page<Projeto> = { items: rows.slice((search.page - 1) * search.pageSize, search.page * search.pageSize), page: search.page, pageSize: search.pageSize, total: rows.length }
    return { data, isPending: false, isError: false, refetch: () => {} }
  }, [search])
}

const columns: ColumnDef<Projeto>[] = [
  { accessorKey: 'numero', header: 'Projeto', meta: { sortField: 'numero' } },
  { accessorKey: 'cliente', header: 'Cliente', meta: { sortField: 'cliente', secondary: true } },
  { accessorKey: 'valor', header: 'Valor', meta: { sortField: 'valor', numeric: true }, cell: (c) => <Money value={c.getValue<number>()} cents={false} /> },
  { accessorKey: 'situacao', header: 'Situação', cell: (c) => <StatusChip tone={c.getValue<Projeto['situacao']>()}>{LABEL[c.getValue<Projeto['situacao']>()]}</StatusChip> },
]

export function GalleryTable() {
  const query = useFakeServer()
  return <DataTable label="Projetos de exemplo" columns={columns} query={query} searchPlaceholder="Buscar projeto ou cliente" empty={{ title: 'Nenhum projeto ainda.' }} />
}

const schema = z.object({ nome: z.string().min(3), email: z.email() })

export function GalleryForm() {
  const form = useZodForm(schema, { defaultValues: { nome: '', email: '' } })
  const [sent, setSent] = useState(false)
  const submit = form.handleSubmit(async () => {
    // Simula ProblemDetails do servidor com erro de campo e erro geral (FND-09c CA 9).
    applyServerErrors(form, new ApiError(422, { detail: 'Já existe um usuário com esse e-mail.', code: 'user_exists', errors: { email: ['Já existe um usuário com esse e-mail.'] } }))
    setSent(true)
  })
  const { errors } = form.formState
  return (
    <form onSubmit={submit} noValidate className="max-w-sm space-y-3">
      <FormError message={errors.root?.server?.message} />
      <Field label="Nome" error={errors.nome?.message}>{(p) => <input {...p} {...form.register('nome')} className={inputClass} />}</Field>
      <Field label="E-mail" error={errors.email?.message}>{(p) => <input {...p} type="email" {...form.register('email')} className={inputClass} />}</Field>
      <button type="submit" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Enviar convite</button>
      {sent ? <p className="text-xs text-muted-foreground">O servidor respondeu com erro de campo (simulado).</p> : null}
    </form>
  )
}

export function GalleryStates() {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <EmptyState title="Nenhum usuário convidado." description="Convide as pessoas da equipe para começar." action={<button type="button" className="text-sm font-semibold text-primary">Convidar usuário</button>} />
      <ErrorState onRetry={() => {}} />
      <ConfirmDialog
        verb="Desativar"
        done="Desativado"
        title="Desativar Ana Souza?"
        description="Ela perde o acesso no próximo clique. O histórico continua com o nome dela."
        destructive
        onConfirm={() => new Promise((r) => setTimeout(r, 300))}
        trigger={<button type="button" className="rounded-md bg-crit px-3 py-2 text-sm font-semibold text-critical-foreground">Desativar usuário</button>}
      />
      <ul className="text-sm text-muted-foreground">
        {describeChanges('{"name":{"old":"Comercial","new":"Vendas"},"passwordHash":{"changed":true}}').map((l) => <li key={l}>{l}</li>)}
      </ul>
    </div>
  )
}
