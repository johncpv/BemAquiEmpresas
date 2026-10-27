import { useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router'
import { ArrowLeft, CheckCircle2, Loader2, XCircle } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { disableBankIntegration, getGetBankIntegrationQueryKey, getListBankIntegrationsQueryKey, testBankIntegration, useGetBankIntegration } from '@/api/generated/bemaqui'
import type { BankConnectionTest, BankIntegrationDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { RecordHistory } from '@/components/history/RecordHistory'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { notifySuccess } from '@/components/ui/messages'
import { NextSteps } from '@/components/ui/next-steps'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime, formatInstantDate } from '@/lib/format'
import { useNavigation } from '@/navigation/navigation-context'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from '../SettingsTabs'
import { useCanSettings } from '../use-can-settings'
import { InterCredentialsForm } from './InterCredentialsForm'
import { IntegrationsNav } from './IntegrationsNav'
import { environmentLabel, expiryText, HINTS, scopeLabel, situationOf, SLOW_AFTER_MS, TEST_PHASE_TEXT, type TestPhase, WAITING_AFTER_MS } from './inter'

function Section({ title, dica, children }: { title: string; dica?: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="space-y-2 rounded-md border border-border bg-card p-4">
      <h2 className="flex items-center text-sm font-semibold uppercase tracking-wide text-muted-foreground">{title}{dica ? <Dica texto={dica} nome={title} /> : null}</h2>
      {children}
    </section>
  )
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-wrap justify-between gap-x-4 gap-y-0.5 text-sm">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0 break-all text-right font-medium tabular-nums">{children}</dd>
    </div>
  )
}

/**
 * "Testar conexão" no padrão 6 (BNK-01a CA 7): Enviado, Aguardando, Demorando depois de 30 s, Confirmado ou Recusado com o motivo e
 * a ação; sem reenvio enquanto processa. A leitura é sem efeito (saldo).
 */
function useConnectionTest(bankAccountId: string, onFinished: () => void, onConfirmed: () => void) {
  const [phase, setPhase] = useState<TestPhase>('idle')
  const [result, setResult] = useState<BankConnectionTest | null>(null)
  const [refusal, setRefusal] = useState<string | null>(null)
  const timers = useRef<number[]>([])
  useEffect(() => () => timers.current.forEach((t) => window.clearTimeout(t)), [])

  const run = async () => {
    if (phase === 'sent' || phase === 'waiting' || phase === 'slow') return
    setPhase('sent')
    setResult(null)
    setRefusal(null)
    timers.current = [window.setTimeout(() => setPhase((p) => (p === 'sent' ? 'waiting' : p)), WAITING_AFTER_MS), window.setTimeout(() => setPhase((p) => (p === 'waiting' || p === 'sent' ? 'slow' : p)), SLOW_AFTER_MS)]
    try {
      const test = await testBankIntegration(bankAccountId)
      setResult(test)
      setPhase(test.ok ? 'confirmed' : 'refused')
      if (test.ok) {
        notifySuccess('Conexão testada')
        onConfirmed()
      }
    } catch (e) {
      setRefusal(e instanceof ApiError ? e.message : 'Não foi possível testar a conexão. Tente de novo.')
      setPhase('refused')
    } finally {
      timers.current.forEach((t) => window.clearTimeout(t))
      onFinished()
    }
  }

  return { phase, result, refusal, run, busy: phase === 'sent' || phase === 'waiting' || phase === 'slow' }
}

function ConnectionTestStatus({ phase, result, refusal }: { phase: TestPhase; result: BankConnectionTest | null; refusal: string | null }) {
  if (phase === 'idle') return null
  if (phase === 'sent' || phase === 'waiting' || phase === 'slow') {
    return <p role="status" className="flex items-center gap-2 text-sm"><Loader2 aria-hidden="true" className="size-4 animate-spin" />{TEST_PHASE_TEXT[phase]}</p>
  }

  if (phase === 'confirmed' && result) {
    return (
      <div role="status" className="space-y-1 text-sm">
        <p className="flex items-center gap-2 font-semibold text-ok-ink"><CheckCircle2 aria-hidden="true" className="size-4" />{result.message}</p>
        <p className="text-muted-foreground">Escopos concedidos: {result.grantedScopes.map(scopeLabel).join(', ')}.</p>
      </div>
    )
  }

  return (
    <div role="alert" className="space-y-1 rounded-md border border-crit-soft bg-crit-soft px-3 py-2 text-sm text-crit-ink">
      <p className="flex items-center gap-2 font-semibold"><XCircle aria-hidden="true" className="size-4" />Recusado: {result?.message ?? refusal}</p>
      {result?.action ? <p>{result.action}</p> : null}
      {result?.missingScopes.length ? <p>Faltam: {result.missingScopes.map(scopeLabel).join(', ')}.</p> : null}
    </div>
  )
}

