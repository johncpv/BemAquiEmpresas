import { useNavigate, useSearch } from '@tanstack/react-router'
import { useCallback } from 'react'

/** Aba atual em ?aba= (o voltar do navegador volta à aba anterior). */
export function useUrlTab(fallback: string): [string, (tab: string) => void] {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const navigate = useNavigate()
  const tab = typeof search.aba === 'string' && search.aba ? search.aba : fallback
  const set = useCallback(
    (next: string) => void navigate({ to: '.', search: (prev: Record<string, unknown>) => ({ ...prev, aba: next === fallback ? undefined : next }) } as never),
    [navigate, fallback],
  )
  return [tab, set]
}
