import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListNumberFormatsQueryKey, useListNumberFormats } from '@/api/generated/bemaqui'
import type { NumberFormatRow } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { useCanSettings } from '../use-can-settings'
import { NumberingPanel } from './NumberingPanel'

/**
 * Configurações → Numeração (CAD-06 CA 9 a 13): cada tipo de documento com o formato e a prévia do próximo número; a edição
 * fica no painel lateral. Sem settings.edit, o painel abre só para leitura.
 */
export function NumberingPage() {
  const query = useListNumberFormats()
  const canEdit = useCanSettings('edit')
  const client = useQueryClient()
  const [editing, setEditing] = useState<NumberFormatRow | null>(null)
  const rows = query.data ?? []
  const noneIssued = rows.length > 0 && rows.every((r) => !r.hasIssued)

  return (
    <>
      <PageHeading title="Configurações: Numeração" />
      <SettingsTabs />
      {query.isPending ? <Skeleton className="h-96" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : (
        <div className="space-y-3">
          {noneIssued ? <p role="status" className="rounded-md border border-border bg-card px-3 py-2 text-sm">Nenhum documento emitido ainda. O primeiro sairá como <b className="tabular-nums">{rows[0].next}</b>.</p> : null}
          <div className="overflow-x-auto rounded-md border border-border">
            <table className="w-full text-sm">
              <caption className="sr-only">Numeração dos documentos</caption>
              <thead className="bg-muted text-left text-xs text-muted-foreground">
                <tr>
                  <th scope="col" className="px-3 py-2">Tipo</th>
                  <th scope="col" className="px-3 py-2">Prefixo</th>
                  <th scope="col" className="hidden px-3 py-2 sm:table-cell">Reinicia por ano</th>
                  <th scope="col" className="hidden px-3 py-2 text-right sm:table-cell">Dígitos</th>
                  <th scope="col" className="hidden px-3 py-2 text-right md:table-cell">Último emitido</th>
                  <th scope="col" className="px-3 py-2">Próximo</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.docType} className="border-t border-border">
                    <td className="px-3 py-2">
                      <button type="button" onClick={() => setEditing(r)} className="font-semibold text-primary hover:underline">{r.label}</button>
                      {r.customized ? <span className="ml-1.5"><StatusChip tone="info">Alterado</StatusChip></span> : null}
                    </td>
                    <td className="px-3 py-2 font-mono">{r.prefix}</td>
                    <td className="hidden px-3 py-2 sm:table-cell">{r.yearly ? 'Sim' : 'Não'}</td>
                    <td className="hidden px-3 py-2 text-right tabular-nums sm:table-cell">{r.digits}</td>
                    <td className="hidden px-3 py-2 text-right tabular-nums md:table-cell">{r.lastIssued || '—'}</td>
                    <td className="px-3 py-2 font-mono tabular-nums">{r.next}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {editing ? (
        <NumberingPanel row={editing} canEdit={canEdit} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await client.invalidateQueries({ queryKey: getListNumberFormatsQueryKey() }) }} />
      ) : null}
    </>
  )
}
