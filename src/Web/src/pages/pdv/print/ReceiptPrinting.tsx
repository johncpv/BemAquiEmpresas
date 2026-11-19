import { Printer } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { FloorButton, FloorSheet } from '@/components/shopfloor/kit'
import type { CatalogPrinter } from '@/pdv/local-store/catalog-types'
import type { LocalStore } from '@/pdv/local-store/local-store'
import { formatCents } from '@/pdv/money'
import { type HardwareBridge, PRINT_FAILED_MESSAGE } from '@/pdv/print/hardware-bridge'
import { printSaleReceipt, type ReceiptHeader, recentReceipts, type StoredReceipt, targetOf } from '@/pdv/print/print-jobs'
import { usePrinting } from '@/pdv/print/use-printing'

type Status = 'idle' | 'printing' | 'printed' | 'skipped' | 'failed'

/**
 * Cupom da venda concluída (IMP-01 CA 2, 4, 5 e 8). Loja em "perguntar": "Imprimir" ou "Não imprimir"; em "imprimir sempre", imprime ao abrir;
 * em "não imprimir", fica o botão. A falha nunca mexe na venda: "Não foi possível imprimir." com "Tentar de novo", "Outra impressora" e
 * "Seguir sem imprimir". Depois da primeira via, cada impressão sai como 2ª via (3ª...) e fica registrada.
 */
export function SalePrint({ store, saleId, header, operatorId, registerId, mode, decryptDocument, bridge, onSkip, autoFocusPrint = false }: {
  store: LocalStore
  saleId: string
  header: ReceiptHeader
  operatorId: string | null
  registerId: string | null
  mode: 'Ask' | 'Always' | 'Never'
  decryptDocument?: (id: string) => Promise<string | null>
  bridge?: HardwareBridge
  /** "Não imprimir" / "Seguir sem imprimir": a tela segue (ex.: foco em Nova venda). */
  onSkip?: () => void
  autoFocusPrint?: boolean
}) {
  const { bridge: port, printers } = usePrinting(store, bridge)
  const [status, setStatus] = useState<Status>('idle')
  const [copy, setCopy] = useState(0)
  const [choosing, setChoosing] = useState(false)
  const [printerId, setPrinterId] = useState<string | null>(null)
  const printer = printers.find((p) => p.id === printerId) ?? printers[0] ?? null
  const busy = useRef(false)

  const print = useCallback(async (chosen: CatalogPrinter | null = printer) => {
    if (busy.current) return
    busy.current = true
    setStatus('printing')
    try {
      const result = await printSaleReceipt({ store, bridge: port, printer: targetOf(chosen), saleId, header, operatorId, registerId, decryptDocument })
      if (result.ok) { setCopy(result.copy ?? 1); setStatus('printed') } else setStatus('failed')
    } catch {
      setStatus('failed')
    } finally {
      busy.current = false
    }
  }, [store, port, printer, saleId, header, operatorId, registerId, decryptDocument])

  // "Imprimir sempre": sai sozinho, uma vez, ao concluir.
  const auto = useRef(false)
  useEffect(() => {
    if (mode === 'Always' && !auto.current) { auto.current = true; void print() }
  }, [mode, print])

  const skip = () => { setStatus('skipped'); onSkip?.() }
  const label = printer ? printer.name : 'impressora do navegador'

  return (
    <div className="space-y-3" aria-live="polite">
      {status === 'failed' ? (
        <div role="alert" className="space-y-3 rounded-md border-2 border-crit-soft bg-crit-soft p-4 text-left text-crit-ink">
          <p className="text-[20px] font-semibold">{PRINT_FAILED_MESSAGE}</p>
          <p>A venda está gravada e não muda. Confira se a impressora está ligada, com papel e conectada, e tente de novo.</p>
          <div className="flex flex-wrap gap-[8px]">
            <FloorButton onClick={() => void print()}>Tentar de novo</FloorButton>
            <FloorButton tone="secondary" onClick={() => setChoosing(true)}>Outra impressora</FloorButton>
            <FloorButton tone="secondary" onClick={skip}>Seguir sem imprimir</FloorButton>
          </div>
        </div>
      ) : status === 'printed' ? (
        <div className="flex flex-wrap items-center justify-center gap-[8px]">
          <p role="status" className="text-[18px] font-semibold text-ok-ink">{copy > 1 ? `${copy}ª via enviada para ${label}.` : `Cupom enviado para ${label}.`}</p>
          <FloorButton tone="secondary" onClick={() => void print()}><Printer aria-hidden="true" className="size-5" />Imprimir 2ª via</FloorButton>
        </div>
      ) : status === 'printing' ? (
        <p role="status" className="text-[18px] font-semibold">Imprimindo…</p>
      ) : mode === 'Ask' && status === 'idle' ? (
        <div className="space-y-2">
          <p className="text-[18px] font-semibold">Imprimir o cupom?</p>
          <div className="flex flex-wrap justify-center gap-[8px]">
            <FloorButton autoFocus={autoFocusPrint} onClick={() => void print()}><Printer aria-hidden="true" className="size-5" />Imprimir</FloorButton>
            <FloorButton tone="secondary" onClick={skip}>Não imprimir</FloorButton>
          </div>
        </div>
      ) : (
        <div className="flex flex-wrap justify-center gap-[8px]">
          <FloorButton tone="secondary" onClick={() => void print()}><Printer aria-hidden="true" className="size-5" />Imprimir cupom</FloorButton>
        </div>
      )}
      <PrinterChooser open={choosing} printers={printers} current={printer?.id ?? null} onCancel={() => setChoosing(false)}
        onChoose={(p) => { setChoosing(false); setPrinterId(p?.id ?? null); void print(p) }} />
    </div>
  )
}

