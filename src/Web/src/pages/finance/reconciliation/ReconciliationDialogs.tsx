import { useId, useState } from 'react'
import {
  createBankEntryFromLine, createBankStatementRule, listReconciliationCandidates, listReconciliationPendingLines, reconcile, settleFromLine, undoReconciliation, useListAccounts,
  useListCostCenters,
} from '@/api/generated/bemaqui'
import type { BankEntryKind, ReconDone, ReconLine, ReconTransaction, ReconUnpairedLine } from '@/api/generated/model'
import { inputClass } from '@/components/form/form-utils'
import { MoneyInput } from '@/components/form/inputs'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Money } from '@/components/ui/money'
import { Dica } from '@/help/Dica'
import { formatDate } from '@/lib/format'
import { toNumber } from '../receivables/receivables'
import { LINE_KIND } from '../statement/bank-statement'
import { entryKindsFor, ruleKeyword, sumMismatch } from './reconciliation'

const link = 'inline-flex min-h-11 items-center px-2 text-sm font-semibold text-primary hover:underline disabled:opacity-50 disabled:no-underline'
const secondary = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-50'

export const NOT_VERIFIED = 'Linha de um arquivo OFX importado como contingência numa conta com API: não veio do banco pela API. Nunca tem confiança Alta.'
export const SEGREGATION = 'Quem importou um arquivo OFX não confirma a conciliação dele: outra pessoa do Financeiro (ou o Proprietário) confirma.'
const ENTRY_HINT = 'Tarifa, IOF, juros ou rendimento lançado direto na conta, sem ordem de pagamento. O Proprietário é avisado e o lançamento entra em "Movimentos sem OP". Numa saída, o sistema pede de novo o código da verificação em duas etapas.'

/** Lado do banco num par ou numa lista: data, descrição, tipo, contraparte e o valor. */
export function LineSummary({ line }: { line: ReconLine }) {
  const who = [line.counterpartyName, line.counterpartyDocument].filter(Boolean).join(' · ')
  return (
    <div className="min-w-0">
      <p className="flex flex-wrap items-baseline justify-between gap-2"><span className="tabular-nums text-muted-foreground">{formatDate(line.date)}</span><Money value={line.amount} className={line.amount < 0 ? 'font-semibold text-crit-ink' : 'font-semibold text-ok-ink'} /></p>
      <p className="truncate text-sm">{line.description} <span className="text-xs text-muted-foreground">· {LINE_KIND[line.kind ?? 'Other'] ?? line.kind} · {line.source === 'Ofx' ? 'OFX' : 'API'}</span></p>
      {who ? <p className="truncate text-xs text-muted-foreground">{who}</p> : null}
    </div>
  )
}

/** Lado do sistema: data, descrição, origem com link ("Ver origem") e o valor. */
export function TransactionSummary({ tx }: { tx: ReconTransaction }) {
  return (
    <div className="min-w-0">
      <p className="flex flex-wrap items-baseline justify-between gap-2"><span className="tabular-nums text-muted-foreground">{formatDate(tx.date)}</span><Money value={tx.amount} className="font-semibold" /></p>
      <p className="truncate text-sm">{tx.description}</p>
      {tx.origin ? <p className="truncate text-xs text-muted-foreground">{tx.origin}</p> : null}
    </div>
  )
}

