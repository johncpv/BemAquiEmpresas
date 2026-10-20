import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { saveOpeningBalances } from '@/api/generated/bemaqui'
import type { OpeningStatus } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { MoneyInput } from '@/components/form/inputs'
import { notifySuccess } from '@/components/ui/messages'
import { EmptyState } from '@/components/ui/states'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { OPENING_DICAS, useCanFinance } from './opening'

interface StepProps { status: OpeningStatus; refresh: () => Promise<void>; onNext: () => void }

const asText = (v: number | null) => (v === null ? null : String(v))

/** Passo 2 (CA 3 e 4): o saldo de cada conta ativa no fim do dia de corte; conta com movimento fica travada. */
export function BalancesStep({ status, refresh, onNext }: StepProps) {
  const canEdit = useCanFinance('edit') && !status.completedAt
  const [values, setValues] = useState<Record<string, string | null>>(() => Object.fromEntries(status.accounts.map((a) => [a.id, asText(a.openingBalance)])))
  const [error, setError] = useState<string | undefined>()
  const [saving, setSaving] = useState(false)
  if (status.accounts.length === 0) {
    return <EmptyState title="Nenhuma conta ativa." description="Cadastre as contas e caixas da empresa para informar os saldos." action={<Link to={'/app/financeiro/contas' as never} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Cadastrar conta</Link>} />
  }

  const save = async () => {
    setSaving(true)
    setError(undefined)
    try {
      const lines = status.accounts.filter((a) => !a.hasMovement).map((a) => ({ bankAccountId: a.id, balance: values[a.id] === null || values[a.id] === '' ? null : Number(values[a.id]) }))
      await saveOpeningBalances({ lines })
      notifySuccess('Saldos salvos')
      await refresh()
      onNext()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar os saldos.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section aria-labelledby="saldos-titulo" className="max-w-3xl space-y-3">
      <h2 id="saldos-titulo" className="flex items-center text-[15px] font-semibold">Saldo no fim do dia {formatDate(status.cutoverDate ?? '')}<Dica nome="Saldo no fim do dia" texto={OPENING_DICAS.fimDoDia} /></h2>
      <FormError message={error} />
      <div className="overflow-x-auto rounded-md border border-border bg-card">
        <table className="w-full text-sm" aria-label="Saldos das contas">
          <thead className="border-b border-border text-xs text-muted-foreground">
            <tr><th className="px-3 py-2 text-left">Conta</th><th className="px-3 py-2 text-left">Saldo no fim do dia</th><th className="px-3 py-2 text-left">Situação</th></tr>
          </thead>
          <tbody>
            {status.accounts.map((a) => (
              <tr key={a.id} className="border-b border-border last:border-0">
                <td className="px-3 py-2 font-semibold">{a.name}</td>
                <td className="px-3 py-2"><MoneyInput aria-label={`Saldo de ${a.name}`} value={values[a.id]} disabled={!canEdit || a.hasMovement} onChange={(v) => setValues((s) => ({ ...s, [a.id]: v }))} /></td>
                <td className="px-3 py-2">
                  {a.hasMovement ? <StatusChip tone="info">Com movimento</StatusChip> : a.openingBalance === null ? <Tag>Saldo inicial não informado</Tag> : <StatusChip tone="ok">Saldo informado</StatusChip>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {status.accounts.some((a) => a.hasMovement) ? <p className="text-sm text-muted-foreground">Conta com movimento não troca o saldo: corrija com um lançamento.</p> : null}
      <div className="flex flex-wrap justify-end gap-2">
        <button type="button" onClick={onNext} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Ir para A receber</button>
        {canEdit ? <button type="button" disabled={saving} onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50">Salvar saldos</button> : null}
      </div>
    </section>
  )
}
