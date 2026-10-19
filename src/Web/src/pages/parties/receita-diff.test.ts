import { describe, expect, it } from 'vitest'
import type { CnpjData, PartyDetail } from '@/api/generated/model'
import { differences } from './receita-diff'

const party = { legalName: 'ALFA LTDA', tradeName: 'Alfa', email: null, phone: '1236531000', addresses: [] } as unknown as PartyDetail
const data: CnpjData = {
  document: '47960950000121', legalName: 'ALFA ELEVADORES LTDA', tradeName: 'Alfa', situation: 'ATIVA', phone: '1236531000', email: 'contato@alfa.com.br',
  address: { zipCode: '12280000', street: 'Rua das Flores', number: '120', complement: null, district: 'Centro', city: 'Caçapava', state: 'SP', ibgeCode: '3508504' },
}

describe('Atualizar pela Receita (CAD-01b CA 8)', () => {
  it('lista só o que difere e o endereço quando completo', () => {
    expect(differences(party, data).map((r) => r.key)).toEqual(['legalName', 'email', 'address'])
  })

  it('endereço incompleto da Receita não entra na comparação', () => {
    expect(differences(party, { ...data, address: { ...data.address!, district: null } }).map((r) => r.key)).not.toContain('address')
  })
})
