import { useState } from 'react'
import { saveNumberFormat } from '@/api/generated/bemaqui'
import type { NumberFormatRow } from '@/api/generated/model'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { Dica } from '@/help/Dica'
import { draftErrors, type NumberingDraft, previewNext, toDraft } from './numbering-preview'

const YEARLY_LOCKED = 'Não é possível mudar o reinício anual depois do primeiro número emitido.'

/** Painel de um tipo de documento (CAD-06 CA 10 a 13): prévia ao vivo e confirmação do que não tem volta. */
export function NumberingPanel({ row, canEdit, onClose, onSaved }: { row: NumberFormatRow; canEdit: boolean; onClose: () => void; onSaved: () => Promise<void> }) {
  const [draft, setDraft] = useState<NumberingDraft>(() => toDraft(row))
  const set = (patch: Partial<NumberingDraft>) => setDraft((d) => ({ ...d, ...patch }))
  const dirty = JSON.stringify(draft) !== JSON.stringify(toDraft(row))
  const errors = draftErrors(row, draft)
  const valid = Object.keys(errors).length === 0
  const preview = previewNext(row, draft)

  const save = async () => {
    await saveNumberFormat(row.docType, { prefix: draft.prefix.trim().toUpperCase(), yearly: draft.yearly, digits: draft.digits, nextNumber: draft.next ? Number(draft.next) : null })
    await onSaved()
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={`Numeração: ${row.label}`} description="Vale do próximo número em diante." dirty={dirty && canEdit}
      onSave={async () => { if (!valid) return false; try { await save(); notifySuccess('Numeração salva'); return true } catch { return false } }}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {canEdit ? (
            <ConfirmDialog verb="Salvar numeração" done="Numeração salva" title="Salvar numeração?" onConfirm={save}
              description={<>O próximo número de {row.label.toLowerCase()} sairá como <b className="font-mono tabular-nums">{preview}</b>. Os números já emitidos não mudam.</>}
              trigger={<button type="button" disabled={!dirty || !valid} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Salvar numeração</button>} />
          ) : null}
        </>
      )}
    >
      <fieldset disabled={!canEdit} className="space-y-4">
        <Field label="Prefixo" required error={errors.prefix} hint="De 1 a 6 letras ou números.">
          {(p) => <input {...p} value={draft.prefix} maxLength={6} onChange={(e) => set({ prefix: e.target.value.toUpperCase() })} className={inputClass + ' font-mono uppercase'} />}
        </Field>
        <div className="flex items-center gap-2 text-sm">
          <input id="numeracao-anual" type="checkbox" checked={draft.yearly} disabled={!canEdit || row.hasIssued} onChange={(e) => set({ yearly: e.target.checked })} className="size-4" />
          <label htmlFor="numeracao-anual" className="font-semibold">Reinicia por ano</label>
          {row.hasIssued ? <Dica nome="Reinicia por ano" texto={YEARLY_LOCKED} /> : null}
        </div>
        <Field label="Dígitos" required error={errors.digits} hint="De 3 a 8. Completa com zeros à esquerda.">
          {(p) => <input {...p} type="number" inputMode="numeric" min={3} max={8} value={draft.digits} onChange={(e) => set({ digits: Number(e.target.value) })} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Próximo número" error={errors.next} dica="Use para continuar a numeração do sistema antigo. Só pode aumentar." hint={row.lastIssued ? `Último emitido: ${row.lastIssued}.` : 'Nenhum número emitido ainda.'}>
          {(p) => <input {...p} inputMode="numeric" value={draft.next} placeholder={String(row.lastIssued + 1)} onChange={(e) => set({ next: e.target.value.replace(/\D/g, '') })} className={inputClass + ' tabular-nums'} />}
        </Field>
      </fieldset>
      <p className="mt-5 rounded-md border border-border bg-muted px-3 py-2 text-sm" aria-live="polite">
        Próximo: <b className="font-mono tabular-nums">{preview}</b>
      </p>
    </SidePanel>
  )
}
