import type { StatusTone } from '@/components/ui/status-chip'

/** O que a tela usa da integração (o mesmo formato do FiscalIntegrationView da API). */
export interface FiscalIntegrationLike {
  configured: boolean
  active: boolean
  environment?: string | null
  allowedEnvironment: string
  lastTestResult?: string | null
}

export const NOT_CONFIGURED = 'Conecte o Emissor Fiscal para emitir notas. Peça ao administrador do Emissor Fiscal uma chave sem o escopo admin.'
export const NOT_ALLOWED = 'A emissão desta empresa só liga na virada para a produção.'

/** Dicas dos termos que confundem (FIS-01, Telas e estados). */
export const HINTS = {
  webhookSecret:
    'É a senha que o Emissor Fiscal usa para assinar os avisos de nota autorizada, rejeitada ou cancelada. Ela aparece uma única vez, quando o administrador cadastra o aviso no painel do Emissor. Sem ela o BemAqui recusa o aviso.',
  admin:
    'O escopo admin deixa a chave mexer em certificados, emitentes e chaves de qualquer empresa no Emissor Fiscal. O BemAqui não precisa disso: use uma chave só com emitir, eventos e consultar.',
  environment:
    'Homologação é o ambiente de teste da SEFAZ: a nota sai, mas não tem valor fiscal. Produção só liga na virada do sistema, depois da conferência com a contadora.',
  baseUrl:
    'O Emissor Fiscal é um serviço na internet, usado por várias empresas. O endereço é o mesmo para homologação e produção: quem decide o ambiente é o cadastro do emitente no Emissor. Por segurança, o endereço é definido pelo sistema e não pode ser trocado aqui.',
  webhookUrl:
    'Cole este endereço no painel do Emissor Fiscal, em Webhooks, Novo webhook. É por ele que o Emissor avisa quando a SEFAZ responde. O endereço é único desta empresa.',
  apiKey: 'A chave é guardada cifrada e nunca é mostrada de novo. Para trocar, cole a chave nova: ela é testada antes de substituir a atual.',
} as const

export const ENVIRONMENT_LABEL: Record<string, string> = {
  Homologation: 'Homologação (sem valor fiscal)',
  Production: 'Produção',
}

const SCOPE_LABEL: Record<string, string> = {
  'nfe:emitir': 'Emitir NF-e',
  'nfe:eventos': 'Cancelar e corrigir NF-e',
  consultar: 'Consultar',
  'nfse:emitir': 'Emitir NFS-e',
}

export const scopeLabel = (scope: string) => SCOPE_LABEL[scope] ?? scope

/** Situação da integração (padrão 6 das Fases 4 a 8): cor semântica com texto. */
export function integrationStatus(view: FiscalIntegrationLike): { label: string; tone: StatusTone } {
  if (!view.configured) return { label: 'Não configurada', tone: 'neutral' }
  if (view.active) return { label: 'Ativa', tone: 'ok' }
  if (view.lastTestResult && view.lastTestResult !== 'ok') return { label: 'Recusada no teste', tone: 'crit' }
  return { label: 'Aguardando o teste', tone: 'warn' }
}

/** Faixa de ambiente de teste: emitente em homologação, ou ainda sem teste num tenant que só pode homologar. */
export function isTestEnvironment(view: FiscalIntegrationLike) {
  if (view.environment) return view.environment === 'Homologation'
  return view.allowedEnvironment === 'Homologation'
}

/** 47225005000186 → 47.225.005/0001-86. */
export function formatCnpj(value?: string | null) {
  const d = (value ?? '').replace(/\D/g, '')
  return d.length === 14 ? `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}` : value ?? ''
}

export function pendingText(count: number) {
  if (count === 0) return 'Nenhuma nota esperando a SEFAZ.'
  return count === 1 ? '1 nota esperando a SEFAZ.' : `${count} notas esperando a SEFAZ.`
}

/** Código do autenticador: 6 dígitos. */
export const isTotpCode = (value: string) => /^\d{6}$/.test(value.trim())
