import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { Copy } from 'lucide-react'
import { type ReactNode, useState } from 'react'
import { getGetFiscalIntegrationQueryKey, saveFiscalIntegration, testFiscalIntegration, useGetFiscalIntegration } from '@/api/generated/bemaqui'
import type { FiscalConnectionTest, FiscalIntegrationView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DialogMessage } from '@/components/ui/dialog-message'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatInstantDate } from '@/lib/format'
import { z } from '@/lib/zod'
import { ModuleGate } from '@/pages/ModuleGate'
import { useCan } from '@/pages/use-can'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { ENVIRONMENT_LABEL, formatCnpj, HINTS, integrationStatus, isTestEnvironment, isTotpCode, NOT_ALLOWED, NOT_CONFIGURED, pendingText, scopeLabel } from './fiscal-integration'
import { IntegrationsNav } from './IntegrationsNav'

const primary = 'min-h-[44px] rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'min-h-[44px] rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'

const schema = z.object({ baseUrl: z.string().trim().min(1, 'Informe o endereço interno do Emissor Fiscal.'), apiKey: z.string(), webhookSecret: z.string() })
type Values = z.input<typeof schema>

/**
 * Configurações → Integrações → Emissor fiscal (FIS-01 CA 7 a 10 e 18): Conexão (URL, chave e segredo do aviso só de escrita), Emitente e
 * ambiente (do último teste) e Situação. Salvar pede o código do autenticador (2FA de novo); "Testar conexão" confere escopos, CNPJ e
 * ambiente; aprovado, o próximo passo é configurar o fiscal da empresa. Sem settings.edit, só leitura.
 */
export function FiscalIntegrationPage() {
  return (
    <ModuleGate code="fiscal">
      <FiscalIntegration />
    </ModuleGate>
  )
}

function FiscalIntegration() {
  const client = useQueryClient()
  const query = useGetFiscalIntegration({ query: { gcTime: 0 } })
  const canEdit = useCan('settings.edit')
  const [editing, setEditing] = useState(false)
  const [test, setTest] = useState<FiscalConnectionTest | null>(null)
  const refresh = () => client.invalidateQueries({ queryKey: getGetFiscalIntegrationQueryKey() })
  const view = query.data

  return (
    <>
      <PageHeading title="Integrações: Emissor fiscal">
        <p className="mt-2 text-muted-foreground">O BemAqui emite as notas pelo Emissor Fiscal, na rede interna. Aqui ficam o endereço, a chave e o segredo do aviso.</p>
      </PageHeading>
      <SettingsTabs />
      <IntegrationsNav />
      {query.isPending ? <Skeleton className="h-64 max-w-3xl" /> : query.isError ? (
        <ErrorState className="max-w-3xl" message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => query.refetch()} />
      ) : !view!.allowed ? (
        <EmptyState className="max-w-3xl" title="Emissão não liberada nesta empresa." description={NOT_ALLOWED} />
      ) : (
        <div className="max-w-3xl space-y-4">
          <EnvironmentBanner service="sefaz" test={isTestEnvironment(view!)} />
          {test?.ok ? <NextStepsAfterTest onDismiss={() => setTest(null)} /> : null}
          {editing ? (
            <ConnectionForm view={view!} onCancel={() => setEditing(false)} onSaved={async () => { await refresh(); setEditing(false) }} />
          ) : !view!.configured ? (
            <EmptyState title="Emissor Fiscal não configurado." description={NOT_CONFIGURED}
              action={canEdit ? <button type="button" onClick={() => setEditing(true)} className={primary}>Configurar</button> : undefined} />
          ) : (
            <Summary view={view!} canEdit={canEdit} test={test} onEdit={() => setEditing(true)} onTested={async (t) => { setTest(t); await refresh() }} />
          )}
        </div>
      )}
    </>
  )
}

function NextStepsAfterTest({ onDismiss }: { onDismiss: () => void }) {
  const navigate = useNavigate()
  return <NextSteps onDismiss={onDismiss} steps={[{ label: 'Configurar o fiscal da empresa', onSelect: () => void navigate({ to: '/app/$module', params: { module: 'fiscal' } }) }]} />
}

function Row({ label, children, dica }: { label: string; children: ReactNode; dica?: string }) {
  return (
    <>
      <dt className="flex items-center text-muted-foreground">{label}{dica ? <Dica texto={dica} nome={label} /> : null}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </>
  )
}

