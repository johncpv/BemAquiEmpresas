import { useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { useCallback } from 'react'
import { getListUsersQueryKey, useListUsers } from '@/api/generated/bemaqui'
import type { UserRow } from '@/api/generated/model'
import { useClientPage } from '@/components/data-table/client-page'
import { DataTable } from '@/components/data-table/DataTable'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { InviteDialog } from './InviteDialog'
import { UserRowActions } from './UserRowActions'

const STATUS = { Active: ['ok', 'Ativo'], Invited: ['warn', 'Convite pendente'], Inactive: ['crit', 'Inativo'] } as const
const TWO_FA = { active: ['ok', 'Ativa'], pending: ['info', 'Pendente'], required_pending: ['warn', 'Obrigatória pendente'] } as const

/** Usuários e acessos → Usuários (FND-06c, protótipo): grupos, 2FA, último acesso, situação e o convite. */
export function UsersPage() {
  const client = useQueryClient()
  const users = useListUsers()
  const refresh = useCallback(async () => { await client.invalidateQueries({ queryKey: getListUsersQueryKey() }) }, [client])
  const text = useCallback((u: UserRow) => `${u.name} ${u.email} ${u.groups.map((g) => g.name).join(' ')}`, [])
  const sortValue = useCallback((u: UserRow, field: string) => (field === 'lastLoginAt' ? u.lastLoginAt ?? '' : (u.name || u.email).toLowerCase()), [])
  const query = useClientPage(users, text, sortValue)

  const columns: ColumnDef<UserRow>[] = [
    { id: 'nome', header: 'Usuário', meta: { sortField: 'nome' }, cell: ({ row: { original: u } }) => <span><b className="block font-semibold">{u.name || '—'}</b><span className="text-xs text-muted-foreground">{u.email}</span></span> },
    { id: 'grupos', header: 'Grupos', meta: { secondary: true }, cell: ({ row: { original: u } }) => <span className="flex flex-wrap gap-1">{u.groups.map((g) => <span key={g.id} className="rounded-full bg-primary-soft px-2 py-0.5 text-xs">{g.name}</span>)}</span> },
    { id: '2fa', header: 'Verificação em duas etapas', meta: { secondary: true }, cell: ({ row: { original: u } }) => { const [tone, label] = TWO_FA[u.twoFactor as keyof typeof TWO_FA] ?? TWO_FA.pending; return <StatusChip tone={tone}>{label}</StatusChip> } },
    { id: 'acesso', header: 'Último acesso', meta: { sortField: 'lastLoginAt', secondary: true }, cell: ({ row: { original: u } }) => <span className="tabular-nums">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : '—'}</span> },
    // LGPD-01b: usuário anonimizado fica bloqueado e sem ações, com o pedido que anonimizou.
    { id: 'situacao', header: 'Situação', cell: ({ row: { original: u } }) => { if (u.anonymizedAt) return <span title={`Cadastro anonimizado em ${formatDateTime(u.anonymizedAt)}${u.anonymizationRequest ? ` (pedido ${u.anonymizationRequest})` : ''}`}><StatusChip tone="neutral">Anonimizado</StatusChip></span>; const [tone, label] = STATUS[u.status as keyof typeof STATUS] ?? STATUS.Active; return <StatusChip tone={tone}>{label}</StatusChip> } },
    { id: 'acoes', header: '', cell: ({ row: { original: u } }) => u.anonymizedAt ? null : <Can perm="identity.edit"><UserRowActions user={u} refresh={refresh} /></Can> },
  ]

  return (
    <>
      <PageHeading title="Usuários e acessos: Usuários">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Cada pessoa entra com o próprio acesso. O que ela vê depende dos grupos.</p>
          <Can perm="identity.create"><InviteDialog onInvited={refresh} /></Can>
        </div>
      </PageHeading>
      <DataTable
        label="Usuários"
        columns={columns}
        query={query}
        getRowId={(u) => u.id}
        searchPlaceholder="Buscar por nome, e-mail ou grupo"
        empty={{ title: 'Convide as pessoas da equipe.', description: 'Cada pessoa recebe um e-mail com o link para criar o acesso.' }}
      />
    </>
  )
}
