import { useEffect, useState, useSyncExternalStore } from 'react'
import { logout, useGetShopfloorDevice } from '@/api/generated/bemaqui'
import type { ShopfloorDeviceContext } from '@/api/generated/model'
import { FAB_ENTRY } from '@/api/http'
import { getAccessToken, isEphemeralSession, isPinSession, onSessionChange, setAccessToken } from '@/api/session'
import { applyTheme } from '@/theme/theme'
import { themeOf } from './device'

/** Encerra a sessão do tablet (revoga o refresh, se houver) e volta à lista de nomes. */
export async function endFloorSession(go: (to: string) => void) {
  try {
    await logout()
  } catch {
    // sem rede: o token some da memória mesmo assim; a sessão por PIN acaba no servidor no máximo nas 12 h
  }

  setAccessToken(null)
  go(FAB_ENTRY)
}

/** A sessão atual é do tablet (por PIN ou o login completo sem persistência)? */
export function useFloorSession() {
  const token = useSyncExternalStore(onSessionChange, getAccessToken, () => null)
  return { pin: isPinSession(token), ephemeral: isEphemeralSession(), token }
}

/** Aparelho do /fab (registrado ou não, tema e o que o usuário pode fazer nele). */
export function useFloorDevice() {
  return useGetShopfloorDevice({ query: { retry: false, staleTime: 60_000 } })
}

/** Tema do aparelho registrado aplicado no tablet (CA 11). */
export function applyDeviceTheme(context: ShopfloorDeviceContext | undefined) {
  if (context?.registered) applyTheme(themeOf(context.theme))
}

/** Rede do navegador (online/offline), atualizada pelos eventos da janela. */
export function useOnline() {
  const [online, setOnline] = useState(() => typeof navigator === 'undefined' || navigator.onLine)
  useEffect(() => {
    const up = () => setOnline(true)
    const down = () => setOnline(false)
    window.addEventListener('online', up)
    window.addEventListener('offline', down)
    return () => {
      window.removeEventListener('online', up)
      window.removeEventListener('offline', down)
    }
  }, [])
  return [online, setOnline] as const
}