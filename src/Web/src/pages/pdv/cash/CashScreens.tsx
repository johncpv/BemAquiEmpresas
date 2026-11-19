import { ArrowDownToLine, ArrowUpFromLine, Delete, LogOut, Printer } from 'lucide-react'
import { useState } from 'react'
import { FloorButton, FloorConfirm, FloorDica, FloorSheet, FloorState } from '@/components/shopfloor/kit'
import { checkMovement, clock, type LocalCashMovement, type LocalCashSession, MAX_REASON_LENGTH } from '@/pdv/cash/cash-session'
import { type Cents, formatCents } from '@/pdv/money'

export const NO_PERMISSION = 'Você não tem permissão para esta ação.'
export const SAVED_LOCALLY = 'Não foi possível registrar. O movimento ficou salvo neste aparelho e será enviado.'

const CASH_OUT_DICA = 'Sangria é o dinheiro que sai da gaveta para o cofre ou para o banco. Sempre com motivo; sai o comprovante para assinar.'
const CASH_IN_DICA = 'Suprimento é o dinheiro que entra na gaveta durante o turno, normalmente para troco. Sempre com motivo.'

/** Teclado numérico grande para dinheiro: os números entram pelos centavos (1, 5, 0, 0, 0 → R$ 150,00). */
export function MoneyPad({ cents, onChange, label }: { cents: Cents; onChange: (cents: Cents) => void; label: string }) {
  const digits = cents === 0 ? '' : String(cents)
  const press = (d: string) => { if (digits.length < 9) onChange(Number(digits + d)) }
  const key = 'min-h-[64px] text-[24px] tabular-nums'
  return (
    <div className="space-y-3">
      <output aria-label={label} aria-live="polite" className="block min-h-[64px] rounded-md border-2 border-input bg-card px-4 py-3 text-center font-display text-[30px] font-bold tabular-nums">
        {formatCents(cents)}
      </output>
      <div role="group" aria-label="Teclado numérico" className="grid grid-cols-3 gap-[8px]">
        {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map((d) => <FloorButton key={d} tone="secondary" className={key} onClick={() => press(d)}>{d}</FloorButton>)}
        <FloorButton tone="secondary" className="min-h-[64px] text-[18px]" onClick={() => onChange(0)}>Limpar</FloorButton>
        <FloorButton tone="secondary" className={key} onClick={() => press('0')}>0</FloorButton>
        <FloorButton tone="secondary" className="min-h-[64px]" aria-label="Apagar o último número" onClick={() => onChange(Number(digits.slice(0, -1) || '0'))}><Delete aria-hidden="true" className="size-7" /></FloorButton>
      </div>
    </div>
  )
}

/** Abrir caixa (CA 1): o fundo de troco (padrão: o da última abertura) e "Abrir caixa"; fundo zero pede confirmação. */
export function CashOpenScreen({ operatorName, defaultCents, onOpen, onLeave }: {
  operatorName: string
  defaultCents: Cents
  onOpen: (openingCents: Cents) => Promise<void>
  onLeave: () => void
}) {
  const [cents, setCents] = useState(defaultCents)
  const [confirmZero, setConfirmZero] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const open = async () => {
    setBusy(true)
    setError(null)
    try { await onOpen(cents) } catch { setError('Não foi possível gravar neste aparelho. Tente de novo.') } finally { setBusy(false) }
  }
  return (
    <section aria-label="Abrir caixa" className="mx-auto max-w-md space-y-4">
      <div>
        <h2 className="font-display text-[26px] font-bold">Abrir caixa</h2>
        <p className="text-[18px] text-muted-foreground">{operatorName}, conte o dinheiro da gaveta e informe o fundo de troco. As vendas deste aparelho ficam no seu nome até você fechar o caixa.</p>
      </div>
      <MoneyPad label="Fundo de troco" cents={cents} onChange={setCents} />
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
      <div className="flex gap-[16px]">
        <FloorButton tone="secondary" className="flex-1" onClick={onLeave} disabled={busy}><LogOut aria-hidden="true" className="size-5" />Sair</FloorButton>
        <FloorButton className="flex-1" disabled={busy} onClick={() => { if (cents === 0) setConfirmZero(true); else void open() }}>{busy ? 'Abrindo…' : 'Abrir caixa'}</FloorButton>
      </div>
      <FloorConfirm open={confirmZero} onOpenChange={setConfirmZero} title="Abrir sem fundo de troco?" verb="Abrir caixa"
        impact={<p>A gaveta começa com R$ 0,00. Você só vai conseguir dar troco depois de um suprimento.</p>} onConfirm={open} />
    </section>
  )
}

