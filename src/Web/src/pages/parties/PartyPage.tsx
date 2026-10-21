import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { deleteParty, getGetPartyQueryKey, inactivateParty, reactivateParty, useGetParty } from '@/api/generated/bemaqui'
import type { AddressKind, PartyDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { NextSteps } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { ImportedTag } from '@/components/ui/imported-tag'
import { Tag } from '@/components/ui/tag'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { usePartyBusinessVisible } from './party-business'
import { PartyBusinessTab } from './PartyBusinessTab'
import { useNavigation } from '@/navigation/navigation-context'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { ROLE_LABELS } from './labels'
import { PartyAddresses } from './PartyAddresses'
import { PartyContacts } from './PartyContacts'
import { PartyDataForm } from './PartyDataForm'
import { ReceitaUpdate } from './ReceitaUpdate'

/** Ação permitida em parties (o mesmo critério do <Can>), para formulários em modo leitura. */
function useCanParties(action: string) {
  const { all } = useNavigation()
  const module = all.find((m) => m.code === 'parties')
  return !!module && !module.locked && module.actions.includes(action)
}

/** Terceiro (CAD-01a, molde "detalhe com abas"): Dados, Endereços, Contatos e Histórico de alterações. */
export function PartyPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const client = useQueryClient()
  const query = useGetParty(id)
  const canEdit = useCanParties('edit')
  const business = usePartyBusinessVisible()
  const [, setTab] = useUrlTab('dados')
  const [steps, setSteps] = useState(search.novo === '1')
  const [addingAddress, setAddingAddress] = useState<AddressKind | null>(null)
  const [addingContact, setAddingContact] = useState(false)
  const [receita, setReceita] = useState(false)
  const setParty = (p: PartyDetail) => client.setQueryData(getGetPartyQueryKey(id), p)

  if (query.isPending) return <div className="space-y-3"><Skeleton className="h-10 w-2/3" /><Skeleton className="h-64" /></div>
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Terceiro não encontrado.' : 'Não foi possível carregar o terceiro.'} message={notFound ? 'Ele pode ter sido excluído. Volte à lista de terceiros.' : undefined} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const party = query.data
  const setActive = async (active: boolean) => setParty(active ? await reactivateParty(id) : await inactivateParty(id))

  return (
    <>
      <PageHeading title={party.tradeName ? `${party.legalName} (${party.tradeName})` : party.legalName}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <StatusChip tone={party.isActive ? 'ok' : 'neutral'}>{party.isActive ? 'Ativo' : 'Inativo'}</StatusChip>
            {party.roles.map((r) => <Tag key={r}>{ROLE_LABELS[r]}</Tag>)}
            {party.document ? <span className="text-sm text-muted-foreground tabular-nums">{party.document}</span> : <Tag>Sem documento</Tag>}
            <ImportedTag source={party.importSource} />
            <Link to="/app/cadastros/terceiros" className="ml-2 text-sm font-semibold text-primary">Voltar à lista</Link>
          </div>
          <div className="flex flex-wrap gap-2">
            <Can perm="parties.edit">
              {party.kind === 'PJ' && party.document ? (
                <button type="button" onClick={() => setReceita(true)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Atualizar pela Receita</button>
              ) : null}
              {party.isActive ? (
                <ConfirmDialog verb="Inativar" done="Terceiro inativado" title="Inativar este terceiro?" description="Ele some dos seletores e continua nos documentos que já o usam. Dá para reativar."
                  onConfirm={() => setActive(false)} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Inativar terceiro</button>} />
              ) : (
                <ConfirmDialog verb="Reativar" done="Terceiro reativado" title="Reativar este terceiro?" onConfirm={() => setActive(true)}
                  trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Reativar</button>} />
              )}
            </Can>
            <Can perm="parties.delete">
              <ConfirmDialog verb="Excluir" done="Terceiro excluído" destructive title="Excluir este terceiro?" description="Ele some das listas e dos seletores. Dá para restaurar cadastrando o mesmo documento."
                onConfirm={async () => { await deleteParty(id); await navigate({ to: '/app/cadastros/terceiros' }) }}
                recovery={party.isActive ? { verb: 'Inativar', done: 'Terceiro inativado', when: (code) => code === 'party_in_use', onSelect: () => setActive(false) } : undefined}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Excluir terceiro</button>} />
            </Can>
            <Can perm="parties.edit">
              <button type="submit" form="dados-terceiro" onClick={() => setTab('dados')} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar terceiro</button>
            </Can>
          </div>
        </div>
      </PageHeading>

      {steps && canEdit ? (
        <div className="mb-4">
          <NextSteps onDismiss={() => setSteps(false)} steps={[
            { label: 'Adicionar endereço de entrega', onSelect: () => { setTab('enderecos'); setAddingAddress('Delivery') } },
            { label: 'Adicionar contato', onSelect: () => { setTab('contatos'); setAddingContact(true) } },
          ]} />
        </div>
      ) : null}

      <UrlTabs label="Seções do terceiro" fallback="dados" tabs={[
        { id: 'dados', label: 'Dados', keepMounted: true, content: <PartyDataForm key={party.id} party={party} canEdit={canEdit} onSaved={setParty} /> },
        { id: 'enderecos', label: `Endereços (${party.addresses.length})`, content: <PartyAddresses party={party} canEdit={canEdit} onSaved={setParty} adding={addingAddress} onAddingChange={setAddingAddress} /> },
        { id: 'contatos', label: `Contatos (${party.contacts.length})`, content: <PartyContacts party={party} canEdit={canEdit} onSaved={setParty} adding={addingContact} onAddingChange={setAddingContact} /> },
        ...(business ? [{ id: 'comercial-financeiro', label: 'Comercial e financeiro', content: <PartyBusinessTab partyId={party.id} /> }] : []),
        { id: 'historico', label: 'Histórico de alterações', content: <RecordHistory entity="Party" id={party.id} /> },
      ]} />
      {receita ? <ReceitaUpdate party={party} onClose={() => setReceita(false)} onSaved={setParty} /> : null}
    </>
  )
}
