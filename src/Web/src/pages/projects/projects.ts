export const PROJECT_DICAS = {
  margemProjetada: 'Receita contratada menos o custo projetado (realizado mais o que falta do orçado ou o comprometido, o maior), sobre a receita.',
  realizado: 'Custo já gasto: títulos a pagar confirmados com o projeto (instalador, frete, outros), horas apontadas na produção e o material baixado para as OS, menos as devoluções.',
  comprometido: 'Já contratado, mas ainda não gasto: título previsto ou material comprado que não foi baixado para a OS.',
  projetado: 'Realizado mais o maior entre o que ainda falta do orçado e o comprometido. Se o realizado passou do orçado e nada está comprometido, é o próprio realizado.',
  implantacao: 'Custo calculado na implantação: horas com o custo-hora do cargo daquele dia e saídas de material lançadas antes do cálculo do custo do projeto.',
  desvio: 'Projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.',
}

export const pct = (v: number | null | undefined) => (v === null || v === undefined ? '—' : `${v.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`)

/** Dias de atraso da entrega prometida (COM-05): só enquanto o projeto não foi entregue. */
export function delayDays(promisedDate: string | null | undefined, status: string | null | undefined, today: string) {
  if (!promisedDate || status === 'Delivered' || status === 'Closed' || status === 'Cancelled' || promisedDate >= today) return null
  return Math.round((Date.parse(today + 'T00:00:00Z') - Date.parse(promisedDate + 'T00:00:00Z')) / 86_400_000)
}
