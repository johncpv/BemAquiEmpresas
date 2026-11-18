import { localStoreAvailable } from '@/pdv/local-store/available'

/**
 * Plataformas do caixa (PP-09, OFF-01 CA 4): **Windows com Chrome ou Edge** e **Android com Chrome**. Fora disso (iPhone, iPad, Mac, Linux,
 * Firefox, Opera, Samsung Internet...) o /pdv não abre: o caixa depende de IndexedDB durável, Web Locks, service worker e WebCrypto, e só
 * nessas combinações o comportamento foi medido (SPK-03). A decisão é pelo texto do navegador (`userAgent`), que é o que o Chrome e o
 * Edge mantêm nos dois sistemas; as APIs obrigatórias também são conferidas, porque um navegador pode se passar por outro.
 */
export const UNSUPPORTED_TITLE = 'Este navegador não é suportado no caixa.'

export const SUPPORTED_BROWSERS = ['Windows com Google Chrome', 'Windows com Microsoft Edge', 'Android com Google Chrome'] as const

export type BrowserSupport = { supported: true; label: string } | { supported: false; reason: 'browser' | 'features' }

/** Navegadores baseados no Chromium que se apresentam com "Chrome/" mas não são Chrome nem Edge. */
const OTHER_CHROMIUM = /\b(OPR|OPiOS|SamsungBrowser|YaBrowser|Vivaldi|Brave|UCBrowser|MiuiBrowser|HuaweiBrowser|EdgA|EdgiOS|Electron|wv)\b|; wv\)/

export function browserFromUserAgent(ua: string): string | null {
  const windows = /Windows NT \d/.test(ua)
  const android = /\bAndroid \d/.test(ua) || /\bAndroid;/.test(ua)
  if (!windows && !android) return null
  if (/CriOS|FxiOS|Firefox\//.test(ua)) return null
  if (windows && /\bEdg\/\d/.test(ua)) return 'Windows com Microsoft Edge'
  if (OTHER_CHROMIUM.test(ua) || /\bEdg\//.test(ua)) return null
  if (!/\b(Headless)?Chrome\/\d/.test(ua)) return null
  return windows ? 'Windows com Google Chrome' : 'Android com Google Chrome'
}

type Env = { userAgent: string; hasIndexedDb: boolean; hasLocks: boolean; hasServiceWorker: boolean; hasSubtleCrypto: boolean }

export function currentEnv(): Env {
  const nav = typeof navigator === 'undefined' ? undefined : navigator
  return {
    userAgent: nav?.userAgent ?? '',
    hasIndexedDb: localStoreAvailable(),
    hasLocks: !!nav && 'locks' in nav && !!nav.locks,
    hasServiceWorker: !!nav && 'serviceWorker' in nav,
    hasSubtleCrypto: typeof crypto !== 'undefined' && !!crypto.subtle,
  }
}

/** O caixa abre neste navegador? `serviceWorker` só é exigido fora do desenvolvimento (o Vite serve sem service worker). */
export function browserSupport(env: Env = currentEnv(), requireServiceWorker = import.meta.env.PROD): BrowserSupport {
  const label = browserFromUserAgent(env.userAgent)
  if (!label) return { supported: false, reason: 'browser' }
  if (!env.hasIndexedDb || !env.hasLocks || !env.hasSubtleCrypto || (requireServiceWorker && !env.hasServiceWorker)) return { supported: false, reason: 'features' }
  return { supported: true, label }
}
