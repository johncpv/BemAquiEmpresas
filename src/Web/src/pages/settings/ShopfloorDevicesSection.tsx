import { useQueryClient } from '@tanstack/react-query'
import { getListShopfloorDevicesQueryKey, revokeShopfloorDevice, useListShopfloorDevices } from '@/api/generated/bemaqui'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Dica } from '@/help/Dica'
import { useCan } from '@/pages/use-can'
import { formatInstantDate as formatDate } from '@/lib/format'
import { revokedText, STATUS, UNUSED_WARNING_DAYS } from './shopfloor-devices'

/**
 * Configurações → Segurança → Aparelhos da fábrica (PRD-03b CA 5, 8 e 9): ver com settings.view; "Revogar aparelho" com settings.edit ou
 * production.approve. Mostra o teto, quem registrou, o último uso e "Sem uso há {n} dias" perto dos 30.
 */
export function ShopfloorDevicesSection() {
  const client = useQueryClient()
  const list = useListShopfloorDevices()
  const canEdit = useCan('settings.edit')
  const canApprove = useCan('production.approve')
  const canRevoke = canEdit || canApprove

  return (
    <section aria-labelledby="aparelhos" className="max-w-3xl space-y-3 rounded-md border border-border bg-card p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="aparelhos" className="font-semibold">Aparelhos da fábrica<Dica texto="Tablets liberados para a entrada por PIN no app do operador. Cada registro vale 180 dias e cai sozinho depois de 30 dias sem uso." /></h2>
        {list.data ? <span className="text-sm text-muted-foreground tabular-nums">{list.data.activeCount} de {list.data.maxActive} ativos</span> : null}
      </div>
      {list.isPending ? <Skeleton className="h-24" /> : list.isError ? <ErrorState onRetry={() => list.refetch()} /> : list.data.devices.length === 0 ? (
        <EmptyState title="Nenhum aparelho da fábrica registrado." description="Para registrar, abra o app do operador (/fab) no tablet, entre com e-mail e senha e toque em Aparelho → Registrar este aparelho." />
      ) : (
        <ul className="divide-y divide-border">
          {list.data.devices.map((d) => (
            <li key={d.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
              <div className="min-w-0 space-y-0.5">
                <p className="flex flex-wrap items-center gap-2 font-semibold">
                  {d.name}
                  <StatusChip tone={STATUS[d.status]?.tone ?? 'neutral'}>{STATUS[d.status]?.label ?? d.status}</StatusChip>
                  {d.status === 'Active' && d.daysUnused >= UNUSED_WARNING_DAYS ? <StatusChip tone="warn">Sem uso há {d.daysUnused} dias</StatusChip> : null}
                </p>
                <p className="text-sm text-muted-foreground tabular-nums">
                  Registrado por {d.createdByName} em {formatDate(d.createdAt)} · último uso {formatDate(d.lastSeenAt)} · {d.pins === 1 ? '1 PIN' : `${d.pins} PINs`}
                  {d.status === 'Active' ? ` · vale até ${formatDate(d.expiresAt)}` : d.revokedAt ? ` · ${revokedText(d)} em ${formatDate(d.revokedAt)}` : ''}
                </p>
              </div>
              {d.status === 'Active' && canRevoke ? (
                <ConfirmDialog verb="Revogar aparelho" done="Aparelho revogado" destructive title={`Revogar o aparelho ${d.name}?`}
                  description="O PIN deixa de funcionar nele e quem estiver entrando por PIN sai na próxima ação. Para voltar a usar, registre o tablet de novo."
                  onConfirm={async () => {
                    client.setQueryData(getListShopfloorDevicesQueryKey(), await revokeShopfloorDevice(d.id))
                  }}
                  trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Revogar aparelho</button>} />
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}