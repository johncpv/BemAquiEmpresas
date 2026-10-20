import { useState } from 'react'
import { completeOpening } from '@/api/generated/bemaqui'
import type { OpeningPartyTotal, OpeningStatus } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { StatusChip } from '@/components/ui/status-chip'
import { reconciliationCsv, useCanFinance } from './opening'

interface StepProps { status: OpeningStatus; refresh: () => Promise<void>; onCompleted: () => void }

function PartyTable({ label, party, rows }: { label: string; party: string; rows: OpeningPartyTotal[] }) {
  if (rows.length === 0) return <p className="text-sm text-muted-foreground">{label}: nenhum título de implantação.</p>
  return (
    <div className="overflow-x-auto rounded-md border border-border bg-card">
      <table className="w-full text-sm" aria-label={label}>
        <thead className="border-b border-border text-xs text-muted-foreground"><tr><th className="px-3 py-2 text-left">{party}</th><th className="px-3 py-2 text-right">Títulos</th><th className="px-3 py-2 text-right">Total</th></tr></thead>
        <tbody>
          {rows.map((r) => <tr key={r.partyId} className="border-b border-border last:border-0"><td className="px-3 py-2">{r.partyName}</td><td className="px-3 py-2 text-right tabular-nums">{r.count}</td><td className="px-3 py-2 text-right"><Money value={r.total} /></td></tr>)}
        </tbody>
        <tfoot className="border-t border-border font-semibold"><tr><td className="px-3 py-2">Total</td><td className="px-3 py-2 text-right tabular-nums">{rows.reduce((n, r) => n + r.count, 0)}</td><td className="px-3 py-2 text-right"><Money value={rows.reduce((n, r) => n + r.total, 0)} /></td></tr></tfoot>
      </table>
    </div>
  )
}

/** Passo 5 (CA 9 e 10): saldo do sistema × extrato por conta, totais por cliente e fornecedor, CSV e conclusão. */
export function ReconciliationStep({ status, refresh, onCompleted }: StepProps) {
  const canApprove = useCanFinance('approve')
  const [statement, setStatement] = useState<Record<string, string | null>>({})
  const [notes, setNotes] = useState(status.notes ?? '')
  const diffs = status.accounts.map((a) => { const t = statement[a.id]; return t === null || t === undefined || t === '' ? null : Math.round((Number(t) - a.systemBalanceAtCutover) * 100) / 100 })
  const pending = diffs.some((d) => d !== null && d !== 0)
  const done = !!status.completedAt

  const download = () => {
    const blob = new Blob(['﻿' + reconciliationCsv(status, statement)], { type: 'text/csv;charset=utf-8' })
    const link = document.createElement('a')
    link.href = URL.createObjectURL(blob)
    link.download = `conferencia-implantacao-${status.cutoverDate ?? ''}.csv`
    link.click()
    URL.revokeObjectURL(link.href)
  }

  return (
    <section aria-labelledby="conferencia-titulo" className="max-w-4xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="conferencia-titulo" className="text-[15px] font-semibold">Saldo do sistema = saldo do banco</h2>
        <button type="button" onClick={download} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Baixar conferência (CSV)</button>
      </div>
      <div className="overflow-x-auto rounded-md border border-border bg-card">
        <table className="w-full text-sm" aria-label="Conferência das contas">
          <thead className="border-b border-border text-xs text-muted-foreground"><tr><th className="px-3 py-2 text-left">Conta</th><th className="px-3 py-2 text-right">Saldo do sistema</th><th className="px-3 py-2 text-left">Saldo no extrato do banco</th><th className="px-3 py-2 text-left">Diferença</th></tr></thead>
          <tbody>
            {status.accounts.map((a, i) => (
              <tr key={a.id} className="border-b border-border last:border-0">
                <td className="px-3 py-2 font-semibold">{a.name}</td>
                <td className="px-3 py-2 text-right"><Money value={a.systemBalanceAtCutover} /></td>
                <td className="px-3 py-2"><MoneyInput aria-label={`Saldo no extrato de ${a.name}`} value={statement[a.id] ?? null} onChange={(v) => setStatement((s) => ({ ...s, [a.id]: v }))} /></td>
                <td className="px-3 py-2">{diffs[i] === null ? <span className="text-muted-foreground">—</span> : <StatusChip tone={diffs[i] === 0 ? 'ok' : 'crit'}>{diffs[i] === 0 ? 'Sem diferença' : <Money value={diffs[i] ?? 0} />}</StatusChip>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <PartyTable label="A receber de implantação" party="Cliente" rows={status.receivables} />
        <PartyTable label="A pagar de implantação" party="Fornecedor" rows={status.payables} />
      </div>
      <div className="space-y-1">
        <label htmlFor="obs-implantacao" className="text-sm font-medium">Observação das diferenças</label>
        <textarea id="obs-implantacao" value={notes} maxLength={500} disabled={done || !canApprove} onChange={(e) => setNotes(e.target.value)} rows={3} className={inputClass} placeholder="Ex.: tarifa do dia 30 lançada pelo banco no dia seguinte." />
      </div>
      {!canApprove && !done ? <p role="note" className="text-sm text-muted-foreground">Quem conclui a implantação é o Proprietário.</p> : null}
      {canApprove && !done ? (
        <div className="flex justify-end">
          <ConfirmDialog
            verb="Concluir implantação"
            done="Implantação concluída"
            title="Concluir a implantação financeira?"
            description="Depois de concluída, não entram mais títulos de implantação e esta tela fica só para consulta."
            preconditions={[
              { ok: !!status.cutoverDate, text: 'Data de corte definida.' },
              { ok: !pending || notes.trim().length > 0, text: 'Diferenças com o extrato justificadas na observação.' },
            ]}
            onConfirm={async () => { await completeOpening({ notes: notes.trim() || null, version: status.version }); await refresh(); onCompleted() }}
            onReload={refresh}
            trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Concluir implantação</button>}
          />
        </div>
      ) : null}
    </section>
  )
}
