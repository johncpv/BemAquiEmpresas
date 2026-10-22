import type { ReactNode } from 'react'
import { Dica } from '@/help/Dica'

export interface Fact {
  label: string
  value: ReactNode
  /** Explicação do termo (padrão de ajuda: termo que confunde ganha a Dica). */
  dica?: string
}

/**
 * Faixa de fatos da OS (padrão 10 da Fase 3): rótulo pequeno e valor, quatro por linha; a 400 px quebra em duas colunas.
 * Comum à OS de fabricação (prometido, horas padrão, etapa atual, roteiro) e à de instalação (instalador, valor, previsão, aceite).
 */
export function FactsBar({ facts, label = 'Fatos da OS' }: { facts: readonly Fact[]; label?: string }) {
  return (
    <dl aria-label={label} className="mb-4 grid grid-cols-2 gap-3 rounded-md border border-border bg-card p-4 text-sm sm:grid-cols-4">
      {facts.map((f) => (
        <div key={f.label} className="min-w-0">
          <dt className="flex items-center text-xs text-muted-foreground">{f.label}{f.dica ? <Dica nome={f.label} texto={f.dica} /> : null}</dt>
          <dd className="min-w-0 break-words">{f.value}</dd>
        </div>
      ))}
    </dl>
  )
}