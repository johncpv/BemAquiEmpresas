import type { ColumnDef } from '@tanstack/react-table'
import { useListImportRuns } from '@/api/generated/bemaqui'
import type { ImportRunRow } from '@/api/generated/model'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableSearch } from '@/components/data-table/table-search'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { useHelp } from '@/help/help-context'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { entityLabel, MODE_LABELS, STATUS } from './labels'

/**
 * Configurações → Importação do Dolibarr (MIG-01a CA 19, D4): só leitura. A carga é feita pela CLI; aqui ficam as execuções,
 * com o detalhe de totais e pendências. Não há botão de executar.
 */
export function ImportRunsPage() {
  const [search] = useTableSearch()
  const help = useHelp()
  const query = useListImportRuns({ page: search.page, pageSize: search.pageSize })

  const columns: ColumnDef<ImportRunRow>[] = [
    { id: 'startedAt', header: 'Início', cell: ({ row: { original: r } }) => <span className="tabular-nums">{formatDateTime(r.startedAt)}</span> },
    { id: 'mode', header: 'Modo', cell: ({ row: { original: r } }) => <Tag>{MODE_LABELS[r.mode] ?? r.mode}</Tag> },
    { id: 'entities', header: 'Entidades', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.entities.split(',').filter(Boolean).map(entityLabel).join(', ') || '—' },
    { id: 'operator', header: 'Operador', meta: { secondary: true }, cell: ({ row: { original: r } }) => r.operator },
    {
      id: 'status', header: 'Situação', cell: ({ row: { original: r } }) => (
        <span className="flex flex-wrap gap-1">
          <StatusChip tone={STATUS[r.status]?.tone ?? 'neutral'}>{STATUS[r.status]?.label ?? r.status}</StatusChip>
          {r.purged ? <Tag>Staging expurgada</Tag> : null}
        </span>
      ),
    },
  ]

  return (
    <>
      <PageHeading title="Configurações: Importação do Dolibarr">
        <p className="mt-2 text-muted-foreground">Execuções da importação dos cadastros do Dolibarr, com o que entrou, o que ficou de fora e por quê.</p>
      </PageHeading>
      <SettingsTabs />
      <DataTable
        label="Execuções da importação"
        columns={columns}
        query={query}
        getRowId={(r) => r.id}
        rowHref={(r) => `/app/configuracoes/importacao/${r.id}`}
        searchable={false}
        empty={{
          title: 'Nenhuma importação feita.',
          description: 'A carga é feita pela CLI; veja o passo a passo na ajuda.',
          action: <button type="button" onClick={() => help.setOpen(true)} className="text-sm font-semibold text-primary">Ver o passo a passo</button>,
        }}
      />
    </>
  )
}
