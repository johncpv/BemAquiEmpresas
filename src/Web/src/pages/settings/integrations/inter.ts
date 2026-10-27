import type { StatusTone } from '@/components/ui/status-chip'

/** Situação da conta na lista e no detalhe (BNK-01a, Telas e estados). */
export const SITUATIONS: Record<string, { label: string; tone: StatusTone }> = {
  NotConnected: { label: 'Não conectada', tone: 'neutral' },
  Sandbox: { label: 'Sandbox', tone: 'info' },
  Production: { label: 'Produção', tone: 'ok' },
  ExpiringSoon: { label: 'Certificado vence em breve', tone: 'warn' },
  Expired: { label: 'Certificado vencido', tone: 'crit' },
}

export const situationOf = (code: string) => SITUATIONS[code] ?? { label: code, tone: 'neutral' as const }

export const ENVIRONMENTS = [
  { value: 'Sandbox', label: 'Sandbox (teste)' },
  { value: 'Production', label: 'Produção' },
] as const

export const environmentLabel = (value: string | null | undefined) => (value === 'Production' ? 'Produção' : 'Sandbox (teste)')

const SCOPE_LABELS: Record<string, string> = {
  'boleto-cobranca.read': 'Cobrança (consultar boletos)',
  'boleto-cobranca.write': 'Cobrança (emitir e cancelar boletos)',
  'extrato.read': 'Extrato (consultar saldo e extrato)',
  'webhook.read': 'Webhook (consultar)',
  'webhook.write': 'Webhook (cadastrar)',
}

export const scopeLabel = (scope: string) => SCOPE_LABELS[scope] ?? scope

/** Textos das dicas (story, "Ajuda"). */
export const HINTS = {
  environment: 'Sandbox é o ambiente de teste do Inter: nenhum boleto é cobrança real. Produção só emite depois da virada do sistema.',
  certificate: 'O Inter gera o certificado junto com a integração: os arquivos .crt e .key, ou um .pfx com senha. Ele vale por um ano e fica guardado cifrado.',
  scopes: 'Só cobrança, extrato e webhook. Credencial que paga é recusada.',
  clientSecret: 'Gerado no Internet Banking do Inter, em Soluções para sua empresa → Nova integração.',
} as const

/** Padrão 6 da Fase 4 (integração assíncrona): Enviado, Aguardando, Demorando depois de 30 s, Confirmado ou Recusado. */
export type TestPhase = 'idle' | 'sent' | 'waiting' | 'slow' | 'confirmed' | 'refused'

export const SLOW_AFTER_MS = 30_000
export const WAITING_AFTER_MS = 1_500

export const TEST_PHASE_TEXT: Record<Exclude<TestPhase, 'idle' | 'confirmed' | 'refused'>, string> = {
  sent: 'Enviado ao Inter…',
  waiting: 'Aguardando a resposta do Inter…',
  slow: 'O Inter está demorando a responder. Aguarde mais um pouco; não precisa testar de novo.',
}

/** Dias até vencer, para o texto do certificado. */
export function expiryText(days: number): string {
  if (days < 0) return 'vencido'
  if (days === 0) return 'vence hoje'
  if (days === 1) return 'vence amanhã'
  return `vence em ${days} dias`
}
