import { Link } from '@tanstack/react-router'
import { useState } from 'react'
import { reverseBankEntry, reverseTransfer } from '@/api/generated/bemaqui'
import type { StatementLine } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Tag } from '@/components/ui/tag'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'

/** Estornar transferência ou lançamento (CA 10 e 13): recebimento e pagamento só pelo título ou pela OP (CA 15). */
function ReverseButton({ line, onDone }: { line: StatementLine; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  const transfer = !!line.transferId
  const verb = transfer ? 'Estornar transferência' : 'Estornar lançamento'
  return (
    <ConfirmDialog destructive verb={verb} done={transfer ? 'Transferência estornada' : 'Lançamento estornado'} title={`${verb}?`} description="Cria o movimento inverso. Nada é apagado e o Proprietário é avisado."
      preconditions={[{ ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
      onConfirm={async () => {
        const body = { reason: reason.trim(), version: line.movementVersion ?? 0 }
        if (line.transferId) await reverseTransfer(line.transferId, body)
        else if (line.bankEntryId) await reverseBankEntry(line.bankEntryId, body)
        await onDone()
      }}
      trigger={<button type="button" className="text-xs font-semibold text-primary hover:underline">Estornar</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}

export function StatementTable({ lines, startLabel, startBalance, canPay, onChanged }: { lines: StatementLine[]; startLabel: string; startBalance: number; canPay: boolean; onChanged: () => Promise<void> }) {
  return (
    <div className="overflow-x-auto rounded-md border border-border bg-card">
      <table className="w-full text-sm" aria-label="Extrato da conta">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr><th className="px-3 py-2 text-left">Data</th><th className="px-3 py-2 text-left">Descrição</th><th className="hidden px-3 py-2 text-left sm:table-cell">Origem</th><th className="px-3 py-2 text-right">Valor</th><th className="px-3 py-2 text-right">Saldo</th><th className="hidden px-3 py-2 sm:table-cell"><span className="sr-only">Ações</span></th></tr>
        </thead>
        <tbody>
          <tr className="border-b border-border bg-muted/50">
            <td className="px-3 py-2" />
            <td className="px-3 py-2 font-semibold"><span className="inline-flex items-center">{startLabel}{startLabel === 'Saldo anterior' ? <Dica nome="Saldo anterior" texto="Saldo inicial mais tudo o que entrou e saiu antes do período." /> : null}</span></td>
            <td className="hidden sm:table-cell" /><td />
            <td className="px-3 py-2 text-right font-semibold"><Money value={startBalance} highlightNegative /></td>
            <td className="hidden sm:table-cell" />
          </tr>
          {lines.map((l) => (
            <tr key={l.transactionId} className="border-b border-border last:border-0">
              <td className="px-3 py-2 tabular-nums">{formatDate(l.date)}</td>
              <td className="px-3 py-2"><span className="inline-flex flex-wrap items-center gap-1.5">{l.description}{l.withoutOrder ? <Tag>Sem OP</Tag> : null}{l.reversed ? <Tag>Estornado</Tag> : null}</span></td>
              <td className="hidden px-3 py-2 sm:table-cell">{l.link ? <Link to={l.link as never} className="text-primary hover:underline">{l.origin}</Link> : l.origin ?? '—'}</td>
              <td className="px-3 py-2 text-right"><Money value={l.amount} className={l.amount < 0 ? 'text-crit-ink' : 'text-ok-ink'} /></td>
              <td className="px-3 py-2 text-right"><Money value={l.balance} highlightNegative /></td>
              <td className="hidden px-3 py-2 text-right sm:table-cell">{canPay && (l.transferId || l.bankEntryId) && !l.isReversal && !l.reversed ? <ReverseButton line={l} onDone={onChanged} /> : null}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
