import { Check } from 'lucide-react'
import { InlineError, Skeleton, ErrorState } from '@/components/ui/states'
import { cn } from '@/lib/utils'
import { DEFAULT_SEGMENT, SEGMENT_REQUIRED, type ProfileOption, type SegmentChoice } from './profile-rules'

/** Passo "Perfil de negócio" (PERF-02 CA 1): um card por perfil, com a linha do que ele libera; marca um ou mais. */
export function ProfileCards({ profiles, loading, failed, onRetry, selected, error, onToggle }: {
  profiles: ProfileOption[]
  loading: boolean
  failed: boolean
  onRetry: () => void
  selected: string[]
  error?: string
  onToggle: (code: string) => void
}) {
  if (loading) return <Skeleton className="h-48" />
  if (failed) return <ErrorState title="Não foi possível carregar os perfis de negócio." onRetry={onRetry} />
  return (
    <fieldset aria-describedby="perfil-erro">
      <legend className="text-sm font-semibold">Perfil de negócio</legend>
      <p className="mb-2 text-xs text-muted-foreground">Define os módulos que a empresa vê. Marque mais de um se ela tiver mais de um negócio; dá para mudar depois na aba Perfis.</p>
      <div className="grid gap-2 sm:grid-cols-2">
        {profiles.map((p) => {
          const on = selected.includes(p.code)
          return (
            <label key={p.code} className={cn('flex cursor-pointer gap-2 rounded-md border p-3 text-sm', on ? 'border-primary bg-card' : 'border-border')}>
              <input type="checkbox" checked={on} onChange={() => onToggle(p.code)} className="mt-0.5 size-4 flex-none accent-primary" />
              <span className="min-w-0">
                <b className="flex items-center gap-1 font-semibold">{p.name}{on ? <Check aria-hidden="true" className="size-3.5" /> : null}</b>
                <span className="block text-xs text-muted-foreground">Libera: {p.modules.map((m) => m.name).join(', ')}.</span>
              </span>
            </label>
          )
        })}
      </div>
      <InlineError id="perfil-erro" message={error} />
    </fieldset>
  )
}

/** Passo "Segmento" (PERF-02 CA 2): só os segmentos dos perfis marcados; "Balcão" vem escolhido no Comércio. */
export function SegmentPicker({ profiles, segments, onChange }: {
  profiles: ProfileOption[]
  segments: SegmentChoice
  onChange: (profile: string, segment: string | null) => void
}) {
  return (
    <div className="space-y-3">
      {profiles.map((p) => {
        const options = [...(SEGMENT_REQUIRED.has(p.code) ? [] : [{ code: null as string | null, name: 'Em geral' }]), ...p.segments]
        const value = segments[p.code] ?? (SEGMENT_REQUIRED.has(p.code) ? DEFAULT_SEGMENT[p.code] : null)
        return (
          <fieldset key={p.code}>
            <legend className="text-sm font-semibold">Segmento de {p.name}</legend>
            <p className="mb-2 text-xs text-muted-foreground">Escolhe os dados iniciais e os padrões das telas; não muda os módulos liberados.</p>
            <div className="flex flex-wrap gap-2">
              {options.map((s) => (
                <label key={s.code ?? 'geral'} className="flex cursor-pointer items-center gap-2 rounded-md border border-border px-3 py-2 text-sm has-[:checked]:border-primary">
                  <input type="radio" name={'segmento-' + p.code} checked={value === s.code} onChange={() => onChange(p.code, s.code)} className="accent-primary" />
                  {s.name}
                </label>
              ))}
            </div>
          </fieldset>
        )
      })}
    </div>
  )
}
