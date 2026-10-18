/**
 * Números no padrão brasileiro (FND-09d CA 5). O valor circula como string normalizada ("1234.56") até a API, para
 * não perder precisão em number; o arredondamento é meio para longe do zero, como o AwayFromZero do backend.
 */
export function parseBrDecimal(text: string, scale: number): string | null {
  const raw = text.trim().replace(/\s|R\$|%/g, '')
  if (raw === '') return null
  const negative = raw.startsWith('-')
  let body = negative ? raw.slice(1) : raw
  if (!/^[\d.,]+$/.test(body)) return null
  if (body.includes(',')) {
    const at = body.lastIndexOf(',')
    body = body.slice(0, at).replace(/[.,]/g, '') + '.' + body.slice(at + 1).replace(/[.,]/g, '')
  } else {
    // Sem vírgula: um único ponto que não separa milhar (ex.: colado "1234.5") é o separador decimal.
    const parts = body.split('.')
    body = parts.length === 2 && parts[1].length !== 3 ? parts[0] + '.' + parts[1] : parts.join('')
  }
  const [intPart = '', fracPart = ''] = body.split('.')
  if (intPart === '' && fracPart === '') return null
  const padded = (fracPart + '0'.repeat(scale + 1)).slice(0, scale + 1)
  let units = BigInt((intPart || '0') + padded.slice(0, scale))
  if (Number(padded[scale]) >= 5) units += 1n
  const s = units.toString().padStart(scale + 1, '0')
  const value = scale > 0 ? s.slice(0, s.length - scale) + '.' + s.slice(s.length - scale) : s
  return negative && units !== 0n ? '-' + value : value
}

export function formatBrDecimal(normalized: string | null | undefined, scale: number): string {
  if (normalized === null || normalized === undefined || normalized === '') return ''
  const negative = normalized.startsWith('-')
  const [intPart, fracPart = ''] = (negative ? normalized.slice(1) : normalized).split('.')
  const grouped = intPart.replace(/^0+(?=\d)/, '').replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  const frac = scale > 0 ? ',' + (fracPart + '0'.repeat(scale)).slice(0, scale) : ''
  return (negative ? '-' : '') + grouped + frac
}

/** Comparação sem float, para faixas (CA 6). */
export function compareDecimal(a: string, b: string): number {
  const scale = Math.max(a.split('.')[1]?.length ?? 0, b.split('.')[1]?.length ?? 0)
  const units = (v: string) => {
    const neg = v.startsWith('-')
    const [i, f = ''] = (neg ? v.slice(1) : v).split('.')
    const n = BigInt(i + (f + '0'.repeat(scale)).slice(0, scale))
    return neg ? -n : n
  }
  const d = units(a) - units(b)
  return d === 0n ? 0 : d > 0n ? 1 : -1
}

/** Soma exata de valores normalizados (ex.: % das parcelas), com o número de casas pedido. */
export function sumDecimal(values: (string | null | undefined)[], scale: number): string {
  let units = 0n
  for (const v of values) {
    if (!v) continue
    const neg = v.startsWith('-')
    const [i, f = ''] = (neg ? v.slice(1) : v).split('.')
    const n = BigInt(i + (f + '0'.repeat(scale)).slice(0, scale))
    units += neg ? -n : n
  }
  const neg = units < 0n
  const s = (neg ? -units : units).toString().padStart(scale + 1, '0')
  const value = scale > 0 ? s.slice(0, s.length - scale) + '.' + s.slice(s.length - scale) : s
  return neg ? '-' + value : value
}
