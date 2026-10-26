import { useState } from 'react'
import { createKioskScreen, updateKioskScreen } from '@/api/generated/bemaqui'
import type { KioskScreenLink, KioskScreenRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { Dica } from '@/help/Dica'
import { DEFAULT_ROTATION, MAX_NETWORKS, parseNetworks, VIEW_LABELS } from './kiosk-screens'

type Draft = { name: string; line: string; views: string[]; rotation: string; networks: string }

/**
 * "Nova tela" e "Editar tela" (PNL-01a CA 3 e 4), no painel lateral: nome, linha (Todas ou uma das linhas dos centros de trabalho),
 * visões e rotação (padrão: todas, 20 s) e as redes permitidas. Editar não troca o link; a TV recebe a configuração nova sozinha.
 */
export function KioskScreenPanel({ screen, lines, views, onClose, onCreated, onSaved }: {
  screen: KioskScreenRow | null
  lines: readonly string[]
  views: readonly string[]
  onClose: () => void
  onCreated: (link: KioskScreenLink) => void
  onSaved: () => void
}) {
  const start: Draft = screen
    ? { name: screen.name, line: screen.line ?? '', views: [...screen.views], rotation: String(screen.rotationSeconds), networks: screen.allowedNetworks.join('\n') }
    : { name: '', line: '', views: [...views], rotation: String(DEFAULT_ROTATION), networks: '' }
  const [draft, setDraft] = useState<Draft>(start)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const dirty = JSON.stringify(draft) !== JSON.stringify(start)
  const field = (name: string) => error?.fields[name]?.[0]

  const save = async () => {
    const input = {
      name: draft.name.trim(),
      line: draft.line || null,
      views: draft.views,
      rotationSeconds: Number.parseInt(draft.rotation, 10) || DEFAULT_ROTATION,
      allowedNetworks: parseNetworks(draft.networks),
    }
    try {
      if (screen) {
        await updateKioskScreen(screen.id, { ...input, version: screen.version })
        notifySuccess('Tela salva')
        onSaved()
      } else {
        const link = await createKioskScreen(input)
        notifySuccess('Tela criada')
        onCreated(link)
      }

      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a tela.', fields: {} })
      return false
    }
  }

  const toggleView = (view: string) =>
    setDraft({ ...draft, views: draft.views.includes(view) ? draft.views.filter((v) => v !== view) : views.filter((v) => v === view || draft.views.includes(v)) })

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title={screen ? `Editar tela ${screen.name}` : 'Nova tela'} dirty={dirty} onSave={save}
      description={screen ? 'O link continua o mesmo; a TV recebe a configuração nova em até 1 minuto.' : 'Depois de criar, o link da TV aparece uma única vez.'}
      footer={(
        <>
          <button type="button" onClick={onClose} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          <button type="button" onClick={() => void save()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">{screen ? 'Salvar tela' : 'Criar tela'}</button>
        </>
      )}
    >
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="space-y-4">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Nome" required error={field('name')} hint="Como a TV aparece na lista, por exemplo TV da Montagem.">
          {(p) => <input {...p} value={draft.name} maxLength={60} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className={inputClass} />}
        </Field>
        <Field label="Linha" error={field('line')} hint={lines.length === 0 ? 'Nenhum centro de trabalho tem linha: a TV mostra todas.' : 'A TV mostra só as OS dessa linha.'}>
          {(p) => (
            <select {...p} value={draft.line} onChange={(e) => setDraft({ ...draft, line: e.target.value })} className={inputClass}>
              <option value="">Todas</option>
              {lines.map((l) => <option key={l} value={l}>{l}</option>)}
            </select>
          )}
        </Field>
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Visões</legend>
          {views.map((v) => (
            <label key={v} className="flex min-h-11 items-center gap-2 text-sm">
              <input type="checkbox" checked={draft.views.includes(v)} onChange={() => toggleView(v)} className="size-4" />
              {VIEW_LABELS[v] ?? v}
            </label>
          ))}
          <p className="text-xs text-muted-foreground">Nenhuma marcada = todas. A TV alterna entre as visões.</p>
          {field('views') ? <p role="alert" className="text-xs font-medium text-crit-ink">{field('views')}</p> : null}
        </fieldset>
        <Field label="Rotação (segundos)" error={field('rotationSeconds')} hint="De 10 a 120 segundos em cada visão.">
          {(p) => <input {...p} type="number" min={10} max={120} inputMode="numeric" value={draft.rotation} onChange={(e) => setDraft({ ...draft, rotation: e.target.value })} className={inputClass + ' tabular-nums'} />}
        </Field>
        <div className="space-y-1">
          <div className="flex items-center">
            <label htmlFor="kiosk-networks" className="text-sm font-medium">Redes permitidas</label>
            <Dica nome="Redes permitidas" texto="Opcional. Se preencher, a TV só abre o painel nessas redes (até 5, no formato 192.168.10.0/24). Vazio: qualquer rede." />
          </div>
          <textarea id="kiosk-networks" rows={3} value={draft.networks} placeholder="192.168.10.0/24" aria-invalid={!!field('allowedNetworks')}
            onChange={(e) => setDraft({ ...draft, networks: e.target.value })} className={inputClass + ' font-mono'} />
          <p className="text-xs text-muted-foreground">Uma por linha, até {MAX_NETWORKS}.</p>
          {field('allowedNetworks') ? <p role="alert" className="text-xs font-medium text-crit-ink">{field('allowedNetworks')}</p> : null}
        </div>
      </form>
    </SidePanel>
  )
}
