/** Exportação para a contadora (RH-03): competência, texto do e-mail e as dicas da tela. */
import { periodLabel } from '../payroll/payroll'

/** "2026-10-01" → "2026-10" (o valor do campo mês). */
export const monthOf = (period: string) => period.slice(0, 7)

/** O mês atual em "aaaa-mm": o limite do campo (só o mês atual ou um anterior, CA 1). */
export function currentMonth(today = new Date()) {
  return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`
}

/** Mês válido para exportar: "aaaa-mm" até o mês atual. */
export const validMonth = (month: string, today = new Date()) => /^\d{4}-(0[1-9]|1[0-2])$/.test(month) && month <= currentMonth(today)

/** Nome do arquivo baixado, igual ao do servidor. */
export const packageFileName = (month: string, layout: string) => `contadora-${month}-${layout}.zip`

/**
 * "Avisar a contadora" (CA 6): abre o e-mail do usuário com o texto pronto, sem anexo e sem destinatário (o RH escolhe). Nenhum dado
 * pessoal no texto: só a competência e o que substitui.
 */
export function mailtoContadora(month: string, replaces: string | null) {
  const label = periodLabel(month)
  const subject = `Pacote do RH de ${label}`
  const body = [
    'Olá,',
    '',
    `Segue o pacote do RH de ${label} (movimentação do mês e, quando houver, a folha fechada), no layout padrão do sistema.`,
    replaces ? `Ele substitui o pacote que enviei antes (gerado em ${replaces}).` : null,
    'O arquivo ZIP vai anexado a este e-mail; o leia-me.txt dentro dele explica o que entrou.',
    '',
    'Obrigado.',
  ].filter((l) => l !== null).join('\n')
  return `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`
}

export const ACCOUNTANT_DICAS = {
  layout: 'O formato dos arquivos que o sistema gera (colunas, datas, decimal com vírgula). O padrão é o v1, descrito na ajuda desta tela. Ele está "A confirmar com a contadora" até alguém conferir as colunas com ela em Configurações → Parâmetros → RH.',
  protegidos: 'O pacote leva dados pessoais da folha (CPF, salário, afastamentos), protegidos pela LGPD. Só quem tem a permissão "Ver dados sensíveis" do RH gera o pacote.',
}
