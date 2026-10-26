import { useEffect, useState } from 'react'

/** Relógio do tempo na etapa (PNL-01b CA 2): anima a cada minuto no navegador; o início vem do servidor. Com {fixed}, não anda (testes). */
export function useMinuteClock(fixed?: Date): Date {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    if (fixed) return
    const timer = setInterval(() => setNow(new Date()), 60_000)
    return () => clearInterval(timer)
  }, [fixed])
  return fixed ?? now
}
