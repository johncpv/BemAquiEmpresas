import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from '@tanstack/react-router'
import { ArrowLeft, Download, FileSearch, PencilLine, ShieldCheck } from 'lucide-react'
import { useState } from 'react'
import {
  concludePrivacyRequest, createPrivacyExportLink, exportPrivacySubjectData, getGetPrivacyRequestHistoryQueryKey, getGetPrivacyRequestQueryKey, openPrivacyCorrection,
  refusePrivacyRequest, useGetPrivacyRequest, useGetPrivacyRequestHistory, verifyPrivacySubjectIdentity,
} from '@/api/generated/bemaqui'
import type { PrivacyRequestDetail, IdentityCheckMethod } from '@/api/generated/model'
import { apiDownload, ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { notifyError } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDate, formatInstantDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { OwnerOnlyNotice } from './PrivacyRequestsPage'
import { channelLabel, dueChipOf, exportOf, exportsData, IDENTITY_METHODS, identityMethodLabel, kindLabel, nextStepOf, statusOf, subjectTypeLabel } from './privacy'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-50'
const primary = 'inline-flex min-h-11 items-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50'
const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

const ACTIONS: Record<string, string> = { Created: 'Registrado', Updated: 'Alterado', SensitiveRead: 'Leitura dos dados (exportação ou download)', Exported: 'Exportação gerada' }

/**
 * Pedido do titular (LGPD-01a, /app/configuracoes/privacidade/$id): "Conferir identidade", "Exportar dados do titular" (assíncrono, com o
 * estado de demora e o erro com o módulo que falhou), "Baixar exportação" (link de uso único), "Abrir cadastro" (Correção), "Concluir pedido" e
 * "Recusar", e o histórico com quem, quando, o quê e o IP. Sem a identidade conferida, exportar e abrir o cadastro ficam desabilitados com o motivo.
 */
export function PrivacyRequestPage() {
  const { id } = useParams({ strict: false }) as { id: string }
  const { isOwner } = useNavigation()
  const client = useQueryClient()
  const navigate = useNavigate()
  const query = useGetPrivacyRequest(id, { query: { enabled: isOwner, retry: false, refetchInterval: (q) => (q.state.data?.exportStatus === 'Running' ? 3000 : false) } })
  const history = useGetPrivacyRequestHistory(id, { query: { enabled: isOwner, retry: false } })

  if (!isOwner || (query.error instanceof ApiError && query.error.code === 'forbidden')) {
    return (
      <>
        <PageHeading title="Pedido do titular" />
        <SettingsTabs />
        <OwnerOnlyNotice />
      </>
    )
  }

  const reload = async () => {
    await client.invalidateQueries({ queryKey: getGetPrivacyRequestQueryKey(id) })
    await client.invalidateQueries({ queryKey: getGetPrivacyRequestHistoryQueryKey(id) })
  }

  const back = <Link to="/app/configuracoes/privacidade" className="inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-muted-foreground hover:text-foreground"><ArrowLeft aria-hidden="true" className="size-4" /> Pedidos do titular</Link>
  if (query.isPending) return <><PageHeading title="Pedido do titular" />{back}<Skeleton className="h-64" /></>
  if (query.isError) {
    return (
      <>
        <PageHeading title="Pedido do titular" />
        {back}
        <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      </>
    )
  }

  const r = query.data
  const status = statusOf(r.status)
  const due = dueChipOf(r.dueChip)
  const exported = exportOf(r.exportStatus)
  const open = r.status === 'Open'
  const needsIdentity = r.requiresIdentity && !r.identityVerified
  const download = async () => {
    try {
      const link = await createPrivacyExportLink(r.id)
      await apiDownload(link.url, link.fileName)
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível baixar a exportação.', () => void download())
    }
  }

  const correction = async () => {
    try {
      const target = await openPrivacyCorrection(r.id, { version: r.version })
      await reload()
      void navigate({ to: target.link as never })
    } catch (e) {
      notifyError(e instanceof ApiError ? e.message : 'Não foi possível abrir o cadastro.')
    }
  }

  return (
    <>
      <PageHeading title={`Pedido ${r.number}`}>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
          {due ? <StatusChip tone={due.tone}>{due.label}</StatusChip> : null}
          {exported ? <StatusChip tone={exported.tone}>{exported.label}</StatusChip> : null}
        </div>
      </PageHeading>
      {back}
      <div className="mt-3 grid gap-4 lg:grid-cols-[2fr_1fr]">
        <div className="space-y-4">
          <section aria-labelledby="privacy-subject" className="rounded-md border border-border bg-card p-5">
            <h2 id="privacy-subject" className="font-semibold">Titular e pedido</h2>
            <dl className="mt-3 grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
              <div><dt className="text-muted-foreground">Titular</dt><dd className="font-semibold">{r.subjectName}</dd></div>
              <div><dt className="text-muted-foreground">Quem é</dt><dd>{subjectTypeLabel(r.subjectType)}</dd></div>
              {r.subjectContact ? <div><dt className="text-muted-foreground">Contato</dt><dd>{r.subjectContact}</dd></div> : null}
              <div><dt className="text-muted-foreground">Tipo</dt><dd>{kindLabel(r.kind)}</dd></div>
              <div><dt className="text-muted-foreground">Data do pedido</dt><dd className="tabular-nums">{formatDate(r.requestedOn)} · {channelLabel(r.channel)}</dd></div>
              <div><dt className="flex items-center text-muted-foreground">Prazo<Dica nome="Prazo" texto="Data do pedido mais os dias corridos de resposta (Configurações → Privacidade)." /></dt><dd className="tabular-nums">{formatDate(r.dueOn)}</dd></div>
              {r.notes ? <div className="sm:col-span-2"><dt className="text-muted-foreground">Observação</dt><dd>{r.notes}</dd></div> : null}
            </dl>
          </section>

          <section aria-labelledby="privacy-identity" className={'rounded-md border border-border bg-card p-5 ' + (needsIdentity && open ? 'border-l-4 border-l-warn' : '')}>
            <h2 id="privacy-identity" className="flex items-center font-semibold">
              Identidade do titular
              <Dica nome="Conferir identidade" texto="Confirme que quem pediu é mesmo o titular antes de entregar ou mexer nos dados. Registre como conferiu, sem copiar o documento." />
            </h2>
            {r.identityVerified ? (
              <p className="mt-2 text-sm">
                Conferida em {r.identityCheckedOn ? formatDate(r.identityCheckedOn) : formatInstantDate(r.identityVerifiedAt!)} por {r.identityVerifiedByName ?? 'Proprietário'}: {identityMethodLabel(r.identityMethod)}.
                {r.identityNotes ? <span className="block text-muted-foreground">{r.identityNotes}</span> : null}
              </p>
            ) : (
              <p className="mt-2 text-sm text-muted-foreground">{r.requiresIdentity ? 'Ainda não conferida. Sem ela, exportar e abrir o cadastro ficam desabilitados.' : 'Este tipo de pedido não exige a conferência, mas ela pode ser registrada.'}</p>
            )}
            {open && !r.identityVerified ? <div className="mt-3"><VerifyIdentityDialog request={r} onDone={reload} /></div> : null}
          </section>

          {open ? (
            <section aria-labelledby="privacy-actions" className="rounded-md border border-border bg-card p-5">
              <h2 id="privacy-actions" className="font-semibold">Atendimento</h2>
              <p className="mt-1 text-sm text-muted-foreground">{nextStepOf(r.kind, r.identityVerified, r.exportStatus)}</p>
              <div className="mt-3 flex flex-wrap gap-2">
                {exportsData(r.kind) ? (
                  <>
                    <ConfirmDialog verb="Exportar" done="Exportação pedida" title="Exportar os dados do titular?"
                      description="O sistema reúne o que cada módulo guarda do titular num ZIP com PDF, JSON e os documentos anexados. Avisaremos no sino quando estiver pronto."
                      preconditions={[{ ok: r.identityVerified, text: 'Confira a identidade do titular antes de exportar.' }, { ok: r.exportStatus !== 'Running', text: 'A exportação já está sendo gerada.' }]}
                      onConfirm={async () => { await exportPrivacySubjectData(r.id, { version: r.version }); await reload() }}
                      trigger={<button type="button" disabled={!r.canExport || r.exportStatus === 'Running'} title={r.identityVerified ? undefined : 'Confira a identidade antes de exportar.'} className={primary}><FileSearch aria-hidden="true" className="size-4" /> Exportar dados do titular</button>} />
                    {r.exportStatus === 'Ready' ? <button type="button" onClick={() => void download()} className={button}><Download aria-hidden="true" className="size-4" /> Baixar exportação</button> : null}
                  </>
                ) : null}
                {r.kind === 'Correction' ? (
                  <button type="button" onClick={() => void correction()} disabled={!r.identityVerified} title={r.identityVerified ? undefined : 'Confira a identidade antes de abrir o cadastro.'} className={primary}><PencilLine aria-hidden="true" className="size-4" /> Abrir cadastro</button>
                ) : null}
                {r.kind === 'Anonymization' || r.kind === 'Deletion' ? (
                  <button type="button" disabled title="A anonimização entra numa próxima versão." className={button}>Prever anonimização</button>
                ) : null}
                <ConcludeDialog request={r} onDone={reload} />
                <RefuseDialog request={r} onDone={reload} />
              </div>
              {r.exportStatus === 'Running' ? <p role="status" className="mt-3 text-sm text-muted-foreground">Gerando a exportação… pode levar alguns minutos. Você pode sair desta tela: o aviso chega no sino.</p> : null}
              {r.exportStatus === 'Failed' ? <p role="alert" className="mt-3 text-sm font-medium text-crit-ink">{r.exportError ?? 'A exportação falhou.'} Tente exportar de novo; se continuar, fale com o suporte.</p> : null}
              {r.exportStatus === 'Ready' && r.exportedAt ? <p className="mt-3 text-xs text-muted-foreground">Gerada em {formatInstantDate(r.exportedAt)}. O link de download vale uma vez e por 15 minutos.</p> : null}
              {r.correctionLink ? <p className="mt-3 text-sm">Cadastro aberto para correção: <Link to={r.correctionLink as never} className="font-semibold underline">abrir de novo</Link></p> : null}
            </section>
          ) : (
            <section aria-labelledby="privacy-answer" className="rounded-md border border-border bg-card p-5">
              <h2 id="privacy-answer" className="font-semibold">{r.status === 'Done' ? 'Resposta' : 'Recusa'}</h2>
              {r.status === 'Done' ? (
                <p className="mt-2 text-sm">{r.response} <span className="block text-muted-foreground">Respondido em {r.respondedOn ? formatDate(r.respondedOn) : '—'}{r.fileSent ? ' · arquivo enviado' : ''}</span></p>
              ) : <p className="mt-2 text-sm">{r.refusalReason}</p>}
              {r.exportStatus === 'Ready' ? <button type="button" onClick={() => void download()} className={button + ' mt-3'}><Download aria-hidden="true" className="size-4" /> Baixar exportação</button> : null}
              {r.exportExpiresAt ? <p className="mt-2 text-xs text-muted-foreground">O arquivo da exportação é apagado em {formatInstantDate(r.exportExpiresAt)}.</p> : null}
            </section>
          )}
        </div>

        <section aria-labelledby="privacy-history" className="rounded-md border border-border bg-card p-5">
          <h2 id="privacy-history" className="flex items-center gap-2 font-semibold"><ShieldCheck aria-hidden="true" className="size-4" /> Histórico do pedido</h2>
          {history.isPending ? <Skeleton className="mt-3 h-32" /> : history.isError ? <ErrorState title="Não foi possível carregar o histórico." onRetry={() => void history.refetch()} /> : history.data.length === 0 ? (
            <p className="mt-3 text-sm text-muted-foreground">Nenhum registro ainda.</p>
          ) : (
            <ol className="mt-3 space-y-2 text-sm">
              {history.data.map((h) => (
                <li key={h.id} className="border-b border-border pb-2 last:border-b-0">
                  <b className="font-semibold">{ACTIONS[h.action] ?? h.action}</b>
                  <span className="block text-muted-foreground tabular-nums">{formatInstantDate(h.at)} · {h.userName}{h.ip ? ` · IP ${h.ip}` : ''}</span>
                </li>
              ))}
            </ol>
          )}
        </section>
      </div>
    </>
  )
}

function VerifyIdentityDialog({ request, onDone }: { request: PrivacyRequestDetail; onDone: () => Promise<unknown> }) {
  const [method, setMethod] = useState<NonNullable<IdentityCheckMethod>>('DocumentPresented')
  const [checkedOn, setCheckedOn] = useState(today())
  const [notes, setNotes] = useState('')
  return (
    <ConfirmDialog verb="Conferir identidade" done="Identidade conferida" title="Conferir a identidade do titular"
      description="Registre como você conferiu que quem pediu é o titular. Não copie o documento para o sistema."
      onOpen={() => { setMethod('DocumentPresented'); setCheckedOn(today()); setNotes('') }}
      onConfirm={async () => { await verifyPrivacySubjectIdentity(request.id, { method, checkedOn, notes: notes.trim() || null, version: request.version }); await onDone() }}
      trigger={<button type="button" className={primary}><ShieldCheck aria-hidden="true" className="size-4" /> Conferir identidade</button>}>
      <div className="space-y-3">
        <Field label="Como conferiu" required>
          {(p) => <select {...p} value={method} onChange={(e) => setMethod(e.target.value as NonNullable<IdentityCheckMethod>)} className={inputClass}>{IDENTITY_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}</select>}
        </Field>
        <Field label="Data" required>
          {(p) => <input {...p} type="date" max={today()} value={checkedOn} onChange={(e) => setCheckedOn(e.target.value)} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Observação" hint="Ex.: RG apresentado na recepção. Até 500 caracteres.">
          {(p) => <textarea {...p} rows={2} maxLength={500} value={notes} onChange={(e) => setNotes(e.target.value)} className={inputClass} />}
        </Field>
      </div>
    </ConfirmDialog>
  )
}

function ConcludeDialog({ request, onDone }: { request: PrivacyRequestDetail; onDone: () => Promise<unknown> }) {
  const [respondedOn, setRespondedOn] = useState(today())
  const [response, setResponse] = useState('')
  const [fileSent, setFileSent] = useState(request.exportStatus === 'Ready')
  return (
    <ConfirmDialog verb="Concluir pedido" done="Pedido concluído" title="Registrar a resposta e concluir o pedido"
      onOpen={() => { setRespondedOn(today()); setResponse(''); setFileSent(request.exportStatus === 'Ready') }}
      preconditions={[{ ok: response.trim().length > 0, text: 'Descreva o que foi feito.' }]}
      onConfirm={async () => { await concludePrivacyRequest(request.id, { respondedOn, response: response.trim(), fileSent, version: request.version }); await onDone() }}
      trigger={<button type="button" className={button}>Registrar resposta</button>}>
      <div className="space-y-3">
        <Field label="Data da resposta" required>
          {(p) => <input {...p} type="date" max={today()} value={respondedOn} onChange={(e) => setRespondedOn(e.target.value)} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="O que foi feito" required hint="Até 500 caracteres.">
          {(p) => <textarea {...p} rows={3} maxLength={500} value={response} onChange={(e) => setResponse(e.target.value)} className={inputClass} />}
        </Field>
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" checked={fileSent} onChange={(e) => setFileSent(e.target.checked)} className="size-4" /> Arquivo enviado ao titular
        </label>
      </div>
    </ConfirmDialog>
  )
}

function RefuseDialog({ request, onDone }: { request: PrivacyRequestDetail; onDone: () => Promise<unknown> }) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog verb="Recusar" done="Pedido recusado" title="Recusar o pedido do titular?" destructive
      description="Informe o motivo, por exemplo retenção legal ou titular não identificado. O pedido não é apagado."
      onOpen={() => setReason('')}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo da recusa.' }]}
      onConfirm={async () => { await refusePrivacyRequest(request.id, { reason: reason.trim(), version: request.version }); await onDone() }}
      trigger={<button type="button" className={button}>Recusar</button>}>
      <Field label="Motivo" required hint="Até 500 caracteres.">
        {(p) => <textarea {...p} rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}
