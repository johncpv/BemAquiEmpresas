import { lazy, Suspense } from 'react'
import { ThemeToggle } from '@/theme/ThemeToggle'

// Só em desenvolvimento: no build de produção o ramo some e a galeria não é empacotada (FND-09a).
const Gallery = import.meta.env.DEV ? lazy(() => import('./dev/Gallery')) : null

/** Página provisória: o shell real (dock, topbar e rotas) chega na FND-09b. */
export function App() {
  if (Gallery && window.location.pathname === '/dev/galeria') {
    return (
      <Suspense fallback={null}>
        <Gallery />
      </Suspense>
    )
  }

  return (
    <main className="px-4 py-6">
      <h1 className="text-[28px] font-bold">BemAqui Empresas</h1>
      <p className="text-muted-foreground">Gestão empresarial e fabricação por processos.</p>
      <ThemeToggle className="mt-4" />
    </main>
  )
}
