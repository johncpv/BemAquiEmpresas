/** CNPJ com dígitos verificadores (mesma regra do servidor, FND-03b CA 3). */
export function isValidCnpj(value: string): boolean {
  const d = value.replace(/\D/g, '').split('').map(Number)
  if (d.length !== 14 || new Set(d).size === 1) return false
  const check = (len: number) => {
    const weights = len === 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
    const rest = weights.reduce((sum, w, i) => sum + w * d[i], 0) % 11
    return rest < 2 ? 0 : 11 - rest
  }
  return d[12] === check(12) && d[13] === check(13)
}
