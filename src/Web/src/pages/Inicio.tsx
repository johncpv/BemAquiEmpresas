import { Link } from '@tanstack/react-router'
import { KeyRound, ShieldCheck, UserPlus } from 'lucide-react'
import { useCallback, useState } from 'react'
import { useNavigation } from '@/navigation/navigation-context'
import { PageHeading } from '@/shell/PageHeading'
import { MinhaPauta } from './inicio/MinhaPauta'

const NEXT_STEPS = [
  { module: 'acessos', tab: 'usuarios', icon: UserPlus, title: 'Convidar a equipe', text: 'Cada pessoa entra com o próprio acesso e só vê o que o grupo dela permite.' },
  { module: 'acessos', tab: 'grupos', icon: KeyRound, title: 'Revisar os grupos', text: 'Os 8 grupos iniciais já vêm com permissões padrão. Ajuste ao jeito da empresa.' },
  { module: 'configuracoes', tab: 'seguranca', icon: ShieldCheck, title: 'Proteger as contas', text: 'Ative a verificação em duas etapas e, se quiser, exija para todos.' },
] as const

/**
 * Início, a página inicial de todos (ADR 0028 §4, DSH-04 CA 7): primeiro a Minha pauta, depois os próximos passos e os atalhos para os
 * módulos com acesso (ADR 0010 item 9). A contagem da pauta vai para o título da aba ("(4) Início").
 */
export function Inicio() {
  const { areas, tenantName } = useNavigation()
  const modules = areas.flatMap((a) => a.modules.filter((m) => !m.locked && m.enabled))
  const [count, setCount] = useState(0)
  const onCount = useCallback((total: number) => setCount(total), [])

  return (
    <>
      <PageHeading title="Início" count={count}>
        <p className="mt-1 text-muted-foreground">Boas-vindas ao BemAqui Empresas da {tenantName}. Veja o que depende de você hoje:</p>
      </PageHeading>

      <MinhaPauta onCount={onCount} />

      <section aria-labelledby="proximos" className="mb-8">
        <h2 id="proximos" className="mb-3 text-[15px] font-semibold">Próximos passos</h2>
        <ul className="grid gap-3 sm:grid-cols-3">
          {NEXT_STEPS.map((s) => (
            <li key={s.tab}>
              <Link to="/app/$module/$tab" params={{ module: s.module, tab: s.tab }} className="flex h-full gap-3 rounded-md border border-border bg-card p-4 hover:border-primary">
                <s.icon aria-hidden="true" className="mt-0.5 size-5 flex-none text-primary" />
                <span>
                  <b className="block font-semibold">{s.title}</b>
                  <span className="text-sm text-muted-foreground">{s.text}</span>
                </span>
              </Link>
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="atalhos">
        <h2 id="atalhos" className="mb-3 text-[15px] font-semibold">Seus módulos</h2>
        <ul className="flex flex-wrap gap-2">
          {modules.map((m) => (
            <li key={m.code}>
              <Link to="/app/$module" params={{ module: m.slug }} className="inline-flex items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-sm font-medium hover:border-primary">
                <m.icon aria-hidden="true" className="size-4 text-muted-foreground" />
                {m.name}
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}
