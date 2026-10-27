import { Link } from '@tanstack/react-router'

const linkClass = 'rounded-md border border-border px-3 py-1.5 font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground'

/** Subnavegação de Configurações → Fiscal (FIS-02a): a configuração e o certificado digital. */
export function FiscalNav() {
  return (
    <nav aria-label="Fiscal" className="mb-4 flex flex-wrap gap-2 text-sm">
      <Link to="/app/configuracoes/fiscal" activeProps={{ 'aria-current': 'page' }} activeOptions={{ exact: true }} className={linkClass}>Configuração fiscal</Link>
      <Link to="/app/configuracoes/fiscal/certificado" activeProps={{ 'aria-current': 'page' }} className={linkClass}>Certificado digital</Link>
    </nav>
  )
}
