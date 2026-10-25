import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { useState } from 'react'
import { cancelInventoryCount, closeInventoryCount, reopenInventoryCount, useGetInventoryCount, useGetInventoryCountDivergences } from '@/api/generated/bemaqui'
import type { InventoryCountDetail, UncountedPolicy } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { useIdempotencyKey } from '@/api/idempotency'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { Money } from '@/components/ui/money'
import { NextSteps, type Step } from '@/components/ui/next-steps'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { formatDateTime } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { InventoryTabs } from '../InventoryTabs'
import { ADJUSTMENT_TONE, closeTotals, COUNT_STATUS, lineLabel, linesLabel, UNCOUNTED_POLICY } from './inventory-counts'
import { CountHistorySection, CountingSection, DivergencesSection } from './InventoryCountSections'
import { formatQuantity } from './stock'
import { useRefreshCount } from './use-refresh-count'

const button = (text: string, primary = false) => (
  <button type="button" className={primary ? 'min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground' : 'min-h-[44px] rounded-md border border-border px-3 py-2 text-sm font-semibold'}>{text}</button>
)

/**
 * "Fechar inventário" (CA 9 e 10, padrão 4): com não contados, a pergunta "Considerar {n} itens não contados como zero?" ou "Manter o
 * saldo do sistema"; o impacto mostra sobras, faltas e o líquido, e o antes → depois de cada linha sobre o saldo de agora.
 */
function CloseDialog({ count, onDone }: { count: InventoryCountDetail; onDone: (detail: InventoryCountDetail) => void }) {
  const [policy, setPolicy] = useState<UncountedPolicy | null>(null)
  const query = useGetInventoryCountDivergences(count.id, policy ? { uncountedPolicy: policy } : {})
  const idempotency = useIdempotencyKey()
  const refresh = useRefreshCount(count.id)
  const d = query.data
  const totals = closeTotals(d?.lines ?? [])
  const uncounted = d?.uncounted ?? 0
  return (
    <ConfirmDialog size="lg" verb="Fechar inventário" done="Inventário fechado. Ajuste enviado para aprovação" title={`Fechar o ${count.number}?`}
      description="O inventário fecha e as diferenças viram um ajuste em modo Diferença para aprovação. O saldo só muda quando outra pessoa aprovar o ajuste."
      onOpen={() => setPolicy(null)} onReload={refresh}
      preconditions={[{ ok: !!d, text: 'Divergências carregadas.' }, ...(uncounted > 0 ? [{ ok: !!policy, text: 'O que fazer com os itens não contados.' }] : [])]}
      impact={d ? (
        <div className="space-y-1" aria-live="polite">
          <p>{linesLabel(d.lines.length)} com diferença · Sobras <Money value={totals.surplus} className="font-semibold" /> · Faltas <Money value={totals.shortage} className="font-semibold" /> · Líquido <Money value={totals.net} className="font-semibold" /></p>
          <ul className="max-h-48 space-y-0.5 overflow-y-auto text-xs tabular-nums">
            {d.lines.map((l) => <li key={l.id}>{lineLabel(l)}: {formatQuantity(l.systemQtyNow)} → <b>{formatQuantity(l.after)}</b> {l.unitCode} ({formatQuantity(l.difference, true)}) · <Money value={l.value} /></li>)}
          </ul>
          {d.lines.length === 0 ? <p className="text-xs text-muted-foreground">Sem divergência: o inventário fecha sem ajuste.</p> : null}
        </div>
      ) : null}
      onConfirm={async () => {
        const detail = await closeInventoryCount(count.id, { uncountedPolicy: policy, version: count.version }, idempotency.options)
        idempotency.renew()
        await refresh()
        onDone(detail)
      }}
      trigger={button('Fechar inventário', true)}>
      {uncounted > 0 ? (
        <fieldset className="space-y-1 text-sm">
          <legend className="font-medium">{d?.uncountedQuestion}</legend>
          {(['Zero', 'KeepSystem'] as const).map((p) => (
            <label key={p} className="flex min-h-[44px] items-center gap-2"><input type="radio" name="nao-contados" checked={policy === p} onChange={() => setPolicy(p)} />{UNCOUNTED_POLICY[p]}</label>
          ))}
        </fieldset>
      ) : null}
    </ConfirmDialog>
  )
}

/**
 * Inventário (EST-05): cabeçalho com número, local e situação; fatos (progresso, foto, escopo, recontagens); a situação do ajuste
 * gerado (CA 12) e as abas Contagem, Divergências e Histórico de alterações. Fechado ou cancelado, fica em modo leitura.
 */