/** "Outra impressora" (CA 8): as impressoras do caixa e a do navegador. */
export function PrinterChooser({ open, printers, current, onChoose, onCancel }: {
  open: boolean
  printers: readonly CatalogPrinter[]
  current: string | null
  onChoose: (printer: CatalogPrinter | null) => void
  onCancel: () => void
}) {
  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onCancel() }} title="Outra impressora"
      description="Escolha onde imprimir. O navegador abre o diálogo de impressão: confira a impressora escolhida nele."
      actions={<FloorButton tone="secondary" className="w-full" onClick={onCancel}>Voltar</FloorButton>}>
      <ul className="space-y-[8px]">
        {printers.map((p) => (
          <li key={p.id}>
            <FloorButton tone={p.id === current ? 'primary' : 'secondary'} className="w-full justify-between" onClick={() => onChoose(p)}>
              <span className="truncate">{p.name}</span><span className="text-[16px] tabular-nums">{p.paperWidthMm} mm · {p.columns} col.</span>
            </FloorButton>
          </li>
        ))}
        <li>
          <FloorButton tone="secondary" className="w-full justify-between" onClick={() => onChoose(null)}>
            <span>Impressora do navegador</span><span className="text-[16px] tabular-nums">80 mm · 42 col.</span>
          </FloorButton>
        </li>
      </ul>
    </FloorSheet>
  )
}

/** Reimprimir (CA 4): as últimas vendas deste aparelho; a reimpressão sai com "2ª VIA" e fica registrada com quem e quando. */
export function ReprintSheet({ open, store, header, operatorId, registerId, decryptDocument, bridge, onClose }: {
  open: boolean
  store: LocalStore
  header: ReceiptHeader
  operatorId: string | null
  registerId: string | null
  decryptDocument?: (id: string) => Promise<string | null>
  bridge?: HardwareBridge
  onClose: () => void
}) {
  const [receipts, setReceipts] = useState<StoredReceipt[] | null>(null)
  const [selected, setSelected] = useState<string | null>(null)
  useEffect(() => {
    if (!open) return
    let alive = true
    void recentReceipts(store).then((r) => { if (alive) setReceipts(r) }).catch(() => { if (alive) setReceipts([]) })
    return () => { alive = false; setSelected(null) }
  }, [open, store])

  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) onClose() }} title="Reimprimir cupom"
      description="As últimas vendas deste caixa. A reimpressão sai com 2ª VIA e fica registrada."
      actions={<FloorButton tone="secondary" className="w-full" onClick={onClose}>Voltar</FloorButton>}>
      {receipts === null ? <p>Carregando…</p> : receipts.length === 0 ? (
        <p className="text-[18px] text-muted-foreground">Nenhuma venda concluída neste aparelho ainda.</p>
      ) : (
        <ul className="divide-y divide-border">
          {receipts.map((r) => (
            <li key={r.saleId} className="space-y-2 py-3">
              <button type="button" onClick={() => setSelected(r.saleId)} aria-pressed={selected === r.saleId}
                className="flex min-h-[56px] w-full items-center justify-between gap-2 rounded-md px-2 text-left text-[18px] hover:bg-muted">
                <span className="min-w-0">
                  <b className="block truncate tabular-nums">{r.number}</b>
                  <span className="text-[16px] text-muted-foreground">{new Date(r.completedAt).toLocaleString('pt-BR')}{r.copies > 0 ? ` · ${r.copies} via(s)` : ' · não impresso'}</span>
                </span>
                <b className="tabular-nums">{formatCents(r.totalCents)}</b>
              </button>
              {selected === r.saleId ? (
                <SalePrint store={store} saleId={r.saleId} header={header} operatorId={operatorId} registerId={registerId} mode="Never" decryptDocument={decryptDocument} bridge={bridge} />
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </FloorSheet>
  )
}
