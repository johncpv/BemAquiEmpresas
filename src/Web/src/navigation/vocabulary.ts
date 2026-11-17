import { useContext } from 'react'
import { NavigationContext } from './navigation-context'

/** Termos do tenant por chave (PERF-03, ADR 0046 §6): só o que difere do padrão; a Konebras (fábrica) recebe vazio. */
export type Vocabulary = Readonly<Record<string, string>>

const EMPTY: Vocabulary = {}

/** Termo do tenant para a chave, ou o padrão. Padrão em minúscula deixa o termo em minúscula (mesma regra do backend). */
export function termOf(vocabulary: Vocabulary, key: string, standard: string): string {
  const term = vocabulary[key]
  if (!term) return standard
  return standard && standard[0] === standard[0].toLowerCase() && standard[0] !== standard[0].toUpperCase() ? term[0].toLowerCase() + term.slice(1) : term
}

/** Troca as marcas {{chave|Termo padrão}} (texto da ajuda) pelo termo do tenant, ou pelo padrão escrito na marca. */
export function applyTerms(text: string, vocabulary: Vocabulary): string {
  return text.includes('{{') ? text.replace(/\{\{([a-z0-9_.]+)\|([^{}|]+)\}\}/g, (_, key: string, standard: string) => termOf(vocabulary, key, standard)) : text
}

/** Vocabulário do tenant (de /me/permissions). Fora do NavigationProvider (testes, telas sem login) devolve o padrão. */
export function useVocabulary(): Vocabulary {
  return useContext(NavigationContext)?.vocabulary ?? EMPTY
}

/** Tradutor de termos: `term('area.factory', 'Fábrica')`. */
export function useTerm(): (key: string, standard: string) => string {
  const vocabulary = useVocabulary()
  return (key, standard) => termOf(vocabulary, key, standard)
}

/** Rótulo de uma área do dock (texto de ModuleAreas no backend) como o tenant a vê. */
export function useAreaLabel(): (area: string) => string {
  const labels = useContext(NavigationContext)?.areaLabels ?? EMPTY
  return (area) => labels[area] ?? area
}
