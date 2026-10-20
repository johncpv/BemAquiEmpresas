import { describe, expect, it } from 'vitest'
import { delayDays } from './projects'

describe('projeto 360 (COM-05)', () => {
  it('atraso só enquanto não entregue', () => {
    expect(delayDays('2026-10-10', 'Planned', '2026-10-20')).toBe(10)
    expect(delayDays('2026-10-10', 'Delivered', '2026-10-20')).toBeNull()
    expect(delayDays('2026-10-25', 'Planned', '2026-10-20')).toBeNull()
    expect(delayDays(null, 'Planned', '2026-10-20')).toBeNull()
  })
})
