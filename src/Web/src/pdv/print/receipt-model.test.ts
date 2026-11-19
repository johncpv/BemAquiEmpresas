import { describe, expect, it } from 'vitest'
import { buildCashReceipt, buildSaleReceipt, buildTestPage, layoutReceipt, NOT_FISCAL_TEXT, receiptDocument, type ReceiptColumns, type SaleReceiptData, wrapText } from './receipt-model'

const texts = (rows: ReturnType<typeof layoutReceipt>) => rows.flatMap((r) => (r.kind === 'text' ? [r.text] : []))

const sale = (over: Partial<SaleReceiptData> = {}): SaleReceiptData => ({
  companyName: 'Mercadinho Bom Preço Comércio de Alimentos e Bebidas em Geral Ltda', companyDocument: '12345678000195', storeName: 'Loja Centro',
  number: 'LOJA-01.1-000123', completedAt: '2026-11-19T15:30:00Z', operatorName: 'Caio O.',
  items: [
    { name: 'Refrigerante de cola garrafa pet dois litros sabor original edição especial', quantity: '12', unit: 'UN', fractional: false, unitCents: 999_999_99 },
    { name: 'Queijo minas', quantity: '0.375', unit: 'KG', fractional: true, unitCents: 4_590, note: 'fatiado fino' },
  ],
  totalCents: 12_000_000_00, payments: [{ label: 'Dinheiro', amountCents: 12_000_050_00 }], changeCents: 50_00,
  customerName: 'Maria da Silva', customerDocument: receiptDocument('52998224725', 'cpf'), copy: 1, ...over,
})

describe('modelo do recibo (IMP-01)', () => {
  it.each([32, 42, 48] as ReceiptColumns[])('CA 3: em %i colunas nenhuma linha passa da largura e nada é cortado', (columns) => {
    const rows = texts(layoutReceipt(buildSaleReceipt(sale()), columns))
    for (const row of rows) expect(row.length).toBeLessThanOrEqual(columns)
    const joined = rows.join(' ').replace(/\s+/g, ' ')
    // Nome longo inteiro (quebrado por palavra) e valores grandes inteiros.
    expect(joined).toContain('Refrigerante de cola garrafa pet dois litros sabor original edição especial')
    expect(rows.some((r) => r.includes('R$ 11.999.999,88'))).toBe(true)
    expect(rows.some((r) => r.includes('R$ 12.000.000,00'))).toBe(true)
    expect(rows.some((r) => r.trimEnd().endsWith('R$ 50,00'))).toBe(true)
  })

  it('CA 2: traz empresa, CNPJ, loja, número, data, operador, itens, total, pagamento, troco, cliente e "Não é documento fiscal"', () => {
    const joined = texts(layoutReceipt(buildSaleReceipt(sale()), 48)).join('\n')
    expect(joined).toContain('12.345.678/0001-95')
    expect(joined).toContain('Loja Centro')
    expect(joined).toContain('LOJA-01.1-000123')
    expect(joined).toContain('19/11/2026 12:30:00')
    expect(joined).toContain('Caio O.')
    expect(joined).toContain('0,375 KG x R$ 45,90')
    expect(joined).toContain('Obs.: fatiado fino')
    expect(joined).toMatch(/TOTAL\s+R\$ 12\.000\.000,00/)
    expect(joined).toMatch(/Dinheiro\s+R\$ 12\.000\.050,00/)
    expect(joined).toMatch(/Troco\s+R\$ 50,00/)
    expect(joined).toContain('Maria da Silva')
    expect(joined.replace(/\s+/g, ' ')).toContain(NOT_FISCAL_TEXT)
  })

  it('"Não é documento fiscal" é obrigatório: sai em destaque no topo e no rodapé, inclusive na 2ª via e no comprovante do caixa', () => {
    for (const doc of [buildSaleReceipt(sale()), buildSaleReceipt(sale({ copy: 2 }))]) {
      const marks = doc.lines.filter((l) => l.kind === 'text' && l.text === NOT_FISCAL_TEXT)
      expect(marks.length).toBe(2)
      expect(marks[0]).toMatchObject({ bold: true, large: true })
    }
    expect(buildCashReceipt({ kind: 'CashOut', place: 'Loja · Caixa 01', at: '2026-11-19T15:30:00Z', amountCents: 50_000, reason: 'Banco', operatorName: 'Ana' })
      .lines.some((l) => l.kind === 'text' && l.text === NOT_FISCAL_TEXT)).toBe(true)
  })

  it('CA 4: a reimpressão sai com "2ª VIA" e a hora da reimpressão; o original não', () => {
    const original = texts(layoutReceipt(buildSaleReceipt(sale()), 42)).join('\n')
    const second = texts(layoutReceipt(buildSaleReceipt(sale({ copy: 2, reprintedAt: '2026-11-19T16:00:00Z' })), 42)).join('\n')
    expect(original).not.toContain('VIA')
    expect(second.replace(/\s+/g, ' ')).toContain('2ª VIA')
    expect(second).toContain('Reimpresso em 19/11/2026 13:00:00')
    expect(buildSaleReceipt(sale({ copy: 3 })).title).toContain('3ª VIA')
  })

  it('PP-26: o CPF sai mascarado no papel; o CNPJ do cliente sai inteiro', () => {
    expect(receiptDocument('52998224725', 'cpf')).toBe('***.982.247-**')
    expect(receiptDocument('12345678000195', 'cnpj')).toBe('12.345.678/0001-95')
    const joined = texts(layoutReceipt(buildSaleReceipt(sale()), 32)).join('\n')
    expect(joined).not.toContain('52998224725')
    expect(joined).not.toContain('529.982.247-25')
  })

  it('CA 6: a página de teste traz a régua das colunas, os acentos, o QR Code e o corte', () => {
    const rows = layoutReceipt(buildTestPage({ printerName: 'Bobina', columns: 32, paperWidthMm: 58, at: '2026-11-19T15:30:00Z' }), 32)
    expect(texts(rows)).toContain('12345678901234567890123456789012')
    expect(texts(rows).join(' ')).toContain('ÁÉÍÓÚ')
    expect(rows.some((r) => r.kind === 'qr')).toBe(true)
    expect(rows.at(-1)?.kind).toBe('cut')
  })

  it('quebra por palavra e corta só a palavra maior que a linha', () => {
    expect(wrapText('um dois tres', 7)).toEqual(['um dois', 'tres'])
    expect(wrapText('abcdefghij', 4)).toEqual(['abcd', 'efgh', 'ij'])
  })
})
