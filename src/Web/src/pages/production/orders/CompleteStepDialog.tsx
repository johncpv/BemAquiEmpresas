import { useState } from 'react'
import { completeFabricationStep } from '@/api/generated/bemaqui'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'
import { useBomRecovery } from '@/pages/engineering/boms/bom-recovery'
import { FABRICATION_DICAS, MAX_STEP_REASON, stepActionLabel } from './fabrication'

/**
 * "Concluir etapa" do supervisor (PRD-02 CA 8, PRD-03a CA 20 e 21, padrão 4), o mesmo diálogo no Kanban, na aba Etapas e na aba
 * Apontamentos da OS: motivo obrigatório (até 200) e confirmação com o mesmo verbo. Quem chama só mostra com production.approve.
 */
export function CompleteStepDialog({ orderId, orderNumber, step, version, open, onOpenChange, onDone }: {
  orderId: string; orderNumber: string; step: { id: string; code?: string | null; name?: string | null }; version: number
  open: boolean; onOpenChange: (open: boolean) => void; onDone: () => Promise<void>
}) {
  const [reason, setReason] = useState('')
  // ENG-02 CA 16: etapa depois da primeira sem BOM liberada → "Abrir BOM da OS".
  const recovery = useBomRecovery(orderId)
  return (
    <ConfirmDialog verb="Concluir etapa" done="Etapa concluída" open={open} onOpenChange={onOpenChange} onOpen={() => setReason('')}
      title={`${stepActionLabel(step)} da ${orderNumber}?`}
      description={(
        <span className="inline-flex items-center">
          A etapa fica concluída pelo supervisor e a próxima fica pronta. Os apontamentos abertos dela fecham agora, com a marca Encerrado pelo supervisor.
          <Dica nome="Concluir etapa" texto={FABRICATION_DICAS.concluirEtapa} />
        </span>
      )}
      preconditions={[{ ok: reason.trim().length > 0, text: 'Informe o motivo.' }]}
      onReload={onDone} recovery={recovery}
      onConfirm={async () => {
        await completeFabricationStep(orderId, step.id, { reason: reason.trim(), version })
        await onDone()
      }}>
      <label className="block space-y-1 text-sm font-medium">
        Motivo
        <textarea rows={3} maxLength={MAX_STEP_REASON} value={reason} onChange={(e) => setReason(e.target.value)} className={inputClass} />
        <span className="block text-right text-xs font-normal text-muted-foreground tabular-nums">{reason.length}/{MAX_STEP_REASON}</span>
      </label>
    </ConfirmDialog>
  )
}
