import { Weekday } from '@/api/generated/model'

/** Dias da semana na ordem de segunda a domingo, com o rótulo curto da tela (CAD-03 CA 8). */
export const WEEKDAYS: { value: Weekday; label: string }[] = [
  { value: Weekday.Monday, label: 'Seg' }, { value: Weekday.Tuesday, label: 'Ter' }, { value: Weekday.Wednesday, label: 'Qua' },
  { value: Weekday.Thursday, label: 'Qui' }, { value: Weekday.Friday, label: 'Sex' }, { value: Weekday.Saturday, label: 'Sáb' },
  { value: Weekday.Sunday, label: 'Dom' },
]

const BY_INDEX = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb']

/** Dia da semana de uma data "aaaa-mm-dd", sem fuso. */
export const weekdayOf = (iso: string) => BY_INDEX[new Date(iso + 'T12:00:00Z').getUTCDay()]

export const TYPE_LABEL = { Holiday: 'Feriado', Stoppage: 'Parada' } as const

export const HOURS_DICA = 'Quantas horas a fábrica trabalha nesse dia. 0 = dia parado. Valor acima das horas por dia de um centro conta como as horas do centro.'
