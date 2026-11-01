import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { getListAssetsQueryKey, previewAssetTransfer, transferAssets } from '@/api/generated/bemaqui'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DateInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { movementChanges, type MovementDraft, REASON_MAX, todayIso } from './assets'
import { PlacementFields } from './AssetMovements'

const button = 'inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60'

/**
 * "Transferir selecionados" (PAT-02 CA 4, padrão 7): escolho o destino (o que fica vazio é mantido em cada bem) e o motivo; o diálogo mostra
 * o impacto em lista, cada bem de → para, pela prévia do servidor. Um bem que impede (baixado, já no destino) aparece com o motivo e o lote
 * não grava nada. No filtro "Responsável desligado", o motivo vem sugerido.
 */
export function TransferAssetsDialog({ selected, suggestedReason, onDone }: { selected: string[]; suggestedReason?: string; onDone: () => void }) {
  const client = useQueryClient()
  const blank = (): MovementDraft => ({ siteId: '', responsibleEmployeeId: '', workCenterId: '', movedOn: todayIso(), reason: suggestedReason ?? '' })
  const [draft, setDraft] = useState(blank)
  const [open, setOpen] = useState(false)
  const set = (patch: Partial<MovementDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const hasTarget = !!(draft.siteId || draft.responsibleEmployeeId || draft.workCenterId)
  const input = {
    assetIds: selected, siteId: draft.siteId || null, responsibleEmployeeId: draft.responsibleEmployeeId || null, workCenterId: draft.workCenterId || null,
    movedOn: draft.movedOn || null, reason: draft.reason.trim(),
  }
  const preview = useQuery({
    // A prévia não depende do texto do motivo (o servidor só exige que exista).
    queryKey: ['asset-transfer-preview', { ...input, reason: undefined }],
    queryFn: () => previewAssetTransfer({ ...input, reason: input.reason || 'Prévia' }),
    enabled: open && selected.length > 0 && hasTarget,
  })
  const refused = preview.data?.refused ?? []
  const previewError = preview.error instanceof ApiError ? preview.error.message : preview.isError ? 'Não foi possível calcular o impacto.' : null

  return (
    <ConfirmDialog verb="Transferir selecionados" done="Bens transferidos" title="Transferir selecionados" size="lg" open={open} onOpenChange={setOpen}
      description="Um registro de movimentação por bem, todos juntos. O que você deixar em branco continua como está em cada bem."
      onOpen={() => setDraft(blank())}
      preconditions={[
        { ok: selected.length > 0, text: 'Selecione ao menos um bem na lista.' },
        { ok: hasTarget, text: 'Escolha o local, o responsável ou o centro de destino.' },
        { ok: draft.reason.trim().length > 0, text: 'Informe o motivo.' },
        { ok: !preview.isFetching && !!preview.data, text: previewError ?? 'Calculando o impacto…' },
        { ok: refused.length === 0, text: 'Tire da seleção os bens que impedem a transferência.' },
      ]}
      impactList={preview.data ? {
        label: 'Vão mudar',
        items: preview.data.items.map((i) => ({ key: i.assetId, primary: `${i.tag} · ${i.name}`, secondary: movementChanges(i.from, i.to).join(' · ') })),
        refused: refused.map((r) => ({ key: r.assetId, primary: `${r.tag} · ${r.name}`, reason: r.reason })),
        total: preview.data.items.length === 1 ? '1 bem' : `${preview.data.items.length} bens`,
      } : undefined}
      onConfirm={async () => {
        await transferAssets(input)
        void client.invalidateQueries({ queryKey: getListAssetsQueryKey() })
        onDone()
      }}
      trigger={<button type="button" className={button}>Transferir selecionados</button>}>
      <PlacementFields draft={draft} set={set} errors={{}} emptyLabel={{ site: 'Manter o de cada bem', responsible: 'Manter o de cada bem', center: 'Manter o de cada bem' }} />
      <Field label="Data" required hint="Até hoje.">{(p) => <DateInput {...p} value={draft.movedOn} onChange={(movedOn) => set({ movedOn })} />}</Field>
      <Field label="Motivo" required hint={`${draft.reason.length}/${REASON_MAX}`}>
        {(p) => <input {...p} value={draft.reason} maxLength={REASON_MAX} onChange={(e) => set({ reason: e.target.value })} className={inputClass} />}
      </Field>
    </ConfirmDialog>
  )
}
