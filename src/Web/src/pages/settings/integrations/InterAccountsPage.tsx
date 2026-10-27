import { Link, useNavigate } from '@tanstack/react-router'
import { ChevronRight, Landmark } from 'lucide-react'
import { useListBankIntegrations } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { EnvironmentBanner } from '@/components/ui/environment-banner'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDate } from '@/lib/format'
import { PageHeading } from '@/shell/PageHeading'
import { ModuleNotReleased } from '@/pages/ModulePage'
import { useNavigation } from '@/navigation/navigation-context'
import { SettingsTabs } from '../SettingsTabs'
import { IntegrationsNav } from './IntegrationsNav'
import { environmentLabel, situationOf } from './inter'

/**
 * Configurações → Integrações → Banco Inter (BNK-01a): as contas com banco 077 e a situação da integração de cada uma (Não
 * conectada, Sandbox, Produção, Certificado vence em breve, Vencido). A conta abre o detalhe; certificado perto de vencer abre direto
 * em "Trocar credenciais".
 */
export function InterAccountsPage() {
  const query = useListBankIntegrations({ query: { retry: false } })
  const navigate = useNavigate()
  const { all } = useNavigation()
  const finance = all.find((m) => m.code === 'finance')
  if (finance?.locked || (query.error instanceof ApiError && query.error.code === 'module_not_released')) {
    return finance ? <ModuleNotReleased module={finance} /> : null
  }

  const items = query.data?.items ?? []
  return (
    <>
      <PageHeading title="Banco Inter">
        <p className="mt-2 text-muted-foreground">Ligue cada conta do Inter ao sistema para emitir boletos e ler o extrato sem digitar nada no banco.</p>
      </PageHeading>
      <SettingsTabs />
      <IntegrationsNav />
      <EnvironmentBanner service="bank" test={items.some((r) => r.situation === 'Sandbox')} />
      {query.isPending ? (
        <div className="space-y-2">{[0, 1].map((i) => <Skeleton key={i} className="h-16" />)}</div>
      ) : query.isError ? (
        <ErrorState message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <EmptyState
          title="Nenhuma conta do Inter cadastrada."
          description="Cadastre a conta com o banco 077 para conectar."
          action={<button type="button" onClick={() => void navigate({ to: '/app/financeiro/contas' } as never)} className="min-h-11 rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground">Nova conta bancária</button>}
        />
      ) : (
        <ul aria-label="Contas do Inter" className="space-y-2">
          {items.map((row) => {
            const situation = situationOf(row.situation)
            const rotate = row.situation === 'ExpiringSoon' || row.situation === 'Expired'
            return (
              <li key={row.bankAccountId}>
                <Link
                  to="/app/configuracoes/integracoes/inter/$contaId"
                  params={{ contaId: row.bankAccountId }}
                  search={rotate ? { acao: 'trocar' } : {}}
                  className={'flex min-h-16 items-center gap-3 rounded-md border border-border bg-card px-4 py-3 hover:bg-muted ' + (row.situation === 'Expired' ? 'border-l-4 border-l-crit' : rotate ? 'border-l-4 border-l-warn' : '')}
                >
                  <Landmark aria-hidden="true" className="size-5 shrink-0 text-muted-foreground" />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-semibold">{row.name}</span>
                    <span className="block text-sm tabular-nums text-muted-foreground">
                      Agência {row.agency ?? '—'} · Conta {row.account ?? '—'}
                      {row.environment ? ` · ${environmentLabel(row.environment)}` : ''}
                      {row.certificateNotAfter ? ` · certificado até ${formatDate(row.certificateNotAfter)}` : ''}
                    </span>
                  </span>
                  <StatusChip tone={situation.tone}>{situation.label}</StatusChip>
                  <ChevronRight aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
                </Link>
              </li>
            )
          })}
        </ul>
      )}
    </>
  )
}
