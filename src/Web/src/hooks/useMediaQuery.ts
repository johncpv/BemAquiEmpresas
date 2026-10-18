import { useSyncExternalStore } from 'react'

/** Media query reativa (dock: popover no desktop, sheet no celular). */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (notify) => {
      const list = window.matchMedia(query)
      list.addEventListener('change', notify)
      return () => list.removeEventListener('change', notify)
    },
    () => window.matchMedia(query).matches,
    () => false,
  )
}

export const MOBILE = '(max-width: 640px)'