/**
 * Detalhe da conta do Inter (BNK-01a CA 3 a 18): seções Ambiente, Credenciais (só de escrita: "Configurado em … por …"), Certificado
 * (validade, titular, impressão digital), Situação (último teste e escopos) e Aviso de pagamento (BNK-02). Sem <c>settings.edit</c>,
 * modo leitura. Depois de salvar, o próximo passo é "Testar conexão"; testada, emitir boleto e importar extrato.
 */
export function InterAccountPage() {
  const { contaId } = useParams({ strict: false }) as { contaId: string }
  const search = useSearch({ strict: false }) as { acao?: string }
  const navigate = useNavigate()
  const client = useQueryClient()
  const canEdit = useCanSettings('edit')
  const { all } = useNavigation()
  const finance = all.find((m) => m.code === 'finance')
  const query = useGetBankIntegration(contaId, { query: { retry: false } })
  const [editing, setEditing] = useState<'save' | 'rotate' | null>(null)
  const [steps, setSteps] = useState<'saved' | 'tested' | null>(null)
  const [disabling, setDisabling] = useState(false)
  const refresh = () => void client.invalidateQueries({ queryKey: getGetBankIntegrationQueryKey(contaId) }).then(() => client.invalidateQueries({ queryKey: getListBankIntegrationsQueryKey() }))
  const test = useConnectionTest(contaId, refresh, () => setSteps('tested'))

  // Link do alerta de vencimento ("?acao=trocar"): abre a troca uma vez, ajustando o estado no render (sem efeito).
  const wantsRotate = search.acao === 'trocar' && !!query.data?.configured && canEdit
  const [rotateOpened, setRotateOpened] = useState(false)
  if (wantsRotate && !rotateOpened) {
    setRotateOpened(true)
    setEditing('rotate')
  }

  if (finance?.locked || (query.error instanceof ApiError && query.error.code === 'module_not_released')) {
    return finance ? <ModuleNotReleased module={finance} /> : null
  }

  const detail = query.data
  const done = (saved: BankIntegrationDetail, verb: string) => {
    client.setQueryData(getGetBankIntegrationQueryKey(contaId), saved)
    void client.invalidateQueries({ queryKey: getListBankIntegrationsQueryKey() })
    setEditing(null)
    setSteps('saved')
    notifySuccess(verb)
    if (search.acao) void navigate({ to: '.', search: {}, replace: true } as never)
  }

  const header = (
    <>
      <PageHeading title={detail ? `Banco Inter · ${detail.accountName}` : 'Banco Inter'}>
        <Link to="/app/configuracoes/integracoes/inter" className="mt-2 inline-flex min-h-11 items-center gap-1 text-sm font-semibold text-primary"><ArrowLeft aria-hidden="true" className="size-4" />Contas do Inter</Link>
      </PageHeading>
      <SettingsTabs />
      <IntegrationsNav />
    </>
  )

  if (query.isPending) return <>{header}<div className="space-y-3">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-24" />)}</div></>
  if (query.isError || !detail) {
    const notFound = query.error instanceof ApiError && query.error.status === 404
    return <>{header}<ErrorState title={notFound ? 'Conta do Inter não encontrada.' : undefined} message={notFound ? 'A conta pode ter sido excluída ou não ser do banco 077. Volte para a lista de contas.' : query.error instanceof ApiError ? query.error.message : undefined} onRetry={notFound ? undefined : () => void query.refetch()} /></>
  }

  const situation = situationOf(detail.situation)
  const sandbox = detail.configured ? detail.environment === 'Sandbox' : !!editing
  const certificate = detail.certificate
  return (
    <>
      {header}
      <EnvironmentBanner service="bank" test={sandbox} />
      {steps === 'saved' && detail.configured ? (
        <div className="mb-4"><NextSteps steps={[{ label: 'Testar conexão', onSelect: () => void test.run() }]} onDismiss={() => setSteps(null)} /></div>
      ) : null}
      {steps === 'tested' ? (
        <div className="mb-4">
          <NextSteps
            steps={[
              { label: 'Ver os títulos a receber', onSelect: () => void navigate({ to: '/app/financeiro/receber' } as never) },
              { label: 'Ver as contas e caixas', onSelect: () => void navigate({ to: '/app/financeiro/contas' } as never) },
            ]}
            onDismiss={() => setSteps(null)}
          />
        </div>
      ) : null}

      {editing ? (
        <InterCredentialsForm detail={detail} mode={editing} onCancel={() => setEditing(null)} onDone={(saved) => done(saved, editing === 'rotate' ? 'Credenciais trocadas' : 'Integração bancária salva')} />
      ) : !detail.configured ? (
        canEdit ? (
          <EmptyState
            title="Esta conta ainda não está ligada ao Inter."
            description="Tenha à mão o client id, o client secret e o certificado gerados no Internet Banking do Inter. O ambiente começa em Sandbox."
            action={<button type="button" onClick={() => setEditing('save')} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Configurar integração</button>}
          />
        ) : (
          <EmptyState title="Esta conta ainda não está ligada ao Inter." description="Só o Proprietário configura a integração bancária." />
        )
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          <Section title="Situação" dica={HINTS.scopes}>
            <p><StatusChip tone={situation.tone}>{situation.label}</StatusChip></p>
            <dl className="space-y-1">
              <Row label="Último teste">{detail.lastTest ? `${formatDateTime(detail.lastTest.at)} · ${detail.lastTest.outcome === 'Ok' ? 'funcionando' : 'recusado'}` : 'ainda não testada'}</Row>
              <Row label="Escopos">{detail.grantedScopes.length ? detail.grantedScopes.map(scopeLabel).join(', ') : '—'}</Row>
            </dl>
            {detail.missingScopes.length ? <p role="status" className="text-sm text-warn-ink">Faltam: {detail.missingScopes.map(scopeLabel).join(', ')}. Libere no Internet Banking do Inter.</p> : null}
            {detail.lastTest?.message && detail.lastTest.outcome !== 'Ok' ? <p className="text-sm text-muted-foreground">{detail.lastTest.message}</p> : null}
            <div aria-live="polite"><ConnectionTestStatus phase={test.phase} result={test.result} refusal={test.refusal} /></div>
            {canEdit ? (
              <button type="button" onClick={() => void test.run()} disabled={test.busy} className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold disabled:opacity-60">
                {test.busy ? 'Testando…' : 'Testar conexão'}
              </button>
            ) : null}
          </Section>
          <Section title="Ambiente" dica={HINTS.environment}>
            <p className="text-sm font-medium">{environmentLabel(detail.environment)}</p>
            {detail.environment === 'Production' && !detail.productionAllowed ? <p className="text-sm text-muted-foreground">A cobrança real será ligada na virada do sistema. Até lá, a conexão pode ser testada.</p> : null}
          </Section>
          <Section title="Credenciais" dica={HINTS.clientSecret}>
            <dl className="space-y-1">
              <Row label="Client id">{detail.clientIdMasked ?? '—'}</Row>
              <Row label="Client secret">configurado</Row>
              <Row label="Conta corrente">{detail.accountNumber ?? '—'}</Row>
              <Row label="Configurado em">{detail.configuredAt ? `${formatInstantDate(detail.configuredAt)} por ${detail.configuredByName ?? 'usuário'}` : '—'}</Row>
              {detail.rotatedAt ? <Row label="Trocado em">{`${formatInstantDate(detail.rotatedAt)} por ${detail.rotatedByName ?? 'usuário'}`}</Row> : null}
            </dl>
          </Section>
          <Section title="Certificado" dica={HINTS.certificate}>
            {certificate ? (
              <dl className="space-y-1">
                <Row label="Validade">{`Certificado válido até ${formatInstantDate(certificate.notAfter)} (${expiryText(certificate.daysToExpire)})`}</Row>
                <Row label="Titular">{certificate.subject}</Row>
                <Row label="Impressão digital">{certificate.thumbprint.slice(0, 16)}…</Row>
                <Row label="Formato">{certificate.format === 'Pfx' ? '.pfx' : '.crt + .key'}</Row>
              </dl>
            ) : <p className="text-sm text-muted-foreground">—</p>}
          </Section>
          <Section title="Aviso de pagamento">
            <p className="text-sm text-muted-foreground">O aviso automático de pagamento do Inter é ligado aqui depois que a conexão estiver funcionando.</p>
          </Section>
          {canEdit ? (
            <div className="flex flex-wrap items-start gap-2 lg:col-span-2">
              <button type="button" onClick={() => setEditing('rotate')} className="min-h-11 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">Trocar credenciais</button>
              <ConfirmDialog
                open={disabling}
                onOpenChange={setDisabling}
                destructive
                verb="Desligar integração"
                done="Integração desligada"
                title={`Desligar a integração da conta ${detail.accountName}?`}
                description="As credenciais e o certificado são apagados. Os boletos já emitidos continuam no banco e são conferidos pelo extrato ou pela baixa manual."
                onConfirm={async () => {
                  await disableBankIntegration(contaId, { version: detail.version ?? undefined })
                  refresh()
                }}
                trigger={<button type="button" className="min-h-11 rounded-md border border-border px-3 text-sm font-semibold text-crit-ink">Desligar integração</button>}
              />
            </div>
          ) : null}
        </div>
      )}
      {detail.integrationId ? (
        <details className="mt-4 rounded-md border border-border bg-card p-4">
          <summary className="min-h-11 cursor-pointer text-sm font-semibold">Histórico de alterações</summary>
          <div className="mt-3"><RecordHistory entity="BankIntegration" id={detail.integrationId} /></div>
        </details>
      ) : null}
    </>
  )
}
