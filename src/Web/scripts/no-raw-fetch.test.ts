import { describe, expect, it } from 'vitest'
// @ts-expect-error módulo .mjs sem tipos
import { findRawCalls } from './no-raw-fetch.mjs'

describe('chamada à API fora do cliente gerado (FND-09c CA 2)', () => {
  it.each(["await fetch('/api/v1/x')", "import axios from 'axios'", 'const r = fetch (url)', 'new XMLHttpRequest()'])('acusa %s', (code) =>
    expect(findRawCalls(code)).toHaveLength(1),
  )

  it.each(['await listMyNotifications()', 'prefetch(x)', 'router.prefetch()', 'const refetch = q.refetch'])('não acusa %s', (code) =>
    expect(findRawCalls(code)).toHaveLength(0),
  )
})
