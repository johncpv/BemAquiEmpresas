import { describe, expect, it } from 'vitest'
import { appContactFields } from './app-contact'

describe('contato informado pelo Conecta (INT-04 CA 11)', () => {
  it('lista só o que veio do aplicativo e ainda não foi confirmado', () => {
    expect(appContactFields({ emailSetByApp: false, phoneSetByApp: false })).toEqual([])
    expect(appContactFields({ emailSetByApp: false, phoneSetByApp: true })).toEqual(['o telefone'])
    expect(appContactFields({ emailSetByApp: true, phoneSetByApp: true })).toEqual(['o e-mail', 'o telefone'])
  })
})