export function InventoryCountPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetInventoryCount(id)
  const navigate = useNavigate()
  const refresh = useRefreshCount(id)
  const [reason, setReason] = useState('')
  const [steps, setSteps] = useState<Step[]>([])
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError || !query.data) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <ErrorState title={notFound ? 'Inventário não encontrado.' : 'Não foi possível carregar o inventário.'} message={notFound ? 'Ele pode ter sido digitado errado. Volte à lista de inventários.' : 'Verifique a conexão e tente de novo.'} onRetry={notFound ? undefined : () => query.refetch()} />
  }

  const c = query.data
  const counting = c.status === 'Counting'
  const closed = (detail: InventoryCountDetail) => setSteps(detail.adjustmentId
    ? [{ label: `Ver ajuste ${detail.adjustmentNumber}`, onSelect: () => void navigate({ to: '/app/estoque/ajustes/$id' as never, params: { id: detail.adjustmentId } as never }) },
       { label: 'Ver saldos do local', onSelect: () => void navigate({ to: '/app/estoque/saldos' as never, search: { local: detail.locationId } as never }) }]
    : [{ label: 'Ver saldos do local', onSelect: () => void navigate({ to: '/app/estoque/saldos' as never, search: { local: detail.locationId } as never }) }])
  return (
    <>
      <PageHeading title={`${c.number} — inventário de ${c.locationName}`}>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <StatusChip tone={COUNT_STATUS[c.status].tone}>{COUNT_STATUS[c.status].label}</StatusChip>
            <span>Aberto por {c.openedByName ?? '—'}</span>
            <Link to={'/app/estoque/inventarios' as never} className="font-semibold text-primary">Voltar aos inventários</Link>
          </p>
          <div className="flex flex-wrap gap-2">
            {c.canCount ? <Link to={'/app/estoque/inventarios/$id/contagem' as never} params={{ id: c.id } as never} className="inline-flex min-h-[44px] items-center rounded-md border border-border px-3 py-2 text-sm font-semibold">Contar no celular</Link> : null}
            {counting && c.canManage ? <CloseDialog count={c} onDone={closed} /> : null}
            {c.canCancel ? (
              <ConfirmDialog destructive verb="Cancelar inventário" done="Inventário cancelado" title={`Cancelar o ${c.number}?`} description="Nenhum ajuste nasce e o local é desbloqueado. As contagens ficam no histórico."
                onOpen={() => setReason('')} preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo do cancelamento escrito.' }]} onReload={refresh}
                onConfirm={async () => { await cancelInventoryCount(c.id, { reason: reason.trim(), version: c.version }); await refresh() }} trigger={button('Cancelar inventário')}>
                <label htmlFor="motivo-cancelar-inventario" className="text-sm font-medium">Motivo do cancelamento</label>
                <textarea id="motivo-cancelar-inventario" value={reason} maxLength={200} rows={2} onChange={(e) => setReason(e.target.value)} className={inputClass} />
              </ConfirmDialog>
            ) : null}
            {c.canReopen ? (
              <ConfirmDialog verb="Reabrir contagem" done="Contagem reaberta" title={`Reabrir a contagem do ${c.number}?`} description="O inventário volta a Em contagem, com as contagens de antes, e o local volta a bloquear ajustes e estornos manuais."
                onReload={refresh} onConfirm={async () => { await reopenInventoryCount(c.id, { version: c.version }); await refresh() }} trigger={button('Reabrir contagem', true)} />
            ) : null}
          </div>
        </div>
      </PageHeading>
      <InventoryTabs />
      {counting ? (
        <p role="note" className="mb-3 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          Inventário {c.number} em contagem neste local: ajustes e estornos manuais em {c.locationName} ficam bloqueados até fechar ou cancelar. Entradas e saídas continuam.
        </p>
      ) : null}
      {c.adjustmentSituation && c.adjustmentId ? (
        <p role="note" className="mb-3 flex flex-wrap items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm">
          {c.adjustmentStatus ? <StatusChip tone={ADJUSTMENT_TONE[c.adjustmentStatus]}>{c.adjustmentSituation}</StatusChip> : c.adjustmentSituation}
          <Link to={'/app/estoque/ajustes/$id' as never} params={{ id: c.adjustmentId } as never} className="font-semibold text-primary">Ver ajuste</Link>
          {c.adjustmentStatus === 'Rejected' ? <span className="text-muted-foreground">Reprovado: reabra a contagem para recontar.</span> : null}
        </p>
      ) : null}
      {c.status === 'Closed' && !c.adjustmentId ? <p role="note" className="mb-3 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm text-info-ink">Fechado sem divergência: nenhum ajuste foi gerado.</p> : null}
      {c.status === 'Cancelled' ? <p role="note" className="mb-3 rounded-md border border-border bg-muted px-3 py-2 text-sm">Cancelado em {c.cancelledAt ? formatDateTime(c.cancelledAt) : '—'} por {c.cancelledByName ?? '—'}: {c.cancelReason}</p> : null}
      {steps.length ? <div className="mb-3"><NextSteps steps={steps} onDismiss={() => setSteps([])} /></div> : null}
      <FactsBar label="Fatos do inventário" facts={[
        { label: 'Progresso', value: c.progress },
        { label: 'Foto do saldo', value: formatDateTime(c.snapshotAt) },
        { label: 'Escopo', value: c.scopeLabel },
        { label: 'Recontagens pendentes', value: String(c.recountPending) },
        ...(c.uncountedPolicy ? [{ label: 'Não contados', value: UNCOUNTED_POLICY[c.uncountedPolicy] }] : []),
      ]} />
      <UrlTabs label="Seções do inventário" fallback="contagem" tabs={[
        { id: 'contagem', label: 'Contagem', content: <CountingSection count={c} /> },
        { id: 'divergencias', label: 'Divergências', disabled: !c.canManage, disabledReason: 'Ver divergências é de quem ajusta o estoque.', content: <DivergencesSection count={c} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <CountHistorySection count={c} /> },
      ]} />
    </>
  )
}
