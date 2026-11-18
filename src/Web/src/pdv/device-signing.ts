/**
 * Chave do aparelho de caixa (PDV-01, ADR 0048 §2): par ECDSA P-256 gerado no navegador com a chave privada NÃO exportável, guardado no
 * IndexedDB do próprio aparelho (o navegador guarda o CryptoKey sem expor os bytes). Só a pública sai, em SubjectPublicKeyInfo base64, no
 * registro. Cada chamada de /api/v1/pos/sync/* leva o instante e a assinatura do texto
 * `{MÉTODO}\n{caminho com a query}\n{instante em ms}\n{SHA-256 do corpo em base64}`, no formato r||s do WebCrypto (o mesmo que o servidor confere).
 */
const DB_NAME = 'bemaqui-pdv'
const STORE = 'device'
const KEY_ID = 'key'

export const TIMESTAMP_HEADER = 'X-Device-Timestamp'
export const SIGNATURE_HEADER = 'X-Device-Signature'

const ALGORITHM: EcKeyGenParams = { name: 'ECDSA', namedCurve: 'P-256' }

function toBase64(buffer: ArrayBuffer): string {
  let binary = ''
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte)
  return btoa(binary)
}

/** Texto assinado (igual ao DeviceSignatureHeaders.Payload do servidor). */
export function signedPayload(method: string, pathAndQuery: string, timestamp: string, bodySha256: string): string {
  return [method.toUpperCase(), pathAndQuery, timestamp, bodySha256].join('\n')
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1)
    request.onupgradeneeded = () => request.result.createObjectStore(STORE)
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('IndexedDB indisponível'))
  })
}

async function withStore<T>(mode: IDBTransactionMode, run: (store: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const db = await openDb()
  try {
    return await new Promise<T>((resolve, reject) => {
      const request = run(db.transaction(STORE, mode).objectStore(STORE))
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error ?? new Error('IndexedDB indisponível'))
    })
  } finally {
    db.close()
  }
}

/** O par de chaves guardado neste aparelho, ou null se o aparelho ainda não gerou (ou o navegador apagou os dados do site). */
export async function loadDeviceKey(): Promise<CryptoKeyPair | null> {
  try {
    return (await withStore<CryptoKeyPair | undefined>('readonly', (s) => s.get(KEY_ID))) ?? null
  } catch {
    return null
  }
}

/** Gera o par novo (a privada não exportável) e guarda. Devolve a chave pública em SubjectPublicKeyInfo base64, para o registro. */
export async function createDeviceKey(): Promise<{ keys: CryptoKeyPair; publicKey: string }> {
  const keys = await crypto.subtle.generateKey(ALGORITHM, false, ['sign', 'verify'])
  await withStore('readwrite', (s) => s.put(keys, KEY_ID))
  return { keys, publicKey: toBase64(await crypto.subtle.exportKey('spki', keys.publicKey)) }
}

/** Esquece a chave (aparelho bloqueado ou trocado): o próximo registro gera outra. A chave do documento do consumidor vai junto. */
export async function forgetDeviceKey(): Promise<void> {
  try {
    await withStore('readwrite', (s) => s.delete(KEY_ID))
    await withStore('readwrite', (s) => s.delete(DOCUMENT_KEY_ID))
  } catch {
    // sem IndexedDB não há o que apagar
  }
}

const DOCUMENT_KEY_ID = 'document-key'

/**
 * Chave AES-GCM do documento do consumidor (PDV-02 CA 14, PP-26), não exportável, guardada ao lado da chave do aparelho e apagada com ela
 * (`forgetDeviceKey`): sem a chave do aparelho, o CPF cifrado deste caixa vira lixo ilegível. O WebCrypto não deriva chave de cifra de uma chave
 * ECDSA não exportável; por isso a chave do documento nasce à parte, presa à vida da chave do aparelho. Nasce no primeiro uso.
 */
export async function loadDocumentKey(): Promise<CryptoKey> {
  const saved = await withStore<CryptoKey | undefined>('readonly', (s) => s.get(DOCUMENT_KEY_ID))
  if (saved) return saved
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt'])
  await withStore('readwrite', (s) => s.put(key, DOCUMENT_KEY_ID))
  return key
}

/** Cabeçalhos da assinatura de uma chamada do aparelho. */
export async function signRequest(keys: CryptoKeyPair, method: string, pathAndQuery: string, body = '', now = Date.now()): Promise<Record<string, string>> {
  const encoder = new TextEncoder()
  const timestamp = String(now)
  const bodyHash = toBase64(await crypto.subtle.digest('SHA-256', encoder.encode(body)))
  const signature = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, keys.privateKey, encoder.encode(signedPayload(method, pathAndQuery, timestamp, bodyHash)))
  return { [TIMESTAMP_HEADER]: timestamp, [SIGNATURE_HEADER]: toBase64(signature) }
}

/** O navegador serve de caixa? WebCrypto e IndexedDB (Windows com Chrome ou Edge, Android com Chrome; PP-09). */
export function deviceSupported(): boolean {
  return typeof indexedDB !== 'undefined' && typeof crypto !== 'undefined' && !!crypto.subtle
}
