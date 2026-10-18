/** Texto para busca local: minúsculas e sem acento ("Ação" casa com "acao"). */
export function normalizeText(value: string): string {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}
