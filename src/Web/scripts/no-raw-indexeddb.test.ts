import { describe, expect, it } from 'vitest'
// @ts-expect-error módulo .mjs sem tipos
import { check, findRawStorage } from './no-raw-indexeddb.mjs'

describe('IndexedDB fora da porta LocalStore (OFF-01 CA 7)', () => {
  it.each([
    "const r = indexedDB.open('x')",
    'window.indexedDB.deleteDatabase(name)',
    "import Dexie from 'dexie'",
    "const { default: Dexie } = await import('dexie')",
    'let db: IDBDatabase',
    'IDBKeyRange.bound(1, 2)',
  ])('acusa %s', (code) => expect(findRawStorage(code)).toHaveLength(1))

  it.each([
    'await store.findByBarcode(code)',
    "import { useLocalStore } from '@/pdv/local-store/local-store'",
    'const myindexedDBName = 1',
    '// o indexedDB fica atrás da porta',
  ])('não acusa %s', (code) => expect(findRawStorage(code)).toHaveLength(0))

  it('o código do front hoje passa (só a porta e a chave do aparelho usam IndexedDB)', () => {
    expect(check(process.cwd())).toEqual([])
  })
})
