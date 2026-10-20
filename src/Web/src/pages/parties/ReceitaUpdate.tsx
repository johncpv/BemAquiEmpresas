import { useEffect, useState } from 'react'
import { addPartyAddress, updatePartyAddress } from '@/api/generated/bemaqui'
import type { PartyDetail } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FormError } from '@/components/form/form'
import { SidePanel } from '@/components/ui/side-panel'
import { notifySuccess } from '@/components/ui/messages'
import { useCnpjLookup } from './lookups'
import { updateFrom } from './party-actions'
import { completeAddress, differences, type ReceitaRow } from './receita-diff'
import { ReceitaPanel } from './ReceitaPanel'

/** "Atualizar pela Receita" (CAD-01b CA 8): compara, o usuário escolhe o que aplicar e confirma "Aplicar selecionados". */
export function ReceitaUpdate({ party, onClose, onSaved }: { party: PartyDetail; onClose: () => void; onSaved: (p: PartyDetail) => void }) {
  const cnpj = useCnpjLookup()
  const [chosen, setChosen] = useState<Set<ReceitaRow['key']>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const { run } = cnpj

  // Uma consulta ao abrir; o próprio terceiro não conta como duplicado (partyId). Tudo o que difere vem marcado.
  useEffect(() => {
    void run((party.document ?? '').replace(/[^0-9A-Z]/gi, ''), party.id).then((r) => r && setChosen(new Set(differences(party, r.data).map((d) => d.key))))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const data = cnpj.state.status === 'done' ? cnpj.state.value.data : null
  const rows = data ? differences(party, data) : []
  const toggle = (key: ReceitaRow['key'], on: boolean) => setChosen((s) => { const next = new Set(s); if (on) next.add(key); else next.delete(key); return next })

  const apply = async () => {
    if (!data) return
    const pick = <T,>(key: ReceitaRow['key'], value: T, keep: T) => (chosen.has(key) ? value : keep)
    try {
      let saved = await updateFrom(party, {
        legalName: pick('legalName', data.legalName, party.legalName), tradeName: pick('tradeName', data.tradeName, party.tradeName),
        email: pick('email', data.email, party.email), phone: pick('phone', data.phone, party.phone),
      })
      const a = data.address
      if (chosen.has('address') && a && completeAddress(a)) {
        const body = { kind: 'Billing' as const, zipCode: a.zipCode!, street: a.street!, number: a.number!, complement: a.complement, district: a.district!, city: a.city!, ibgeCode: a.ibgeCode, state: a.state!, isDefault: true }
        const billing = party.addresses.find((x) => x.kind === 'Billing' && x.isDefault)
        saved = billing ? await updatePartyAddress(party.id, billing.id, body) : await addPartyAddress(party.id, body)
      }

      onSaved(saved)
      notifySuccess('Dados atualizados')
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível aplicar os dados.')
    }
  }

  return (
    <SidePanel open onOpenChange={(o) => !o && onClose()} title="Atualizar pela Receita"
      footer={(
        <>
          <button type="button" onClick={onClose} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Voltar</button>
          {rows.length ? <button type="button" onClick={() => void apply()} disabled={chosen.size === 0} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-50">Aplicar selecionados</button> : null}
        </>
      )}>
      <div className="space-y-3">
        <FormError message={error ?? undefined} />
        {cnpj.state.status !== 'done' ? <ReceitaPanel state={cnpj.state} onUse={() => undefined} /> : rows.length === 0 ? (
          <p role="status" className="text-sm text-muted-foreground">O cadastro já está igual aos dados da Receita.</p>
        ) : (
          <fieldset className="space-y-2">
            <legend className="text-sm text-muted-foreground">Marque o que quer trazer da Receita. O resto não muda.</legend>
            {rows.map((r) => (
              <label key={r.key} className="flex gap-3 rounded-md border border-border p-3 text-sm">
                <input type="checkbox" checked={chosen.has(r.key)} onChange={(e) => toggle(r.key, e.target.checked)} className="mt-0.5 size-4 accent-primary" />
                <span className="space-y-0.5">
                  <b className="block font-semibold">{r.label}</b>
                  <span className="block text-muted-foreground">Atual: {r.current || '—'}</span>
                  <span className="block">Receita: {r.receita}</span>
                </span>
              </label>
            ))}
          </fieldset>
        )}
      </div>
    </SidePanel>
  )
}
