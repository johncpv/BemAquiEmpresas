import { useQueryClient } from '@tanstack/react-query'
import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import {
  getGetPartyCollectionQueryKey, getGetReceivableCollectionQueryKey, pauseReceivableCollection, resumeReceivableCollection, useGetReceivableCollection,
} from '@/api/generated/bemaqui'
import type { CollectionNoticeView, CollectionPauseView } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { COLLECTION_DICAS, noticeStatus } from '@/pages/settings/billing/collection-rule'
import { useCanSettings } from '@/pages/settings/use-can-settings'

const button = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

/** Lembretes enviados (CA 8, padrão 6): passo, data, situação do e-mail e o motivo do ignorado. */
export function NoticeList({ notices, showNumber }: { notices: CollectionNoticeView[]; showNumber?: boolean }) {
  return (
    <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
      {notices.map((n) => {
        const s = noticeStatus(n.status)
        return (
          <li key={n.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
            <span>
              {showNumber ? <Link to={'/app/financeiro/receber/$id' as never} params={{ id: n.receivableId } as never} search={{ aba: 'lembretes' } as never} className="mr-2 font-semibold text-primary hover:underline">{n.receivableNumber}</Link> : null}
              <b>{n.stepLabel}</b> <span className="tabular-nums text-muted-foreground">{formatDate(n.scheduledFor)}</span>
              {n.reason ? <span className="block text-xs text-muted-foreground">{n.reason}</span> : null}
            </span>
            <span className="flex items-center gap-2">
              {n.sentAt ? <span className="text-xs tabular-nums text-muted-foreground">{formatDateTime(n.sentAt)}</span> : null}
              <StatusChip tone={s.tone}>{s.label}</StatusChip>
            </span>
          </li>
        )
      })}
    </ul>
  )
}

/** Histórico das pausas: quem pausou, o motivo, até quando e quem retomou. */
export function PauseHistory({ pauses }: { pauses: CollectionPauseView[] }) {
  if (pauses.length === 0) return null
  return (
    <details className="text-sm">
      <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Pausas anteriores ({pauses.length})</summary>
      <ul className="mt-2 space-y-1">
        {pauses.map((p) => (
          <li key={p.id} className="rounded-md border border-border bg-card px-3 py-2">
            <b>{p.reason}</b> · pausada por {p.createdByName ?? 'sistema'} em {formatDateTime(p.createdAt)}
            {p.until ? <> · até {formatDate(p.until)}</> : null}
            {p.resumedAt ? <> · retomada por {p.resumedByName ?? 'sistema'} em {formatDateTime(p.resumedAt)}</> : p.active ? <> · <b>ativa</b></> : <> · terminou</>}
          </li>
        ))}
      </ul>
    </details>
  )
}

/** "Pausar cobrança" (CA 6): motivo obrigatório e o último dia opcional; "Retomar cobrança" confirma com o mesmo verbo. */
export function PauseControls({ active, canEdit, subject, onPause, onResume }: {
  active: CollectionPauseView | null | undefined
  canEdit: boolean
  subject: string
  onPause: (reason: string, until: string | null) => Promise<unknown>
  onResume: () => Promise<unknown>
}) {
  const [reason, setReason] = useState('')
  const [until, setUntil] = useState('')
  if (active) {
    return (
      <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
        <span><b>Cobrança pausada</b>: {active.reason}{active.until ? ` · até ${formatDate(active.until)}` : ' · até alguém retomar'}</span>
        {canEdit ? (
          <ConfirmDialog verb="Retomar cobrança" done="Cobrança retomada" title={`Retomar a cobrança ${subject}?`} description="Os próximos passos da régua voltam a sair. Os que caíram na pausa não são enviados."
            onConfirm={onResume} trigger={<button type="button" className={button}>Retomar cobrança</button>} />
        ) : null}
      </div>
    )
  }

  if (!canEdit) return null
  return (
    <div className="flex items-center">
      <ConfirmDialog verb="Pausar cobrança" done="Cobrança pausada" title={`Pausar a cobrança ${subject}?`} description="Os lembretes automáticos param até a data informada ou até alguém retomar."
        onOpen={() => { setReason(''); setUntil('') }}
        onConfirm={async () => {
          if (!reason.trim()) throw new DialogMessage('Informe o motivo da pausa (ex.: negociação).')
          await onPause(reason.trim(), until || null)
        }}
        trigger={<button type="button" className={button}>Pausar cobrança</button>}>
        <Field label="Motivo" required hint="Até 200 caracteres. Fica no histórico.">{(p) => <input {...p} value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} />}</Field>
        <Field label="Pausar até" hint="Opcional. Vazio: até alguém retomar.">{(p) => <DateInput {...p} value={until} onChange={setUntil} />}</Field>
      </ConfirmDialog>
      <Dica nome="Pausar cobrança" texto={COLLECTION_DICAS.pausar} />
    </div>
  )
}

/** Aba "Lembretes" do título (CA 6 e 8): o próximo previsto, os enviados e a pausa do título (e a do cliente, se houver). */
export function ReceivableCollectionTab({ receivableId, partyId }: { receivableId: string; partyId: string }) {
  const client = useQueryClient()
  const query = useGetReceivableCollection(receivableId)
  const canConfigure = useCanSettings('view')
  if (query.isPending) return <div className="space-y-2"><Skeleton className="h-12" /><Skeleton className="h-24" /></div>
  if (query.isError || !query.data) return <ErrorState title="Não foi possível carregar os lembretes." onRetry={() => void query.refetch()} />
  const v = query.data
  const set = (next: typeof v) => {
    client.setQueryData(getGetReceivableCollectionQueryKey(receivableId), next)
    void client.invalidateQueries({ queryKey: getGetPartyCollectionQueryKey(partyId) })
  }
  return (
    <div className="space-y-4 text-sm">
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-card p-3">
        {v.next.date ? (
          <span>Próximo lembrete: <b className="tabular-nums">{formatDate(v.next.date)}</b> ({v.next.stepLabel?.toLowerCase()})</span>
        ) : (
          <span>{v.next.blocked}</span>
        )}
        {!v.ruleActive && canConfigure ? <Link to={'/app/configuracoes/cobranca' as never} hash="regua" className="font-semibold text-primary hover:underline">Configurar a régua</Link> : null}
      </div>
      {v.partyPause ? <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-warn-ink"><b>Cobrança pausada para o cliente</b>: {v.partyPause.reason}. Retome no cadastro do cliente.</p> : null}
      <PauseControls active={v.activePause} canEdit={v.canEdit} subject="deste título"
        onPause={async (reason, until) => set(await pauseReceivableCollection(receivableId, { reason, until }))}
        onResume={async () => set(await resumeReceivableCollection(receivableId))} />
      {v.notices.length === 0 ? (
        <EmptyState title="Nenhum lembrete para este título ainda." description={v.ruleActive ? 'Os lembretes saem nos dias da régua, no horário configurado.' : 'Ligue a régua em Configurações → Cobrança para enviar lembretes automáticos.'} />
      ) : <NoticeList notices={v.notices} />}
      <PauseHistory pauses={v.pauses.filter((p) => !p.active)} />
    </div>
  )
}
