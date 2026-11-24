import { Printer } from 'lucide-react'
import { useMemo, useState } from 'react'
import { FloorButton, FloorConfirm, FloorDica } from '@/components/shopfloor/kit'
import { type CashCountLine, type CashCountResult, countedCashCents, depositCents } from '@/pdv/cash/cash-session'
import { buildClosingReceipt } from '@/pdv/cash/closing-receipt'
import type { CatalogPaymentMethod, CatalogPrinter } from '@/pdv/local-store/catalog-types'
import { type Cents, formatCents } from '@/pdv/money'
import { usePdv } from '@/pdv/pdv-context'
import { BrowserPrintBridge, PRINT_FAILED_MESSAGE } from '@/pdv/print/hardware-bridge'
import { INSTALLED_PRINTER_META, printDocument, printersOf, targetOf } from '@/pdv/print/print-jobs'
import { MoneyPad } from './CashScreens'

export const BLIND_DICA = 'Conferência cega: você conta o dinheiro e os comprovantes sem ver quanto deveria haver. A diferença aparece de verdade, e quem confere é o gerente.'
export const KEPT_DICA = 'O dinheiro que fica na gaveta para o troco do próximo turno. O resto vai para o cofre (recolhimento), com comprovante.'
export const SENT_MESSAGE = 'Fechamento enviado para conferência.'

/** Cédulas e moedas do real, para a contagem por peça (opcional). */
const PIECES: readonly Cents[] = [20000, 10000, 5000, 2000, 1000, 500, 200, 100, 50, 25, 10, 5]

type Step = 'count' | 'kept'

/**
 * Fechar caixa (CX-02 CA 2, 4 e 12): o operador informa o dinheiro (com a contagem por cédula e moeda, opcional) e o total dos comprovantes de
 * cada forma de cartão e de Pix; depois, o fundo de troco que fica. **A tela não mostra esperado, diferença nem total de vendas** (o aparelho nem
 * tem esses números). "Confirmar contagem" é definitivo: recontar é do supervisor.
 */
export function CashCountScreen({ methods, operatorName, defaultKeptCents, onConfirm }: {
  methods: readonly CatalogPaymentMethod[]
  operatorName: string
  defaultKeptCents: Cents
  onConfirm: (lines: CashCountLine[], keptCents: Cents) => Promise<void>
}) {
  const cashMethod = methods.find((m) => m.kind === 'Cash') ?? null
  const others = useMemo(() => methods.filter((m) => m.kind !== 'Cash').sort((a, b) => a.name.localeCompare(b.name, 'pt-BR')), [methods])
  const cashKey = cashMethod?.id ?? 'cash'
  const [values, setValues] = useState<Record<string, Cents>>({})
  const [pieces, setPieces] = useState<Record<number, number> | null>(null)
  const [editing, setEditing] = useState<string>(cashKey)
  const [step, setStep] = useState<Step>('count')
  const [kept, setKept] = useState<Cents | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const piecesTotal = pieces ? PIECES.reduce((sum, p) => sum + p * (pieces[p] ?? 0), 0) : null
  const cashCents = piecesTotal ?? values[cashKey] ?? 0
  const lines: CashCountLine[] = [
    { methodId: cashMethod?.id ?? null, countedCents: cashCents, denominations: pieces ? PIECES.filter((p) => (pieces[p] ?? 0) > 0).map((p) => ({ cents: p, quantity: pieces[p] ?? 0 })) : null },
    ...others.map((m) => ({ methodId: m.id, countedCents: values[m.id] ?? 0 })),
  ]
  const keptCents = Math.min(kept ?? defaultKeptCents, cashCents)
  const cashIds = new Set(cashMethod ? [cashMethod.id] : [])

  const confirm = async () => {
    setError(null)
    try {
      await onConfirm(lines, keptCents)
    } catch (e) {
      setError(e instanceof Error && e.message ? e.message : 'Não foi possível gravar neste aparelho. Tente de novo.')
    }
  }

  if (step === 'kept') {
    return (
      <section aria-label="Fundo que fica" className="mx-auto max-w-md space-y-4">
        <div>
          <h2 className="flex items-center font-display text-[26px] font-bold">Fundo que fica<FloorDica nome="Fundo que fica" texto={KEPT_DICA} /></h2>
          <p className="text-[18px] text-muted-foreground">Dinheiro contado: <strong className="tabular-nums">{formatCents(cashCents)}</strong>. Quanto fica na gaveta para o próximo turno?</p>
        </div>
        <MoneyPad label="Fundo que fica" cents={keptCents} onChange={(v) => { setKept(v); setError(null) }} />
        <p className="rounded-md border border-border bg-card p-4 text-[18px]">Recolhimento ao cofre: <strong className="tabular-nums">{formatCents(depositCents(countedCashCents(lines, cashIds), keptCents))}</strong></p>
        {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => setStep('count')}>Voltar à contagem</FloorButton>
          <FloorButton className="flex-1" onClick={() => setConfirming(true)}>Confirmar contagem</FloorButton>
        </div>
        <FloorConfirm open={confirming} onOpenChange={setConfirming} title="Confirmar a contagem?" verb="Confirmar contagem"
          impact={<p>Depois de confirmar, você não pode refazer a contagem: recontar é com o supervisor. Sai o comprovante do recolhimento para o cofre.</p>}
          onConfirm={confirm} />
      </section>
    )
  }

  return (
    <section aria-label="Fechar caixa" className="mx-auto max-w-3xl space-y-4">
      <div>
        <h2 className="flex items-center font-display text-[26px] font-bold">Fechar caixa<FloorDica nome="Conferência cega" texto={BLIND_DICA} /></h2>
        <p className="text-[18px] text-muted-foreground">{operatorName}, conte o dinheiro da gaveta e some os comprovantes de cada forma. Toque na forma para digitar.</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <ul aria-label="Formas de pagamento" className="space-y-2">
          <li>
            <button type="button" aria-pressed={editing === cashKey} onClick={() => setEditing(cashKey)}
              className="flex min-h-[64px] w-full items-center justify-between rounded-md border-2 border-input bg-card px-4 text-left text-[18px] aria-pressed:border-primary">
              <span className="font-semibold">{cashMethod?.name ?? 'Dinheiro'}</span><span className="font-bold tabular-nums">{formatCents(cashCents)}</span>
            </button>
          </li>
          {others.map((m) => (
            <li key={m.id}>
              <button type="button" aria-pressed={editing === m.id} onClick={() => setEditing(m.id)}
                className="flex min-h-[64px] w-full items-center justify-between rounded-md border-2 border-input bg-card px-4 text-left text-[18px] aria-pressed:border-primary">
                <span className="font-semibold">{m.name}</span><span className="font-bold tabular-nums">{formatCents(values[m.id] ?? 0)}</span>
              </button>
            </li>
          ))}
        </ul>
        <div className="space-y-3">
          {editing === cashKey && pieces ? (
            <fieldset className="space-y-2">
              <legend className="text-[18px] font-semibold">Contagem por cédula e moeda</legend>
              <div className="grid grid-cols-2 gap-2">
                {PIECES.map((p) => (
                  <label key={p} className="flex items-center justify-between gap-2 rounded-md border border-border bg-card px-3 py-2 text-[18px]">
                    <span className="tabular-nums">{formatCents(p)}</span>
                    <input inputMode="numeric" aria-label={`Quantidade de ${formatCents(p)}`} value={pieces[p] ? String(pieces[p]) : ''} maxLength={5}
                      onChange={(e) => setPieces({ ...pieces, [p]: Number(e.target.value.replace(/\D/g, '') || '0') })}
                      className="min-h-[48px] w-20 rounded-md border-2 border-input bg-card px-2 text-right tabular-nums" />
                  </label>
                ))}
              </div>
              <FloorButton tone="secondary" onClick={() => setPieces(null)}>Digitar o total</FloorButton>
            </fieldset>
          ) : (
            <>
              <MoneyPad label={editing === cashKey ? 'Dinheiro contado' : `Total dos comprovantes de ${others.find((m) => m.id === editing)?.name ?? ''}`}
                cents={editing === cashKey ? cashCents : values[editing] ?? 0} onChange={(v) => setValues({ ...values, [editing]: v })} />
              {editing === cashKey ? <FloorButton tone="secondary" onClick={() => setPieces({})}>Contar por cédula e moeda</FloorButton> : null}
            </>
          )}
        </div>
      </div>
      <div className="flex justify-end">
        <FloorButton onClick={() => setStep('kept')}>Continuar</FloorButton>
      </div>
    </section>
  )
}

