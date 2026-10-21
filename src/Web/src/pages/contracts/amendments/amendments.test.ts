import { describe, expect, it } from 'vitest'
import { milestoneAmounts, proposeReductions } from './amendments'

describe('aditivo (COM-07)', () => {
  it('propõe a redução do último título para o primeiro', () => {
    const reducible = [{ id: 'b', number: 'CR-2', amount: 300 }, { id: 'a', number: 'CR-1', amount: 500 }]
    expect(proposeReductions(reducible, 400)).toEqual([{ receivableId: 'b', newAmount: 0 }, { receivableId: 'a', newAmount: 400 }])
    expect(proposeReductions(reducible, 900)).toBeNull()
  })

  it('valores dos marcos pelo maior resto', () => {
    expect(milestoneAmounts(100, [33.3333, 33.3333, 33.3334])).toEqual([33.33, 33.33, 33.34])
  })
})
