import type { StatusTone } from '@/components/ui/status-chip'

/** Situação da nota de entrada (FIS-04a): rótulo e tom do chip. */
export const INCOMING_STATUS: Record<string, { label: string; tone: StatusTone }> = {
  Imported: { label: 'Importada', tone: 'info' },
  Checked: { label: 'Conferida', tone: 'ok' },
  Received: { label: 'Recebida', tone: 'neutral' },
  Rejected: { label: 'Recusada', tone: 'crit' },
}

export const STATUS_FILTERS = [
  { value: undefined, label: 'Todas' },
  { value: 'Imported', label: 'A conferir' },
  { value: 'Checked', label: 'Conferidas' },
  { value: 'Received', label: 'Recebidas' },
  { value: 'Rejected', label: 'Recusadas' },
] as const

/** Até 20 arquivos de 2 MB por vez (CA 1). */
export const MAX_FILES = 20
export const MAX_BYTES = 2 * 1024 * 1024

export const SEFAZ_TITLE = 'Situação na SEFAZ não conferida'

/** Textos das `<Dica>` da story (Telas e estados). */
export const DICAS = {
  dePara: 'O de-para liga o código que o fornecedor usa ao item do nosso estoque. Fica lembrado para a próxima nota do mesmo fornecedor.',
  lembrado: 'Este item foi ligado numa nota anterior do mesmo fornecedor. Confira e siga.',
  sugerido: 'Sugestão pelo NCM e pelo nome parecido. Aceite, escolha outro item ou marque Sem item de estoque.',
  fator: 'Quanto 1 unidade do fornecedor vale na nossa unidade de estoque. Ex.: 1 CX = 100 UN, fator 100.',
  assinatura: 'O sistema conferiu a assinatura digital da nota: é do certificado ICP-Brasil do próprio emitente e o arquivo não foi alterado.',
  sefaz: 'O sistema confere a assinatura e o protocolo, mas não consulta se a nota foi cancelada depois. Em caso de dúvida, consulte a chave no portal da SEFAZ.',
  semItem: 'Serviço ou despesa na mesma nota: não entra no estoque e vai para a conta do plano escolhida.',
} as const

/** Textos das `<Dica>` do "Receber pela nota" (FIS-04b). */
export const RECEIVE_DICAS = {
  duplicatas: 'Duplicatas são as parcelas de cobrança que o fornecedor pôs na nota, cada uma com número, vencimento e valor. Cada duplicata vira um título a pagar.',
  semDuplicatas: 'A nota não trouxe duplicatas: os vencimentos vêm da condição do pedido.',
  credito: 'Crédito de impostos é o tributo da nota que a empresa recupera na apuração. O padrão é nenhum: ICMS, IPI, PIS e COFINS ficam no custo, até a contadora confirmar em Configurações → Fiscal, seção Entrada.',
} as const

/** Chave de acesso em blocos de 4, para ler e conferir no portal. */
export function formatAccessKey(key: string) {
  return key.replace(/(\d{4})(?=\d)/g, '$1 ')
}

/** CNPJ com a máscara (14 dígitos). */
export function formatCnpj(value: string) {
  return value.length === 14 ? `${value.slice(0, 2)}.${value.slice(2, 5)}.${value.slice(5, 8)}/${value.slice(8, 12)}-${value.slice(12)}` : value
}

const qty = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 4 })
const price = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: 6 })
const pct = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2, signDisplay: 'exceptZero' })

export const formatQty = (value: number) => qty.format(value)
export const formatPrice = (value: number) => price.format(value)

/** Tom da divergência (CA 10): acima é crítico, abaixo é alerta, igual é ok. */
export function divergenceTone(level: string | undefined | null): StatusTone {
  return level === 'Critical' ? 'crit' : level === 'Warning' ? 'warn' : 'ok'
}

/** Texto da diferença ao lado da cor (nunca só a cor). */
export function quantityText(difference: number, unit: string | null | undefined) {
  if (difference === 0) return 'Quantidade igual ao saldo do pedido'
  const sign = difference > 0 ? 'acima' : 'abaixo'
  return `${formatQty(Math.abs(difference))} ${unit ?? ''} ${sign} do saldo do pedido`.replace(/\s+/g, ' ')
}

export function priceText(difference: number, percent: number | null | undefined) {
  if (difference === 0) return 'Preço igual ao do pedido'
  const sign = difference > 0 ? 'acima' : 'abaixo'
  return `${formatPrice(Math.abs(difference))} ${sign} do pedido${percent != null ? ` (${pct.format(percent)}%)` : ''}`
}

export function totalText(name: string, difference: number, percent: number | null | undefined) {
  if (difference === 0) return `${name} igual ao do pedido`
  const sign = difference > 0 ? 'acima' : 'abaixo'
  return `${name} ${formatPrice(Math.abs(difference))} ${sign} do pedido${percent != null ? ` (${pct.format(percent)}%)` : ''}`
}

/** Resultado de um arquivo na importação (CA 1): cada arquivo com o seu motivo, sem bloquear os outros. */
export type FileResult =
  | { name: string; state: 'pending' | 'sending' }
  | { name: string; state: 'ok'; id: string; label: string; remembered: number }
  | { name: string; state: 'error'; message: string; link?: string }

/** Confere o arquivo antes de enviar (extensão e tamanho); o servidor confere o resto. */
export function precheck(file: { name: string; size: number }): string | null {
  if (!file.name.toLowerCase().endsWith('.xml')) return 'Envie o arquivo .xml da nota.'
  if (file.size === 0) return 'O arquivo está vazio.'
  if (file.size > MAX_BYTES) return 'O arquivo precisa ter até 2 MB.'
  return null
}

/** "{n} notas importadas" (verbo da story). */
export function importedLabel(count: number) {
  return count === 1 ? '1 nota importada' : `${count} notas importadas`
}
