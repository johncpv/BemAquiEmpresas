import type { Shift } from '@/api/generated/model'

const minutes = (t: string) => {
  const m = /^(\d{1,2}):(\d{2})$/.exec(t)
  return m ? Number(m[1]) * 60 + Number(m[2]) : null
}

/** Horas por dia dos turnos válidos, como o servidor calcula (CAD-03 CA 12). */
export function hoursPerDay(shifts: Shift[]) {
  const total = shifts.reduce((sum, s) => {
    const a = minutes(s.start)
    const b = minutes(s.end)
    return a !== null && b !== null && b > a ? sum + (b - a) : sum
  }, 0)
  return Math.round((total / 60) * 100) / 100
}

/** Mesmas regras do servidor (CA 13), para avisar antes de salvar; o servidor confere de novo. */
export function shiftsError(shifts: Shift[]): string | undefined {
  if (shifts.length === 0) return 'Informe ao menos um turno.'
  const parsed: [number, number][] = []
  for (const s of shifts) {
    const a = minutes(s.start)
    const b = minutes(s.end)
    if (a === null || b === null) return 'Informe início e fim de cada turno no formato 00:00.'
    if (b <= a) return 'Turno que vira o dia não é aceito.'
    parsed.push([a, b])
  }
  parsed.sort((x, y) => x[0] - y[0])
  for (let i = 1; i < parsed.length; i++) if (parsed[i][0] < parsed[i - 1][1]) return 'Os turnos não podem se sobrepor.'
  return undefined
}

export const formatHours = (h: number) => new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(h)

export const shiftsSummary = (shifts: Shift[]) => shifts.map((s) => `${s.start}–${s.end}`).join(' · ')
