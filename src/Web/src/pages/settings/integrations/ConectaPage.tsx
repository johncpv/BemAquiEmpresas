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
import { browser, CONECTA_MODULE, connectionState } from './conecta'
import { IntegrationsNav } from './IntegrationsNav'
import { OutboundHealthSection } from './OutboundWebhooks'

const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold'

/**
 * Configurações → Integrações → Conecta (INT-01 CA 1, 20 e 21): sem conexão, o que o Conecta faz e "Conectar"; com conexão, a situação com chip,
 * motivo e ação, o que o Conecta poderá fazer, o último uso e a referência técnica recolhida. "Conectar" leva ao Conecta, que volta à tela de
 * autorizar; "Desconectar" existe só aqui.
 */
export function ConectaPage() {
  const query = useGetConectaConnection({ query: { retry: false } })
  const client = useQueryClient()
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string>()

  if (query.error instanceof ApiError && query.error.code === 'module_not_released') return <ModuleNotReleased module={CONECTA_MODULE} />

  const connect = async () => {
    setStartError(undefined)
    setStarting(true)
    try {
      const start = await startConectaConnection()
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
                ) : (
                  <button type="button" onClick={() => void connect()} disabled={starting || !page.configured} className={primary}>
                    {state.action === 'reauthorize' ? 'Autorizar de novo' : 'Conectar'}
                  </button>
                )}
              </div>
              <InlineError id="conecta-erro" message={startError} />
            </Can>
          </section>

          {active ? <Can perm="conecta.view"><OutboundHealthSection /></Can> : null}

          {active ? (
            <section aria-labelledby="conecta-proximo" className="rounded-md border border-border bg-card p-4">
              <h2 id="conecta-proximo" className="text-base font-semibold">Próximo passo</h2>
              <p className="mt-1 text-sm text-muted-foreground">Ative o WhatsApp do Conecta para os avisos de cobrança chegarem ao cliente.</p>
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
    </dl>
  )
}