/** CA 2: o caixa está aberto por outro operador neste aparelho. */
export function CashBusyScreen({ message, onLeave }: { message: string; onLeave: () => void }) {
  return (
    <FloorState title={message} message="Peça para quem abriu fechar o caixa antes de você começar o seu turno."
      action={<FloorButton tone="secondary" onClick={onLeave}><LogOut aria-hidden="true" className="size-5" />Sair</FloorButton>} />
  )
}

const KIND_LABEL: Record<LocalCashMovement['kind'], string> = { Opening: 'Fundo de troco', CashOut: 'Sangria', CashIn: 'Suprimento' }

/**
 * Painel do caixa (CA 10): operador, hora da abertura, número de vendas, sangrias e suprimentos. **Nunca** o total esperado em dinheiro
 * (conferência cega, CX-02).
 */
export function CashPanel({ session, onBack, onMovement, onClose, notice }: {
  session: LocalCashSession
  onBack: () => void
  onMovement: (kind: 'CashOut' | 'CashIn') => void
  onClose: () => void
  notice: { tone: 'error' | 'ok'; text: string } | null
}) {
  const moves = session.movements.filter((m) => m.kind !== 'Opening')
  return (
    <section aria-label="Caixa" className="mx-auto max-w-3xl space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-[8px]">
        <h2 className="font-display text-[26px] font-bold">Caixa aberto</h2>
        <FloorButton tone="secondary" onClick={onBack}>Voltar à venda</FloorButton>
      </div>
      <dl className="grid gap-[8px] rounded-md border border-border bg-card p-4 text-[18px] sm:grid-cols-3">
        <div><dt className="text-muted-foreground">Operador</dt><dd className="font-semibold">{session.operatorName}</dd></div>
        <div><dt className="text-muted-foreground">Aberto às</dt><dd className="font-semibold tabular-nums">{clock(session.openedAt)}</dd></div>
        <div><dt className="text-muted-foreground">Vendas</dt><dd className="font-semibold tabular-nums">{session.salesCount}</dd></div>
      </dl>
      {notice ? <p role={notice.tone === 'error' ? 'alert' : 'status'} className={notice.tone === 'error' ? 'rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink' : 'rounded-md border border-border bg-card p-4 text-[18px] font-semibold'}>{notice.text}</p> : null}
      <div className="rounded-md border border-border bg-card p-4">
        <h3 className="text-[18px] font-semibold">Sangrias e suprimentos</h3>
        {moves.length === 0
          ? <p className="mt-2 text-[18px] text-muted-foreground">Nenhuma movimentação neste turno.</p>
          : (
            <ul className="mt-2 divide-y divide-border">
              {moves.map((m) => (
                <li key={m.id} className="flex flex-wrap items-baseline justify-between gap-2 py-2 text-[18px]">
                  <span><span className="tabular-nums">{clock(m.at)}</span> · {KIND_LABEL[m.kind]} · {m.reason} <span className="text-muted-foreground">({m.operatorName})</span></span>
                  <span className="font-semibold tabular-nums">{m.kind === 'CashOut' ? '− ' : '+ '}{formatCents(m.amountCents)}</span>
                </li>
              ))}
            </ul>
          )}
      </div>
      <div className="flex flex-wrap gap-[8px]">
        <span className="inline-flex items-center"><FloorButton tone="secondary" onClick={() => onMovement('CashOut')}><ArrowUpFromLine aria-hidden="true" className="size-5" />Sangria</FloorButton><FloorDica nome="Sangria" texto={CASH_OUT_DICA} /></span>
        <span className="inline-flex items-center"><FloorButton tone="secondary" onClick={() => onMovement('CashIn')}><ArrowDownToLine aria-hidden="true" className="size-5" />Suprimento</FloorButton><FloorDica nome="Suprimento" texto={CASH_IN_DICA} /></span>
        <FloorButton tone="secondary" className="ml-auto" onClick={onClose}>Fechar caixa</FloorButton>
      </div>
    </section>
  )
}

