import type { ColumnDef } from '@tanstack/react-table'
import { useCallback, useMemo, useState } from 'react'
import { useClientPage } from '@/components/data-table/client-page'
import { DataTable } from '@/components/data-table/DataTable'
import { useTableFilters } from '@/components/data-table/table-search'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'
import { PageHeading } from '@/shell/PageHeading'
import { MoldNav } from './MoldNav'
import { type Unit, UNITS } from './sample'

const empty: Unit = { id: '', code: '', name: '', decimals: 0, active: true }

/**
 * Molde 3 — painel lateral (FND-09d CA 2, 19 e 21): cadastro simples (até ~6 campos) editado sem sair da lista;
 * filtros na URL com "Mostrar inativos"; fechar com alteração não salva pergunta "Salvar" ou "Descartar".
 */
export default function PanelMold() {
  const [units, setUnits] = useState(UNITS)
  const [editing, setEditing] = useState<Unit | null>(null)
  const [draft, setDraft] = useState<Unit>(empty)
  const [tried, setTried] = useState(false)
  const { inativos } = useTableFilters([])
  const visible = useMemo(() => units.filter((u) => inativos === 'true' || u.active), [units, inativos])
  const text = useCallback((u: Unit) => `${u.code} ${u.name}`, [])
  const sort = useCallback((u: Unit, field: string) => (field === 'code' ? u.code : u.name), [])
  const query = useClientPage({ data: visible, isPending: false, isError: false, refetch: () => undefined }, text, sort)

  const open = (u: Unit) => { setEditing(u); setDraft(u); setTried(false) }
  const dirty = editing !== null && JSON.stringify(editing) !== JSON.stringify(draft)
  const invalid = { code: !/^[A-Z0-9]{1,6}$/.test(draft.code), name: draft.name.trim().length < 2 }
  const save = async () => {
    setTried(true)
    if (invalid.code || invalid.name) return false
    setUnits((list) => (draft.id ? list.map((u) => (u.id === draft.id ? draft : u)) : [...list, { ...draft, id: draft.code.toLowerCase() }]))
    setEditing(null)
    notifySuccess('Unidade salva')
    return true
  }

  const columns: ColumnDef<Unit>[] = [
    { id: 'code', header: 'Sigla', meta: { sortField: 'code' }, cell: ({ row: { original: u } }) => <button type="button" onClick={() => open(u)} className="font-semibold text-primary hover:underline">{u.code}</button> },
    { id: 'name', header: 'Nome', meta: { sortField: 'name' }, cell: ({ row: { original: u } }) => u.name },
    { id: 'decimals', header: 'Casas decimais', meta: { numeric: true, secondary: true }, cell: ({ row: { original: u } }) => u.decimals },
    { id: 'status', header: 'Situação', cell: ({ row: { original: u } }) => <StatusChip tone={u.active ? 'ok' : 'neutral'}>{u.active ? 'Ativa' : 'Inativa'}</StatusChip> },
  ]

  return (
    <>
      <MoldNav current="painel" />
      <PageHeading title="Unidades de medida">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Unidades usadas nos itens e nas conversões. O cadastro é simples, então abre num painel ao lado.</p>
          <button type="button" onClick={() => open(empty)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Nova unidade</button>
        </div>
      </PageHeading>
      <DataTable label="Unidades" columns={columns} query={query} getRowId={(u) => u.id} inactiveFilter searchPlaceholder="Buscar por sigla ou nome"
        empty={{ title: 'Cadastre a primeira unidade.', description: 'UN, KG, M e M2 já vêm prontas; crie outras quando precisar.' }} />

      <SidePanel
        open={editing !== null}
        onOpenChange={(o) => !o && setEditing(null)}
        title={draft.id ? `Unidade ${editing?.code}` : 'Nova unidade'}
        dirty={dirty}
        onSave={save}
        footer={(
          <>
            {draft.id && draft.active ? (
              <ConfirmDialog verb="Inativar" done="Unidade inativada" title={`Inativar a unidade ${draft.code}?`} description="Ela some dos seletores e continua nos itens que já a usam."
                onConfirm={async () => { setUnits((l) => l.map((u) => (u.id === draft.id ? { ...u, active: false } : u))); setEditing(null) }}
                trigger={<button type="button" className="mr-auto rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar</button>} />
            ) : null}
            <button type="button" onClick={() => setEditing(null)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
            <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar unidade</button>
          </>
        )}
      >
        <div className="space-y-3">
          <Field label="Sigla" required error={tried && invalid.code ? 'De 1 a 6 letras ou números, sem espaço.' : undefined} hint="Ex.: UN, KG, M2.">
            {(p) => <input {...p} value={draft.code} onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase() })} maxLength={6} className={inputClass} />}
          </Field>
          <Field label="Nome" required error={tried && invalid.name ? 'Informe o nome da unidade.' : undefined}>
            {(p) => <input {...p} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
          </Field>
          <Field label="Casas decimais" dica="Quantas casas a quantidade aceita nesta unidade. UN não aceita fração; KG aceita até 3.">
            {(p) => (
              <select {...p} value={draft.decimals} onChange={(e) => setDraft({ ...draft, decimals: Number(e.target.value) })} className={inputClass}>
                {[0, 1, 2, 3, 4].map((n) => <option key={n} value={n}>{n}</option>)}
              </select>
            )}
          </Field>
          <p className="text-xs text-muted-foreground">Mude algum campo e tente fechar o painel (Esc, clique fora ou o X): a guarda pergunta se quer salvar ou descartar.</p>
        </div>
      </SidePanel>
    </>
  )
}
