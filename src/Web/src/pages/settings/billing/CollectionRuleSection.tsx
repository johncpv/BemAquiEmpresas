import { useQueryClient } from '@tanstack/react-query'
import { useId, useRef, useState } from 'react'
import { getGetCollectionRuleQueryKey, previewCollectionReminder, saveCollectionRule, useGetCollectionRule } from '@/api/generated/bemaqui'
import type { CollectionPreview, CollectionRuleView } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError, FormSection } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RecordHistory } from '@/components/history/RecordHistory'
import { notifySuccess } from '@/components/ui/messages'
import { ErrorState, Skeleton } from '@/components/ui/states'
import { Dica } from '@/help/Dica'
import { today } from '@/pages/finance/receivables/receivables'
import { COLLECTION_DICAS, daysError, insertAt, MAX_TEXT, nextRunText, PLACEHOLDER_HINTS, SEND_TIMES, type StepDraft, stepLabel, textError, timeline } from './collection-rule'

const button = 'min-h-11 rounded-md border border-border bg-card px-3 py-2 text-sm font-semibold disabled:opacity-60'
const primary = 'min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'

/**
 * Seção "Régua de cobrança" de Configurações → Cobrança (FIN-09 CA 1 e 2): linha do tempo dos passos, editor do texto com os campos
 * clicáveis, prévia com um título de exemplo, horário de envio, Reply-To e a chave "Régua ligada". Sem settings.edit, modo leitura.
 */
export function CollectionRuleSection() {
  const query = useGetCollectionRule({ query: { retry: false } })
  return (
    <section id="regua" aria-labelledby="regua-titulo" className="mt-8 scroll-mt-24 space-y-3">
      <h2 id="regua-titulo" className="flex items-center text-lg font-semibold">Régua de cobrança<Dica nome="Régua de cobrança" texto={COLLECTION_DICAS.regua} /></h2>
      {query.isPending ? (
        <div className="space-y-3"><Skeleton className="h-10" /><Skeleton className="h-48" /></div>
      ) : query.isError || !query.data ? (
        <ErrorState title="Não foi possível carregar a régua de cobrança." message={query.error instanceof ApiError ? query.error.message : undefined} onRetry={() => void query.refetch()} />
      ) : (
        <RuleForm key={`${query.data.id ?? 'novo'}-${query.data.version}`} view={query.data} />
      )}
    </section>
  )
}

let seq = 0
const draftsOf = (view: CollectionRuleView): StepDraft[] => view.steps.map((s) => ({ key: `s${seq++}`, days: String(s.days), text: s.text }))

