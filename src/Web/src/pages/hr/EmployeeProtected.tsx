import { Lock } from 'lucide-react'
import { Dica } from '@/help/Dica'
import { EMPLOYEE_DICAS } from './employee'

/** Aba sensível sem hr.view_sensitive (CA 8): nada é pedido ao servidor. */
export function EmployeeProtected() {
  return (
    <div className="flex max-w-3xl items-center gap-2 rounded-md border border-border bg-muted px-4 py-6 text-sm text-muted-foreground">
      <Lock aria-hidden="true" className="size-4" />
      Dados protegidos
      <Dica nome="Dados protegidos" texto={EMPLOYEE_DICAS.protegidos} />
    </div>
  )
}
