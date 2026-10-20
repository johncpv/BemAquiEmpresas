import { useState } from 'react'
import { createQuote, listQuoteCustomers } from '@/api/generated/bemaqui'
import { useIdempotencyKey } from '@/api/idempotency'
import { type ComboOption, Combobox } from '@/components/form/combobox'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

const searchCustomers = async (term: string): Promise<ComboOption[]> => (await listQuoteCustomers({ q: term })).map((c) => ({ id: c.id, label: c.name, hint: c.document ?? undefined }))

/** Novo orçamento (CA 4 e 5): escolhe o cliente; contato de cobrança e obra vêm sugeridos. */
export function NewQuoteDialog({ onCreated }: { onCreated: (id: string) => void }) {
  const [party, setParty] = useState<ComboOption | null>(null)
  const idempotency = useIdempotencyKey()
  return (
    <ConfirmDialog verb="Criar orçamento" done="Orçamento criado" title="Novo orçamento" description="O contato de cobrança e o endereço da obra do cliente vêm preenchidos. Dá para trocar depois."
      preconditions={[{ ok: !!party, text: 'Cliente escolhido.' }]}
      onConfirm={async () => {
        const saved = await createQuote({ partyId: party?.id ?? '', contactId: null, siteAddressId: null }, idempotency.options)
        idempotency.renew()
        onCreated(saved.id)
      }}
      trigger={<button type="button" className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Novo orçamento</button>}>
      <div className="mt-3 space-y-1">
        <span className="text-sm font-medium">Cliente</span>
        <Combobox aria-label="Cliente" queryKey={['quote-customers']} search={searchCustomers} value={party?.id ?? null} selected={party} placeholder="Busque pelo nome ou CNPJ" onChange={setParty} />
        <p className="text-xs text-muted-foreground">Só terceiros com o papel Cliente. Não achou? Cadastre em Cadastros → Terceiros.</p>
      </div>
    </ConfirmDialog>
  )
}
