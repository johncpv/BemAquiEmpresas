import { fireEvent, render, screen } from '@testing-library/react'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import type { HelpDocument } from '@/api/generated/model'
import { router } from '@/router'
import { Dica } from './Dica'
import { Markdown } from './markdown'
import { normalizeRoute, resolveHelp, searchHelp } from './resolve'

const HELP_DIR = join(process.cwd(), '..', '..', 'Ajuda')
const header = (text: string, field: string) => new RegExp(`^${field}:(.*)$`, 'm').exec(text)?.[1].trim() ?? ''
const helpFiles = readdirSync(HELP_DIR).filter((f) => f.endsWith('.md')).map((f) => readFileSync(join(HELP_DIR, f), 'utf8'))

// Rotas que só redirecionam (não são telas) e as galerias de desenvolvimento (/dev, /app/dev: só em dev e homologação).
const NOT_SCREENS = new Set(['__root__', '/', '/app/acessos', '/app/configuracoes', '/app/configuracoes/integracoes', '/app/cadastros', '/app/financeiro', '/app/estoque', '/app/compras', '/app/producao', '/app/engenharia', '/app/rh', '/app/rh/ponto', '/app/operador', '/plataforma', '/dev/galeria'])

describe('cobertura da ajuda (FND-14 CA 8 e 10)', () => {
  it('toda rota do TanStack Router tem uma ajuda em Ajuda/', () => {
    const routes = Object.keys(router.routesById).map(normalizeRoute).filter((r) => !NOT_SCREENS.has(r) && !r.startsWith('/app/dev'))
    const covered = new Set(helpFiles.map((t) => header(t, 'rota')))
    const missing = [...new Set(routes)].filter((r) => !covered.has(r))
    expect(missing, 'rotas sem ajuda: crie Ajuda/<chave>.md com o cabeçalho rota').toEqual([])
  })
})

const doc = (key: string, route: string, tab: string | null, isDefault: boolean): HelpDocument => ({ key, title: key, route, tab, isDefault, module: null, sections: [{ title: 'Como usar', body: 'Clique em **Salvar grupo**.' }] })

describe('resolução e busca (CA 4 e 5)', () => {
  const docs = [doc('modulo', '/app/$module', null, true), doc('nao-liberado', '/app/$module', 'nao-liberado', false), doc('inicio', '/app', null, true)]

  it('rota + aba; sem aba, a padrão da rota', () => {
    expect(resolveHelp(docs, '/app/$module', 'nao-liberado')?.key).toBe('nao-liberado')
    expect(resolveHelp(docs, '/app/$module', null)?.key).toBe('modulo')
    expect(resolveHelp(docs, normalizeRoute('/app/'), null)?.key).toBe('inicio')
  })

  it('busca prioriza a tela atual', () => {
    expect(searchHelp(docs, 'salvar', 'inicio')[0].doc.key).toBe('inicio')
    expect(searchHelp(docs, 'xyz', 'inicio')).toEqual([])
  })
})

describe('Markdown seguro (CA 9)', () => {
  it('HTML e script viram texto, nunca elemento', () => {
    const { container } = render(<Markdown text={'<script>alert(1)</script> **forte**\n- <img src=x onerror=alert(1)> item'} />)
    expect(container.querySelector('script')).toBeNull()
    expect(container.querySelector('img')).toBeNull()
    expect(container.textContent).toContain('<script>alert(1)</script>')
    expect(container.querySelector('strong')?.textContent).toBe('forte')
    expect(container.querySelector('li')).not.toBeNull()
  })
})

describe('<Dica> (CA 7)', () => {
  it('texto ligado por aria-describedby e aberto pelo foco', async () => {
    render(<Dica texto="Quem ainda não tem será levado à configuração." />)
    const trigger = screen.getByRole('button', { name: 'Dica' })
    const described = document.getElementById(trigger.getAttribute('aria-describedby')!)
    expect(described?.textContent).toBe('Quem ainda não tem será levado à configuração.')
    fireEvent.focus(trigger)
    expect(await screen.findAllByText('Quem ainda não tem será levado à configuração.')).toHaveLength(2)
  })
})
