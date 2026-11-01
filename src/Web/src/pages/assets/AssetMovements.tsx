import { useQueryClient } from '@tanstack/react-query'
import { type ReactNode, useState } from 'react'
import {
  disposeAsset, getGetAssetQueryKey, getListAssetMovementsQueryKey, getListAssetsQueryKey, listAssetBuyerChoices, moveAsset, reactivateAsset, useListAssetMovements,
  useListAssetResponsibleChoices, useListAssetSites, useListAssetWorkCenterChoices,
} from '@/api/generated/bemaqui'
import type { AssetDetail, AssetDisposalReason, AssetMovementResult, AssetMovementView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Combobox } from '@/components/form/combobox'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput, MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { formatDate, formatDateTime } from '@/lib/format'
import {
  DISPOSAL_REASON_LABELS, DISPOSAL_REASON_OPTIONS, type DisposalReason, MOVEMENT_KIND_LABELS, movementChanged, movementChanges, movementDraft, type MovementDraft, placeText, REASON_MAX, todayIso,
} from './assets'

const button = 'inline-flex min-h-11 items-center rounded-md border border-border px-3 py-2 text-sm font-semibold'

/** Depois de movimentar, baixar ou reativar: o bem, a lista, a aba e o histórico se atualizam. */
function useRefreshAsset(id: string) {
  const client = useQueryClient()
  return (result?: AssetMovementResult) => {
    if (result) client.setQueryData(getGetAssetQueryKey(id), result.asset)
    void client.invalidateQueries({ queryKey: getGetAssetQueryKey(id) })
    void client.invalidateQueries({ queryKey: getListAssetsQueryKey() })
    void client.invalidateQueries({ queryKey: getListAssetMovementsQueryKey(id) })
    void client.invalidateQueries({ predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/audit/') })
  }
}

/** Erros do servidor por campo (422) no diálogo: devolve true quando algum caiu num campo conhecido. */
function useFieldErrors(fields: string[]) {
  const [errors, setErrors] = useState<Partial<Record<string, string>>>({})
  const onFieldErrors = (e: ApiError) => {
    const known = Object.fromEntries(Object.entries(e.fields).filter(([k]) => fields.includes(k)).map(([k, v]) => [k, v[0]]))
    setErrors(known)
    return Object.keys(known).length > 0
  }
  return { errors, setErrors, onFieldErrors }
}

/**
 * Aba Movimentações (PAT-02 CA 3): do mais recente ao mais antigo, em cartões (data, de → para, motivo, quem registrou e quando). O
 * movimento não é editado nem apagado; um erro se corrige com outra movimentação.
 */
export function AssetMovements({ asset, canEdit, onMove }: { asset: AssetDetail; canEdit: boolean; onMove: () => void }) {
  const query = useListAssetMovements(asset.id)
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-20" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar as movimentações." message="Verifique a conexão e tente de novo." onRetry={() => query.refetch()} />
  const movable = asset.status !== 'Disposed'
  const action = canEdit && movable ? <button type="button" onClick={onMove} className={button}>Registrar movimentação</button> : undefined
  if (query.data.length === 0) {
    return <EmptyState title="Nenhuma movimentação. O bem está onde foi cadastrado." description="Quando o bem trocar de local, de responsável ou de centro, registre aqui: fica no histórico, com quem fez e quando." action={action} />
  }

  return (
    <div className="space-y-3">
      {action ? <div className="flex justify-end">{action}</div> : null}
      <ol className="space-y-3">
        {query.data.map((m) => <MovementCard key={m.id} movement={m} />)}
      </ol>
    </div>
  )
}

function MovementCard({ movement: m }: { movement: AssetMovementView }) {
  const changes = movementChanges(m.from, m.to)
  return (
    <li className="rounded-md border border-border bg-card p-3 text-sm">
      <p className="flex flex-wrap items-baseline gap-x-2">
        <b className="font-semibold">{MOVEMENT_KIND_LABELS[m.kind]}{m.disposalReason ? `: ${DISPOSAL_REASON_LABELS[m.disposalReason as DisposalReason]}` : ''}</b>
        <time className="tabular-nums text-muted-foreground" dateTime={m.movedOn}>{formatDate(m.movedOn)}</time>
      </p>
      {m.kind === 'Transfer' ? (
        <ul className="mt-1">{changes.map((line) => <li key={line}>{line}</li>)}</ul>
      ) : (
        <p className="mt-1 text-muted-foreground">{placeText(m.to)}</p>
      )}
      {m.disposalValue != null ? <p className="mt-1">Valor da venda: <Money value={m.disposalValue} />{m.buyerName ? ` · Comprador: ${m.buyerName}` : ''}</p> : m.buyerName ? <p className="mt-1">Comprador: {m.buyerName}</p> : null}
      {m.reason ? <p className="mt-1">Motivo: {m.reason}</p> : null}
      <p className="mt-1 text-xs text-muted-foreground">
        Registrada por {m.createdByName ?? 'sistema'} em <time className="tabular-nums" dateTime={m.createdAt}>{formatDateTime(m.createdAt)}</time>
      </p>
    </li>
  )
}

function ReasonField({ value, onChange, error, label = 'Motivo', required = true }: { value: string; onChange: (v: string) => void; error?: string; label?: string; required?: boolean }) {
  return (
    <Field label={label} required={required} error={error} hint={`${value.length}/${REASON_MAX}`}>
      {(p) => <input {...p} value={value} maxLength={REASON_MAX} onChange={(e) => onChange(e.target.value)} className={inputClass} />}
    </Field>
  )
}

/** Local, responsável e centro do destino (selects com o atual mesmo inativo ou desligado). */
export function PlacementFields({ draft, set, errors, current, emptyLabel }: {
  draft: Pick<MovementDraft, 'siteId' | 'responsibleEmployeeId' | 'workCenterId'>
  set: (patch: Partial<MovementDraft>) => void
  errors: Partial<Record<string, string>>
  current?: AssetDetail
  /** No lote, vazio é "Manter o de cada bem". */
  emptyLabel?: { site: string; responsible: string; center: string }
}) {
  const sites = useListAssetSites()
  const responsibles = useListAssetResponsibleChoices()
  const centers = useListAssetWorkCenterChoices()
  const empty = emptyLabel ?? { site: 'Sem local', responsible: 'Sem responsável', center: 'Sem centro' }
  const siteOptions = (sites.data ?? []).filter((s) => s.isActive || s.id === current?.siteId)
  const currentSiteMissing = current?.siteId && !siteOptions.some((s) => s.id === current.siteId)
  const currentCenterMissing = current?.workCenterId && !(centers.data ?? []).some((c) => c.id === current.workCenterId)
  const currentResponsibleMissing = current?.responsibleEmployeeId && !(responsibles.data ?? []).some((r) => r.id === current.responsibleEmployeeId)
  return (
    <>
      <Field label="Local" error={errors.siteId}>
        {(p) => (
          <select {...p} value={draft.siteId} onChange={(e) => set({ siteId: e.target.value })} className={inputClass}>
            <option value="">{empty.site}</option>
            {currentSiteMissing ? <option value={current!.siteId!}>{current!.siteName ?? 'Local atual'}</option> : null}
            {siteOptions.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        )}
      </Field>
      <Field label="Responsável" error={errors.responsibleEmployeeId}
        warning={current?.responsibleTerminated && draft.responsibleEmployeeId === current.responsibleEmployeeId ? 'Desligado. Troque o responsável.' : undefined}>
        {(p) => (
          <select {...p} value={draft.responsibleEmployeeId} onChange={(e) => set({ responsibleEmployeeId: e.target.value })} className={inputClass}>
            <option value="">{empty.responsible}</option>
            {currentResponsibleMissing ? <option value={current!.responsibleEmployeeId!}>{current!.responsibleName ?? 'Responsável atual'}{current!.responsibleTerminated ? ' (Desligado)' : ''}</option> : null}
            {(responsibles.data ?? []).map((r) => <option key={r.id} value={r.id}>{r.name}{r.registration ? ` · ${r.registration}` : ''} · {r.positionName}</option>)}
          </select>
        )}
      </Field>
      <Field label="Centro de trabalho" error={errors.workCenterId}>
        {(p) => (
          <select {...p} value={draft.workCenterId} onChange={(e) => set({ workCenterId: e.target.value })} className={inputClass}>
            <option value="">{empty.center}</option>
            {currentCenterMissing ? <option value={current!.workCenterId!}>{current!.workCenterName ?? 'Centro atual'}</option> : null}
            {(centers.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        )}
      </Field>
    </>
  )
}

/**
 * "Registrar movimentação" (CA 1 e 2): o formulário vem com o lugar atual e a data de hoje; o usuário troca o que muda e diz o motivo. Nada
 * mudou não confirma (o servidor também recusa, 422).
 */
export function MoveAssetDialog({ asset, open, onOpenChange, suggestedReason }: { asset: AssetDetail; open: boolean; onOpenChange: (o: boolean) => void; suggestedReason?: string }) {
  const refresh = useRefreshAsset(asset.id)
  const [draft, setDraft] = useState(() => movementDraft(asset, todayIso(), suggestedReason))
  const { errors, setErrors, onFieldErrors } = useFieldErrors(['siteId', 'responsibleEmployeeId', 'workCenterId', 'movedOn', 'reason'])
  const set = (patch: Partial<MovementDraft>) => { setErrors({}); setDraft((d) => ({ ...d, ...patch })) }
  const changed = movementChanged(draft, asset)
  const target = usePlaceNames(draft, asset)
  return (
    <ConfirmDialog verb="Registrar movimentação" done="Movimentação registrada" title={`Registrar movimentação de ${asset.tag}`} size="lg" open={open} onOpenChange={onOpenChange}
      description="Troque o local, o responsável ou o centro de trabalho. A movimentação fica no histórico do bem e não é apagada."
      onOpen={() => { setDraft(movementDraft(asset, todayIso(), suggestedReason)); setErrors({}) }}
      preconditions={[
        { ok: changed, text: 'Informe pelo menos um local, responsável ou centro diferente do atual.' },
        { ok: draft.reason.trim().length > 0, text: 'Informe o motivo.' },
      ]}
      impact={changed ? <ul className="text-sm">{movementChanges(asset, target).map((l) => <li key={l}>{l}</li>)}</ul> : undefined}
      onFieldErrors={onFieldErrors}
      onConfirm={async () => {
        refresh(await moveAsset(asset.id, {
          siteId: draft.siteId || null, responsibleEmployeeId: draft.responsibleEmployeeId || null, workCenterId: draft.workCenterId || null, movedOn: draft.movedOn || null,
          reason: draft.reason.trim(), version: asset.version,
        }))
      }}>
      <PlacementFields draft={draft} set={set} errors={errors} current={asset} />
      <Field label="Data" required error={errors.movedOn} hint="Até hoje.">{(p) => <DateInput {...p} value={draft.movedOn} onChange={(movedOn) => set({ movedOn })} />}</Field>
      <ReasonField value={draft.reason} onChange={(reason) => set({ reason })} error={errors.reason} />
    </ConfirmDialog>
  )
}

/** Nomes do destino para o impacto "de → para": o atual quando não mudou, senão o da lista. */
function usePlaceNames(d: Pick<MovementDraft, 'siteId' | 'responsibleEmployeeId' | 'workCenterId'>, a?: AssetDetail) {
  const sites = useListAssetSites()
  const responsibles = useListAssetResponsibleChoices()
  const centers = useListAssetWorkCenterChoices()
  const pick = (id: string, cur: string | null | undefined, curName: string | null | undefined, list: { id: string; name: string }[] | undefined) =>
    !id ? null : id === (cur ?? '') ? curName ?? null : list?.find((x) => x.id === id)?.name ?? null
  return {
    siteName: pick(d.siteId, a?.siteId, a?.siteName, sites.data),
    responsibleName: pick(d.responsibleEmployeeId, a?.responsibleEmployeeId, a?.responsibleName, responsibles.data),
    workCenterName: pick(d.workCenterId, a?.workCenterId, a?.workCenterName, centers.data),
  }
}

interface DisposalDraft {
  reason: DisposalReason | ''
  disposedOn: string
  value: string | null
  buyer: { id: string; label: string } | null
  notes: string
}

/**
 * "Baixar bem" (CA 6, `destructive`): motivo, data e, na venda, o valor e o comprador opcional. O bem passa a Baixado e some das listas e dos
 * seletores; nada é apagado. Na venda, `onDone` recebe o título a receber pré-preenchido (CA 7), quando houver.
 */
export function DisposeAssetDialog({ asset, open, onOpenChange, onDone }: { asset: AssetDetail; open: boolean; onOpenChange: (o: boolean) => void; onDone: (result: AssetMovementResult) => void }) {
  const refresh = useRefreshAsset(asset.id)
  const blank = (): DisposalDraft => ({ reason: '', disposedOn: todayIso(), value: null, buyer: null, notes: '' })
  const [draft, setDraft] = useState(blank)
  const { errors, setErrors, onFieldErrors } = useFieldErrors(['disposalReason', 'disposedOn', 'value', 'buyerPartyId', 'reason'])
  const set = (patch: Partial<DisposalDraft>) => { setErrors({}); setDraft((d) => ({ ...d, ...patch })) }
  const sale = draft.reason === 'Sale'
  return (
    <ConfirmDialog verb="Baixar bem" done="Bem baixado" destructive title={`Baixar ${asset.tag}?`} size="lg" open={open} onOpenChange={onOpenChange}
      description="O bem sai das listas e dos seletores, mas o cadastro e o histórico ficam. Se foi engano, dá para reativar."
      onOpen={() => { setDraft(blank()); setErrors({}) }}
      preconditions={[
        { ok: !!draft.reason, text: 'Escolha o motivo da baixa.' },
        { ok: !sale || Number(draft.value ?? 0) > 0, text: 'Informe o valor da venda.' },
        { ok: draft.reason !== 'Other' || draft.notes.trim().length > 0, text: 'Descreva o motivo da baixa.' },
      ]}
      impact={sale ? <p className="text-sm">A venda não lança nada no Financeiro. Depois de baixar, você pode lançar o título a receber já preenchido.</p> : undefined}
      onFieldErrors={onFieldErrors}
      onConfirm={async () => {
        const result = await disposeAsset(asset.id, {
          disposalReason: draft.reason as AssetDisposalReason, disposedOn: draft.disposedOn || null, reason: draft.notes.trim() || null,
          value: sale && draft.value != null ? Number(draft.value) : null, buyerPartyId: sale ? draft.buyer?.id ?? null : null, version: asset.version,
        })
        refresh(result)
        onDone(result)
      }}>
      <Field label="Motivo da baixa" required error={errors.disposalReason}>
        {(p) => (
          <select {...p} value={draft.reason} onChange={(e) => set({ reason: e.target.value as DisposalReason | '' })} className={inputClass}>
            <option value="">Escolha</option>
            {DISPOSAL_REASON_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        )}
      </Field>
      <Field label="Data da baixa" required error={errors.disposedOn} hint="Até hoje.">{(p) => <DateInput {...p} value={draft.disposedOn} onChange={(disposedOn) => set({ disposedOn })} />}</Field>
      {sale ? (
        <>
          <Field label="Valor da venda" required error={errors.value}>{(p) => <MoneyInput {...p} value={draft.value} onChange={(value) => set({ value })} />}</Field>
          <Field label="Comprador" error={errors.buyerPartyId} hint="Opcional. Vem preenchido no título a receber.">
            {(p) => (
              <Combobox {...p} queryKey={['asset-buyers']} placeholder="Buscar cliente" selected={draft.buyer}
                search={async (q) => (await listAssetBuyerChoices({ q })).map((b) => ({ id: b.id, label: b.name, hint: b.document ?? undefined }))}
                onChange={(o) => set({ buyer: o ? { id: o.id, label: o.label } : null })} />
            )}
          </Field>
        </>
      ) : null}
      <ReasonField label="Observação" required={draft.reason === 'Other'} value={draft.notes} onChange={(notes) => set({ notes })} error={errors.reason} />
    </ConfirmDialog>
  )
}

/** "Reativar bem" (CA 8): o baixado por engano volta a Ativo com o motivo; nada é apagado. */
export function ReactivateAssetDialog({ asset, open, onOpenChange, onDone }: { asset: AssetDetail; open: boolean; onOpenChange: (o: boolean) => void; onDone: (result: AssetMovementResult) => void }) {
  const refresh = useRefreshAsset(asset.id)
  const [reason, setReason] = useState('')
  const { errors, setErrors, onFieldErrors } = useFieldErrors(['reason', 'reactivatedOn'])
  return (
    <ConfirmDialog verb="Reativar bem" done="Bem reativado" title={`Reativar ${asset.tag}?`} open={open} onOpenChange={onOpenChange}
      description="O bem volta a Ativo, no mesmo local e com o mesmo responsável. A baixa continua no histórico."
      onOpen={() => { setReason(''); setErrors({}) }}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
      onFieldErrors={onFieldErrors}
      onConfirm={async () => {
        const result = await reactivateAsset(asset.id, { reason: reason.trim(), version: asset.version })
        refresh(result)
        onDone(result)
      }}>
      <ReasonField value={reason} onChange={(v) => { setErrors({}); setReason(v) }} error={errors.reason} />
    </ConfirmDialog>
  )
}

/** Aviso fixo na ficha (some quando o usuário fecha). */
export function Notice({ tone, children, onClose }: { tone: 'warn' | 'ok'; children: ReactNode; onClose: () => void }) {
  const cls = tone === 'warn' ? 'border-warn-soft bg-warn-soft text-warn-ink' : 'border-ok-soft bg-ok-soft text-ok-ink'
  return (
    <div role="status" className={`mb-4 flex flex-wrap items-center gap-2 rounded-md border px-3 py-2 text-sm ${cls}`}>
      <span className="flex-1">{children}</span>
      <button type="button" onClick={onClose} className="min-h-11 font-semibold">Fechar</button>
    </div>
  )
}