/** Sangria ou suprimento (CA 4 e 5): valor e motivo. Nada aqui compara o valor com o dinheiro calculado. */
export function CashMovementSheet({ kind, onCancel, onConfirm }: {
  kind: 'CashOut' | 'CashIn' | null
  onCancel: () => void
  onConfirm: (amountCents: Cents, reason: string) => Promise<void>
}) {
  const [cents, setCents] = useState(0)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const title = kind === 'CashOut' ? 'Sangria' : 'Suprimento'
  const reset = () => { setCents(0); setReason(''); setError(null) }
  const confirm = async () => {
    const check = checkMovement(cents, reason)
    if (!check.ok) { setError(check.message); return }
    setBusy(true)
    try { await onConfirm(check.amountCents, check.reason); reset() } catch { setError('Não foi possível gravar neste aparelho. Tente de novo.') } finally { setBusy(false) }
  }
  return (
    <FloorSheet open={kind !== null} onOpenChange={(o) => { if (!o) { reset(); onCancel() } }} title={title}
      description={kind === 'CashOut' ? 'Valor que sai da gaveta e o motivo. Sai o comprovante em duas vias para assinar.' : 'Valor que entra na gaveta e o motivo.'}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => { reset(); onCancel() }} disabled={busy}>Voltar</FloorButton>
          <FloorButton className="flex-1" onClick={() => void confirm()} disabled={busy}>{busy ? 'Gravando…' : `Registrar ${title.toLowerCase()}`}</FloorButton>
        </div>
      )}>
      <MoneyPad label={`Valor da ${title.toLowerCase()}`} cents={cents} onChange={(v) => { setCents(v); setError(null) }} />
      <label className="block space-y-1">
        <span className="font-semibold">Motivo</span>
        <input value={reason} maxLength={MAX_REASON_LENGTH} onChange={(e) => { setReason(e.target.value); setError(null) }}
          placeholder={kind === 'CashOut' ? 'Ex.: Depósito no banco' : 'Ex.: Troco'}
          className="block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[18px]" />
      </label>
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
    </FloorSheet>
  )
}

/** Comprovante da sangria (duas vias, com assinatura de quem retira) ou do suprimento (CA 4 e 5). Imprime pelo navegador até a IMP-01. */
export function CashReceiptSheet({ movement, place, onDone }: { movement: LocalCashMovement | null; place: string; onDone: () => void }) {
  if (!movement) return null
  const copies = movement.kind === 'CashOut' ? ['1ª via — caixa', '2ª via — quem retira'] : ['Via única']
  const title = movement.kind === 'CashOut' ? 'Sangria registrada' : 'Suprimento registrado'
  return (
    <FloorSheet open onOpenChange={(o) => { if (!o) onDone() }} title={title}
      actions={(
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => window.print()}><Printer aria-hidden="true" className="size-5" />Imprimir</FloorButton>
          <FloorButton className="flex-1" onClick={onDone}>Concluir</FloorButton>
        </div>
      )}>
      {copies.map((copy) => (
        <article key={copy} aria-label={`Comprovante ${copy}`} className="space-y-1 rounded-md border-2 border-dashed border-border bg-card p-4 font-mono text-[16px]">
          <p className="font-semibold">{movement.kind === 'CashOut' ? 'COMPROVANTE DE SANGRIA' : 'COMPROVANTE DE SUPRIMENTO'} · {copy}</p>
          <p>{place}</p>
          <p>{new Date(movement.at).toLocaleString('pt-BR')}</p>
          <p>Valor: <strong className="tabular-nums">{formatCents(movement.amountCents)}</strong></p>
          <p>Motivo: {movement.reason}</p>
          <p>Operador: {movement.operatorName}</p>
          {movement.kind === 'CashOut' ? <p className="pt-6">______________________________<br />Assinatura de quem retira</p> : null}
        </article>
      ))}
    </FloorSheet>
  )
}
