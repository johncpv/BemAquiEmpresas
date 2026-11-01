import { Link } from '@tanstack/react-router'
import { useNavigation } from '@/navigation/navigation-context'

const TABS = [
  { to: '/app/configuracoes/empresa', label: 'Empresa' },
  { to: '/app/configuracoes/numeracao', label: 'Numeração' },
  { to: '/app/configuracoes/parametros', label: 'Parâmetros' },
  { to: '/app/configuracoes/seguranca', label: 'Segurança' },
  { to: '/app/configuracoes/ia', label: 'Inteligência artificial' },
  { to: '/app/configuracoes/importacao', label: 'Importação do Dolibarr' },
  // FND-16: só com a visualização do módulo dono de algum provedor (finance.view ou fiscal.view); sem ela, a aba some (CA 16).
  { to: '/app/configuracoes/integracoes', label: 'Integrações', slug: 'integracoes' },
  // BNK-01b: com finance.view ou settings.view; módulo Financeiro não liberado mostra a tela padrão.
  { to: '/app/configuracoes/cobranca', label: 'Cobrança', slug: 'cobranca' },
  // FIS-02a: com fiscal.view ou settings.view; módulo Fiscal não liberado mostra a tela padrão.
  { to: '/app/configuracoes/fiscal', label: 'Fiscal', slug: 'fiscal' },
  // LGPD-01a: só o Proprietário (o controlador) vê a aba; a API recusa os demais com 403.
  { to: '/app/configuracoes/privacidade', label: 'Privacidade (LGPD)', slug: 'privacidade' },
] as const

/** Abas de Configurações: cada aba é uma rota própria (FND-09b CA 8). */
export function SettingsTabs() {
  const { all } = useNavigation()
  const allowed = new Set(all.find((m) => m.code === 'settings')?.tabs.map((t) => t.slug) ?? [])
  return (
    <nav aria-label="Configurações" className="mb-5 flex gap-1 overflow-x-auto border-b border-border">
      {TABS.filter((t) => !('slug' in t) || allowed.has(t.slug)).map((t) => (
        <Link key={t.to} to={t.to} activeProps={{ 'aria-current': 'page' }} className="-mb-px whitespace-nowrap border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-muted-foreground aria-[current=page]:border-primary aria-[current=page]:text-foreground">
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
