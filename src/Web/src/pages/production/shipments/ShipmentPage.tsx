import { useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { useRef, useState } from 'react'
import {
  cancelShipment, confirmShipment, confirmShipmentGeneralStock, getDownloadAttachmentUrl, getDownloadShipmentPdfUrl, getGetShipmentImpactQueryKey, getGetShipmentQueryKey,
  getListAttachmentsQueryKey, getListReadyToShipQueryKey, getListShipmentsQueryKey, reverseShipment, uploadAttachment, useGetShipment, useGetShipmentHistory, useGetShipmentImpact,
  useListAttachments,
} from '@/api/generated/bemaqui'
import type { ShipmentDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { OrderHeader } from '@/components/ui/order-header'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDate, formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { impactLines, quantity, SHIPMENT_DICAS, SHIPMENT_STATUS, SHIPMENT_TEXTS, shipmentActions, shippedNext } from './shipments'

const secondary = 'inline-flex min-h-10 items-center rounded-md border border-border px-3 text-sm font-semibold'
const primary = 'inline-flex min-h-10 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground'
const OWNER = 'Shipment'

/**
 * Romaneio (/app/producao/expedicao/$id, PRD-06b CA 3 a 17): cabeçalho com a situação, "Baixar PDF" e as ações ("Confirmar expedição",
 * "Confirmar estoque geral", "Cancelar romaneio", "Estornar expedição"); abas Itens (padrão), Documentos e Histórico de alterações. O
 * expedido fica em modo leitura, com os próximos passos (CA 8).
 */
export function ShipmentPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { salvo } = useSearch({ strict: false }) as { salvo?: unknown }
  const navigate = useNavigate()
  const client = useQueryClient()
  const can = { edit: useCan('production.edit'), approve: useCan('production.approve'), stock: useCan('inventory.approve'), create: useCan('production.create') }
  const canApproveProjects = useCan('projects.approve')
  const [justSaved, setJustSaved] = useState(String(salvo) === '1')
  const [justShipped, setJustShipped] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const query = useGetShipment(id)
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar o romaneio." message="Ele pode não existir. Volte à expedição e tente de novo." onRetry={() => query.refetch()} />
  const s = query.data
  const status = SHIPMENT_STATUS[s.status]
  const actions = shipmentActions(s, can)
  const refresh = async (next?: ShipmentDetail) => {
    if (next) client.setQueryData(getGetShipmentQueryKey(id), next)
    await client.invalidateQueries({ queryKey: getGetShipmentQueryKey(id) })
    await client.invalidateQueries({ queryKey: getGetShipmentImpactQueryKey(id) })
    await client.invalidateQueries({ queryKey: getListShipmentsQueryKey() })
    await client.invalidateQueries({ queryKey: getListReadyToShipQueryKey() })
    await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, id) })
  }
  const pdf = () => void apiDownload(getDownloadShipmentPdfUrl(id), `${s.number}.pdf`)
  const next = shippedNext(s, canApproveProjects)
  return (
    <>
      <OrderHeader title={`${s.number} — ${s.projectNumber}`} status={status} project={{ id: s.projectId, number: s.projectNumber }}
        meta={(
          <span className="flex flex-wrap items-center gap-x-3 text-sm text-muted-foreground">
            <span className="inline-flex items-center">Romaneio<Dica nome="Romaneio" texto={SHIPMENT_DICAS.romaneio} /></span>
            <span>{s.partyName}</span>
            {s.hasGeneralStock ? <span className="inline-flex items-center"><Tag>Estoque geral</Tag><Dica nome="Estoque geral" texto={SHIPMENT_DICAS.estoqueGeral} /></span> : null}
            {s.selfApproved ? <StatusChip tone="warn">Autoaprovado</StatusChip> : null}
          </span>
        )}
        actions={(
          <span className="flex flex-wrap gap-2">
            <button type="button" onClick={pdf} className={secondary}>Baixar PDF</button>
            {actions.confirm ? <ConfirmShipmentDialog shipment={s} open={confirmOpen} onOpenChange={setConfirmOpen} onDone={async (d) => { setJustSaved(false); setJustShipped(d.status === 'Shipped'); await refresh(d) }} /> : null}
            {actions.confirmStock ? <ConfirmStockDialog shipment={s} onDone={async (d) => { setJustShipped(true); await refresh(d) }} /> : null}
            {actions.cancel ? <ReasonDialog shipment={s} kind="cancel" onDone={refresh} /> : null}
            {actions.reverse ? <ReasonDialog shipment={s} kind="reverse" onDone={async (d) => { setJustShipped(false); await refresh(d) }} /> : null}
          </span>
        )} />
      {justSaved && s.status === 'Draft' ? (
        <div className="mb-4"><NextSteps onDismiss={() => setJustSaved(false)} steps={[
          ...(actions.confirm ? [{ label: 'Confirmar expedição', onSelect: () => setConfirmOpen(true) }] : []),
          { label: 'Baixar PDF', onSelect: pdf },
          { label: 'Ver expedição', onSelect: () => void navigate({ to: '/app/producao/expedicao' } as never) },
        ]} /></div>
      ) : null}
      {(justShipped || s.status === 'Shipped') && next.length > 0 ? (
        <div className="mb-4 space-y-1">
          <NextSteps onDismiss={() => setJustShipped(false)} steps={next.map((step) => step === 'instalacao'
            ? { label: 'Ver OS de instalação', onSelect: () => void navigate({ to: '/app/producao/instalacoes/$id', params: { id: s.next.installationOrderId! } } as never) }
            : step === 'entrega'
              ? { label: 'Registrar entrega', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: s.projectId }, search: { aba: 'resumo' } } as never) }
              : { label: 'Ver projeto', onSelect: () => void navigate({ to: '/app/projetos/$id', params: { id: s.projectId }, search: { aba: 'linha-do-tempo' } } as never) })} />
          <p className="text-xs text-muted-foreground">{SHIPMENT_TEXTS.nfe}.</p>
        </div>
      ) : null}
      {s.status === 'AwaitingStockApproval' ? (
        <p role="note" className="mb-4 rounded-md border border-l-4 border-warn-soft border-l-warn bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          Aguardando a confirmação do estoque geral por outra pessoa com aprovação no Estoque{s.stockApprovalRequestedAt ? ` (pedida em ${formatDateTime(s.stockApprovalRequestedAt)})` : ''}.
          {s.next.confirmStockBlocker && can.stock ? ` ${s.next.confirmStockBlocker}` : ''}
        </p>
      ) : null}
      {s.status === 'Shipped' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Expedido em {s.shippedAt ? formatDateTime(s.shippedAt) : '—'}{s.shippedByName ? ` por ${s.shippedByName}` : ''}{s.stockApprovedByName ? `; estoque geral confirmado por ${s.stockApprovedByName}` : ''}. Só leitura.</p> : null}
      {s.status === 'Cancelled' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Romaneio cancelado{s.cancelledByName ? ` por ${s.cancelledByName}` : ''}: {s.cancelReason}</p> : null}
      {s.status === 'Reversed' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">Expedição estornada em {s.reversedAt ? formatDateTime(s.reversedAt) : '—'}{s.reversedByName ? ` por ${s.reversedByName}` : ''}: {s.reversalReason}</p> : null}
      <FactsBar label="Fatos do romaneio" facts={[
        { label: 'Entrega', value: s.deliveryAddress },
        { label: 'Transportadora', value: s.carrierName ?? '—' },
        { label: 'Placa e motorista', value: [s.vehiclePlate, s.driverName].filter(Boolean).join(' · ') || '—' },
        { label: 'Data prevista', value: <span className="tabular-nums">{s.plannedDate ? formatDate(s.plannedDate) : '—'}{s.volumes || s.grossWeight ? <span className="block text-xs text-muted-foreground">{s.volumes ? `${s.volumes} volumes` : ''}{s.volumes && s.grossWeight ? ' · ' : ''}{s.grossWeight ? `${quantity(s.grossWeight)} kg` : ''}</span> : null}</span> },
      ]} />
      <UrlTabs label="Seções do romaneio" fallback="itens" tabs={[
        { id: 'itens', label: 'Itens', content: <ShipmentItems shipment={s} /> },
        { id: 'documentos', label: 'Documentos', content: <ShipmentDocuments shipment={s} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <ShipmentHistory id={s.id} /> },
      ]} />
    </>
  )
}

