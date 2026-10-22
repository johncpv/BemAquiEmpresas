import { useState } from 'react'
import { RowsEditor } from '@/components/form/rows-editor'
import { inputClass } from '@/components/form/form-utils'
import { SidePanel } from '@/components/ui/side-panel'
import { MAX_CHECKLIST, MAX_CHECKLIST_TEXT } from './routings'

/**
 * Checklist da etapa (CA 4): de 0 a 30 itens de até 120 caracteres, reordenáveis; em tela cheia a 400 px (SidePanel). Volta
 * para o editor com "Aplicar checklist"; a gravação é no "Salvar roteiro".
 */
export function ChecklistPanel({ stepName, index, items, disabled, onApply, onClose }: { stepName: string; index: number; items: string[]; disabled: boolean; onApply: (items: string[]) => void; onClose: () => void }) {
  const [rows, setRows] = useState(() => items.map((text) => ({ text })))
  const dirty = JSON.stringify(rows.map((r) => r.text)) !== JSON.stringify(items)
  const apply = () => {
    onApply(rows.map((r) => r.text.trim()).filter(Boolean))
    onClose()
    return Promise.resolve(true)
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={`Checklist da etapa ${index + 1}${stepName ? ` — ${stepName}` : ''}`} description="O que o operador confere antes de concluir a etapa." dirty={dirty && !disabled} onSave={apply}
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {disabled ? null : <button type="button" onClick={() => void apply()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Aplicar checklist</button>}
        </>
      )}>
      <RowsEditor noun="item" rows={rows} onChange={setRows} max={MAX_CHECKLIST} reorder disabled={disabled} newRow={() => ({ text: '' })}
        headers={['Item do checklist']}
        summary={`${rows.length} de ${MAX_CHECKLIST} itens`}
        renderRow={(row, i, update) => [
          <input key="t" aria-label={`Item ${i + 1} do checklist`} value={row.text} maxLength={MAX_CHECKLIST_TEXT} onChange={(e) => update({ text: e.target.value })} className={inputClass} />,
        ]} />
      {rows.length === 0 ? <p className="mt-3 text-sm text-muted-foreground">Sem checklist. Adicione o que precisa ser conferido nesta etapa, ou deixe vazio.</p> : null}
    </SidePanel>
  )
}