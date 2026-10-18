import * as Switch from '@radix-ui/react-switch'
import type { GroupModuleRow } from '@/api/generated/model'
import { StatusChip } from '@/components/ui/status-chip'
import { cn } from '@/lib/utils'
import { type MatrixRow, toggleAction, toggleEnabled } from './group-matrix'

const STANDARD = [
  ['view', 'Ver'],
  ['create', 'Criar'],
  ['edit', 'Editar'],
  ['approve', 'Aprovar'],
  ['delete', 'Excluir'],
] as const

interface Props {
  modules: readonly GroupModuleRow[]
  value: Record<string, MatrixRow>
  onChange: (code: string, row: MatrixRow) => void
  readOnly: boolean
}

/**
 * Matriz módulo × ação (FND-06b, protótipo): chave "Habilitado" (Switch do Radix), ações padrão em colunas e as
 * específicas na coluna "Outras" com a descrição; "Aprovar" explica o que aprovar significa no módulo (CA 5, 6).
 */
export function GroupMatrixTable({ modules, value, onChange, readOnly }: Props) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[720px] border-collapse text-sm" aria-label="Permissões do grupo">
        <thead className="border-b border-border text-xs text-muted-foreground">
          <tr>
            <th scope="col" className="px-3 py-2 text-left font-semibold">Funcionalidade</th>
            <th scope="col" className="px-2 py-2 text-center font-semibold">Habilitado</th>
            {STANDARD.map(([, label]) => <th key={label} scope="col" className="px-2 py-2 text-center font-semibold">{label}</th>)}
            <th scope="col" className="px-2 py-2 text-left font-semibold">Outras</th>
          </tr>
        </thead>
        <tbody>
          {modules.map((m) => {
            const row = value[m.code] ?? { enabled: false, actions: [] }
            const order = m.available.map((a) => a.code)
            const set = (action: string, checked: boolean) => onChange(m.code, toggleAction(row, action, checked, order))
            if (!m.released) {
              return (
                <tr key={m.code} className="border-b border-border text-muted-foreground last:border-0">
                  <td className="px-3 py-2"><b className="font-semibold">{m.name}</b><span className="block text-xs">Funcionalidade não liberada nessa versão do sistema.</span></td>
                  <td className="px-2 py-2 text-center"><StatusChip tone="info">Não liberado</StatusChip></td>
                  <td colSpan={STANDARD.length + 1} />
                </tr>
              )
            }

            return (
              <tr key={m.code} className={cn('border-b border-border last:border-0', !row.enabled && 'text-muted-foreground')}>
                <td className="px-3 py-2"><b className="font-semibold">{m.name}</b><span className="block text-xs text-muted-foreground">{m.area}</span></td>
                <td className="px-2 py-2 text-center">
                  <Switch.Root
                    checked={row.enabled}
                    disabled={readOnly}
                    onCheckedChange={(on) => onChange(m.code, toggleEnabled(on))}
                    aria-label={`Habilitar ${m.name}`}
                    className="relative inline-flex h-5 w-9 items-center rounded-full bg-input disabled:opacity-60 data-[state=checked]:bg-primary"
                  >
                    <Switch.Thumb className="block size-4 translate-x-0.5 rounded-full bg-card transition-transform data-[state=checked]:translate-x-[18px]" />
                  </Switch.Root>
                </td>
                {STANDARD.map(([code, label]) => {
                  const action = m.available.find((a) => a.code === code)
                  if (!action) return <td key={code} className="px-2 py-2 text-center"><span aria-hidden="true" className="text-muted-foreground">—</span><span className="sr-only">Não se aplica</span></td>
                  return (
                    <td key={code} className="px-2 py-2 text-center">
                      <input
                        type="checkbox"
                        checked={row.actions.includes(code)}
                        disabled={readOnly}
                        onChange={(e) => set(code, e.target.checked)}
                        aria-label={`${label} em ${m.name}`}
                        title={action.description || undefined}
                        className="size-4 accent-primary"
                      />
                    </td>
                  )
                })}
                <td className="px-2 py-2">
                  {m.available.filter((a) => !a.isStandard).map((a) => (
                    <label key={a.code} title={a.description} className="flex items-center gap-1.5 text-xs">
                      <input type="checkbox" checked={row.actions.includes(a.code)} disabled={readOnly} onChange={(e) => set(a.code, e.target.checked)} className="size-4 accent-primary" />
                      {a.label}
                    </label>
                  ))}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
