import { describe, expect, it } from 'vitest'
import { divergenceTone, formatAccessKey, formatCnpj, importedLabel, precheck, priceText, quantityText, totalText } from './incoming'

describe('nota de entrada (FIS-04a)', () => {
  it('divergência acima é crítica, abaixo é alerta, sempre com o texto', () => {
    expect(divergenceTone('Critical')).toBe('crit')
    expect(divergenceTone('Warning')).toBe('warn')
    expect(divergenceTone('Ok')).toBe('ok')
    expect(quantityText(2, 'UN')).toBe('2 UN acima do saldo do pedido')
    expect(quantityText(-5, 'UN')).toBe('5 UN abaixo do saldo do pedido')
    expect(quantityText(0, 'UN')).toBe('Quantidade igual ao saldo do pedido')
    expect(priceText(0.5, 4.17)).toMatch(/^R\$\s0,50 acima do pedido \(\+4,17%\)$/)
    expect(totalText('Frete', -10, -25)).toMatch(/^Frete R\$\s10,00 abaixo do pedido \(-25%\)$/)
  })

  it('confere extensão e tamanho antes de enviar e escreve o verbo', () => {
    expect(precheck({ name: 'nota.XML', size: 10 })).toBeNull()
    expect(precheck({ name: 'nota.pdf', size: 10 })).toBe('Envie o arquivo .xml da nota.')
    expect(precheck({ name: 'nota.xml', size: 3 * 1024 * 1024 })).toBe('O arquivo precisa ter até 2 MB.')
    expect(importedLabel(1)).toBe('1 nota importada')
    expect(importedLabel(3)).toBe('3 notas importadas')
  })

  it('formata chave e CNPJ', () => {
    expect(formatAccessKey('35261047225005000186550010000012341123456785')).toBe('3526 1047 2250 0500 0186 5500 1000 0012 3411 2345 6785')
    expect(formatCnpj('47225005000186')).toBe('47.225.005/0001-86')
  })
})
