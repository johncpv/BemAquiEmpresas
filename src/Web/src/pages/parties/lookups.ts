import { useState } from 'react'
import { lookupCep, lookupCnpj } from '@/api/generated/bemaqui'
import type { AddressData, CnpjLookupResult } from '@/api/generated/model'
import { ApiError } from '@/api/http'

export type LookupState<T> = { status: 'idle' } | { status: 'loading' } | { status: 'done'; value: T } | { status: 'error'; message: string }

const reason = (e: unknown, fallback: string) => (e instanceof ApiError ? e.message : fallback)

/** Consulta de CNPJ (CAD-01b CA 1 a 7): o servidor confere duplicidade e validade antes de chamar o provedor. */
export function useCnpjLookup() {
  const [state, setState] = useState<LookupState<CnpjLookupResult>>({ status: 'idle' })
  const run = async (cnpj: string, partyId?: string) => {
    setState({ status: 'loading' })
    try {
      const value = await lookupCnpj(cnpj, partyId ? { partyId } : undefined)
      // Resposta sem dados é tratada como falha: o cadastro segue manual.
      if (!value?.data?.legalName) throw new Error('sem dados')
      setState({ status: 'done', value })
      return value
    } catch (e) {
      setState({ status: 'error', message: reason(e, 'Não foi possível consultar o CNPJ agora. Preencha os dados ou tente de novo.') })
      return null
    }
  }

  return { state, run, reset: () => setState({ status: 'idle' }) }
}

/** Consulta de CEP (CA 10): dispara ao completar os 8 dígitos; o chamador preenche só os campos vazios. */
export function useCepLookup(onFound: (address: AddressData) => void) {
  const [state, setState] = useState<LookupState<AddressData>>({ status: 'idle' })
  const [last, setLast] = useState('')
  const onCep = async (cep: string) => {
    if (cep.length !== 8 || cep === last) return
    setLast(cep)
    setState({ status: 'loading' })
    try {
      const value = await lookupCep(cep)
      if (!value?.city) throw new Error('sem dados')
      setState({ status: 'done', value })
      onFound(value)
    } catch (e) {
      setState({ status: 'error', message: reason(e, 'Não foi possível consultar o CEP agora.') })
    }
  }

  const message = state.status === 'loading' ? 'Consultando o CEP…' : state.status === 'error' ? state.message : state.status === 'done' ? 'Endereço preenchido pelo CEP. Confira o número.' : undefined
  return { onCep, message, tone: state.status === 'error' ? ('warn' as const) : ('info' as const) }
}

/** Preenche só o que está vazio (CA 2 e 10): o sistema sugere, nunca apaga o que o usuário digitou. */
export function emptyOnly<T extends Record<string, string>>(current: T, incoming: Partial<Record<keyof T, string | null | undefined>>): Partial<T> {
  const patch: Partial<T> = {}
  for (const [key, value] of Object.entries(incoming) as [keyof T, string | null | undefined][]) {
    if (value && !current[key]?.trim()) patch[key] = value as T[keyof T]
  }

  return patch
}
