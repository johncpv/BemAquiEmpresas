import { applyPattern, CNPJ_PATTERN, CPF_PATTERN, isValidCnpj, isValidCpf, onlyAlnum, onlyDigits } from '@/lib/masks'
import type { CustomerDocumentRecord } from '@/pdv/local-store/sale-journal-types'

/**
 * CPF ou CNPJ do consumidor na venda (PDV-02 CA 14, PP-26): serve só para a nota e para achar a compra numa devolução. Não é obrigatório.
 * O dígito é conferido na hora; o documento fica **cifrado** no aparelho (AES-GCM com a chave do documento, que nasce e morre com a chave do
 * aparelho), num registro à parte que pode ser apagado. Nunca vai em claro para log, URL, evento ou fila de saída.
 */
export type DocumentCheck = { ok: true; kind: 'cpf' | 'cnpj'; value: string } | { ok: false; message: string }

export function checkCustomerDocument(text: string): DocumentCheck {
  const alnum = onlyAlnum(text)
  if (/^\d{11}$/.test(alnum)) return isValidCpf(alnum) ? { ok: true, kind: 'cpf', value: alnum } : { ok: false, message: 'CPF inválido: confira os números.' }
  if (/^[0-9A-Z]{12}\d{2}$/.test(alnum)) return isValidCnpj(alnum) ? { ok: true, kind: 'cnpj', value: alnum } : { ok: false, message: 'CNPJ inválido: confira os números.' }
  return { ok: false, message: onlyDigits(text).length < 11 ? 'O CPF tem 11 números; o CNPJ, 14.' : 'Digite um CPF (11 números) ou um CNPJ (14).' }
}

/** "123.456.789-09" ou "12.345.678/0001-95", para mostrar ao operador (só na tela). */
export function formatDocument(value: string, kind: 'cpf' | 'cnpj'): string {
  return applyPattern(value, kind === 'cpf' ? CPF_PATTERN : CNPJ_PATTERN)
}

export interface DocumentCipher {
  encrypt(plain: string): Promise<{ iv: string; cipher: string }>
  decrypt(record: Pick<CustomerDocumentRecord, 'iv' | 'cipher'>): Promise<string>
}

const toBase64 = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes))
const fromBase64 = (text: string) => Uint8Array.from(atob(text), (c) => c.charCodeAt(0))

/** Cifra AES-GCM com a chave dada (no caixa, a chave do documento guardada junto da chave do aparelho; nos testes, uma chave qualquer). */
export function aesDocumentCipher(getKey: () => Promise<CryptoKey>): DocumentCipher {
  return {
    async encrypt(plain) {
      const iv = crypto.getRandomValues(new Uint8Array(12))
      const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, await getKey(), new TextEncoder().encode(plain))
      return { iv: toBase64(iv), cipher: toBase64(new Uint8Array(data)) }
    },
    async decrypt(record) {
      const data = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: fromBase64(record.iv) }, await getKey(), fromBase64(record.cipher))
      return new TextDecoder().decode(data)
    },
  }
}
