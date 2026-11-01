/** Tipos de titular da tabela de retenção (LGPD-01b CA 1), na ordem da tela. */
export const RETENTION_SUBJECTS = [
  { value: 'Party', label: 'Terceiro (pessoa física)' },
  { value: 'Contact', label: 'Contato de terceiro' },
  { value: 'Employee', label: 'Funcionário' },
  { value: 'User', label: 'Usuário do sistema' },
] as const

const BASES: Record<string, string> = {
  LastPayment: 'do último pagamento',
  Termination: 'do desligamento',
  FiscalYearEnd: 'do fim do exercício',
}

/** A regra em texto: "A qualquer momento", "Depois do último pagamento" ou "5 anos depois do desligamento". */
export function ruleText(r: { rule: string; years: number; baseDate: string }): string {
  if (r.rule === 'Anytime') return 'A qualquer momento'
  const base = BASES[r.baseDate] ?? 'da data base'
  if (r.years === 0) return `Depois ${base}`
  return `${r.years} ${r.years === 1 ? 'ano' : 'anos'} depois ${base}`
}