/** Itens em cartões (400 px): item, quantidade, local, lote, séries e o movimento de saída. */
function ShipmentItems({ shipment: s }: { shipment: ShipmentDetail }) {
  if (s.lines.length === 0) return <EmptyState title="Romaneio sem itens." />
  return (
    <ul className="space-y-2" aria-label="Itens do romaneio">
      {s.lines.map((l) => (
        <li key={l.id} className="rounded-md border border-border bg-card p-3 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span><b className="font-semibold">{l.sequence}. {l.itemCode}</b> {l.itemName}</span>
            <span className="flex items-center gap-2">
              {l.fromGeneralStock ? <Tag>Estoque geral</Tag> : null}
              <span className="tabular-nums">{quantity(l.quantity)} {l.unitCode}</span>
            </span>
          </div>
          <p className="text-xs text-muted-foreground">{l.locationName}{l.lotNumber ? ` · lote ${l.lotNumber}` : ''}{l.movementId ? ' · saída lançada no estoque' : ''}</p>
          {l.serials.length > 0 ? <p className="mt-1 tabular-nums">Séries: {l.serials.map((x) => x.serialNumber).join(', ')}</p> : null}
        </li>
      ))}
    </ul>
  )
}

/**
 * "Confirmar expedição" (CA 4 e 5): diálogo de impacto (tela cheia a 400 px) com a saída por item, o local, as séries que passam a
 * "Expedido" e o físico antes → depois; o 409 de saldo aparece dentro do diálogo. Com estoque geral, o romaneio vai para a confirmação.
 */
function ConfirmShipmentDialog({ shipment: s, open, onOpenChange, onDone }: { shipment: ShipmentDetail; open: boolean; onOpenChange: (v: boolean) => void; onDone: (d: ShipmentDetail) => Promise<void> }) {
  const impact = useGetShipmentImpact(s.id, { query: { enabled: open } })
  const general = s.hasGeneralStock
  return (
    <ConfirmDialog size="lg" verb="Confirmar expedição" done={general ? SHIPMENT_TEXTS.aguardando : SHIPMENT_TEXTS.confirmada} title={`Confirmar a expedição do ${s.number}?`} open={open} onOpenChange={onOpenChange}
      description={general
        ? 'Este romaneio tem produto do estoque geral: ele fica aguardando a confirmação de outra pessoa com aprovação no Estoque, e só então o estoque baixa.'
        : 'O produto acabado sai do estoque agora, pelo custo médio do momento, e as séries passam a Expedido. O PDF fica guardado na aba Documentos.'}
      impact={impact.isPending ? <Skeleton className="h-16" /> : impact.isError ? <p className="text-sm text-crit-ink">Não foi possível ler o impacto. Feche e abra de novo.</p> : (
        <ul className="list-disc space-y-1 pl-5 text-sm">{impactLines(impact.data).map((l) => <li key={l}>{l}</li>)}</ul>
      )}
      onConfirm={async () => onDone(await confirmShipment(s.id, { version: s.version }))}
      trigger={<button type="button" className={primary}>Confirmar expedição</button>} />
  )
}

/** "Confirmar estoque geral" (CA 6): outra pessoa com inventory.approve decide sobre o que foi montado, sem editar; a expedição acontece junto. */
function ConfirmStockDialog({ shipment: s, onDone }: { shipment: ShipmentDetail; onDone: (d: ShipmentDetail) => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const impact = useGetShipmentImpact(s.id, { query: { enabled: open } })
  if (!s.next.canConfirmStock) {
    return (
      <span className="inline-flex flex-wrap items-center gap-2">
        <button type="button" disabled aria-describedby={`estoque-${s.id}`} className={`${primary} opacity-50`}>Confirmar estoque geral</button>
        <span id={`estoque-${s.id}`} className="text-xs text-muted-foreground">{s.next.confirmStockBlocker}</span>
      </span>
    )
  }

  return (
    <ConfirmDialog size="lg" verb="Confirmar estoque geral" done={SHIPMENT_TEXTS.estoqueConfirmado} title={`Confirmar o estoque geral do ${s.number}?`} open={open} onOpenChange={setOpen}
      description={`Montado por ${s.createdByName ?? 'outro usuário'}. Você decide sobre o que foi montado: a saída do estoque acontece agora, inclusive a do produto sem projeto.`}
      impact={impact.isPending ? <Skeleton className="h-16" /> : impact.isError ? <p className="text-sm text-crit-ink">Não foi possível ler o impacto.</p> : (
        <ul className="list-disc space-y-1 pl-5 text-sm">{impactLines(impact.data).map((l) => <li key={l}>{l}</li>)}</ul>
      )}
      onConfirm={async () => onDone(await confirmShipmentGeneralStock(s.id, { version: s.version }))}
      trigger={<button type="button" className={primary}>Confirmar estoque geral</button>} />
  )
}

/** "Cancelar romaneio" (CA 10) e "Estornar expedição" (CA 11): diálogo destructive com motivo obrigatório. */
function ReasonDialog({ shipment: s, kind, onDone }: { shipment: ShipmentDetail; kind: 'cancel' | 'reverse'; onDone: (d: ShipmentDetail) => Promise<void> }) {
  const [reason, setReason] = useState('')
  const cancel = kind === 'cancel'
  return (
    <ConfirmDialog size="lg" destructive verb={cancel ? 'Cancelar romaneio' : 'Estornar expedição'} done={cancel ? SHIPMENT_TEXTS.cancelado : SHIPMENT_TEXTS.estornada}
      title={cancel ? `Cancelar o ${s.number}?` : `Estornar a expedição do ${s.number}?`}
      description={cancel
        ? 'O romaneio fica cancelado e as séries ficam livres para outro romaneio. Nada muda no estoque.'
        : 'Use quando o produto não saiu de fato. O estoque recebe as entradas inversas, as séries voltam ao estoque e o romaneio fica estornado.'}
      onOpen={() => setReason('')}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
      impact={cancel ? <p className="text-sm">{SHIPMENT_STATUS[s.status].label} → Cancelado</p> : (
        <ul className="list-disc space-y-1 pl-5 text-sm">
          {s.lines.map((l) => <li key={l.id}>Entrada de {quantity(l.quantity)} {l.unitCode} de {l.itemCode} em {l.locationName}{l.serials.length > 0 ? ` · séries ${l.serials.map((x) => x.serialNumber).join(', ')} voltam ao estoque` : ''}</li>)}
        </ul>
      )}
      onConfirm={async () => onDone(cancel ? await cancelShipment(s.id, { reason: reason.trim(), version: s.version }) : await reverseShipment(s.id, { reason: reason.trim(), version: s.version }))}
      trigger={<button type="button" className={secondary}>{cancel ? 'Cancelar romaneio' : 'Estornar expedição'}</button>}>
      <label className="block space-y-1 text-sm font-medium">Motivo<textarea rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}

/** Documentos (CA 14, dono Shipment): o PDF guardado na expedição e os comprovantes (pdf, png, jpg até 10 MB) com production.create. */
function ShipmentDocuments({ shipment: s }: { shipment: ShipmentDetail }) {
  const list = useListAttachments(OWNER, s.id)
  const client = useQueryClient()
  const canCreate = useCan('production.create')
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (file: File) => {
    setSending(true)
    setError(undefined)
    try {
      await uploadAttachment(OWNER, s.id, { file })
      notifySuccess('Arquivo anexado')
      await client.invalidateQueries({ queryKey: getListAttachmentsQueryKey(OWNER, s.id) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o arquivo.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }
  const action = canCreate ? (
    <>
      <input ref={input} id="comprovante-romaneio" type="file" accept="image/png,image/jpeg,application/pdf" capture="environment" className="sr-only"
        onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
      <label htmlFor="comprovante-romaneio" className="inline-flex min-h-[56px] cursor-pointer items-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground">{sending ? 'Enviando…' : 'Anexar comprovante'}</label>
    </>
  ) : null
  let body
  if (list.isPending) body = <Skeleton className="h-24" />
  else if (list.isError) body = <ErrorState onRetry={() => list.refetch()} />
  else if (list.data.length === 0) body = <EmptyState title="Nenhum documento no romaneio." description="O PDF fica guardado aqui na confirmação. Anexe o canhoto assinado e as fotos da entrega (PDF, PNG ou JPG até 10 MB)." action={action} />
  else {
    body = (
      <>
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {list.data.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <button type="button" onClick={() => void apiDownload(getDownloadAttachmentUrl(a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
              <span className="flex items-center gap-2 text-xs text-muted-foreground">
                {a.origin === 'System' ? <Tag>Gerado pelo sistema</Tag> : null}
                {a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}{formatDateTime(a.createdAt)}{a.uploadedByName ? ` · ${a.uploadedByName}` : ''}
              </span>
            </li>
          ))}
        </ul>
        <div className="mt-3">{action}</div>
      </>
    )
  }
  return <div className="space-y-2"><FormError message={error} />{body}</div>
}

const ACTIONS: Record<string, string> = { Created: 'Criou', Updated: 'Alterou', Deleted: 'Excluiu' }
const ENTITY: Record<string, string> = { Shipment: 'o romaneio', ShipmentLine: 'a linha', ShipmentLineSerial: 'a série da linha' }

/** Histórico (CA 17): antes e depois do romaneio, das linhas e das séries, com usuário e data; placa e motorista mascarados. */
function ShipmentHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetShipmentHistory(id, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças no romaneio aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>{ENTITY[entry.entity] ?? entry.entity}</span>
              <span className="text-muted-foreground"> · {entry.userId ? 'usuário ' + entry.userId.slice(0, 8) : entry.origin ?? 'sistema'} · </span>
              <time className="tabular-nums text-muted-foreground" dateTime={entry.at}>{formatDateTime(entry.at)}</time>
            </p>
            <ul className="mt-1 text-muted-foreground">{describeChanges(entry.changes).map((line) => <li key={line}>{line}</li>)}</ul>
          </li>
        ))}
      </ol>
      {pages > 1 ? (
        <div className="mt-3 flex items-center gap-2 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Anterior</button>
          <span className="tabular-nums">{page} de {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="rounded-md border border-border px-3 py-1.5 font-semibold disabled:opacity-50">Próxima</button>
        </div>
      ) : null}
    </div>
  )
}
