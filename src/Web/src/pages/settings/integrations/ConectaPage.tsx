import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetConectaConnectionQueryKey, revokeConnectedApp, startConectaConnection, useGetConectaConnection } from '@/api/generated/bemaqui'
import type { AppConnectionView, AppScopeView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { ErrorState, InlineError, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { PageHeading } from '@/shell/PageHeading'
import { browser, canGainText, CONECTA_MODULE, connectionState, normalizePairing, returnedFromConecta } from './conecta'
import { INTEGRATION_V1_ROUTES } from './integration-v1-routes'
import { IntegrationsNav } from './IntegrationsNav'
import { OutboundHealthSection } from './OutboundWebhooks'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

/**
 * Configurações → Integrações → Conecta (INT-01 CA 1, 20 e 21; INT-04 CA 8 a 10): sem conexão, o que o Conecta faz e "Conectar", que pede o código de
 * pareamento gerado no Conecta; com conexão, a situação com chip, motivo e ação, o que o Conecta poderá fazer (e o que pode ganhar, com "Autorizar de
 * novo"), o último uso e a referência técnica recolhida. "Conectar" leva ao Conecta, que volta com `?conectado=1`; "Desconectar" existe só aqui.
 */
export function ConectaPage() {
  const query = useGetConectaConnection({ query: { retry: false } })
  const client = useQueryClient()
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string>()
  const [pairing, setPairing] = useState('')
  const [returned] = useState(() => returnedFromConecta(browser.search()))

  if (query.error instanceof ApiError && query.error.code === 'module_not_released') return <ModuleNotReleased module={CONECTA_MODULE} />

  /** "Autorizar de novo" (conexão ativa ou suspensa): o Conecta já conhece a conta, sem código de pareamento. */
  const reauthorize = async () => {
    setStartError(undefined)
    setStarting(true)
    try {
      const start = await startConectaConnection({ pairingCode: null })
      browser.assign(start.redirectUrl)
    } catch (e) {
      setStartError(e instanceof ApiError ? e.message : 'Não foi possível começar a conexão. Tente de novo.')
      setStarting(false)
    }
  }

  const page = query.data
  const connection = page?.connection ?? null
  const state = connectionState(connection)
  const active = connection?.status === 'Active'
  const normalized = normalizePairing(pairing)
  const gain = active ? canGainText(connection?.canGain) : null

  return (
    <>
      <PageHeading title="Conecta" description="Venda, cobre e avise o cliente pelo WhatsApp com o Conecta, usando os dados do ERP, sem copiar chave nenhuma." />
      <IntegrationsNav />
      {query.isPending ? (
        <div className="space-y-3" aria-busy="true">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-20" />)}</div>
      ) : query.isError || !page ? (
        <ErrorState title="Não foi possível carregar a conexão." onRetry={() => void query.refetch()} />
      ) : (
        <div className="max-w-3xl space-y-4">
          <section aria-labelledby="conecta-situacao" className={'rounded-md border border-border bg-card p-4 ' + (state.tone === 'crit' ? 'border-l-4 border-l-crit' : state.tone === 'warn' ? 'border-l-4 border-l-warn' : '')}>
            <div className="flex flex-wrap items-center gap-2">
              <h2 id="conecta-situacao" className="text-base font-semibold">Situação</h2>
              <StatusChip tone={state.tone}>{connection ? connection.statusText : 'Não conectado'}</StatusChip>
              {connection?.status === 'Suspended' ? <Dica texto="Suspensa: o Conecta parou de usar o ERP até um Proprietário autorizar de novo. Nada foi apagado." /> : null}
            </div>
            <p className="mt-2 text-sm text-muted-foreground">
              {connection ? connection.reasonText : 'O Conecta ainda não está ligado a este ERP. Ao conectar, você escolhe a conta do Conecta e confirma com o código do autenticador.'}
            </p>
            {connection?.lastUsedAt ? <p className="mt-1 text-sm tabular-nums text-muted-foreground">Último uso em {formatDateTime(connection.lastUsedAt)}</p> : null}
            {!page.configured ? <p className="mt-2 text-sm text-muted-foreground">A conexão com o Conecta ainda não foi configurada neste ambiente. Fale com o suporte do BemAqui.</p> : null}
            <Can perm="settings.edit">
              <div className="mt-3 flex flex-wrap gap-2">
                {active && connection ? (
                  <ConfirmDialog
                    verb="Desconectar"
                    done="Desconectado"
                    destructive
                    title="Desconectar o Conecta?"
                    description="O Conecta para de atender pedidos e cobranças por WhatsApp agora. Pedidos já criados continuam no ERP."
                    onConfirm={async () => {
                      await revokeConnectedApp(connection.id)
                      await client.invalidateQueries({ queryKey: getGetConectaConnectionQueryKey() })
                    }}
                    trigger={<button type="button" className={secondary}>Desconectar</button>}
                  />
                ) : state.action === 'reauthorize' ? (
                  <button type="button" onClick={() => void reauthorize()} disabled={starting || !page.configured} className={primary}>Autorizar de novo</button>
                ) : (
                  <ConfirmDialog
                    verb="Conectar"
                    done="Abrindo o Conecta"
                    title="Conectar ao Conecta"
                    description="Você vai confirmar no Conecta com um usuário administrador."
                    onOpen={() => setPairing('')}
                    preconditions={[{ ok: normalized !== null, text: 'Informe o código de pareamento: 8 letras e números, como ABCD-2345.' }]}
                    onConfirm={async () => {
                      const start = await startConectaConnection({ pairingCode: normalized })
                      browser.assign(start.redirectUrl)
                    }}
                    trigger={<button type="button" disabled={!page.configured} className={primary}>Conectar</button>}
                  >
                    <div className="space-y-1">
                      <div className="flex items-center gap-2">
                        <label htmlFor="conecta-pareamento" className="text-sm font-semibold">Código de pareamento</label>
                        <Dica texto="Peça o código a quem administra o Conecta. Ele fica em Configurações → Integrações → BemAqui Empresas, no Conecta." />
                      </div>
                      <input
                        id="conecta-pareamento"
                        value={pairing}
                        onChange={(e) => setPairing(e.target.value)}
                        placeholder="ABCD-2345"
                        autoComplete="off"
                        autoCapitalize="characters"
                        maxLength={9}
                        className="min-h-11 w-full rounded-md border border-input bg-background px-3 py-2 font-mono text-sm uppercase tabular-nums"
                      />
                    </div>
                  </ConfirmDialog>
                )}
              </div>
              <InlineError id="conecta-erro" message={startError} />
            </Can>
          </section>

          {gain ? (
            <section aria-labelledby="conecta-ganhar" className="rounded-md border border-border border-l-4 border-l-info bg-card p-4">
              <h2 id="conecta-ganhar" className="text-base font-semibold">{gain}</h2>
              <p className="mt-1 text-sm text-muted-foreground">Autorize de novo uma vez: a conexão nova já nasce com tudo isso, e os pedidos do Conecta continuam com ele.</p>
              <Can perm="settings.edit">
                <button type="button" onClick={() => void reauthorize()} disabled={starting || !page.configured} className={primary + ' mt-3'}>Autorizar de novo</button>
              </Can>
            </section>
          ) : null}

          {active ? <Can perm="conecta.view"><OutboundHealthSection /></Can> : null}

          {active ? (
            <section aria-labelledby="conecta-proximo" className="rounded-md border border-border bg-card p-4">
              <h2 id="conecta-proximo" className="text-base font-semibold">{returned ? `Conectado à conta ${connection?.accountLabel ?? 'escolhida'} do Conecta` : 'Próximo passo'}</h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {returned
                  ? 'Próximo passo: abra uma conversa no Conecta. O painel do cliente mostra pedidos e cobranças.'
                  : 'Ative o WhatsApp do Conecta para os avisos de cobrança chegarem ao cliente.'}
              </p>
            </section>
          ) : null}

          <ScopesSection title="O que o Conecta poderá fazer" scopes={connection && connection.status !== 'Revoked' ? connection.scopes : page.allowedScopes} />

          <details className="rounded-md border border-border bg-card p-4">
            <summary className="cursor-pointer text-sm font-semibold">Para desenvolvedores</summary>
            <TechnicalReference connection={connection} />
          </details>
        </div>
      )}
    </>
  )
}

function ScopesSection({ title, scopes }: { title: string; scopes: readonly AppScopeView[] }) {
  return (
    <section aria-labelledby="conecta-pode" className="rounded-md border border-border bg-card p-4">
      <div className="flex items-center gap-2">
        <h2 id="conecta-pode" className="text-base font-semibold">{title}</h2>
        <Dica texto="O Conecta só faz o que você também pode fazer no ERP. Se a sua permissão mudar, o que o Conecta pode fazer muda junto, na hora." />
      </div>
      <ul className="mt-2 list-disc space-y-1 pl-5 text-sm">
        {scopes.map((s) => <li key={s.scope}>{s.text}</li>)}
      </ul>
      <p className="mt-2 text-sm text-muted-foreground">Você pode desconectar quando quiser.</p>
    </section>
  )
}

function TechnicalReference({ connection }: { connection: AppConnectionView | null }) {
  return (
    <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
      <dt className="text-muted-foreground">Autorização</dt>
      <dd>OAuth 2.0 com PKCE (S256), código de uso único</dd>
      <dt className="text-muted-foreground">API do aplicativo</dt>
      <dd className="break-all font-mono">/api/v1/integration/*</dd>
      {connection ? (
        <>
          <dt className="text-muted-foreground">Conexão</dt>
          <dd className="break-all font-mono">{connection.id}</dd>
          <dt className="text-muted-foreground">Nova autorização até</dt>
          <dd className="tabular-nums">{formatDateTime(connection.reconsentDueAt)}</dd>
          <dt className="text-muted-foreground">Escopos</dt>
          <dd className="break-all font-mono">{connection.scopes.map((s) => s.scope).join(' ')}</dd>
        </>
      ) : null}
      <dt className="text-muted-foreground">Erros</dt>
      <dd>ProblemDetails (RFC 9457) com <span className="font-mono">code</span> estável; 429 com Retry-After</dd>
      <dt className="col-span-2 mt-2 text-muted-foreground">Rotas da API v1 e o escopo de cada uma</dt>
      <dd className="col-span-2">
        <ul className="space-y-1" aria-label="Rotas da API de integração v1">
          {INTEGRATION_V1_ROUTES.map((r) => (
            <li key={r.method + ' ' + r.path} className="flex flex-wrap gap-x-2 break-all font-mono text-xs">
              <span className="font-semibold">{r.method}</span>
              <span>{r.path}</span>
              {r.scope ? <span className="text-muted-foreground">{r.scope}</span> : null}
            </li>
          ))}
        </ul>
      </dd>
    </dl>
  )
}