/** "Escolher à mão" (CA 4): uma linha com uma ou mais transações (ou o caminho "Vincular a uma OP paga", só pagamentos de OP). */
export function ManualDialog({ accountId, line, paymentsOnly, onDone, trigger }: { accountId: string; line: ReconLine; paymentsOnly?: boolean; onDone: (n: number) => Promise<void>; trigger: React.ReactNode }) {
  const [options, setOptions] = useState<ReconTransaction[] | null>(null)
  const [picked, setPicked] = useState<string[]>([])
  const chosen = (options ?? []).filter((t) => picked.includes(t.id))
  const mismatch = sumMismatch([line.amount], chosen.map((t) => t.amount))
  return (
    <ConfirmDialog size="lg" verb="Conciliar" done="Conciliado" title={paymentsOnly ? 'Vincular a uma OP paga' : 'Escolher à mão'}
      description={paymentsOnly ? 'Pagamentos de ordens já registrados nesta conta, até 30 dias da linha. Nenhum pagamento é criado aqui.' : 'Transações não conciliadas desta conta, até 30 dias da linha. A soma precisa ser igual ao valor da linha.'}
      onOpen={() => {
        setPicked([])
        setOptions(null)
        void listReconciliationCandidates(accountId, { date: line.date, outflow: line.amount < 0, paymentsOnly: paymentsOnly || undefined }).then(setOptions, () => setOptions([]))
      }}
      preconditions={[{ ok: chosen.length > 0, text: 'Ao menos uma transação marcada.' }, { ok: chosen.length === 0 || !mismatch, text: mismatch ?? 'Soma igual à linha.' }]}
      impact={<p className="text-sm">Linha do banco <Money value={line.amount} className="font-semibold" /> · marcadas <Money value={chosen.reduce((a, t) => a + t.amount, 0)} className="font-semibold" /></p>}
      onConfirm={async () => {
        await reconcile(accountId, { lines: [{ id: line.id, version: line.version }], transactions: chosen.map((t) => ({ id: t.id, version: t.version })) })
        await onDone(1)
      }}
      trigger={trigger}>
      <div className="mt-3 rounded-md border border-border"><LineSummary line={line} /></div>
      {options === null ? <p role="status" className="mt-3 text-sm text-muted-foreground">Carregando as transações…</p> : options.length === 0
        ? <p className="mt-3 text-sm">{paymentsOnly ? 'Nenhum pagamento de OP não conciliado perto desta data. Use "Abrir OP para regularizar".' : 'Nenhuma transação não conciliada perto desta data.'}</p>
        : (
          <ul className="mt-3 max-h-72 space-y-1 overflow-y-auto" aria-label="Transações do sistema">
            {options.map((t) => (
              <li key={t.id}>
                <label className="flex min-h-11 items-center gap-3 rounded-md border border-border px-3 py-2">
                  <input type="checkbox" className="size-5 accent-primary" checked={picked.includes(t.id)} onChange={(e) => setPicked((p) => e.target.checked ? [...p, t.id] : p.filter((x) => x !== t.id))} />
                  <TransactionSummary tx={t} />
                </label>
              </li>
            ))}
          </ul>
        )}
    </ConfirmDialog>
  )
}

/** "Escolher linhas" (N linhas para 1 transação, CA 4). */
export function LinesForTransactionDialog({ accountId, tx, onDone }: { accountId: string; tx: ReconTransaction; onDone: (n: number) => Promise<void> }) {
  const [options, setOptions] = useState<ReconLine[] | null>(null)
  const [picked, setPicked] = useState<string[]>([])
  const chosen = (options ?? []).filter((l) => picked.includes(l.id))
  const mismatch = sumMismatch(chosen.map((l) => l.amount), [tx.amount])
  const blocked = chosen.find((l) => l.blocked)
  return (
    <ConfirmDialog size="lg" verb="Conciliar" done="Conciliado" title="Escolher linhas do banco" description="Linhas pendentes desta conta, até 30 dias da transação. A soma precisa ser igual ao valor da transação."
      onOpen={() => { setPicked([]); setOptions(null); void listReconciliationPendingLines(accountId, { date: tx.date, outflow: tx.amount < 0 }).then(setOptions, () => setOptions([])) }}
      preconditions={[{ ok: chosen.length > 0, text: 'Ao menos uma linha marcada.' }, { ok: chosen.length === 0 || !mismatch, text: mismatch ?? 'Soma igual à transação.' }, { ok: !blocked, text: blocked?.blockedReason ?? SEGREGATION }]}
      onConfirm={async () => {
        await reconcile(accountId, { lines: chosen.map((l) => ({ id: l.id, version: l.version })), transactions: [{ id: tx.id, version: tx.version }] })
        await onDone(chosen.length)
      }}
      trigger={<button type="button" className={link}>Escolher linhas</button>}>
      <div className="mt-3 rounded-md border border-border p-2"><TransactionSummary tx={tx} /></div>
      {options === null ? <p role="status" className="mt-3 text-sm text-muted-foreground">Carregando as linhas…</p> : options.length === 0 ? <p className="mt-3 text-sm">Nenhuma linha pendente perto desta data.</p> : (
        <ul className="mt-3 max-h-72 space-y-1 overflow-y-auto" aria-label="Linhas do banco">
          {options.map((l) => (
            <li key={l.id}>
              <label className="flex min-h-11 items-center gap-3 rounded-md border border-border px-3 py-2">
                <input type="checkbox" className="size-5 accent-primary" checked={picked.includes(l.id)} onChange={(e) => setPicked((p) => e.target.checked ? [...p, l.id] : p.filter((x) => x !== l.id))} />
                <LineSummary line={l} />
              </label>
            </li>
          ))}
        </ul>
      )}
    </ConfirmDialog>
  )
}

