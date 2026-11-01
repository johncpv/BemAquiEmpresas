import { Link } from '@tanstack/react-router'
import { Eraser, ListChecks, RotateCcw } from 'lucide-react'
import { useState } from 'react'
import { anonymizePrivacySubject, previewPrivacyAnonymization, useGetTwoFactorStatus } from '@/api/generated/bemaqui'
import type { PrivacyAnonymizationPreview, PrivacyRequestDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { TwoFactorCodeField } from '@/components/form/two-factor-code'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatDate, formatInstantDate } from '@/lib/format'
import { ANONYMIZATION_DICAS, partialText, planBlocks, type PlanLine, type PlanModule } from './anonymization'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-50'
const destructive = 'inline-flex min-h-11 items-center gap-1.5 rounded-md bg-crit px-3 py-2 text-sm font-semibold text-critical-foreground disabled:opacity-50'

function Block({ title, lines, empty, dica }: { title: string; lines: PlanLine[]; empty: string; dica?: { nome: string; texto: string } }) {
  return (
    <section aria-label={title} className="rounded-md border border-border">
      <h3 className="flex items-center border-b border-border bg-muted px-3 py-2 text-sm font-semibold">
        {title} ({lines.length}){dica ? <Dica nome={dica.nome} texto={dica.texto} /> : null}
      </h3>
      {lines.length === 0 ? <p className="px-3 py-2 text-sm text-muted-foreground">{empty}</p> : (
        <ul className="divide-y divide-border text-sm">
          {lines.map((l) => (
            <li key={l.key} className="px-3 py-2">
              <span className="block font-medium">{l.label} <span className="tabular-nums text-muted-foreground">({l.count})</span></span>
              <span className="block text-muted-foreground">{l.module}{l.detail ? ` · ${l.detail}` : ''}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/**
 * Anonimização no pedido do titular (LGPD-01b): "Prever anonimização" (prévia por módulo em três blocos), "Anonimizar" com o nome digitado e o
 * 2FA de novo (padrão 7, ConfirmDialog destrutivo, impacto por módulo, irreversível), os estados anonimizando, parcial com "Tentar de novo",
 * concluído e erro, e o aviso dos backups. Empilha a 400 px; o diálogo fica em tela cheia.
 */
export function AnonymizationPanel({ request, onChanged }: { request: PrivacyRequestDetail; onChanged: () => Promise<unknown> }) {
  const [preview, setPreview] = useState<PrivacyAnonymizationPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [code, setCode] = useState('')
  const twoFactor = useGetTwoFactorStatus({ query: { enabled: open } })
  const status = request.anonymizationStatus
  const steps = request.anonymizationSteps ?? []
  const running = status === 'Running'
  const partial = status === 'Partial'
  const done = status === 'Done'

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setPreview(await previewPrivacyAnonymization(request.id))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível montar a prévia. Tente de novo.')
    } finally {
      setLoading(false)
    }
  }

  const modules = (preview?.modules ?? []) as PlanModule[]
  const blocks = planBlocks(modules)
  const blockers = preview?.blockers ?? []
  const verb = partial ? 'Tentar de novo' : 'Anonimizar'

  if (done) {
    return (
      <div role="status" className="mt-3 space-y-1 rounded-md border border-ok-soft bg-ok-soft px-3 py-2 text-sm">
        <p className="font-semibold">Dados anonimizados{request.anonymizedAt ? ` em ${formatInstantDate(request.anonymizedAt)}` : ''}{request.anonymizedByName ? ` por ${request.anonymizedByName}` : ''}.</p>
        {request.anonymizationSummary ? (
          <p className="tabular-nums">
            {request.anonymizationSummary.anonymized} anonimizados · {request.anonymizationSummary.retained} retidos
            {request.anonymizationSummary.retainedUntil ? ` (até ${formatDate(request.anonymizationSummary.retainedUntil)})` : ''} · {request.anonymizationSummary.kept} documentos mantidos
          </p>
        ) : null}
        <p className="text-muted-foreground">A anonimização não pode ser desfeita. Registre a resposta ao titular e conclua o pedido.</p>
      </div>
    )
  }

  return (
    <div className="mt-3 space-y-3">
      {running ? <p role="status" className="text-sm text-muted-foreground">Anonimizando… cada módulo roda numa etapa própria e pode levar alguns minutos. Você pode sair desta tela: o aviso chega no sino.</p> : null}
      {partial ? (
        <div role="alert" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
          <p className="font-semibold">{partialText(steps)}</p>
          <ul className="mt-1 list-disc pl-5">{steps.filter((s) => s.status === 'Failed' && s.error).map((s) => <li key={s.module}>{s.error}</li>)}</ul>
          <p className="mt-1">Clique em "Tentar de novo": só os módulos que faltam são refeitos.</p>
        </div>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <button type="button" onClick={() => void load()} disabled={loading || running || !request.canAnonymize} className={button}
          title={request.identityVerified ? undefined : 'Confira a identidade antes de anonimizar.'}>
          <ListChecks aria-hidden="true" className="size-4" /> Prever anonimização
        </button>
        {preview ? (
          <ConfirmDialog size="lg" destructive verb={verb} done="Anonimização iniciada" open={open} onOpenChange={setOpen} onOpen={() => setCode('')}
            title={`Anonimizar os dados de ${preview.confirmationText}?`}
            description={<>A anonimização é <b>irreversível</b>: não existe desfazer. Cada dado pessoal da prévia é trocado pelo marcador "{preview.marker}"; os documentos com movimento continuam, e o que tem obrigação legal fica retido.</>}
            typed={{ word: preview.confirmationText, label: `Para confirmar, digite o nome do titular: ${preview.confirmationText}` }}
            twoFactorRequired={twoFactor.data ? !twoFactor.data.enabled : false}
            preconditions={blockers.map((b) => ({ ok: false, text: b }))}
            impactList={{ label: 'Será anonimizado', items: blocks.anonymize.map((l) => ({ key: l.key, primary: `${l.label} (${l.count})`, secondary: l.module })),
              refused: blocks.retain.map((l) => ({ key: l.key, primary: `${l.label} (${l.count})`, reason: `${l.module} · ${l.detail ?? ''}` })) }}
            onConfirm={async () => {
              if (code.length !== 6) throw new DialogMessage('Digite os 6 dígitos do aplicativo autenticador.')
              await anonymizePrivacySubject(request.id, { confirmation: preview.confirmationText, twoFactorCode: code, version: request.version })
              setPreview(null)
              await onChanged()
            }}
            trigger={<button type="button" disabled={running || !request.canAnonymize} className={destructive}>{partial ? <RotateCcw aria-hidden="true" className="size-4" /> : <Eraser aria-hidden="true" className="size-4" />} {verb}</button>}>
            <TwoFactorCodeField code={code} onChange={setCode} />
          </ConfirmDialog>
        ) : null}
      </div>
      {loading ? <Skeleton className="h-40" /> : null}
      {error ? <ErrorState title="Não foi possível montar a prévia." message={error} onRetry={() => void load()} /> : null}
      {preview && !loading ? (
        <div className="space-y-3">
          {preview.deletionNotice ? <p className="rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm">{preview.deletionNotice}</p> : null}
          {blockers.length > 0 ? <ul role="alert" className="list-disc rounded-md border border-crit-soft bg-crit-soft py-2 pl-8 pr-3 text-sm text-crit-ink">{blockers.map((b) => <li key={b}>{b}</li>)}</ul> : null}
          <div className="grid gap-3 lg:grid-cols-3">
            <Block title="Será anonimizado" lines={blocks.anonymize} empty="Nada a anonimizar agora." dica={{ nome: 'Marcador', texto: ANONYMIZATION_DICAS.marcador }} />
            <Block title="Fica retido até…" lines={blocks.retain} empty="Nada fica retido." dica={{ nome: 'Retido', texto: ANONYMIZATION_DICAS.retido }} />
            <Block title="Documentos mantidos" lines={blocks.keep} empty="Nenhum documento com movimento." />
          </div>
          <p className="text-xs text-muted-foreground">
            Os prazos de retenção estão em <Link to={'/app/configuracoes/privacidade/retencao' as never} className="font-semibold underline">Configurações → Privacidade → Retenção</Link>.
          </p>
        </div>
      ) : null}
      {request.backupNotice ? <p className="text-xs text-muted-foreground">{request.backupNotice}</p> : null}
    </div>
  )
}
