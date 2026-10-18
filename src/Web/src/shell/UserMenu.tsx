import * as Menu from '@radix-ui/react-dropdown-menu'
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { logout, useGetCurrentSession } from '@/api/generated/bemaqui'
import { setAccessToken } from '@/api/session'
import { isThemePreference, THEMES } from '@/theme/theme'
import { useTheme } from '@/theme/theme-context'

/** Menu do usuário (FND-09b): perfil, 2FA, tema a 400px (revisão §2.4) e Sair (FND-04 CA 8). */
export function UserMenu({ showTheme }: { showTheme: boolean }) {
  const { theme, setTheme } = useTheme()
  const me = useGetCurrentSession()
  const navigate = useNavigate()
  const client = useQueryClient()
  const name = me.data?.name || me.data?.email || 'Usuário'
  const initials = name.split(/[\s@.]+/).filter(Boolean).map((p) => p[0]).slice(0, 2).join('').toUpperCase()
  const itemClass = 'cursor-pointer rounded-sm px-2 py-1.5 text-sm outline-none data-[highlighted]:bg-muted'

  const signOut = async () => {
    await logout().catch(() => {})
    setAccessToken(null)
    client.clear()
    await navigate({ to: '/entrar' })
  }

  return (
    <Menu.Root>
      <Menu.Trigger className="grid size-8 place-items-center rounded-full bg-primary-soft text-xs font-bold text-primary">
        <span aria-hidden="true">{initials}</span>
        <span className="sr-only">Menu de {name}</span>
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content align="end" sideOffset={8} className="z-40 min-w-56 rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-lg">
          <Menu.Label className="px-2 py-1.5 text-sm font-semibold">{name}</Menu.Label>
          <Menu.Separator className="my-1 h-px bg-border" />
          <Menu.Item className={itemClass} onSelect={() => void navigate({ to: '/app/conta' })}>Meu perfil</Menu.Item>
          <Menu.Item className={itemClass} onSelect={() => void navigate({ to: '/app/conta/seguranca' })}>Verificação em duas etapas</Menu.Item>
          {showTheme ? (
            <>
              <Menu.Separator className="my-1 h-px bg-border" />
              <Menu.Label className="px-2 py-1 text-xs text-muted-foreground">Tema</Menu.Label>
              <Menu.RadioGroup value={theme} onValueChange={(v) => isThemePreference(v) && setTheme(v)}>
                {THEMES.map((t) => (
                  <Menu.RadioItem key={t.value} value={t.value} className={`${itemClass} data-[state=checked]:font-semibold`}>
                    {t.label}
                  </Menu.RadioItem>
                ))}
              </Menu.RadioGroup>
            </>
          ) : null}
          <Menu.Separator className="my-1 h-px bg-border" />
          <Menu.Item className={itemClass} onSelect={() => void signOut()}>Sair</Menu.Item>
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  )
}
