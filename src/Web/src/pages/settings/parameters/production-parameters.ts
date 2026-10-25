/** Textos e regras da seção Produção de Configurações → Parâmetros (PRD-04; PLN-01b CA 12, ADR 0042). */
export const INSTALLATION_DICA =
  'Dias úteis do calendário padrão somados ao fim da fabricação para chegar à entrega prevista do projeto, quando ele tem instalação (OS de instalação ou custo de instalação no orçamento). Com a OS de instalação, vale o maior entre este prazo e o fim digitado nela.'

/** Selo do valor sugerido que ninguém conferiu (ADR 0042 §1). */
export const INSTALLATION_PENDING_LABEL = 'A confirmar'

export const MIN_INSTALLATION_DAYS = 0
export const MAX_INSTALLATION_DAYS = 120

/** Prazo de instalação de 0 a 120 dias úteis; mensagem de erro ou undefined. */
export function installationDaysError(text: string): string | undefined {
  if (text === '') return `Informe de ${MIN_INSTALLATION_DAYS} a ${MAX_INSTALLATION_DAYS} dias úteis.`
  const value = Number(text)
  return Number.isInteger(value) && value >= MIN_INSTALLATION_DAYS && value <= MAX_INSTALLATION_DAYS ? undefined : `Informe de ${MIN_INSTALLATION_DAYS} a ${MAX_INSTALLATION_DAYS} dias úteis.`
}
