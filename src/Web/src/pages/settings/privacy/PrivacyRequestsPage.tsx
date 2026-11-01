import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from '@tanstack/react-router'
import { ChevronRight, Plus, ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import { getGetPrivacySettingsQueryKey, getListPrivacyRequestsQueryKey, savePrivacySettings, useGetPrivacySettings, useListPrivacyRequests } from '@/api/generated/bemaqui'
import type { ListPrivacyRequestsParams } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { dueChipOf, kindLabel, KINDS, statusOf, subjectTypeLabel } from './privacy'
import { RegisterPrivacyRequestPanel } from './RegisterPrivacyRequestPanel'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Tela de acesso negado de quem não é do Proprietário (CA 2): a API responde 403 e a aba nem aparece no menu. */
export function OwnerOnlyNotice() {
  return (
    <EmptyState title="Só o Proprietário atende os pedidos do titular."
      description="A empresa é a controladora dos dados pessoais (LGPD). Peça a um membro do grupo Proprietário para registrar ou responder o pedido." />
  )
}

/**
 * Configurações → Privacidade (LGPD-01a, /app/configuracoes/privacidade): os pedidos do titular (número, titular abreviado, tipo, prazo e
 * situação), com filtros, "Registrar pedido" e o prazo de resposta editável. Só o Proprietário. A 400 px, a lista vira cartões.
 */
export function PrivacyRequestsPage() {
  const { isOwner } = useNavigation()
  const client = useQueryClient()
  const navigate = useNavigate()
  const [filters, setFilters] = useState<ListPrivacyRequestsParams>({ pageSize: 100 })
  const [registering, setRegistering] = useState(false)
  const list = useListPrivacyRequests(filters, { query: { enabled: isOwner, retry: false } })

  if (!isOwner || (list.error instanceof ApiError && list.error.code === 'forbidden')) {
    return (
      <>
        <PageHeading title="Privacidade (LGPD)" />
        <SettingsTabs />
        <OwnerOnlyNotice />
      </>
    )
  }

  const items = list.data?.items ?? []
  const register = (
    <button type="button" onClick={() => setRegistering(true)} className="inline-flex min-h-11 items-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">
      <Plus aria-hidden="true" className="size-4" /> Registrar pedido
    </button>
  )
  return (
    <>
      <PageHeading title="Privacidade (LGPD)">
        <p className="mt-2 max-w-3xl text-muted-foreground">
          Registre cada pedido que uma pessoa faz sobre os dados dela e responda dentro do prazo.
          <span className="ml-1 inline-flex align-middle"><Dica nome="Controlador" texto="A empresa é a controladora dos dados pessoais e responde ao titular; o BemAqui é o operador. Por isso só o Proprietário atende os pedidos." /></span>
        </p>
      </PageHeading>
      <SettingsTabs />
      <div className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <label className="text-sm font-medium">
            Situação
            <select value={filters.status ?? ''} onChange={(e) => setFilters({ ...filters, status: (e.target.value || undefined) as ListPrivacyRequestsParams['status'] })} className={inputClass + ' mt-1 min-h-11'}>
              <option value="">Todas</option>
              <option value="Open">Abertos</option>
              <option value="Done">Concluídos</option>
              <option value="Refused">Recusados</option>
            </select>
          </label>
          <label className="text-sm font-medium">
            Tipo
            <select value={filters.kind ?? ''} onChange={(e) => setFilters({ ...filters, kind: (e.target.value || undefined) as ListPrivacyRequestsParams['kind'] })} className={inputClass + ' mt-1 min-h-11'}>
              <option value="">Todos</option>
              {KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}
            </select>
          </label>
          <div className="ml-auto flex flex-wrap gap-2"><Link to={'/app/configuracoes/privacidade/retencao' as never} className={button}>Retenção dos dados</Link>{register}</div>
        </div>
        {list.isPending ? (
          <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-16" />)}</div>
        ) : list.isError ? (
          <ErrorState message={list.error instanceof ApiError ? list.error.message : undefined} onRetry={() => void list.refetch()} />
        ) : items.length === 0 ? (
          <EmptyState title="Nenhum pedido de titular registrado."
            description="Registre aqui cada pedido recebido por e-mail, telefone ou pessoalmente."
            action={register} />
        ) : (
          <ul aria-label="Pedidos do titular" className="space-y-2">
            {items.map((row) => {
              const status = statusOf(row.status)
              const due = dueChipOf(row.dueChip)
              return (
                <li key={row.id}>
                  <Link to="/app/configuracoes/privacidade/$id" params={{ id: row.id }}
                    className={'flex min-h-16 flex-wrap items-center gap-3 rounded-md border border-border bg-card px-4 py-3 hover:bg-muted ' + (row.dueChip === 'critical' ? 'border-l-4 border-l-crit' : row.dueChip === 'warning' ? 'border-l-4 border-l-warn' : '')}>
                    <ShieldCheck aria-hidden="true" className="size-5 shrink-0 text-muted-foreground" />
                    <span className="min-w-0 flex-1">
                      <span className="block font-semibold tabular-nums">{row.number} · {kindLabel(row.kind)}</span>
                      <span className="block text-sm text-muted-foreground">{row.subjectMasked} · {subjectTypeLabel(row.subjectType)}</span>
                    </span>
                    <span className="text-sm tabular-nums text-muted-foreground">Prazo {formatDate(row.dueOn)}</span>
                    {due ? <StatusChip tone={due.tone}>{due.label}</StatusChip> : null}
                    <StatusChip tone={status.tone}>{status.label}</StatusChip>
                    <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
                  </Link>
                </li>
              )
            })}
          </ul>
        )}
        <ResponseDaysSection />
      </div>
      {registering ? (
        <RegisterPrivacyRequestPanel onClose={() => setRegistering(false)} onCreated={(id) => {
          setRegistering(false)
          void client.invalidateQueries({ queryKey: getListPrivacyRequestsQueryKey() })
          void navigate({ to: '/app/configuracoes/privacidade/$id', params: { id } })
        }} />
      ) : null}
    </>
  )
}

/** Prazo de resposta (CA 1): dias corridos, pré-configurado em 15 e editável na mesma tela. */
function ResponseDaysSection() {
  const client = useQueryClient()
  const settings = useGetPrivacySettings({ query: { retry: false } })
  const [value, setValue] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  if (settings.isPending) return <Skeleton className="h-24 max-w-xl" />
  if (settings.isError) return <ErrorState title="Não foi possível carregar o prazo de resposta." onRetry={() => void settings.refetch()} />
  const current = value ?? String(settings.data.responseDays)
  const save = async () => {
    setError(null)
    try {
      await savePrivacySettings({ responseDays: Number.parseInt(current, 10) })
      notifySuccess('Prazo salvo')
      setValue(null)
      await client.invalidateQueries({ queryKey: getGetPrivacySettingsQueryKey() })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar o prazo.')
    }
  }

  return (
    <section aria-labelledby="privacy-days" className="max-w-xl space-y-3 rounded-md border border-border bg-card p-5">
      <h2 id="privacy-days" className="flex items-center font-semibold">
        Prazo de resposta
        <Dica nome="Prazo" texto="Dias corridos a partir da data do pedido (fim de semana e feriado contam). A LGPD pede 15 dias para a declaração completa; o jurídico pode ajustar." />
      </h2>
      <div className="flex flex-wrap items-end gap-3">
        <label className="text-sm font-medium">
          Dias corridos
          <input type="number" min={1} max={30} inputMode="numeric" value={current} onChange={(e) => setValue(e.target.value)} aria-invalid={!!error} className={inputClass + ' mt-1 w-28 tabular-nums'} />
        </label>
        <button type="button" onClick={() => void save()} disabled={value === null} className={button + ' disabled:opacity-50'}>Salvar prazo</button>
      </div>
      {error ? <p role="alert" className="text-sm font-medium text-crit-ink">{error}</p> : <p className="text-xs text-muted-foreground">Vale para os próximos pedidos.</p>}
    </section>
  )
}
