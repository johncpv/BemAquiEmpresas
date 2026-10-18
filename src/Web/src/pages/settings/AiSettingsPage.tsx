import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getGetAiSettingsQueryKey, removeAiSettings, useGetAiSettings } from '@/api/generated/bemaqui'
import type { AiSettingsDto } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { formatDateTime } from '@/lib/format'
import { Can } from '@/pages/ModuleGate'
import { PageHeading } from '@/shell/PageHeading'
import { AiSettingsForm } from './AiSettingsForm'
import { SettingsTabs } from './SettingsTabs'

/**
 * Configurações → Inteligência artificial (FND-15 CA 9 a 13, R-AJD-8): provedor, modelo e chave write-only. Sem
 * provedor, o Pergunte ao BemAqui funciona só por busca nos textos de ajuda.
 */
export function AiSettingsPage() {
  const client = useQueryClient()
  const settings = useGetAiSettings()
  const [editing, setEditing] = useState(false)
  const refresh = () => client.invalidateQueries({ queryKey: getGetAiSettingsQueryKey() })

  return (
    <>
      <PageHeading title="Configurações: Inteligência artificial">
        <p className="mt-2 text-muted-foreground">O Pergunte ao BemAqui usa este provedor para responder com base só nos textos de ajuda. Nenhum dado da empresa é enviado.</p>
      </PageHeading>
      <SettingsTabs />
      {settings.isPending ? <Skeleton className="h-48 max-w-xl" /> : settings.isError ? <ErrorState onRetry={() => settings.refetch()} /> : editing ? (
        <AiSettingsForm settings={settings.data} onDone={async () => { await refresh(); setEditing(false) }} onCancel={() => setEditing(false)} />
      ) : settings.data.configured ? (
        <Summary settings={settings.data} onEdit={() => setEditing(true)} onRemoved={refresh} />
      ) : (
        <EmptyState
          className="max-w-xl"
          title="Nenhum provedor configurado."
          description="O Pergunte ao BemAqui funciona só por busca nos textos de ajuda."
          action={<Can perm="settings.edit"><button type="button" onClick={() => setEditing(true)} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Configurar provedor</button></Can>}
        />
      )}
    </>
  )
}

function Summary({ settings, onEdit, onRemoved }: { settings: AiSettingsDto; onEdit: () => void; onRemoved: () => Promise<unknown> }) {
  const provider = settings.providers.find((p) => p.code === settings.provider)
  return (
    <section aria-label="Provedor configurado" className="max-w-xl space-y-3 rounded-md border border-border bg-card p-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <b className="font-semibold">{provider?.name ?? settings.provider}</b>
        <StatusChip tone="ok">Configurado</StatusChip>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Modelo</dt>
        <dd className="min-w-0 break-all">{settings.model}</dd>
        <dt className="text-muted-foreground">Chave de API</dt>
        <dd className="tabular-nums">{settings.keySetAt ? `Chave configurada em ${formatDateTime(settings.keySetAt)}` : '—'}</dd>
      </dl>
      <Can perm="settings.edit">
        <div className="flex flex-wrap gap-2 pt-1">
          <button type="button" onClick={onEdit} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Alterar configuração</button>
          <ConfirmDialog
            verb="Remover"
            done="Provedor removido"
            destructive
            title="Remover o provedor de IA?"
            description="O Pergunte ao BemAqui continua funcionando, só por busca nos textos de ajuda."
            onConfirm={async () => { await removeAiSettings(); await onRemoved() }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Remover</button>}
          />
        </div>
      </Can>
    </section>
  )
}
