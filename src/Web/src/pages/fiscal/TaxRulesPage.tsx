import { Link, useNavigate } from '@tanstack/react-router'
import { Calculator, Download, Plus } from 'lucide-react'
import { getExportFiscalSettingsPdfUrl, useListTaxRules } from '@/api/generated/bemaqui'
import type { TaxRuleSummary } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { useTableFilters } from '@/components/data-table/table-search'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { TO_CONFIRM_DICA, TO_CONFIRM_LABEL } from '@/components/ui/to-confirm-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { DICAS, EMPTY_ACTIVE, OPERATION_LABEL, SEED_DRAFTS, STATUS } from './tax-rules'
import { TaxSimulationPanel } from './TaxSimulationPanel'

const primary = 'inline-flex min-h-11 items-center gap-1 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'inline-flex min-h-11 items-center gap-1 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

const FILTERS = [
  { value: undefined, label: 'Todas' },
  { value: 'a-confirmar', label: 'A confirmar' },
  { value: 'a-revisar', label: 'A revisar' },
] as const

/**
 * Fiscal → Regras fiscais (FIS-02b, `/app/fiscal/regras`): regras por operação com versão, vigência, situação, chip de conferência e quem
 * ativou; filtros "A confirmar" e "A revisar" na URL; "Simular" em painel lateral (`?simular=1`); "Nova regra fiscal" com fiscal.edit.
 */
export function TaxRulesPage() {
  const navigate = useNavigate()
  const { filtro, simular } = useTableFilters(['filtro', 'simular'])
  const canEdit = useCan('fiscal.edit')
  const list = useListTaxRules({ filter: filtro || undefined }, { query: { retry: false } })
  const setSearch = (patch: Record<string, string | undefined>) => void navigate({ to: '.', search: (s: Record<string, unknown>) => ({ ...s, ...patch }) } as never)
  const download = () => apiDownload(getExportFiscalSettingsPdfUrl(), 'configuracao-fiscal-para-a-contadora.pdf')
  const data = list.data

  return (
    <>
      <PageHeading title="Regras fiscais" count={data?.toConfirm}>
        <p className="mt-2 text-muted-foreground">CFOP, CST e alíquotas de cada operação. A nota sai com a regra ativa que vale para o cliente e o item.</p>
        <p className="mt-1 flex flex-wrap items-center gap-1 text-xs text-muted-foreground">CFOP<Dica nome="CFOP" texto={DICAS.cfop} /> · {TO_CONFIRM_LABEL}<Dica nome={TO_CONFIRM_LABEL} texto={TO_CONFIRM_DICA} /></p>
      </PageHeading>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <div role="group" aria-label="Filtro" className="flex flex-wrap gap-2">
          {FILTERS.map((f) => (
            <button key={f.label} type="button" aria-pressed={filtro === f.value} onClick={() => setSearch({ filtro: f.value })}
              className={cn('min-h-11 rounded-full border px-3 text-sm font-semibold', filtro === f.value ? 'border-primary bg-primary-soft text-primary' : 'border-border bg-card')}>
              {f.label}
              {f.value === 'a-confirmar' && data ? <span className="ml-1 tabular-nums">({data.toConfirm})</span> : null}
              {f.value === 'a-revisar' && data ? <span className="ml-1 tabular-nums">({data.toReview})</span> : null}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => setSearch({ simular: '1' })} className={secondary}><Calculator aria-hidden="true" className="size-4" />Simular</button>
          {canEdit ? <Link to="/app/fiscal/regras/nova" className={primary}><Plus aria-hidden="true" className="size-4" />Nova regra fiscal</Link> : null}
        </div>
      </div>

      {list.isPending ? (
        <div className="space-y-2"><Skeleton className="h-16" /><Skeleton className="h-16" /><Skeleton className="h-16" /></div>
      ) : list.isError || !data ? (
        <ErrorState message={list.error instanceof ApiError ? list.error.message : undefined} onRetry={() => void list.refetch()} />
      ) : (
        <div className="space-y-3">
          {data.seedDraftsOnly ? (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-warn-soft bg-warn-soft p-3">
              <p className="text-sm font-semibold text-warn-ink">{SEED_DRAFTS}</p>
              <div className="flex flex-wrap gap-2">
                <button type="button" onClick={() => setSearch({ simular: '1' })} className={secondary}>Simular</button>
                {data.canEdit ? <button type="button" onClick={() => void download()} className={secondary}><Download aria-hidden="true" className="size-4" />Baixar para a contadora</button> : null}
              </div>
            </div>
          ) : null}
          {data.active === 0 && !data.seedDraftsOnly && !filtro ? (
            <EmptyState title={EMPTY_ACTIVE} action={canEdit ? <Link to="/app/fiscal/regras/nova" className={primary}>Nova regra fiscal</Link> : undefined} />
          ) : null}
          {data.rules.length === 0 && filtro ? <EmptyState title="Nenhuma regra neste filtro." description="Volte para Todas para ver as regras." /> : null}
          <ul className="space-y-2">
            {data.rules.map((r) => <RuleRow key={r.ruleId} rule={r} />)}
          </ul>
        </div>
      )}
      <TaxSimulationPanel open={simular === '1'} onOpenChange={(o) => setSearch({ simular: o ? '1' : undefined })} />
    </>
  )
}

function RuleRow({ rule }: { rule: TaxRuleSummary }) {
  const status = STATUS[rule.status]
  return (
    <li>
      <Link to="/app/fiscal/regras/$id" params={{ id: rule.ruleId }}
        className={cn('block rounded-md border border-border bg-card p-3 hover:bg-muted', rule.needsReview && 'border-l-4 border-l-warn')}>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="font-semibold">{rule.name}</p>
            <p className="text-xs text-muted-foreground">
              {OPERATION_LABEL[rule.operation]} · versão <span className="tabular-nums">{rule.ruleVersion}</span> · desde <span className="tabular-nums">{formatDate(rule.validFrom)}</span>
              {rule.validTo ? <> até <span className="tabular-nums">{formatDate(rule.validTo)}</span></> : null}
              {rule.cfopInternal ? <> · CFOP <span className="tabular-nums">{rule.cfopInternal}/{rule.cfopInterstate}</span></> : null}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusChip tone={status.tone}>{status.label}</StatusChip>
            {rule.reviewState === 'Confirmed' ? <StatusChip tone="ok">Conferida</StatusChip> : <StatusChip tone="warn">{TO_CONFIRM_LABEL}</StatusChip>}
            {rule.needsReview ? <StatusChip tone="warn">A revisar</StatusChip> : null}
            {rule.difal ? <StatusChip tone="crit">DIFAL bloqueado</StatusChip> : null}
            {rule.draftVersion ? <StatusChip tone="info">Versão {rule.draftVersion} em rascunho</StatusChip> : null}
          </div>
        </div>
        {rule.activatedAt ? <p className="mt-1 text-xs text-muted-foreground">Ativada por {rule.activatedByName ?? 'usuário removido'} em {formatDateTime(rule.activatedAt)}</p> : null}
      </Link>
    </li>
  )
}
