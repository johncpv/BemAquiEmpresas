import * as Menu from '@radix-ui/react-dropdown-menu'
import { isThemePreference, THEMES } from '@/theme/theme'
import { useTheme } from '@/theme/theme-context'

/** Menu do usuário (FND-09b): a 400px também leva o seletor de tema (revisão §2.4). */
export function UserMenu({ name, showTheme }: { name: string; showTheme: boolean }) {
  const { theme, setTheme } = useTheme()
  const initials = name.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase()
  const itemClass = 'cursor-pointer rounded-sm px-2 py-1.5 text-sm outline-none data-[highlighted]:bg-muted'
  return (
    <Menu.Root>
      <Menu.Trigger aria-label={`Menu de ${name}`} className="grid size-8 place-items-center rounded-full bg-primary-soft text-xs font-bold text-primary">
        {initials}
      </Menu.Trigger>
      <Menu.Portal>
        <Menu.Content align="end" sideOffset={8} className="z-40 min-w-56 rounded-md border border-border bg-popover p-1 text-popover-foreground shadow-lg">
          <Menu.Label className="px-2 py-1.5 text-sm font-semibold">{name}</Menu.Label>
          <Menu.Separator className="my-1 h-px bg-border" />
          <Menu.Item className={itemClass}>Meu perfil</Menu.Item>
          <Menu.Item className={itemClass}>Verificação em duas etapas</Menu.Item>
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
          <Menu.Item className={itemClass}>Sair</Menu.Item>
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  )
}