function Summary({ view, canEdit, test, onEdit, onTested }: { view: FiscalIntegrationView; canEdit: boolean; test: FiscalConnectionTest | null; onEdit: () => void; onTested: (t: FiscalConnectionTest) => Promise<void> }) {
  const [testing, setTesting] = useState(false)
  const [error, setError] = useState<string>()
  const status = integrationStatus(view)
  const runTest = async () => {
    setTesting(true)
    setError(undefined)
    try {
      const result = await testFiscalIntegration()
      await onTested(result)
      if (result.ok) notifySuccess('Conexão testada')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível testar agora. Tente de novo em instantes.')
    } finally {
      setTesting(false)
    }
  }
  const failedTest = test && !test.ok ? test.message : view.lastTestResult && view.lastTestResult !== 'ok' ? view.lastTestMessage : null

  return (
    <>
      <section aria-labelledby="fiscal-conexao" className="space-y-3 rounded-md border border-border bg-card p-5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="fiscal-conexao" className="font-semibold">Conexão</h2>
          <StatusChip tone={status.tone}>{status.label}</StatusChip>
        </div>
        <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
          <Row label="Endereço">{view.baseUrl}</Row>
          <Row label="Chave de API">{view.apiKeySetAt ? <span className="tabular-nums">Configurada em {formatInstantDate(view.apiKeySetAt)}</span> : '—'}</Row>
          <Row label="Segredo do aviso" dica={HINTS.webhookSecret}>
            {view.webhookSecretSetAt ? <span className="tabular-nums">Configurado em {formatInstantDate(view.webhookSecretSetAt)}</span> : '—'}
            {view.webhookPreviousValidUntil ? <span className="block text-xs text-muted-foreground tabular-nums">O anterior vale até {formatDateTime(view.webhookPreviousValidUntil)}</span> : null}
          </Row>
          {view.webhookUrl ? (
            <Row label="Endereço do aviso">
              <span className="flex items-start gap-1">
                <code className="min-w-0 break-all text-xs">{view.webhookUrl}</code>
                <button type="button" aria-label="Copiar o endereço do aviso" onClick={() => { void navigator.clipboard?.writeText(view.webhookUrl!); notifySuccess('Copiado') }}
                  className="grid size-11 shrink-0 place-items-center rounded-md border border-border">
                  <Copy aria-hidden="true" className="size-4" />
                </button>
              </span>
            </Row>
          ) : null}
        </dl>
        {failedTest ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">{failedTest}</p> : null}
        {error ? <p role="alert" className="text-sm text-crit-ink">{error}</p> : null}
        {canEdit ? (
          <div className="flex flex-wrap gap-2 pt-1">
            <button type="button" onClick={() => void runTest()} disabled={testing} className={view.active ? secondary : primary}>{testing ? 'Testando…' : 'Testar conexão'}</button>
            <button type="button" onClick={onEdit} className={secondary}>Alterar conexão</button>
          </div>
        ) : <p className="text-xs text-muted-foreground">Só o Proprietário altera e testa a integração.</p>}
      </section>

      <section aria-labelledby="fiscal-emitente" className="space-y-3 rounded-md border border-border bg-card p-5">
        <h2 id="fiscal-emitente" className="font-semibold">Emitente e ambiente</h2>
        {view.issuerDocument ? (
          <dl className="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
            <Row label="Emitente">{view.issuerName} <span className="tabular-nums">({formatCnpj(view.issuerDocument)})</span></Row>
            <Row label="Ambiente" dica={HINTS.environment}>{view.environment ? ENVIRONMENT_LABEL[view.environment] ?? view.environment : '—'}</Row>
            <Row label="Escopos da chave" dica={HINTS.admin}>
              <span className="flex flex-wrap gap-1">{view.scopes.map((s) => <StatusChip key={s} tone="info">{scopeLabel(s)}</StatusChip>)}</span>
            </Row>
            <Row label="Certificado A1">{view.certificateValidUntil ? <span className="tabular-nums">Válido até {formatInstantDate(view.certificateValidUntil)}</span> : 'Não enviado ao Emissor Fiscal'}</Row>
            <Row label="Tenant no Emissor"><code className="break-all text-xs">{view.emitterTenantId}</code></Row>
          </dl>
        ) : <p className="text-sm text-muted-foreground">Aparece depois do primeiro "Testar conexão" aprovado.</p>}
      </section>

      <section aria-labelledby="fiscal-situacao" className="space-y-2 rounded-md border border-border bg-card p-5 text-sm">
        <h2 id="fiscal-situacao" className="font-semibold">Situação</h2>
        <p>Último teste: <span className="tabular-nums">{view.lastTestAt ? formatDateTime(view.lastTestAt) : 'nenhum'}</span></p>
        <p>Último aviso recebido: <span className="tabular-nums">{view.lastWebhookAt ? formatDateTime(view.lastWebhookAt) : 'nenhum'}</span></p>
        <p className={view.pendingInvoices > 0 ? 'font-semibold text-warn-ink' : undefined}>
          {pendingText(view.pendingInvoices)}
          {view.oldestPendingSince ? <span className="tabular-nums"> A mais antiga espera desde {formatDateTime(view.oldestPendingSince)}; o sistema confere a cada 10 minutos e nunca emite de novo.</span> : null}
        </p>
      </section>
    </>
  )
}

function ConnectionForm({ view, onCancel, onSaved }: { view: FiscalIntegrationView; onCancel: () => void; onSaved: () => Promise<unknown> }) {
  const form = useZodForm(schema, { defaultValues: { baseUrl: view.baseUrl?.replace(/\/$/, '') ?? view.defaultBaseUrl, apiKey: '', webhookSecret: '' } })
  const [replaceKey, setReplaceKey] = useState(!view.configured)
  const [replaceSecret, setReplaceSecret] = useState(!view.configured)
  const [confirming, setConfirming] = useState(false)
  const [code, setCode] = useState('')

  const missing = (v: Values) => {
    let ok = true
    if (replaceKey && !v.apiKey.trim()) { form.setError('apiKey', { type: 'manual', message: 'Informe a chave de API do Emissor Fiscal.' }); ok = false }
    if (replaceSecret && !v.webhookSecret.trim()) { form.setError('webhookSecret', { type: 'manual', message: 'Informe o segredo do aviso.' }); ok = false }
    return ok
  }

  const open = form.handleSubmit((v) => { if (missing(v)) { setCode(''); setConfirming(true) } })

  const save = async () => {
    if (!isTotpCode(code)) {
      throw new DialogMessage('Digite os 6 números do aplicativo autenticador.')
    }

    const v = form.getValues()
    await saveFiscalIntegration({
      baseUrl: v.baseUrl.trim(),
      apiKey: replaceKey ? v.apiKey.trim() : null,
      webhookSecret: replaceSecret ? v.webhookSecret.trim() : null,
      code: code.trim(),
    })
    await onSaved()
  }

  return (
    <form onSubmit={open} noValidate aria-label="Conexão com o Emissor Fiscal" className="space-y-4 rounded-md border border-border bg-card p-5">
      <FormError message={form.formState.errors.root?.server?.message} />
      <Field label="Endereço interno do Emissor Fiscal" required error={form.formState.errors.baseUrl?.message} hint={`O serviço na rede dedicada, por exemplo ${view.defaultBaseUrl}.`}>
        {(p) => <input {...p} {...form.register('baseUrl')} inputMode="url" autoComplete="off" spellCheck={false} className={inputClass} />}
      </Field>
      {replaceKey ? (
        <Field label="Chave de API" required error={form.formState.errors.apiKey?.message} hint={HINTS.apiKey} dica={HINTS.admin}>
          {(p) => <input {...p} type="password" autoComplete="new-password" spellCheck={false} {...form.register('apiKey')} className={inputClass} />}
        </Field>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <span className="tabular-nums">Chave configurada em {view.apiKeySetAt ? formatInstantDate(view.apiKeySetAt) : '—'}</span>
          <button type="button" onClick={() => setReplaceKey(true)} className={secondary}>Trocar a chave</button>
        </div>
      )}
      {replaceSecret ? (
        <Field label="Segredo do aviso (webhook)" required error={form.formState.errors.webhookSecret?.message} hint="Mostrado uma vez pelo Emissor Fiscal ao cadastrar o aviso." dica={HINTS.webhookSecret}>
          {(p) => <input {...p} type="password" autoComplete="new-password" spellCheck={false} {...form.register('webhookSecret')} className={inputClass} />}
        </Field>
      ) : (
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <span className="tabular-nums">Segredo configurado em {view.webhookSecretSetAt ? formatInstantDate(view.webhookSecretSetAt) : '—'}</span>
          <button type="button" onClick={() => setReplaceSecret(true)} className={secondary}>Trocar o segredo</button>
        </div>
      )}
      <div className="flex flex-wrap justify-end gap-2 pt-1">
        <button type="button" onClick={onCancel} className={secondary}>Voltar</button>
        <button type="submit" className={primary}>Salvar integração fiscal</button>
      </div>
      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        verb="Salvar integração fiscal"
        done="Integração fiscal salva"
        title="Confirme com o autenticador"
        description="A chave e o segredo são guardados cifrados e nunca são mostrados de novo. Digite o código do aplicativo autenticador para salvar."
        onConfirm={save}
        onFieldErrors={(e) => { const handled = applyServerErrors(form, e); if (handled) setConfirming(false); return handled }}
      >
        <label className="block space-y-1 text-sm font-medium">
          Código do autenticador
          <input value={code} onChange={(e) => { setCode(e.target.value.replace(/\D/g, '').slice(0, 6)) }}
            inputMode="numeric" autoComplete="one-time-code" className={inputClass + ' tabular-nums'} />
        </label>
      </ConfirmDialog>
    </form>
  )
}
