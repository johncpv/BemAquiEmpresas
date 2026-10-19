import type { CnpjLookupResult } from '@/api/generated/model'
import { formatDate } from '@/lib/format'
import { phoneMask } from '@/lib/masks'
import type { LookupState } from './lookups'

/**
 * Quadro "Dados da Receita" (CAD-01b CA 1 a 6): mostra o que a consulta trouxe e só preenche o formulário quando o usuário
 * clica "Usar estes dados" (R-PRP-7). Situação diferente de Ativa só avisa.
 */
export function ReceitaPanel({ state, onUse }: { state: LookupState<CnpjLookupResult>; onUse: (result: CnpjLookupResult) => void }) {
  if (state.status === 'idle') return null
  if (state.status === 'loading') return <p role="status" className="text-sm text-muted-foreground sm:col-span-2">Consultando o CNPJ na Receita…</p>
  if (state.status === 'error') return <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink sm:col-span-2">{state.message}</p>

  const { data, lookedUpAt, fromCache } = state.value
  const a = data.address
  const address = a ? [a.street, a.number, a.complement, a.district, a.city && a.state ? `${a.city}/${a.state}` : a.city, a.zipCode].filter(Boolean).join(', ') : null
  return (
    <section aria-label="Dados da Receita" className="space-y-2 rounded-md border border-border bg-card p-3 text-sm sm:col-span-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold">Dados da Receita</h3>
        <span className="text-xs text-muted-foreground tabular-nums">{fromCache ? 'Consultado em ' : 'Consultado agora, '}{formatDate(lookedUpAt.slice(0, 10))}</span>
      </div>
      {!data.activeAtReceita && data.situation ? (
        <p role="status" className="rounded-md border border-warn-soft bg-warn-soft px-2 py-1 text-warn-ink">Situação na Receita: {data.situation}</p>
      ) : null}
      <dl className="grid gap-x-4 gap-y-1 sm:grid-cols-[auto_1fr]">
        <dt className="text-muted-foreground">Razão social</dt><dd>{data.legalName}</dd>
        {data.tradeName ? <><dt className="text-muted-foreground">Nome fantasia</dt><dd>{data.tradeName}</dd></> : null}
        {address ? <><dt className="text-muted-foreground">Endereço</dt><dd>{address}</dd></> : null}
        {data.phone ? <><dt className="text-muted-foreground">Telefone</dt><dd className="tabular-nums">{phoneMask.format(data.phone)}</dd></> : null}
        {data.email ? <><dt className="text-muted-foreground">E-mail</dt><dd>{data.email}</dd></> : null}
      </dl>
      <button type="button" onClick={() => onUse(state.value)} className="rounded-md bg-primary px-3 py-1.5 text-sm font-semibold text-primary-foreground">Usar estes dados</button>
    </section>
  )
}
