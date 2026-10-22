import { Money } from '@/components/ui/money'
import { StageIndicator } from '@/components/ui/stage-indicator'
import { DEFAULT_LETTERS, DEFAULT_STAGES } from '@/lib/stages'
import { StatusChip } from '@/components/ui/status-chip'
import { ThemeToggle } from '@/theme/ThemeToggle'
import { GalleryForm, GalleryStates, GalleryTable } from './GalleryData'
import { MessageRegion } from '@/components/ui/message-region'

/** Roteiro longo (12 etapas): o indicador passa ao formato compacto (ENG-01 CA 14). */
const GALLERY_LONG_ROUTING = Array.from({ length: 12 }, (_, i) => `Etapa ${i + 1}`)

const SWATCHES = ['background', 'card', 'muted', 'border', 'input', 'primary', 'primary-soft', 'rail', 'signal', 'ok', 'warn', 'crit', 'info'] as const

/** Galeria de componentes (FND-09a CA 16): só em desenvolvimento, em /dev/galeria. Comparar com o protótipo nos dois temas. */
export default function Gallery() {
  return (
    <main className="mx-auto max-w-5xl space-y-8 px-4 py-6">
      <MessageRegion />
      <header className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-[28px] font-bold">Galeria de componentes</h1>
        <ThemeToggle />
      </header>

      <section aria-labelledby="tokens" className="space-y-3">
        <h2 id="tokens" className="text-[15px] font-semibold">Tokens</h2>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          {SWATCHES.map((token) => (
            <div key={token} className="overflow-hidden rounded-md border border-border bg-card">
              <div className="h-10" style={{ background: `var(--${token})` }} />
              <code className="block px-2 py-1 text-xs text-muted-foreground">--{token}</code>
            </div>
          ))}
        </div>
      </section>

      <section aria-labelledby="chips" className="space-y-3">
        <h2 id="chips" className="text-[15px] font-semibold">Estados</h2>
        <div className="flex flex-wrap gap-2">
          <StatusChip tone="ok">Em dia</StatusChip>
          <StatusChip tone="warn">Em risco</StatusChip>
          <StatusChip tone="crit">Atrasada</StatusChip>
          <StatusChip tone="info">Aguardando material</StatusChip>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar</button>
          <button type="button" className="rounded-md bg-ok px-3 py-2 text-sm font-semibold text-success-foreground">Aprovar</button>
          <button type="button" className="rounded-md bg-crit px-3 py-2 text-sm font-semibold text-critical-foreground">Cancelar OS</button>
          <input aria-label="Campo de exemplo" placeholder="Campo" className="rounded-md border border-input bg-card px-3 py-2 text-sm" />
        </div>
      </section>

      <section aria-labelledby="etapas" className="space-y-3">
        <h2 id="etapas" className="text-[15px] font-semibold">Indicador de etapa</h2>
        <div className="flex flex-wrap gap-6">
          {DEFAULT_STAGES.map((stage, i) => (
            <StageIndicator key={stage} stages={DEFAULT_STAGES} letters={DEFAULT_LETTERS} current={i} />
          ))}
        </div>
        <div className="flex flex-wrap gap-6">
          <StageIndicator stages={DEFAULT_STAGES} letters={DEFAULT_LETTERS} current={2} showLabel />
          <StageIndicator stages={GALLERY_LONG_ROUTING} current={2} showLabel />
        </div>
      </section>

      <section aria-labelledby="numeros" className="space-y-3">
        <h2 id="numeros" className="text-[15px] font-semibold">Números</h2>
        <p className="font-display text-[28px] font-bold font-wide">
          <Money value={1234567.5} />
        </p>
        <p>
          Sem centavos: <Money value={1234.5} cents={false} /> · Negativo: <Money value={-850} highlightNegative />
        </p>
      </section>

      <section aria-labelledby="tabela" className="space-y-3">
        <h2 id="tabela" className="text-[15px] font-semibold">Tabela</h2>
        <GalleryTable />
      </section>

      <section aria-labelledby="formulario" className="space-y-3">
        <h2 id="formulario" className="text-[15px] font-semibold">Formulário</h2>
        <GalleryForm />
      </section>

      <section aria-labelledby="vazio-erro" className="space-y-3">
        <h2 id="vazio-erro" className="text-[15px] font-semibold">Vazio, erro e confirmação</h2>
        <GalleryStates />
      </section>

      <section aria-labelledby="tv" className="theme-tv space-y-2 rounded-md p-4">
        <h2 id="tv" className="font-extrabold font-wider">Painel da TV</h2>
        <p>Sempre escuro, texto ≥ 24px.</p>
      </section>
    </main>
  )
}
