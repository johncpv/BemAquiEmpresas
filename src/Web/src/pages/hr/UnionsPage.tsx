import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { createUnion, getListUnionsQueryKey, inactivateUnion, reactivateUnion, updateUnion, useListUnions } from '@/api/generated/bemaqui'
import type { UnionDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CnpjInput } from '@/components/form/inputs'
import { SidePanel } from '@/components/ui/side-panel'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'
import { cnpjMask } from '@/lib/masks'
import { Can } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { MONTHS } from './hr'
import { HrTabs } from './HrTabs'

/** RH → Sindicatos (CAD-04a CA 17): nome, CNPJ e mês da data-base. */
export function UnionsPage() {
  const query = useListUnions()
  const client = useQueryClient()
  const [editing, setEditing] = useState<{ union: UnionDetail | null } | null>(null)
  const newButton = <Can perm="hr.create"><button type="button" onClick={() => setEditing({ union: null })} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo sindicato</button></Can>
  return (
    <>
      <PageHeading title="RH: Sindicatos">
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="text-muted-foreground">Os sindicatos das categorias, com o mês da data-base.</p>
          {newButton}
        </div>
      </PageHeading>
      <HrTabs />
      {query.isPending ? <Skeleton className="h-48" /> : query.isError ? <ErrorState onRetry={() => query.refetch()} /> : query.data.length === 0 ? (
        <EmptyState title="Cadastre o sindicato da categoria." description="Ele é escolhido no cadastro do funcionário." action={newButton} />
      ) : (
        <ul className="divide-y divide-border rounded-md border border-border">
          {query.data.map((u) => (
            <li key={u.id} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2 text-sm">
              <button type="button" onClick={() => setEditing({ union: u })} className="font-semibold text-primary hover:underline">{u.name}</button>
              <span className="text-muted-foreground tabular-nums">{u.document ? cnpjMask.format(u.document) : 'Sem CNPJ'} · data-base em {MONTHS[u.baseMonth - 1].toLowerCase()}</span>
              <StatusChip tone={u.isActive ? 'ok' : 'neutral'}>{u.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
            </li>
          ))}
        </ul>
      )}
      {editing ? <UnionPanel union={editing.union} onClose={() => setEditing(null)} onSaved={() => { void client.invalidateQueries({ queryKey: getListUnionsQueryKey() }); setEditing(null) }} /> : null}
    </>
  )
}

function UnionPanel({ union, onClose, onSaved }: { union: UnionDetail | null; onClose: () => void; onSaved: () => void }) {
  const start = { name: union?.name ?? '', document: union?.document ?? '', baseMonth: union?.baseMonth ?? 5 }
  const [draft, setDraft] = useState(start)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const canWrite = useCan(union ? 'hr.edit' : 'hr.create')
  const save = async () => {
    try {
      const input = { ...draft, document: draft.document || null }
      if (union) await updateUnion(union.id, input)
      else await createUnion(input)
      notifySuccess('Sindicato salvo')
      onSaved()
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o sindicato.', fields: {} })
      return false
    }
  }
  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={union ? union.name : 'Novo sindicato'} dirty={canWrite && JSON.stringify(draft) !== JSON.stringify(start)} onSave={save}
      footer={(
        <>
          {union ? (
            <Can perm="hr.edit">
              <button type="button" onClick={async () => { if (union.isActive) await inactivateUnion(union.id); else await reactivateUnion(union.id); notifySuccess(union.isActive ? 'Inativado' : 'Reativado'); onSaved() }} className="mr-auto rounded-md border border-border px-3 py-2 text-sm font-semibold">{union.isActive ? 'Inativar' : 'Reativar'}</button>
            </Can>
          ) : null}
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canWrite ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar sindicato</button> : null}
        </>
      )}
    >
      <fieldset disabled={!canWrite} className="space-y-3">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Nome" required error={error?.fields.name?.[0]}>{(p) => <input {...p} value={draft.name} maxLength={150} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}</Field>
        <Field label="CNPJ" error={error?.fields.document?.[0]}>{(p) => <CnpjInput {...p} value={draft.document} onChange={(document) => setDraft({ ...draft, document })} />}</Field>
        <Field label="Mês da data-base" required error={error?.fields.baseMonth?.[0]} hint="O mês do reajuste da convenção coletiva.">
          {(p) => (
            <select {...p} value={draft.baseMonth} onChange={(e) => setDraft({ ...draft, baseMonth: Number(e.target.value) })} className={inputClass}>
              {MONTHS.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
            </select>
          )}
        </Field>
      </fieldset>
    </SidePanel>
  )
}