/**
 * "Lançar sem OP" (CA 6): o diálogo da FIN-04 com a data e o valor da linha travados; tipo e conta do plano vindos da sugestão. Saída só
 * como tarifa, IOF ou juros; o 2FA de novo é pedido pelo servidor e repetido pelo cliente gerado.
 */
export function LineEntryDialog({ accountId, item, cap, canApprove, onDone }: { accountId: string; item: ReconUnpairedLine; cap: number; canApprove: boolean; onDone: () => Promise<void> }) {
  const line = item.line
  const kindId = useId()
  const chart = useListAccounts()
  const centers = useListCostCenters()
  const kinds = entryKindsFor(line.amount)
  const initialKind = (kinds.find((k) => k.value === item.action?.entryKind)?.value ?? kinds[0].value) as BankEntryKind
  const [kind, setKind] = useState<BankEntryKind>(initialKind)
  const [plan, setPlan] = useState(item.action?.accountId ?? '')
  const [center, setCenter] = useState('')
  const [reason, setReason] = useState('')
  const [backdate, setBackdate] = useState('')
  const out = line.amount < 0
  const options = (chart.data ?? []).filter((a) => a.acceptsEntries && a.isActive && (out ? a.type === 'Expense' : a.type === 'Revenue'))
  const chosen = plan && options.some((a) => a.id === plan) ? plan : options.find((a) => a.code === (out ? '3.5' : '1.3'))?.id ?? ''
  const overCap = line.source === 'Ofx' && Math.abs(line.amount) > cap
  return (
    <ConfirmDialog size="lg" verb="Lançar sem OP" done="Lançamento registrado" title={item.action?.text ?? 'Lançar sem OP'}
      description="Data e valor vêm da linha do banco. O Proprietário recebe o aviso e a linha fica conciliada."
      onOpen={() => { setKind(initialKind); setPlan(item.action?.accountId ?? ''); setCenter(''); setReason(item.action?.text ? item.action.text.replace(/^Lançar como /, '').replace(/ sem OP$/, '') + ' do extrato' : ''); setBackdate('') }}
      preconditions={[
        { ok: !line.blocked, text: line.blockedReason ?? SEGREGATION },
        { ok: !overCap || canApprove, text: `Linha de OFX acima de ${cap.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}: só quem aprova em Caixa e pagamentos confirma.` },
        { ok: reason.trim().length > 0, text: 'Motivo escrito.' }, { ok: !!chosen, text: 'Conta do plano escolhida.' },
      ]}
      impact={<p className="text-sm">{out ? 'Sai' : 'Entra'} da conta em {formatDate(line.date)}: <Money value={Math.abs(line.amount)} className="font-semibold" /></p>}
      onConfirm={async () => {
        await createBankEntryFromLine(accountId, line.id, { kind, accountId: chosen || null, costCenterId: center || null, reason: reason.trim(), backdateReason: backdate.trim() || null, lineVersion: line.version })
        await onDone()
      }}
      trigger={<button type="button" disabled={line.blocked} title={line.blocked ? line.blockedReason ?? undefined : undefined} className={secondary}>Lançar sem OP</button>}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        {/* A Dica fica fora do <label>: botão dentro do rótulo rouba o nome e o clique do campo. */}
        <div className="space-y-1 text-sm font-medium"><span className="inline-flex items-center"><label htmlFor={kindId}>Tipo</label><Dica nome="Lançar sem OP" texto={ENTRY_HINT} /></span>
          <select id={kindId} value={kind} onChange={(e) => { setKind(e.target.value as BankEntryKind); setPlan('') }} className={inputClass}>{kinds.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}</select>
        </div>
        <label className="space-y-1 text-sm font-medium">Valor (da linha)<MoneyInput value={String(Math.abs(line.amount))} onChange={() => undefined} disabled /></label>
        <label className="space-y-1 text-sm font-medium">Data (da linha)<input value={formatDate(line.date)} disabled className={inputClass} /></label>
        <label className="space-y-1 text-sm font-medium">Conta do plano
          <select value={chosen} onChange={(e) => setPlan(e.target.value)} className={inputClass}><option value="">Escolha</option>{options.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium">Centro de custo (opcional)
          <select value={center} onChange={(e) => setCenter(e.target.value)} className={inputClass}><option value="">Sem centro</option>{(centers.data ?? []).filter((c) => c.isActive).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select>
        </label>
        <label className="space-y-1 text-sm font-medium sm:col-span-2">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
        <label className="space-y-1 text-sm font-medium sm:col-span-2">Motivo da data (data antiga ou período fechado)<input value={backdate} maxLength={200} onChange={(e) => setBackdate(e.target.value)} className={inputClass} /></label>
      </div>
    </ConfirmDialog>
  )
}

/** "Baixar título" (CA 7): a baixa da FIN-01 com a data e a conta da linha; a diferença vira juros, multa ou desconto, editáveis. */
export function LineSettleDialog({ accountId, item, onDone }: { accountId: string; item: ReconUnpairedLine; onDone: () => Promise<void> }) {
  const line = item.line
  const action = item.action!
  const balance = action.receivableBalance ?? line.amount
  const initial = () => {
    const diff = Math.round((line.amount - balance) * 100) / 100
    return { principal: String(Math.min(balance, line.amount)), interest: diff > 0 ? String(diff) : null, discount: diff < 0 ? String(-diff) : null }
  }

  const [principal, setPrincipal] = useState<string | null>(initial().principal)
  const [interest, setInterest] = useState<string | null>(initial().interest)
  const [fine, setFine] = useState<string | null>(null)
  const [discount, setDiscount] = useState<string | null>(initial().discount)
  const [backdate, setBackdate] = useState('')
  const total = Math.round((toNumber(principal) + toNumber(interest) + toNumber(fine) - toNumber(discount)) * 100) / 100
  return (
    <ConfirmDialog size="lg" verb="Baixar título" done="Baixa registrada" title={action.text} description="Data e conta vêm da linha do banco. Juros, multa e desconto seguem os limites do financeiro."
      onOpen={() => { const i = initial(); setPrincipal(i.principal); setInterest(i.interest); setFine(null); setDiscount(i.discount); setBackdate('') }}
      preconditions={[{ ok: !line.blocked, text: line.blockedReason ?? SEGREGATION }, { ok: total === line.amount, text: 'O total da baixa é o valor da linha.' }]}
      impact={(
        <dl className="grid grid-cols-2 gap-1">
          <dt>Saldo do título</dt><dd className="text-right"><Money value={balance} /></dd>
          <dt className="font-semibold">Entra na conta ({formatDate(line.date)})</dt><dd className="text-right font-semibold"><Money value={total} /></dd>
        </dl>
      )}
      onConfirm={async () => {
        await settleFromLine(accountId, line.id, {
          receivableId: action.receivableId!, principal: toNumber(principal), interest: toNumber(interest), fine: toNumber(fine), discount: toNumber(discount),
          backdateReason: backdate.trim() || null, receivableVersion: action.receivableVersion ?? 0, lineVersion: line.version,
        })
        await onDone()
      }}
      trigger={<button type="button" disabled={line.blocked} title={line.blocked ? line.blockedReason ?? undefined : undefined} className={secondary}>Baixar título</button>}>
      <div className="mt-3 grid gap-3 sm:grid-cols-2">
        <label className="space-y-1 text-sm font-medium">Principal<MoneyInput value={principal} onChange={setPrincipal} /></label>
        <label className="space-y-1 text-sm font-medium">Juros<MoneyInput value={interest} onChange={setInterest} /></label>
        <label className="space-y-1 text-sm font-medium">Multa<MoneyInput value={fine} onChange={setFine} /></label>
        <label className="space-y-1 text-sm font-medium">Desconto<MoneyInput value={discount} onChange={setDiscount} /></label>
        <label className="space-y-1 text-sm font-medium sm:col-span-2">Motivo da data (data antiga ou período fechado)<input value={backdate} maxLength={200} onChange={(e) => setBackdate(e.target.value)} className={inputClass} /></label>
      </div>
    </ConfirmDialog>
  )
}

/** "Criar regra a partir desta linha" (CA 2): palavra, tipo do banco, tipo do lançamento e conta do plano. */
export function RuleDialog({ line, onDone }: { line: ReconLine; onDone: () => Promise<void> }) {
  const chart = useListAccounts()
  const kinds = entryKindsFor(line.amount)
  const [keyword, setKeyword] = useState(ruleKeyword(line.description))
  const [byKind, setByKind] = useState(line.kind !== 'Other')
  const [kind, setKind] = useState<BankEntryKind>(kinds[0].value as BankEntryKind)
  const [plan, setPlan] = useState('')
  const out = line.amount < 0
  const options = (chart.data ?? []).filter((a) => a.acceptsEntries && a.isActive && (out ? a.type === 'Expense' : a.type === 'Revenue'))
  return (
    <ConfirmDialog verb="Salvar regra" done="Regra salva" title="Criar regra a partir desta linha" description="As próximas linhas com esta palavra (e este tipo do banco) já vêm com a sugestão de lançamento sem OP."
      onOpen={() => { setKeyword(ruleKeyword(line.description)); setByKind(line.kind !== 'Other'); setKind(kinds[0].value as BankEntryKind); setPlan('') }}
      preconditions={[{ ok: keyword.trim().length >= 3 || byKind, text: 'Palavra com 3 letras ou mais, ou o tipo do banco.' }]}
      onConfirm={async () => {
        await createBankStatementRule({ keyword: keyword.trim() || null, lineKind: byKind ? line.kind : null, entryKind: kind, accountId: plan || null })
        await onDone()
      }}
      trigger={<button type="button" className={link}>Criar regra a partir desta linha</button>}>
      <div className="mt-3 space-y-3">
        <label className="block space-y-1 text-sm font-medium">Palavra na descrição<input value={keyword} maxLength={60} onChange={(e) => setKeyword(e.target.value)} className={inputClass} /></label>
        <label className="flex min-h-11 items-center gap-2 text-sm"><input type="checkbox" className="size-5 accent-primary" checked={byKind} onChange={(e) => setByKind(e.target.checked)} />Só linhas do tipo {LINE_KIND[line.kind ?? 'Other'] ?? line.kind}</label>
        <label className="block space-y-1 text-sm font-medium">Lançar como
          <select value={kind} onChange={(e) => setKind(e.target.value as BankEntryKind)} className={inputClass}>{kinds.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}</select>
        </label>
        <label className="block space-y-1 text-sm font-medium">Conta do plano (opcional)
          <select value={plan} onChange={(e) => setPlan(e.target.value)} className={inputClass}><option value="">A mais usada para o tipo</option>{options.map((a) => <option key={a.id} value={a.id}>{a.code} {a.name}</option>)}</select>
        </label>
      </div>
    </ConfirmDialog>
  )
}

/** "Desfazer conciliação" (CA 10): com motivo, sem estornar nada; no período fechado, só quem aprova (o servidor confere). */
export function UndoDialog({ accountId, done, canApprove, onDone }: { accountId: string; done: ReconDone; canApprove: boolean; onDone: () => Promise<void> }) {
  const [reason, setReason] = useState('')
  return (
    <ConfirmDialog destructive verb="Desfazer conciliação" done="Conciliação desfeita" title="Desfazer esta conciliação?" onOpen={() => setReason('')}
      description="A linha volta a pendente e as transações a não conciliadas. Nada é estornado: um lançamento ou uma baixa feitos pela conciliação só saem pelo estorno."
      impact={<ul className="list-disc space-y-1 pl-5">{done.lines.map((l) => <li key={l.id}>{formatDate(l.date)} · {l.description} · <Money value={l.amount} /></li>)}</ul>}
      preconditions={[{ ok: !done.inClosedPeriod || canApprove, text: 'Período fechado: só quem aprova em Caixa e pagamentos desfaz.' }, { ok: reason.trim().length > 0, text: 'Motivo escrito.' }]}
      onConfirm={async () => { await undoReconciliation(accountId, done.id, { reason: reason.trim(), version: done.version }); await onDone() }}
      trigger={<button type="button" className="inline-flex min-h-11 items-center px-2 text-sm font-semibold text-crit-ink hover:underline">Desfazer conciliação</button>}>
      <label className="mt-3 block space-y-1 text-sm font-medium">Motivo<input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} className={inputClass} /></label>
    </ConfirmDialog>
  )
}
