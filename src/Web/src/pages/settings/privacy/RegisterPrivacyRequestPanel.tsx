import { useState } from 'react'
import { createPrivacyRequest, useSearchPrivacySubjects } from '@/api/generated/bemaqui'
import type { CreatePrivacyRequestInput, PersonalDataSubjectMatch } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { SidePanel } from '@/components/ui/side-panel'
import { CHANNELS, KINDS, nextStepOf, SUBJECT_TYPES } from './privacy'

const today = () => new Date().toLocaleDateString('sv-SE', { timeZone: 'America/Sao_Paulo' })

type Draft = {
  subjectType: CreatePrivacyRequestInput['subjectType']
  subject: PersonalDataSubjectMatch | null
  term: string
  subjectName: string
  subjectContact: string
  kind: CreatePrivacyRequestInput['kind']
  requestedOn: string
  channel: CreatePrivacyRequestInput['channel']
  notes: string
}

/**
 * "Registrar pedido" (LGPD-01a CA 1): quem pediu (pelo seletor do módulo dono, ou "Titular não encontrado" com nome e contato), o tipo, a
 * data, o canal e a observação (até 500). O tipo já sugere o próximo passo.
 */
export function RegisterPrivacyRequestPanel({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const start: Draft = { subjectType: 'Party', subject: null, term: '', subjectName: '', subjectContact: '', kind: 'Access', requestedOn: today(), channel: 'Email', notes: '' }
  const [draft, setDraft] = useState<Draft>(start)
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const unknown = draft.subjectType === 'Unknown'
  const search = useSearchPrivacySubjects({ kind: draft.subjectType, q: draft.term }, { query: { enabled: !unknown && draft.term.trim().length >= 2 && !draft.subject } })
  const field = (name: string) => error?.fields[name]?.[0]
  const dirty = JSON.stringify(draft) !== JSON.stringify(start)

  const save = async () => {
    try {
      const created = await createPrivacyRequest({
        subjectType: draft.subjectType,
        subjectId: unknown ? null : draft.subject?.id ?? null,
        subjectName: unknown ? draft.subjectName.trim() : null,
        subjectContact: unknown ? draft.subjectContact.trim() : null,
        kind: draft.kind,
        requestedOn: draft.requestedOn,
        channel: draft.channel,
        notes: draft.notes.trim() || null,
      })
      notifySuccess(`Pedido ${created.number} registrado`)
      onCreated(created.id)
      return true
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível registrar o pedido.', fields: {} })
      return false
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title="Registrar pedido" dirty={dirty} onSave={save}
      description="O prazo é calculado pela data do pedido e pelos dias de resposta."
      footer={(
        <>
          <button type="button" onClick={onClose} className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          <button type="button" onClick={() => void save()} className="min-h-11 rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Registrar pedido</button>
        </>
      )}
    >
      <form noValidate onSubmit={(e) => { e.preventDefault(); void save() }} className="space-y-4">
        <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
        <Field label="Quem pediu" required>
          {(p) => (
            <select {...p} value={draft.subjectType} onChange={(e) => setDraft({ ...draft, subjectType: e.target.value as Draft['subjectType'], subject: null, term: '' })} className={inputClass}>
              {SUBJECT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          )}
        </Field>
        {unknown ? (
          <>
            <Field label="Nome do titular" required error={field('subjectName')}>
              {(p) => <input {...p} value={draft.subjectName} maxLength={150} onChange={(e) => setDraft({ ...draft, subjectName: e.target.value })} className={inputClass} />}
            </Field>
            <Field label="Meio de contato" required error={field('subjectContact')} hint="E-mail ou telefone que o titular informou.">
              {(p) => <input {...p} value={draft.subjectContact} maxLength={200} onChange={(e) => setDraft({ ...draft, subjectContact: e.target.value })} className={inputClass} />}
            </Field>
          </>
        ) : draft.subject ? (
          <div className="flex items-center justify-between gap-2 rounded-md border border-border bg-muted px-3 py-2 text-sm">
            <span><b className="font-semibold">{draft.subject.name}</b>{draft.subject.detail ? <span className="block text-muted-foreground">{draft.subject.detail}</span> : null}</span>
            <button type="button" onClick={() => setDraft({ ...draft, subject: null })} className="min-h-11 rounded-md border border-border bg-card px-3 text-sm font-semibold">Trocar</button>
          </div>
        ) : (
          <Field label="Buscar titular" required error={field('subjectId')} hint="Nome, e-mail ou CPF (pelo menos 2 letras).">
            {(p) => (
              <div className="space-y-2">
                <input {...p} type="search" value={draft.term} onChange={(e) => setDraft({ ...draft, term: e.target.value })} className={inputClass} />
                {search.isFetching ? <p className="text-xs text-muted-foreground">Buscando…</p> : null}
                {search.data && search.data.length === 0 ? <p className="text-xs text-muted-foreground">Ninguém encontrado. Marque "Titular não encontrado" se a pessoa não tem cadastro.</p> : null}
                {search.data && search.data.length > 0 ? (
                  <ul aria-label="Titulares encontrados" className="divide-y divide-border rounded-md border border-border">
                    {search.data.map((m) => (
                      <li key={m.id}>
                        <button type="button" onClick={() => setDraft({ ...draft, subject: m })} className="block min-h-11 w-full px-3 py-2 text-left text-sm hover:bg-muted">
                          <b className="font-semibold">{m.name}</b>{m.detail ? <span className="block text-muted-foreground">{m.detail}</span> : null}
                        </button>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>
            )}
          </Field>
        )}
        <Field label="Tipo do pedido" required error={field('kind')} hint={nextStepOf(draft.kind, false)}>
          {(p) => (
            <select {...p} value={draft.kind} onChange={(e) => setDraft({ ...draft, kind: e.target.value as Draft['kind'] })} className={inputClass}>
              {KINDS.map((k) => <option key={k.value} value={k.value}>{k.label}</option>)}
            </select>
          )}
        </Field>
        <Field label="Data do pedido" required error={field('requestedOn')}>
          {(p) => <input {...p} type="date" max={today()} value={draft.requestedOn} onChange={(e) => setDraft({ ...draft, requestedOn: e.target.value })} className={inputClass + ' tabular-nums'} />}
        </Field>
        <Field label="Canal" required>
          {(p) => (
            <select {...p} value={draft.channel} onChange={(e) => setDraft({ ...draft, channel: e.target.value as Draft['channel'] })} className={inputClass}>
              {CHANNELS.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
            </select>
          )}
        </Field>
        <Field label="Observação" error={field('notes')} hint={`${draft.notes.length} de 500 caracteres.`}>
          {(p) => <textarea {...p} rows={3} maxLength={500} value={draft.notes} onChange={(e) => setDraft({ ...draft, notes: e.target.value })} className={inputClass} />}
        </Field>
      </form>
    </SidePanel>
  )
}
