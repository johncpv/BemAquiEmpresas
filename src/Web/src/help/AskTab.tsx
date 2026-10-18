import { useState } from 'react'
import { askHelp } from '@/api/generated/bemaqui'
import type { HelpAnswer, HelpDocument } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { ErrorState } from '@/components/ui/states'
import { Markdown } from './markdown'
import { suggestedQuestions } from './resolve'

interface AskTabProps {
  current?: HelpDocument
  onOpenDoc: (key: string) => void
}

/**
 * Pergunte ao BemAqui (FND-15): a resposta vem só da ajuda do sistema e sempre cita a seção; sem IA ou sem citação
 * válida, mostra os trechos. Pergunta com dado da empresa pede confirmação antes de ir para a IA.
 */
export function AskTab({ current, onOpenDoc }: AskTabProps) {
  const [question, setQuestion] = useState('')
  const [asked, setAsked] = useState('')
  const [answer, setAnswer] = useState<HelpAnswer | null>(null)
  const [state, setState] = useState<'idle' | 'thinking' | 'error'>('idle')
  const [error, setError] = useState<string | undefined>()
  const suggestions = suggestedQuestions(current)

  const ask = async (text: string, confirmed = false) => {
    const q = text.trim()
    if (!q) return
    setAsked(q)
    setState('thinking')
    setError(undefined)
    try {
      setAnswer(await askHelp({ question: q, currentKey: current?.key ?? null, confirmed }))
      setState('idle')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : undefined)
      setState('error')
    }
  }

  return (
    <div className="space-y-4">
      <p className="text-xs text-muted-foreground">As respostas vêm só da ajuda do sistema.</p>
      <form onSubmit={(e) => { e.preventDefault(); void ask(question) }} className="space-y-2">
        <label htmlFor="pergunta" className="block text-sm font-medium">Sua pergunta</label>
        <textarea id="pergunta" value={question} onChange={(e) => setQuestion(e.target.value)} maxLength={500} rows={3} placeholder="Ex.: como convido uma pessoa da equipe?" className="w-full rounded-md border border-input bg-card px-3 py-2 text-sm" />
        <button type="submit" disabled={state === 'thinking' || !question.trim()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60">Perguntar</button>
      </form>

      {!answer && state === 'idle' && suggestions.length > 0 ? (
        <div>
          <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">Perguntas desta tela</p>
          <ul className="space-y-1">
            {suggestions.map((s) => (
              <li key={s}><button type="button" onClick={() => { setQuestion(s); void ask(s) }} className="text-left text-sm text-primary">{s}</button></li>
            ))}
          </ul>
        </div>
      ) : null}

      <div aria-live="polite" className="space-y-3">
        {state === 'thinking' ? <p role="status" className="animate-pulse text-sm text-muted-foreground">Procurando na ajuda…</p> : null}
        {state === 'error' ? <ErrorState title="Não foi possível responder agora." message={error} onRetry={() => void ask(asked)} /> : null}
        {state === 'idle' && answer ? <AnswerView answer={answer} onOpenDoc={onOpenDoc} onConfirm={() => void ask(asked, true)} /> : null}
      </div>
    </div>
  )
}

function AnswerView({ answer, onOpenDoc, onConfirm }: { answer: HelpAnswer; onOpenDoc: (key: string) => void; onConfirm: () => void }) {
  if (answer.kind === 'confirm') {
    return (
      <div role="alert" className="space-y-2 rounded-md border border-warn-soft bg-warn-soft px-3 py-2 text-sm text-warn-ink">
        <p>{answer.text}</p>
        <button type="button" onClick={onConfirm} className="font-semibold underline">Enviar assim mesmo</button>
      </div>
    )
  }

  if (answer.kind === 'not_found') {
    return <p className="text-sm">{answer.text} Veja o índice na aba "Ajuda da tela" ou tente outras palavras.</p>
  }

  const sources = (
    <ul aria-label="Fontes" className="space-y-2">
      {answer.citations.map((c) => (
        <li key={c.key + c.section} className="rounded-md border border-border bg-card p-3">
          <button type="button" onClick={() => onOpenDoc(c.key)} className="text-left text-sm font-semibold text-primary">{c.documentTitle} › {c.section}</button>
          {answer.kind === 'snippets' ? <div className="mt-1 text-sm"><Markdown text={c.body} /></div> : null}
        </li>
      ))}
    </ul>
  )

  return (
    <article className="space-y-3">
      {answer.kind === 'answer' ? <p className="text-sm">{answer.text}</p> : <p className="text-sm font-semibold">{answer.text}</p>}
      {answer.kind === 'answer' ? <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Fonte</p> : null}
      {sources}
    </article>
  )
}
