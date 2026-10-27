import { Link } from '@tanstack/react-router'
import { useNavigation } from '@/navigation/navigation-context'

const linkClass = 'rounded-md border border-border px-3 py-1.5 font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground'

/**
 * Subnavegação de Integrações (FND-16, BNK-01a): "Eventos recebidos" e "Banco Inter" (com o Financeiro liberado e visível); o
 * WhatsApp entra na WPP-01.
 */
export function IntegrationsNav() {
  const { all } = useNavigation()
  const finance = all.find((m) => m.code === 'finance')
  return (
    <nav aria-label="Integrações" className="mb-4 flex flex-wrap gap-2 text-sm">
      <Link to="/app/configuracoes/integracoes/eventos" activeProps={{ 'aria-current': 'page' }} className={linkClass}>
        Eventos recebidos
      </Link>
      {finance ? (
        <Link to="/app/configuracoes/integracoes/inter" activeProps={{ 'aria-current': 'page' }} activeOptions={{ exact: false }} className={linkClass}>
          Banco Inter
        </Link>
      ) : null}
    </nav>
  )
}
