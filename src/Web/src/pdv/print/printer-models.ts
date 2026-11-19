import type { ReceiptColumns } from './receipt-model'

/**
 * Lista curta de modelos do assistente "Instalar impressora" (IMP-01 CA 6), com a largura e as colunas que cada um usa por padrão. Nenhum foi
 * testado ainda com o kit de hardware (PP-19, SPK-01): o resultado dos testes entra aqui e em `Ajuda/pdv-impressora.md`.
 */
export interface PrinterModel {
  key: string
  label: string
  paperWidthMm: 58 | 80
  columns: ReceiptColumns
}

export const PRINTER_MODELS: readonly PrinterModel[] = [
  { key: 'termica-80', label: 'Térmica 80 mm (a maioria, 48 colunas)', paperWidthMm: 80, columns: 48 },
  { key: 'epson-tm-t20', label: 'Epson TM-T20', paperWidthMm: 80, columns: 48 },
  { key: 'elgin-i9', label: 'Elgin i9', paperWidthMm: 80, columns: 48 },
  { key: 'bematech-mp4200', label: 'Bematech MP-4200 TH', paperWidthMm: 80, columns: 48 },
  { key: 'daruma-dr800', label: 'Daruma DR800', paperWidthMm: 80, columns: 48 },
  { key: 'termica-58', label: 'Térmica 58 mm (32 colunas)', paperWidthMm: 58, columns: 32 },
  { key: 'portatil-58-bt', label: 'Portátil Bluetooth 58 mm (pareada no sistema)', paperWidthMm: 58, columns: 32 },
  { key: 'outro', label: 'Outro modelo', paperWidthMm: 80, columns: 42 },
]
