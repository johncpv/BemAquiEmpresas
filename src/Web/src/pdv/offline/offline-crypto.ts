/**
 * Conferência do PIN e do código sem rede no aparelho (OFF-03 CA 1, 2, 7 e 15; adendo à ADR 0026). O servidor manda, por pessoa, os parâmetros
 * PBKDF2 do hash que ele guarda (algoritmo, iterações, sal, tamanho) e o verificador `HMAC-SHA256(chave derivada, mensagem do verificador)`.
 * Aqui, o segredo digitado passa pela mesma função lenta (WebCrypto) e a chave derivada:
 * - confere o verificador (a pessoa digitou o segredo certo);
 * - assina a prova da entrada ou da aprovação, que o servidor confere com o hash dele. Quem tem só o verificador não forja a prova.
 *
 * A chave derivada vive só na memória, dentro de um `CryptoKey` não extraível; o segredo e a chave nunca vão para o disco nem para log.
 * As mensagens são as mesmas do servidor (`PosOfflineProof`): mudar aqui exige mudar lá.
 */
export interface OfflineVerifier {
  userId: string
  displayName: string
  kind: 'operator' | 'supervisor'
  algorithm: string
  iterations: number
  salt: string
  keyLength: number
  nonce: string
  verifier: string
  actions: string[]
}

export const verifierMessage = (kind: string, deviceId: string, userId: string, nonce: string) =>
  `bq-pos-offline-verifier|v1|${kind}|${deviceId.toLowerCase()}|${userId.toLowerCase()}|${nonce}`

export const loginMessage = (deviceId: string, userId: string, loginId: string, at: string) =>
  `bq-pos-offline-login|v1|${deviceId.toLowerCase()}|${userId.toLowerCase()}|${loginId.toLowerCase()}|${at}`

export interface ApprovalProofInput {
  deviceId: string
  approvalId: string
  action: string
  operatorId: string
  supervisorId: string
  saleId: string
  lineId: string | null
  beforeCents: number
  afterCents: number
  at: string
}

export const approvalMessage = (a: ApprovalProofInput) =>
  ['bq-pos-offline-approval', 'v1', a.deviceId, a.approvalId, a.action, a.operatorId, a.supervisorId, a.saleId, a.lineId ?? '', String(a.beforeCents), String(a.afterCents), a.at]
    .map((part, i) => (i >= 2 && i <= 8 && i !== 4 ? part.toLowerCase() : part)).join('|')

export function toBase64(buffer: ArrayBuffer): string {
  let binary = ''
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte)
  return btoa(binary)
}

export function fromBase64(text: string): Uint8Array<ArrayBuffer> {
  const binary = atob(text)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}

const HASHES: Record<string, string> = { 'PBKDF2-SHA512': 'SHA-512', 'PBKDF2-SHA256': 'SHA-256' }

/** Refaz a derivação do servidor com o segredo digitado e devolve a chave HMAC (não extraível). */
export async function deriveKey(secret: string, verifier: Pick<OfflineVerifier, 'algorithm' | 'iterations' | 'salt' | 'keyLength'>): Promise<CryptoKey> {
  const hash = HASHES[verifier.algorithm]
  if (!hash) throw new Error('Formato do verificador desconhecido.')
  const base = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), 'PBKDF2', false, ['deriveBits'])
  const bits = await crypto.subtle.deriveBits({ name: 'PBKDF2', salt: fromBase64(verifier.salt), iterations: verifier.iterations, hash }, base, verifier.keyLength * 8)
  return crypto.subtle.importKey('raw', bits, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign'])
}

export async function mac(key: CryptoKey, message: string): Promise<string> {
  return toBase64(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(message)))
}

/** Confere o segredo digitado contra o verificador deste aparelho; certo, devolve a chave para assinar a prova (null: errado). */
export async function checkSecret(secret: string, verifier: OfflineVerifier, deviceId: string): Promise<CryptoKey | null> {
  const key = await deriveKey(secret, verifier)
  const expected = await mac(key, verifierMessage(verifier.kind, deviceId, verifier.userId, verifier.nonce))
  return expected === verifier.verifier ? key : null
}
