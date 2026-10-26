import { useCallback, useEffect, useState } from 'react'
import { nextIndex } from './panel'

/**
 * Rotação das visões (CA 7): troca a cada {seconds} s; com uma visão só, não troca. O <c>tick</c> conta cada rotação (também com uma
 * visão só), para a TV pequena rolar as colunas do quadro (CA 11). Pausada na prévia pelo botão "Pausar rotação".
 */
export function useRotation(count: number, seconds: number, paused: boolean) {
  const [index, setIndex] = useState(0)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    if (paused) return
    const timer = setInterval(() => {
      setIndex((i) => nextIndex(i, count))
      setTick((t) => t + 1)
    }, Math.max(seconds, 1) * 1000)
    return () => clearInterval(timer)
  }, [count, seconds, paused])

  const go = useCallback((step: number) => {
    setIndex((i) => nextIndex(i, count, step))
    setTick((t) => t + 1)
  }, [count])

  return { index: count > 0 ? index % count : 0, tick, next: () => go(1), previous: () => go(-1) }
}
