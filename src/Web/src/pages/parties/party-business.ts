import { useCan } from '@/pages/use-can'

/** A aba aparece se ao menos um bloco for visível (FIN-06b CA 5). */
export function usePartyBusinessVisible() {
  const quotes = useCan('quotes.view')
  const projects = useCan('projects.view')
  const finance = useCan('finance.view')
  return quotes || projects || finance
}
