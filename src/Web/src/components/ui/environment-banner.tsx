import { FlaskConical } from 'lucide-react'
import { cn } from '@/lib/utils'

/** Serviço externo cujo ambiente de teste a faixa avisa (D18): banco, SEFAZ (FIS-02a) ou WhatsApp (WPP-01). */
export type EnvironmentService = 'bank' | 'sefaz' | 'whatsapp' | 'tenant'

const MESSAGES: Record<EnvironmentService, string> = {
  bank: 'Ambiente de teste do banco: os boletos não são cobranças reais.',
  sefaz: 'Ambiente de homologação da SEFAZ: as notas não têm valor fiscal.',
  whatsapp: 'Ambiente de teste do WhatsApp: as mensagens não chegam aos clientes.',
  tenant: 'Ambiente de homologação: os dados e os documentos são de teste.',
}

/**
 * Faixa única de ambiente de teste (BNK-01a CA 13, D18): no topo da tela de integração, do boleto e do extrato quando a conta do
 * Inter está em Sandbox ou o tenant está em homologação; a FIS-02a e a WPP-01 usam a mesma faixa para a SEFAZ e o WhatsApp. Cor
 * semântica informativa (nunca o acento) e <c>role="status"</c>. Fora do teste, não aparece.
 */
export function EnvironmentBanner({ service, test, message, className }: { service: EnvironmentService; test: boolean; message?: string; className?: string }) {
  if (!test) return null
  return (
    <p role="status" data-environment={service} className={cn('mb-4 flex items-start gap-2 rounded-md border border-info-soft bg-info-soft px-3 py-2 text-sm font-semibold text-info-ink', className)}>
      <FlaskConical aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
      <span>{message ?? MESSAGES[service]}</span>
    </p>
  )
}
