import QRCode from 'qrcode'
import { layoutReceipt, type LaidOutRow, type ReceiptColumns, type ReceiptDocument } from './receipt-model'

/**
 * Porta `HardwareBridge` (IMP-01 CA 1, rascunho B item 3): **toda** impressão do PDV passa por aqui (cupom, 2ª via, comprovante do caixa e
 * página de teste). Quem imprime não sabe como: nesta story o adaptador é o `BrowserPrintBridge` (diálogo de impressão do navegador, funciona
 * sem rede, CA 9); a IMP-02 traz o adaptador ESC/POS (corte, gaveta e QR nativos) e a IMP-03 a ponte e a impressora de rede, sem mudar tela.
 */
export interface HardwareBridge {
  readonly kind: 'browser' | 'escpos' | 'bridge'
  print(doc: ReceiptDocument, printer: PrinterTarget): Promise<PrintOutcome>
}

/** Para onde imprimir: a impressora do cadastro do caixa (ou a padrão do navegador, sem cadastro). */
export interface PrinterTarget {
  /** Id da impressora do cadastro; null = sem cadastro (impressora padrão do navegador, 80 mm com 42 colunas). */
  id: string | null
  name: string
  paperWidthMm: 58 | 80
  columns: ReceiptColumns
}

export type PrintOutcome = { ok: true } | { ok: false; message: string }

/** Mensagem do CA 8: a venda não é afetada; a tela oferece "Tentar de novo", "Outra impressora" e "Seguir sem imprimir". */
export const PRINT_FAILED_MESSAGE = 'Não foi possível imprimir.'

/** Sem impressora cadastrada no caixa: a padrão do navegador, com 42 colunas (cabe em qualquer bobina de 80 mm). */
export const BROWSER_DEFAULT_PRINTER: PrinterTarget = { id: null, name: 'Impressora do navegador', paperWidthMm: 80, columns: 42 }

const escapeHtml = (text: string) => text.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!)

/** Área útil da bobina (mm): 58 mm imprime cerca de 48 mm; 80 mm, cerca de 72 mm. */
export const printableWidthMm = (paperWidthMm: 58 | 80) => (paperWidthMm === 58 ? 48 : 72)

/**
 * HTML da impressão do navegador: uma linha por linha do layout, em fonte monoespaçada com o tamanho que faz `columns` caracteres caberem na
 * área útil da bobina. QR Code em SVG; o corte vira uma linha tracejada (o corte de verdade depende do driver, ou da IMP-02). Sem cor literal:
 * a página impressa é preto no branco por definição do papel, com `color: black` só dentro do documento da impressão.
 */
export async function renderReceiptHtml(doc: ReceiptDocument, printer: PrinterTarget): Promise<string> {
  const rows = layoutReceipt(doc, printer.columns)
  const width = printableWidthMm(printer.paperWidthMm)
  // A largura de um caractere monoespaçado é cerca de 0,6 do tamanho da fonte.
  const fontMm = Math.floor((width / (printer.columns * 0.6)) * 100) / 100
  const body = (await Promise.all(rows.map((row) => renderRow(row, width)))).join('\n')
  return `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>${escapeHtml(doc.title)}</title><style>
@page { size: ${printer.paperWidthMm}mm auto; margin: 0; }
html, body { margin: 0; padding: 0; background: white; color: black; }
body { width: ${width}mm; padding: 2mm ${(printer.paperWidthMm - width) / 2}mm; font-family: "Courier New", Courier, monospace; font-size: ${fontMm}mm; line-height: 1.25; }
pre { margin: 0; white-space: pre; font: inherit; }
.b { font-weight: bold; }
.l { font-size: 2em; line-height: 1.1; }
.qr { display: flex; justify-content: center; padding: 1mm 0; }
.qr svg { width: ${Math.min(30, width - 10)}mm; height: auto; }
.cut { border-top: 0.3mm dashed black; margin: 2mm 0; }
</style></head><body>
${body}
</body></html>`
}

async function renderRow(row: LaidOutRow, widthMm: number): Promise<string> {
  switch (row.kind) {
    case 'text': {
      const cls = [row.bold ? 'b' : '', row.large ? 'l' : ''].filter(Boolean).join(' ')
      return `<pre${cls ? ` class="${cls}"` : ''}>${escapeHtml(row.text) || '&nbsp;'}</pre>`
    }
    case 'qr': {
      const svg = await QRCode.toString(row.data, { type: 'svg', margin: 1, errorCorrectionLevel: 'M', width: Math.round(widthMm * 4) })
      return `<div class="qr">${svg}</div>`
    }
    case 'cut':
      return '<div class="cut" aria-label="corte"></div>'
    case 'drawer':
      return ''
  }
}

/**
 * Adaptador pela impressão do navegador (CA 1 e 9): monta o HTML num `iframe` escondido e chama o `print()` dele. Funciona sem rede (nada vai
 * ao servidor). O navegador não informa papel acabado nem impressora desligada: o que ele informa (página bloqueada, erro ao abrir o diálogo)
 * vira `{ ok: false }`; o resto o operador confere no papel e reimprime a 2ª via.
 */
export class BrowserPrintBridge implements HardwareBridge {
  readonly kind = 'browser' as const

  private readonly doc: Document

  constructor(doc: Document = document) {
    this.doc = doc
  }

  async print(receipt: ReceiptDocument, printer: PrinterTarget): Promise<PrintOutcome> {
    let frame: HTMLIFrameElement | null = null
    try {
      const html = await renderReceiptHtml(receipt, printer)
      frame = this.doc.createElement('iframe')
      frame.setAttribute('aria-hidden', 'true')
      frame.tabIndex = -1
      frame.style.position = 'fixed'
      frame.style.width = '0'
      frame.style.height = '0'
      frame.style.border = '0'
      frame.style.right = '0'
      frame.style.bottom = '0'
      this.doc.body.appendChild(frame)
      const win = frame.contentWindow
      const target = frame.contentDocument ?? win?.document
      if (!win || !target) return { ok: false, message: PRINT_FAILED_MESSAGE }
      target.open()
      target.write(html)
      target.close()
      await new Promise<void>((resolve) => setTimeout(resolve, 50))
      win.focus()
      win.print()
      return { ok: true }
    } catch {
      return { ok: false, message: PRINT_FAILED_MESSAGE }
    } finally {
      const used = frame
      if (used) setTimeout(() => used.remove(), 1000)
    }
  }
}
