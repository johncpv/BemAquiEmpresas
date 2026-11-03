/** Data de hoje em São Paulo (AAAA-MM-DD), a mesma do nome que o servidor dá ao arquivo exportado (ADR 0045 §3). */
export const exportDay = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })
