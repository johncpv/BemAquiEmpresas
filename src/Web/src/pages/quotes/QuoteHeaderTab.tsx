import { useState } from 'react'
import { updateQuote, useListQuoteCustomerAddresses, useListQuoteCustomerContacts } from '@/api/generated/bemaqui'
import type { QuoteDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'

/** Aba Dados (CA 4 a 6): contato e obra do cliente, prazo prometido e observações. */
export function QuoteHeaderTab({ quote, editable, refresh }: { quote: QuoteDetail; editable: boolean; refresh: () => Promise<void> }) {
  const contacts = useListQuoteCustomerContacts(quote.partyId)
  const addresses = useListQuoteCustomerAddresses(quote.partyId)
  const [contact, setContact] = useState(quote.contactId ?? '')
  const [site, setSite] = useState(quote.siteAddressId ?? '')
  const [days, setDays] = useState(quote.deliveryDays === null || quote.deliveryDays === undefined ? '' : String(quote.deliveryDays))
  const [notes, setNotes] = useState(quote.notes ?? '')
  const [error, setError] = useState<{ message: string; fields: Record<string, string[]> } | null>(null)
  const save = async () => {
    try {
      await updateQuote(quote.id, { contactId: contact || null, siteAddressId: site || null, deliveryDays: days === '' ? null : Number(days), notes: notes.trim() || null, version: quote.version })
      notifySuccess('Orçamento salvo')
      setError(null)
      await refresh()
    } catch (e) {
      setError(e instanceof ApiError ? { message: e.message, fields: e.fields } : { message: 'Não foi possível salvar o orçamento.', fields: {} })
    }
  }

  const field = (k: string) => error?.fields[k]?.[0]
  return (
    <fieldset disabled={!editable} className="max-w-3xl space-y-3">
      <FormError message={error && Object.keys(error.fields).length === 0 ? error.message : undefined} />
      <p className="text-sm"><span className="text-muted-foreground">Cliente: </span><b>{quote.partyName}</b></p>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Contato" error={field('contactId')} hint="Vem com o contato de cobrança do cliente.">
          {(p) => <select {...p} value={contact} onChange={(e) => setContact(e.target.value)} className={inputClass}><option value="">Sem contato</option>{(contacts.data ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}{c.role ? ` · ${c.role}` : ''}</option>)}</select>}
        </Field>
        <Field label="Endereço da obra" error={field('siteAddressId')} hint="Vem com o endereço de obra padrão do cliente.">
          {(p) => <select {...p} value={site} onChange={(e) => setSite(e.target.value)} className={inputClass}><option value="">Sem endereço</option>{(addresses.data ?? []).map((a) => <option key={a.id} value={a.id}>{a.street}, {a.number} · {a.city}/{a.state}</option>)}</select>}
        </Field>
        <Field label="Prazo de entrega prometido (dias)" error={field('deliveryDays')} hint="Dias corridos depois da aprovação.">
          {(p) => <input {...p} inputMode="numeric" maxLength={4} value={days} onChange={(e) => setDays(e.target.value.replace(/\D/g, ''))} className={inputClass + ' tabular-nums'} />}
        </Field>
      </div>
      <Field label="Observações" error={field('notes')}>{(p) => <textarea {...p} rows={4} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} className={inputClass} />}</Field>
      {editable ? <button type="button" onClick={() => void save()} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar orçamento</button> : null}
    </fieldset>
  )
}
