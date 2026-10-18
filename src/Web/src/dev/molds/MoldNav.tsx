import { Link } from '@tanstack/react-router'

const ITEMS = [
  { id: 'cadastro', to: '/app/dev/moldes/cadastro', label: '1. Cadastro em seções' },
  { id: 'detalhe', to: '/app/dev/moldes/detalhe', label: '2. Detalhe com abas' },
  { id: 'painel', to: '/app/dev/moldes/painel', label: '3. Painel lateral' },
  { id: 'componentes', to: '/app/dev/componentes', label: 'Componentes' },
] as const

/** Navegação entre os moldes da FND-09d (galeria de aprovação do PO; só em desenvolvimento e homologação). */
export function MoldNav({ current }: { current: (typeof ITEMS)[number]['id'] }) {
  return (
    <nav aria-label="Moldes de tela" className="mb-4 flex flex-wrap items-center gap-2 rounded-md border border-dashed border-info bg-info-soft px-3 py-2 text-sm">
      <b className="mr-1 text-info-ink">Galeria de aprovação (FND-09d):</b>
      {ITEMS.map((i) => (
        <Link key={i.id} to={i.to} aria-current={i.id === current ? 'page' : undefined} className="rounded-md px-2 py-1 font-semibold text-primary aria-[current=page]:bg-card aria-[current=page]:text-foreground">
          {i.label}
        </Link>
      ))}
    </nav>
  )
}
