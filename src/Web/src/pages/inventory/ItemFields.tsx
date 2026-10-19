import { useListItemCategories, useListUnits, useListWarrantyDefaults } from '@/api/generated/bemaqui'
import type { CategoryDetail, ItemType } from '@/api/generated/model'
import { Field, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { CestInput, NcmInput, QuantityInput } from '@/components/form/inputs'
import { Dica } from '@/help/Dica'
import { type ItemDraft, withType } from './item-draft'
import { DICAS, ORIGINS, requiresNcm, TYPE_OPTIONS } from './item-labels'

/** Categorias em ordem de árvore, com o caminho ("Chapas › Aço carbono"), para o seletor. */
function categoryOptions(all: CategoryDetail[], currentId: string) {
  const byParent = new Map<string | null, CategoryDetail[]>()
  for (const c of all) byParent.set(c.parentId ?? null, [...(byParent.get(c.parentId ?? null) ?? []), c])
  const out: { id: string; label: string }[] = []
  const walk = (parent: string | null, path: string[]) => {
    for (const c of byParent.get(parent) ?? []) {
      if (c.isActive || c.id === currentId) out.push({ id: c.id, label: [...path, c.name].join(' › ') })
      walk(c.id, [...path, c.name])
    }
  }
  walk(null, [])
  return out
}

interface Props { draft: ItemDraft; onChange: (d: ItemDraft) => void; errors: Partial<Record<string, string>>; readOnly: boolean; active: boolean }

/** Campos do item em seções (CAD-02a CA 6 a 12): o tipo vem primeiro e comanda NCM, lote, série e garantia. */
export function ItemFields({ draft, onChange, errors, readOnly, active }: Props) {
  const units = useListUnits({ includeInactive: true })
  const categories = useListItemCategories()
  const warranties = useListWarrantyDefaults()
  const set = (patch: Partial<ItemDraft>) => onChange({ ...draft, ...patch })
  const service = draft.type === 'Service'
  const ncmRequired = active && !!draft.type && requiresNcm(draft.type)

  return (
    <fieldset disabled={readOnly} className="space-y-4">
      <FormSection title="Identificação">
        <Field label="Tipo" required error={errors.type} hint="Comanda o NCM obrigatório, o lote, a série e a garantia sugeridos.">
          {(p) => (
            <select {...p} value={draft.type} onChange={(e) => onChange(withType(draft, e.target.value as ItemType, warranties.data ?? []))} className={inputClass}>
              <option value="">Escolha</option>
              {TYPE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          )}
        </Field>
        <Field label="Código" required error={errors.code} hint="Digitado. Use o mesmo código do sistema antigo.">
          {(p) => <input {...p} value={draft.code} maxLength={30} onChange={(e) => set({ code: e.target.value })} className={inputClass + ' font-mono'} />}
        </Field>
        <Field label="Nome" required error={errors.name}>
          {(p) => <input {...p} value={draft.name} maxLength={150} onChange={(e) => set({ name: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Unidade de estoque" required error={errors.unitId}>
          {(p) => (
            <select {...p} value={draft.unitId} onChange={(e) => set({ unitId: e.target.value })} className={inputClass}>
              <option value="">Escolha</option>
              {(units.data ?? []).filter((u) => u.isActive || u.id === draft.unitId).map((u) => <option key={u.id} value={u.id}>{u.code} — {u.name}</option>)}
            </select>
          )}
        </Field>
        <Field label="Categoria" error={errors.categoryId}>
          {(p) => (
            <select {...p} value={draft.categoryId} onChange={(e) => set({ categoryId: e.target.value })} className={inputClass}>
              <option value="">Sem categoria</option>
              {categoryOptions(categories.data ?? [], draft.categoryId).map((c) => <option key={c.id} value={c.id}>{c.label}</option>)}
            </select>
          )}
        </Field>
      </FormSection>
      <FormSection title="Fiscal">
        <Field label="NCM" required={ncmRequired} error={errors.ncm} dica={DICAS.ncm}>
          {(p) => <NcmInput {...p} value={draft.ncm} onChange={(ncm) => set({ ncm })} />}
        </Field>
        <Field label="CEST" error={errors.cest} dica={DICAS.cest}>
          {(p) => <CestInput {...p} value={draft.cest} onChange={(cest) => set({ cest })} />}
        </Field>
        <Field label="Origem" dica={DICAS.origem}>
          {(p) => (
            <select {...p} value={draft.origin} onChange={(e) => set({ origin: Number(e.target.value) })} className={inputClass}>
              {ORIGINS.map((o) => <option key={o.code} value={o.code}>{o.label}</option>)}
            </select>
          )}
        </Field>
      </FormSection>
      <FormSection title="Estoque" description={service ? 'Serviço não tem estoque, lote nem série.' : undefined}>
        {service ? null : (
          <>
            <Field label="Estoque mínimo" error={errors.minStock} hint="Na unidade de estoque. Abaixo dele, o sistema avisa para comprar.">
              {(p) => <QuantityInput {...p} value={draft.minStock} onChange={(minStock) => set({ minStock })} />}
            </Field>
            <div className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
              <label className="flex items-center gap-2">
                <input type="checkbox" className="size-4" checked={draft.tracksLot} onChange={(e) => set({ tracksLot: e.target.checked, touched: { ...draft.touched, lot: true } })} />
                Controla lote
              </label>
              <Dica nome="Controla lote" texto={DICAS.lote} />
              <label className="flex items-center gap-2">
                <input type="checkbox" className="size-4" checked={draft.tracksSerial} onChange={(e) => set({ tracksSerial: e.target.checked, touched: { ...draft.touched, serial: true } })} />
                Controla número de série
              </label>
              <Dica nome="Controla número de série" texto={DICAS.serie} />
            </div>
          </>
        )}
        <Field label="Garantia (meses)" error={errors.warranty} dica={DICAS.garantia}>
          {(p) => <input {...p} inputMode="numeric" maxLength={3} value={draft.warranty} placeholder="Sem garantia" onChange={(e) => set({ warranty: e.target.value.replace(/\D/g, ''), touched: { ...draft.touched, warranty: true } })} className={inputClass + ' tabular-nums'} />}
        </Field>
      </FormSection>
    </fieldset>
  )
}
