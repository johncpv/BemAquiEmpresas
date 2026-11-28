import { confirmPartyContact } from '@/api/generated/bemaqui'
import type { PartyDetail } from '@/api/generated/model'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Dica } from '@/help/Dica'
import { Can } from '@/pages/ModuleGate'
import { appContactFields } from './app-contact'

/**
 * Contato informado pelo Conecta (INT-04 CA 11): até uma pessoa confirmar, não vale como prova de identidade do cliente (o código de verificação não vai
 * para ele e o Conecta não aceita esse telefone). "Confirmar contato" grava a confirmação, que fica no histórico.
 */
export function AppContactNotice({ party, onConfirmed }: { party: PartyDetail; onConfirmed: (party: PartyDetail) => void }) {
  const fields = appContactFields(party)
  if (fields.length === 0) return null
  const what = fields.join(' e ')
  return (
    <section aria-label="Contato informado pelo Conecta" className="mb-4 flex flex-wrap items-center gap-3 rounded-md border border-border border-l-4 border-l-warn bg-card p-4">
      <p className="flex-1 text-sm">
        <strong>{what.charAt(0).toUpperCase() + what.slice(1)} {fields.length > 1 ? 'foram informados' : 'foi informado'} pelo Conecta.</strong>{' '}
        Confira com o cliente e confirme: até lá, não serve para mandar o código de verificação.
      </p>
      <Dica texto="O Conecta pode cadastrar o contato que o cliente digitou no WhatsApp. Como ninguém conferiu, o ERP não usa esse contato para provar quem é o cliente." />
      <Can perm="parties.edit">
        <ConfirmDialog
          verb="Confirmar contato"
          done="Contato confirmado"
          title="Confirmar o contato?"
          description="Confirme só se você conferiu com o cliente. Depois disso, o contato vale para mandar o código de verificação."
          onConfirm={async () => onConfirmed(await confirmPartyContact(party.id))}
          trigger={<button type="button" className="min-h-11 rounded-md border border-border px-3 py-2 text-sm font-semibold">Confirmar contato</button>}
        />
      </Can>
    </section>
  )
}
