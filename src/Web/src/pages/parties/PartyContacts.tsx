import { useState } from 'react'
import { removePartyContact } from '@/api/generated/bemaqui'
import type { PartyContactDto, PartyDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { EmptyState } from '@/components/ui/states'
import { Tag } from '@/components/ui/tag'
import { ContactPanel } from './ContactPanel'

const phone = (p?: string | null) => p?.replace(/^(\d{2})(\d{4,5})(\d{4})$/, '($1) $2-$3')

/** Aba Contatos (CA 14): nome, cargo, e-mail, telefone e "Recebe cobrança". */
export function PartyContacts({ party, canEdit, onSaved, adding, onAddingChange }: { party: PartyDetail; canEdit: boolean; onSaved: (p: PartyDetail) => void; adding: boolean; onAddingChange: (v: boolean) => void }) {
  const [editing, setEditing] = useState<PartyContactDto | null>(null)
  return (
    <div className="max-w-3xl space-y-3">
      {party.contacts.length === 0 ? (
        <EmptyState title="Nenhum contato ainda." description="Quem recebe a cobrança e quem fala pelo terceiro." action={canEdit ? <button type="button" onClick={() => onAddingChange(true)} className="text-sm font-semibold text-primary">Adicionar contato</button> : undefined} />
      ) : (
        <ul className="space-y-2">
          {party.contacts.map((c) => (
            <li key={c.id} className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-border bg-card p-3 text-sm">
              <div>
                <p className="font-semibold">{c.name}{c.role ? <span className="font-normal text-muted-foreground"> · {c.role}</span> : null}</p>
                <p className="text-muted-foreground">{[c.email, phone(c.phone)].filter(Boolean).join(' · ') || 'Sem e-mail e telefone'}</p>
                {c.receivesBilling ? <Tag className="mt-1">Recebe cobrança</Tag> : null}
              </div>
              {canEdit ? (
                <div className="flex gap-1">
                  <button type="button" onClick={() => setEditing(c)} className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Editar</button>
                  <ConfirmDialog verb="Remover" done="Contato removido" destructive title={`Remover o contato ${c.name}?`} onConfirm={async () => onSaved(await removePartyContact(party.id, c.id))}
                    trigger={<button type="button" className="rounded-md border border-border px-2 py-1 text-xs font-semibold">Remover</button>} />
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      {canEdit && party.contacts.length > 0 ? <button type="button" onClick={() => onAddingChange(true)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Adicionar contato</button> : null}
      {editing || adding ? <ContactPanel key={editing?.id ?? 'novo'} party={party} contact={editing} onClose={() => { setEditing(null); onAddingChange(false) }} onSaved={onSaved} /> : null}
    </div>
  )
}
