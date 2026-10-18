import { toast } from 'sonner'

export interface NextStep {
  label: string
  onSelect: () => void
}

/** Sucesso com o particípio do verbo e, quando houver, o próximo passo com link direto (FND-09c CA 11 e 12, R-PRP-2). */
export function toastDone(message: string, nextStep?: NextStep) {
  toast.success(message, nextStep ? { action: { label: nextStep.label, onClick: nextStep.onSelect } } : undefined)
}

export function toastError(message: string) {
  toast.error(message)
}