function RuleForm({ view }: { view: CollectionRuleView }) {
  const client = useQueryClient()
  const canEdit = view.canEdit
  const [active, setActive] = useState(view.isActive)
  const [sendTime, setSendTime] = useState(view.sendTime)
  const [replyTo, setReplyTo] = useState(view.replyTo ?? '')
  const [steps, setSteps] = useState<StepDraft[]>(() => draftsOf(view))
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const [busy, setBusy] = useState(false)
  const [preview, setPreview] = useState<{ key: string; data?: CollectionPreview; error?: string } | null>(null)
  const editors = useRef<Record<string, HTMLTextAreaElement | null>>({})
  const firstTime = view.id === null || view.id === undefined

  const update = (key: string, patch: Partial<StepDraft>) => setSteps((all) => all.map((s) => (s.key === key ? { ...s, ...patch } : s)))
  const remove = (key: string) => setSteps((all) => all.filter((s) => s.key !== key))
  const add = () => setSteps((all) => {
    const used = new Set(all.map((s) => Number(s.days)))
    const days = [5, 7, 15, 20, 30, 45, 60].find((d) => !used.has(d)) ?? 1
    return [...all, { key: `s${seq++}`, days: String(days), text: 'Olá, {cliente}. O título {numero}, no valor de {valor}, venceu em {vencimento}.' }]
  })
  const insert = (key: string, token: string) => {
    const el = editors.current[key]
    const step = steps.find((s) => s.key === key)
    if (!step) return
    const next = insertAt(step.text, token, el?.selectionStart, el?.selectionEnd)
    if (next.text.length > MAX_TEXT) return
    update(key, { text: next.text })
    requestAnimationFrame(() => { el?.focus(); el?.setSelectionRange(next.caret, next.caret) })
  }

  const localError = steps.map((s) => daysError(s.days, steps, s.key) ?? textError(s.text, view.placeholders)).find((e) => e !== null) ?? null

  const save = async (isActive = active) => {
    setBusy(true)
    setError(null)
    try {
      const saved = await saveCollectionRule({ isActive, sendTime, replyTo: replyTo.trim() || null, version: view.id ? view.version : null, steps: steps.map((s) => ({ days: Number(s.days), text: s.text })) })
      client.setQueryData(getGetCollectionRuleQueryKey(), saved)
      notifySuccess(saved.isActive ? `Régua salva. ${nextRunText(saved.nextRunCount, saved.nextRunDate, today())}` : 'Régua salva')
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar a régua. Tente de novo.', fields: {} })
    } finally {
      setBusy(false)
    }
  }

  const showPreview = async (step: StepDraft) => {
    setPreview({ key: step.key })
    try {
      setPreview({ key: step.key, data: await previewCollectionReminder({ days: Number(step.days), text: step.text }) })
    } catch (e) {
      setPreview({ key: step.key, error: e instanceof ApiError ? e.message : 'Não foi possível montar a prévia. Tente de novo.' })
    }
  }

  const fieldError = (i: number, name: 'days' | 'text') => error?.fields[`steps[${i}].${name}`]?.[0]

  return (
    <div className="space-y-4">
      {!active ? (
        <div role="status" className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm">
          <span>A régua está desligada. Ligue para enviar lembretes automáticos.{firstTime ? ' Os passos e textos abaixo são sugestões: ajuste o que quiser.' : ''}</span>
          {canEdit ? <button type="button" disabled={busy || !!localError} onClick={() => { setActive(true); void save(true) }} className={primary}>Ligar régua</button> : null}
        </div>
      ) : (
        <p role="status" className="rounded-md border border-ok-soft bg-ok-soft px-3 py-2 text-sm font-semibold text-ok-ink">
          Régua ligada. {nextRunText(view.nextRunCount, view.nextRunDate, today())}
        </p>
      )}
      {!canEdit ? <p role="note" className="rounded-md border border-border bg-muted px-3 py-2 text-sm">Modo leitura: só quem edita Configurações altera a régua.</p> : null}
      <fieldset disabled={!canEdit} className="space-y-4">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <FormSection title="Passos" description="Dias em relação ao vencimento: negativo é antes, 0 é no dia, positivo é depois. Passo em sábado, domingo ou feriado sai no próximo dia útil.">
          <ol aria-label="Linha do tempo da régua" className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
            {timeline(steps).map((s, i) => (
              <li key={s.key} className="flex items-center gap-2 text-sm">
                {i > 0 ? <span aria-hidden="true" className="hidden h-px w-6 bg-border sm:inline-block" /> : null}
                <span className={`rounded-full px-2.5 py-1 font-semibold tabular-nums ${s.days === 0 ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>{s.label}</span>
              </li>
            ))}
          </ol>
          <ul className="space-y-3">
            {steps.map((step, i) => (
              <StepEditor key={step.key} step={step} index={i} placeholders={view.placeholders} canRemove={steps.length > 1}
                daysError={fieldError(i, 'days') ?? daysError(step.days, steps, step.key) ?? undefined} textError={fieldError(i, 'text') ?? textError(step.text, view.placeholders) ?? undefined}
                onChange={(patch) => update(step.key, patch)} onRemove={() => remove(step.key)} onInsert={(token) => insert(step.key, token)} onPreview={() => void showPreview(step)}
                editorRef={(el) => { editors.current[step.key] = el }}
                preview={preview?.key === step.key ? preview : null} />
            ))}
          </ul>
          {canEdit && steps.length < view.maxSteps ? <button type="button" onClick={add} className={button}>Incluir passo</button> : null}
          {steps.length >= view.maxSteps ? <p className="text-xs text-muted-foreground">A régua tem até {view.maxSteps} passos.</p> : null}
        </FormSection>
        <FormSection title="Envio" description="Os lembretes saem pela fila de e-mails e somam no limite diário acima. O que passar do limite sai no próximo dia útil, sem pular o passo.">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Horário de envio" hint="Horário de Brasília, de 07:00 a 20:00." error={error?.fields.sendTime?.[0]}>
              {(p) => (
                <select {...p} value={sendTime} onChange={(e) => setSendTime(e.target.value)} className={inputClass + ' tabular-nums sm:max-w-40'}>
                  {(SEND_TIMES.includes(sendTime) ? SEND_TIMES : [sendTime, ...SEND_TIMES]).map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              )}
            </Field>
            <Field label="Respostas do cliente vão para" error={error?.fields.replyTo?.[0]}
              hint={view.companyEmail ? `Vazio: ${view.companyEmail} (e-mail da empresa).` : 'Informe o e-mail do financeiro. Sem ele, o cliente não tem a quem responder.'}>
              {(p) => <input {...p} type="email" maxLength={254} value={replyTo} onChange={(e) => setReplyTo(e.target.value)} placeholder="financeiro@suaempresa.com.br" className={inputClass} />}
            </Field>
          </div>
          <label className="flex min-h-11 items-center gap-2 text-sm font-semibold">
            <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} className="size-4 accent-primary" />
            Régua ligada
          </label>
        </FormSection>
        {canEdit ? (
          <div className="flex flex-wrap items-center gap-2">
            <button type="button" disabled={busy || !!localError} onClick={() => void save()} className={primary}>Salvar régua</button>
            {localError ? <span className="text-xs text-crit-ink">Corrija os passos antes de salvar.</span> : null}
          </div>
        ) : null}
      </fieldset>
      {view.id ? (
        <details className="text-sm">
          <summary className="cursor-pointer text-xs font-semibold text-muted-foreground">Histórico de alterações da régua</summary>
          <div className="mt-2"><RecordHistory entity="CollectionRule" id={view.id} /></div>
        </details>
      ) : null}
    </div>
  )
}

interface StepEditorProps {
  step: StepDraft
  index: number
  placeholders: string[]
  canRemove: boolean
  daysError?: string
  textError?: string
  onChange: (patch: Partial<StepDraft>) => void
  onRemove: () => void
  onInsert: (token: string) => void
  onPreview: () => void
  editorRef: (el: HTMLTextAreaElement | null) => void
  preview: { data?: CollectionPreview; error?: string } | null
}

/** Um passo: dias, texto literal com os campos clicáveis e a prévia logo abaixo (a 400 px, tudo em coluna). */
function StepEditor({ step, index, placeholders, canRemove, daysError: dErr, textError: tErr, onChange, onRemove, onInsert, onPreview, editorRef, preview }: StepEditorProps) {
  const fieldsId = useId()
  const valid = /^-?\d{1,2}$/.test(step.days.trim())
  return (
    <li className="space-y-2 rounded-md border border-border bg-card p-3">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div className="w-40">
          <Field label={`Passo ${index + 1}: dias`} error={dErr} hint={valid ? stepLabel(Number(step.days)) : undefined}>
            {(p) => <input {...p} inputMode="numeric" maxLength={3} value={step.days} onChange={(e) => onChange({ days: e.target.value.replace(/[^\d-]/g, '') })} className={inputClass + ' tabular-nums'} />}
          </Field>
        </div>
        {canRemove ? <button type="button" onClick={onRemove} className={button}>Remover passo</button> : null}
      </div>
      <Field label="Texto do e-mail" error={tErr} hint={`${step.text.length.toLocaleString('pt-BR')} de 1.000 caracteres. Texto puro: sai como está, sem formatação.`}>
        {(p) => <textarea {...p} ref={editorRef} rows={4} maxLength={MAX_TEXT} value={step.text} onChange={(e) => onChange({ text: e.target.value })} className={inputClass + ' min-h-24'} />}
      </Field>
      <div>
        <p id={fieldsId} className="flex items-center text-xs font-semibold text-muted-foreground">Campos do texto<Dica nome="Campos do texto" texto={COLLECTION_DICAS.campos} /></p>
        <div role="group" aria-labelledby={fieldsId} className="mt-1 flex flex-wrap gap-1.5">
          {placeholders.map((token) => (
            <button key={token} type="button" title={PLACEHOLDER_HINTS[token]} onClick={() => onInsert(token)} className="min-h-11 rounded-md border border-border bg-muted px-2 font-mono text-xs sm:min-h-8">
              {token}
            </button>
          ))}
        </div>
      </div>
      <button type="button" onClick={onPreview} disabled={!!tErr} className={button}>Ver prévia</button>
      {preview ? (
        <div aria-live="polite" className="rounded-md border border-border bg-muted p-3 text-sm">
          {preview.error ? <p role="alert" className="text-crit-ink">{preview.error}</p> : !preview.data ? <Skeleton className="h-16" /> : (
            <>
              <p className="text-xs text-muted-foreground">{preview.data.sampleNote}</p>
              <p className="mt-1 font-semibold">{preview.data.subject}</p>
              <p className="mt-2 whitespace-pre-line">{preview.data.body}</p>
            </>
          )}
        </div>
      ) : null}
    </li>
  )
}