/**
 * Depois da contagem (CX-02 CA 6, 9 e 12): só "Fechamento enviado para conferência." (o resultado é do gerente), o comprovante do recolhimento em
 * duas vias e a via do operador do relatório, para imprimir. "Concluir" leva à abertura do próximo turno.
 */
export function CashClosedScreen({ result, place, methodName, onDone }: {
  result: CashCountResult
  place: string
  methodName: (methodId: string | null) => string
  onDone: () => void
}) {
  const store = usePdv()?.store ?? null
  const [failed, setFailed] = useState(false)
  const doc = buildClosingReceipt({ result, place, methodName })
  const print = async () => {
    const printers = store ? printersOf(store.list('printers'), await store.getMeta<CatalogPrinter>(INSTALLED_PRINTER_META).catch(() => undefined)) : []
    setFailed(!(await printDocument(new BrowserPrintBridge(), doc, targetOf(printers[0]))).ok)
  }
  return (
    <section aria-label="Caixa fechado" className="mx-auto max-w-md space-y-4">
      <p role="status" className="rounded-md border border-border bg-card p-4 text-[20px] font-semibold">{SENT_MESSAGE}</p>
      {failed ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{PRINT_FAILED_MESSAGE} A contagem já está gravada.</p> : null}
      <article aria-label="Comprovante do fechamento" className="space-y-1 rounded-md border-2 border-dashed border-border bg-card p-4 font-mono text-[15px]">
        {doc.lines.map((line, i) => line.kind === 'text' ? <p key={i} className={line.bold ? 'font-semibold' : undefined}>{line.text}</p>
          : line.kind === 'pair' ? <p key={i} className="flex justify-between gap-2"><span>{line.left}</span><span className="tabular-nums">{line.right}</span></p>
          : line.kind === 'rule' ? <hr key={i} className="border-border" />
          : line.kind === 'cut' ? <hr key={i} className="my-2 border-dashed border-border" /> : null)}
      </article>
      <div className="flex gap-[16px]">
        <FloorButton tone="secondary" className="flex-1" onClick={() => void print()}><Printer aria-hidden="true" className="size-5" />{failed ? 'Tentar de novo' : 'Imprimir'}</FloorButton>
        <FloorButton className="flex-1" onClick={onDone}>Concluir</FloorButton>
      </div>
    </section>
  )
}
