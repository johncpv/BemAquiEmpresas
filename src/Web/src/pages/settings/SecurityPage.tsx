import { useQueryClient } from '@tanstack/react-query'
import { getGetSecuritySettingsQueryKey, saveSecuritySettings, useGetSecuritySettings } from '@/api/generated/bemaqui'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { SettingsTabs } from './SettingsTabs'
import { ShopfloorDevicesSection } from './ShopfloorDevicesSection'

/**
 * Configurações → Segurança (FND-06c CA 13): "Exigir verificação em duas etapas para todos", com confirmação no mesmo
 * verbo ("Exigir" → "Exigência ligada"). Vale no próximo login de cada usuário. PRD-03b: "Aparelhos da fábrica".
 */
export function SecurityPage() {
  const client = useQueryClient()
  const settings = useGetSecuritySettings()
  const save = async (require: boolean) => {
    await saveSecuritySettings({ requireTwoFactor: require })
    await client.invalidateQueries({ queryKey: getGetSecuritySettingsQueryKey() })
  }

  return (
    <>
      <PageHeading title="Configurações: Segurança" />
      <SettingsTabs />
      {settings.isPending ? <Skeleton className="h-28 max-w-xl" /> : settings.isError ? <ErrorState onRetry={() => settings.refetch()} /> : (
        <section className="max-w-xl space-y-3 rounded-md border border-border bg-card p-5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <b className="font-semibold">Exigir verificação em duas etapas para todos<Dica texto="Quem ainda não tem será levado à configuração no próximo acesso." /></b>
            <StatusChip tone={settings.data.requireTwoFactor ? 'ok' : 'info'}>{settings.data.requireTwoFactor ? 'Exigida' : 'Não exigida'}</StatusChip>
          </div>
          <p className="text-sm text-muted-foreground">
            Quem ainda não tem será levado à configuração no próximo acesso. O Proprietário e quem aprova ou paga já precisam da verificação, mesmo sem esta opção.
          </p>
          <Can perm="settings.edit">
            {settings.data.requireTwoFactor ? (
              <ConfirmDialog verb="Deixar de exigir" done="Exigência desligada" title="Deixar de exigir a verificação para todos?" description="Continua obrigatória para o Proprietário e para quem aprova ou paga." onConfirm={() => save(false)} trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Deixar de exigir</button>} />
            ) : (
              <ConfirmDialog verb="Exigir" done="Exigência ligada" title="Exigir a verificação em duas etapas de todos?" description="Quem ainda não tem será levado à configuração no próximo acesso." onConfirm={() => save(true)} trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Exigir</button>} />
            )}
          </Can>
        </section>
      )}
      <div className="mt-6"><ShopfloorDevicesSection /></div>
    </>
  )
}
