import * as ToggleGroup from '@radix-ui/react-toggle-group'
import { cn } from '@/lib/utils'
import { isThemePreference, THEMES } from './theme'
import { useTheme } from './theme-context'

/** Seletor "Auto · Claro · Escuro" do protótipo (ToggleGroup do Radix, revisão §2.4). */
export function ThemeToggle({ className }: { className?: string }) {
  const { theme, setTheme } = useTheme()
  return (
    <ToggleGroup.Root
      type="single"
      value={theme}
      onValueChange={(value) => isThemePreference(value) && setTheme(value)}
      aria-label="Tema"
      className={cn('inline-flex rounded-md border border-border bg-card p-0.5', className)}
    >
      {THEMES.map((t) => (
        <ToggleGroup.Item
          key={t.value}
          value={t.value}
          className="rounded-sm px-2.5 py-1 text-xs font-medium text-muted-foreground data-[state=on]:bg-primary-soft data-[state=on]:text-foreground"
        >
          {t.label}
        </ToggleGroup.Item>
      ))}
    </ToggleGroup.Root>
  )
}
