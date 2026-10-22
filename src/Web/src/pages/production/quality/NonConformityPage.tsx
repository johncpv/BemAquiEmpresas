import { useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from '@tanstack/react-router'
import { Camera } from 'lucide-react'
import { useRef, useState } from 'react'
import {
  closeNonConformity, getDownloadNonConformityAttachmentUrl, getGetNonConformityQueryKey, getListNonConformityAttachmentsQueryKey, setNonConformityCost, startNonConformityAction,
  updateNonConformity, uploadNonConformityAttachment, useGetNonConformity, useGetNonConformityHistory, useListNonConformityAttachments,
} from '@/api/generated/bemaqui'
import type { NonConformityDetail } from '@/api/generated/model'
import { ApiError, apiDownload } from '@/api/http'
import { FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { describeChanges } from '@/components/history/describe-changes'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { FactsBar } from '@/components/ui/facts-bar'
import { notifySuccess } from '@/components/ui/messages'
import { formatMoney } from '@/lib/format'
import { OrderHeader } from '@/components/ui/order-header'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { UrlTabs } from '@/components/ui/url-tabs'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { useCan } from '@/pages/use-can'
import { ageLabel, MAX_ACTION, MAX_CAUSE, MAX_DESCRIPTION, MAX_RESULT, NC_STATUS, QUALITY_DICAS } from './quality'

const primary = 'inline-flex min-h-10 items-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'inline-flex min-h-10 items-center rounded-md border border-border px-3 text-sm font-semibold'

/**
 * Não conformidade (/app/producao/nao-conformidades/$id, CA 8 a 10, 15): abas Detalhes (padrão), Documentos e Histórico de
 * alterações. Com production.edit: salvar, "Iniciar ação" e "Encerrar NC"; encerrada fica em modo leitura. Custo estimado só
 * com projects.view_cost, informativo.
 */
export function NonConformityPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const query = useGetNonConformity(id)
  if (query.isPending) return <Skeleton className="h-96" />
  if (query.isError) return <ErrorState title="Não foi possível carregar a não conformidade." message="Ela pode não existir. Volte à lista ou tente de novo." onRetry={() => query.refetch()} />
  const n = query.data
  const status = NC_STATUS[n.status]
  return (
    <>
      <OrderHeader title={`${n.number} — ${n.orderNumber}`} status={status} project={{ id: n.projectId, number: n.projectNumber }}
        meta={(
          <span className="flex flex-wrap items-center gap-x-3 text-sm text-muted-foreground">
            <span>{n.partyName}</span>
            <span>Qualidade: {n.stepCode} · {n.stepName}</span>
            <span className="tabular-nums">Tentativa {n.attempt}</span>
            <span className="tabular-nums">{ageLabel(n.ageDays)}</span>
          </span>
        )}
        actions={<Link to="/app/producao/os/$id" params={{ id: n.orderId }} search={{ aba: 'qualidade' } as never} className={secondary}>Ver a OS {n.orderNumber}</Link>} />
      {n.status === 'Closed' ? <p role="note" className="mb-4 rounded-md border border-border bg-muted px-3 py-2 text-sm">NC encerrada em {n.closedAt ? formatDateTime(n.closedAt) : '—'}{n.closedByName ? ` por ${n.closedByName}` : ''}. Só leitura.</p> : null}
      <FactsBar facts={[
        { label: 'Responsável', value: n.responsibleName },
        { label: 'Etapa de retorno', dica: QUALITY_DICAS.etapaRetorno, value: n.returnToStepName },
        { label: 'Aberta', value: <span className="tabular-nums">{formatDateTime(n.openedAt)}{n.openedByName ? <span className="block text-xs text-muted-foreground">{n.openedByName}</span> : null}</span> },
        { label: 'Ação iniciada', value: n.actionStartedAt ? <span className="tabular-nums">{formatDateTime(n.actionStartedAt)}</span> : '—' },
      ]} />
      <UrlTabs label="Seções da não conformidade" fallback="detalhes" tabs={[
        { id: 'detalhes', label: 'Detalhes', content: <Details key={n.version} nc={n} /> },
        { id: 'documentos', label: 'Documentos', content: <NcDocuments nc={n} /> },
        { id: 'historico', label: 'Histórico de alterações', content: <NcHistory id={n.id} /> },
      ]} />
    </>
  )
}

function Details({ nc: n }: { nc: NonConformityDetail }) {
  const client = useQueryClient()
  const canEdit = useCan('production.edit') && n.status !== 'Closed'
  const [description, setDescription] = useState(n.description)
  const [cause, setCause] = useState(n.cause ?? '')
  const [action, setAction] = useState(n.action ?? '')
  const [responsible, setResponsible] = useState(n.responsibleUserId)
  const [result, setResult] = useState('')
  const [cost, setCost] = useState(n.cost == null ? '' : String(n.cost).replace('.', ','))
  const [error, setError] = useState<string | undefined>()
  const [busy, setBusy] = useState(false)
  const refresh = (next: NonConformityDetail) => client.setQueryData(getGetNonConformityQueryKey(n.id), next)
  const dirty = description !== n.description || cause !== (n.cause ?? '') || action !== (n.action ?? '') || responsible !== n.responsibleUserId

  const save = async () => {
    setBusy(true)
    setError(undefined)
    try {
      refresh(await updateNonConformity(n.id, { description, cause: cause || null, action: action || null, responsibleUserId: responsible, version: n.version }))
      notifySuccess('NC salva')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar.')
    } finally {
      setBusy(false)
    }
  }

  const saveCost = async () => {
    setBusy(true)
    setError(undefined)
    try {
      const value = cost.trim() === '' ? null : Number(cost.replace(/\./g, '').replace(',', '.'))
      if (value !== null && (Number.isNaN(value) || value < 0)) throw new Error('Informe um custo de zero para cima.')
      refresh(await setNonConformityCost(n.id, { cost: value, version: n.version }))
      notifySuccess('Custo salvo')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Não foi possível salvar o custo.')
    } finally {
      setBusy(false)
    }
  }

  if (!canEdit) {
    return (
      <dl className="grid gap-4 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-2">
        <div className="sm:col-span-2"><dt className="font-medium text-muted-foreground">Descrição</dt><dd className="whitespace-pre-line">{n.description}</dd></div>
        <div><dt className="font-medium text-muted-foreground">Causa</dt><dd className="whitespace-pre-line">{n.cause ?? '—'}</dd></div>
        <div><dt className="font-medium text-muted-foreground">Ação corretiva</dt><dd className="whitespace-pre-line">{n.action ?? '—'}</dd></div>
        {n.result ? <div className="sm:col-span-2"><dt className="font-medium text-muted-foreground">Resultado</dt><dd className="whitespace-pre-line">{n.result}</dd></div> : null}
        {n.costVisible ? <div><dt className="flex items-center font-medium text-muted-foreground">Custo estimado<Dica nome="Custo estimado" texto={QUALITY_DICAS.custo} /></dt><dd className="tabular-nums">{n.cost == null ? '—' : formatMoney(n.cost)}</dd></div> : null}
      </dl>
    )
  }

  return (
    <div className="space-y-4">
      <FormError message={error} />
      <div className="grid gap-4 rounded-md border border-border bg-card p-4 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium sm:col-span-2">Descrição (obrigatória)
          <textarea rows={4} maxLength={MAX_DESCRIPTION} value={description} onChange={(e) => setDescription(e.target.value)} className={inputClass} />
        </label>
        <label className="space-y-1 text-sm font-medium">Causa
          <textarea rows={3} maxLength={MAX_CAUSE} value={cause} onChange={(e) => setCause(e.target.value)} className={inputClass} />
        </label>
        <label className="space-y-1 text-sm font-medium">Ação corretiva
          <textarea rows={3} maxLength={MAX_ACTION} value={action} onChange={(e) => setAction(e.target.value)} className={inputClass} />
        </label>
        <label className="space-y-1 text-sm font-medium">Responsável
          <select value={responsible} onChange={(e) => setResponsible(e.target.value)} className={inputClass}>
            {n.responsibles.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
          </select>
        </label>
        <div className="flex flex-wrap items-end gap-2 sm:col-span-2">
          <button type="button" className={primary} disabled={busy || !dirty || description.trim() === ''} onClick={() => void save()}>Salvar</button>
          {n.status === 'Open' ? (
            <ConfirmDialog verb="Iniciar ação" done="Ação iniciada" title={`Iniciar a ação da ${n.number}?`} description="A NC passa para Em ação com a causa e a ação corretiva abaixo."
              preconditions={[{ ok: cause.trim() !== '', text: 'Informe a causa.' }, { ok: action.trim() !== '', text: 'Informe a ação corretiva.' }]}
              impact={<p className="text-sm">Aberta → Em ação</p>}
              onConfirm={async () => refresh(await startNonConformityAction(n.id, { cause: cause.trim(), action: action.trim(), version: n.version }))}
              trigger={<button type="button" className={secondary}>Iniciar ação</button>} />
          ) : null}
          <ConfirmDialog verb="Encerrar NC" done="NC encerrada" title={`Encerrar a ${n.number}?`} description="Depois de encerrada, a NC fica só para leitura."
            onOpen={() => setResult('')}
            preconditions={[
              { ok: (n.cause ?? '').trim() !== '' || cause.trim() !== '', text: 'Informe e salve a causa.' },
              { ok: (n.action ?? '').trim() !== '' || action.trim() !== '', text: 'Informe e salve a ação corretiva.' },
              { ok: result.trim() !== '', text: 'Informe o resultado.' },
            ]}
            impact={<p className="text-sm">{NC_STATUS[n.status].label} → Encerrada</p>}
            onConfirm={async () => refresh(await closeNonConformity(n.id, { result: result.trim(), version: n.version }))}
            trigger={<button type="button" className={secondary}>Encerrar NC</button>}>
            <label className="mt-3 block space-y-1 text-sm font-medium">Resultado (obrigatório)
              <textarea rows={3} maxLength={MAX_RESULT} value={result} onChange={(e) => setResult(e.target.value)} className={inputClass} />
            </label>
          </ConfirmDialog>
        </div>
      </div>
      {n.costVisible ? (
        <div className="flex flex-wrap items-end gap-2 rounded-md border border-border bg-card p-4">
          <label className="space-y-1 text-sm font-medium">
            <span className="flex items-center">Custo estimado (R$)<Dica nome="Custo estimado" texto={QUALITY_DICAS.custo} /></span>
            <input inputMode="decimal" value={cost} onChange={(e) => setCost(e.target.value)} className={`${inputClass} tabular-nums`} />
          </label>
          <button type="button" className={secondary} disabled={busy} onClick={() => void saveCost()}>Salvar custo</button>
        </div>
      ) : null}
    </div>
  )
}

/** Documentos da NC (CA 8): fotos e evidências; anexar com production.edit ou production.approve_quality; "Tirar foto" no tablet. */
function NcDocuments({ nc: n }: { nc: NonConformityDetail }) {
  const list = useListNonConformityAttachments(n.id)
  const client = useQueryClient()
  const canEdit = useCan('production.edit')
  const canInspect = useCan('production.approve_quality')
  const canWrite = (canEdit || canInspect) && n.status !== 'Closed'
  const input = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | undefined>()
  const [sending, setSending] = useState(false)
  const upload = async (files: File[]) => {
    setSending(true)
    setError(undefined)
    try {
      for (const file of files) await uploadNonConformityAttachment(n.id, { file })
      notifySuccess(files.length === 1 ? 'Arquivo anexado' : 'Arquivos anexados')
      await client.invalidateQueries({ queryKey: getListNonConformityAttachmentsQueryKey(n.id) })
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível anexar o arquivo.')
    } finally {
      setSending(false)
      if (input.current) input.current.value = ''
    }
  }
  const action = canWrite ? (
    <>
      <input ref={input} id="anexo-nc" type="file" accept="image/png,image/jpeg,application/pdf" capture="environment" multiple className="sr-only"
        onChange={(e) => { const f = Array.from(e.target.files ?? []); if (f.length > 0) void upload(f) }} />
      <label htmlFor="anexo-nc" className="inline-flex min-h-[56px] cursor-pointer items-center gap-2 rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground">
        <Camera aria-hidden="true" className="size-5" />{sending ? 'Enviando…' : 'Tirar foto ou escolher arquivo'}
      </label>
    </>
  ) : null
  let body
  if (list.isPending) body = <Skeleton className="h-24" />
  else if (list.isError) body = <ErrorState onRetry={() => list.refetch()} />
  else if (list.data.length === 0) body = <EmptyState title="Nenhuma foto ou documento na NC." description="Anexe as fotos do defeito e as evidências da correção (PNG, JPG ou PDF até 10 MB)." action={action} />
  else {
    body = (
      <>
        <ul className="divide-y divide-border rounded-md border border-border bg-card text-sm">
          {list.data.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
              <button type="button" onClick={() => void apiDownload(getDownloadNonConformityAttachmentUrl(n.id, a.id), a.fileName)} className="font-semibold text-primary hover:underline">{a.fileName}</button>
              <span className="flex items-center gap-2 text-xs text-muted-foreground">{a.scanStatus === 1 ? null : <StatusChip tone="info">Em verificação</StatusChip>}{formatDateTime(a.createdAt)}{a.uploadedByName ? ` · ${a.uploadedByName}` : ''}</span>
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

/** Histórico da NC (CA 15): antes e depois, usuário e data. */
function NcHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  const query = useGetNonConformityHistory(id, { page, pageSize: 20 })
  if (query.isPending) return <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-10" />)}</div>
  if (query.isError) return <ErrorState title="Não foi possível carregar o histórico." onRetry={() => query.refetch()} />
  const data = query.data
  if (data.items.length === 0) return <EmptyState title="Sem alterações registradas." description="As mudanças na NC aparecem aqui, com quem fez e quando." />
  const pages = Math.max(1, Math.ceil(data.total / data.pageSize))
  return (
    <div>
      <ol className="space-y-3">
        {data.items.map((entry) => (
          <li key={entry.id} className="rounded-md border border-border bg-card p-3 text-sm">
            <p>
              <b className="font-semibold">{ACTIONS[entry.action] ?? entry.action}</b> <span>a NC</span>
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