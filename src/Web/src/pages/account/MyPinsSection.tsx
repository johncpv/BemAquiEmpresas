import { useQueryClient } from '@tanstack/react-query'
import { deleteMyPin, getListMyPinsQueryKey, useListMyPins } from '@/api/generated/bemaqui'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { formatInstantDate as formatDate } from '@/lib/format'

/** Meu perfil → Segurança → Meus PINs (PRD-03b CA 9): os tablets em que tenho PIN e "Apagar meu PIN". */
export function MyPinsSection() {
  const client = useQueryClient()
  const pins = useListMyPins()
  return (
    <section aria-labelledby="meus-pins" className="mt-6 max-w-md space-y-3 rounded-md border border-border bg-card p-5">
      <h2 id="meus-pins" className="font-semibold">Meus PINs<Dica texto="PIN é um número de 6 dígitos que abre o app do operador num tablet da fábrica registrado. Cada tablet tem o seu." /></h2>
      {pins.isPending ? <Skeleton className="h-16" /> : pins.isError ? <ErrorState onRetry={() => pins.refetch()} /> : pins.data.length === 0 ? (
        <EmptyState title="Você não tem PIN em nenhum tablet." description="No tablet da fábrica, entre com e-mail e senha e toque em Criar PIN para entrar mais rápido." />
      ) : (
        <ul className="divide-y divide-border">
          {pins.data.map((p) => (
            <li key={p.deviceId} className="flex flex-wrap items-center justify-between gap-2 py-3">
              <div>
                <p className="font-semibold">{p.deviceName}</p>
                <p className="text-sm text-muted-foreground tabular-nums">Criado em {formatDate(p.createdAt)}{p.lastUsedAt ? ` · usado em ${formatDate(p.lastUsedAt)}` : ' · ainda não usado'}</p>
              </div>
              <ConfirmDialog verb="Apagar meu PIN" done="PIN apagado" destructive title={`Apagar o seu PIN do ${p.deviceName}?`}
                description="O seu nome sai da lista desse tablet. Para voltar a usar PIN lá, entre com e-mail e senha no tablet e crie outro."
                onConfirm={async () => {
                  await deleteMyPin(p.deviceId)
                  await client.invalidateQueries({ queryKey: getListMyPinsQueryKey() })
                }}
                trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Apagar meu PIN</button>} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}