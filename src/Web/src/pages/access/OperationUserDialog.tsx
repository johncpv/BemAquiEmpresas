import * as Dialog from '@radix-ui/react-dialog'
import { useState } from 'react'
import { createOperationUser, resetOperationUserPin, updateOperationUser, useListGroups, useListOperationUserStores } from '@/api/generated/bemaqui'
import type { UserRow } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { Field, FormError } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { notifySuccess } from '@/components/ui/messages'
import { Dica } from '@/help/Dica'
import { pinProblem } from './operation-users'

const button = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold'
const content = 'fixed left-1/2 top-[8vh] z-50 max-h-[84vh] w-[min(460px,calc(100vw-32px))] -translate-x-1/2 overflow-y-auto rounded-md border border-border bg-popover p-5 text-popover-foreground shadow-lg'

function errorOf(e: unknown): string {
  if (e instanceof ApiError) return Object.values(e.fields).flat()[0] ?? e.message
  return 'Não foi possível salvar.'
}

function toggle(set: Set<string>, id: string, on: boolean) {
  const next = new Set(set)
  if (on) next.add(id)
  else next.delete(id)
  return next
}

/** Lojas onde o operador trabalha (o PIN só vale nos caixas delas). */
function StorePicker({ selected, onChange, enabled }: { selected: Set<string>; onChange: (s: Set<string>) => void; enabled: boolean }) {
  const stores = useListOperationUserStores({ query: { enabled } })
  return (
    <fieldset className="space-y-1">
      <legend className="text-sm font-medium">Lojas<Dica nome="Lojas" texto="O operador só entra por PIN nos caixas registrados destas lojas." /></legend>
      {stores.isPending ? <p className="text-sm text-muted-foreground">Carregando…</p> : (stores.data ?? []).length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhuma loja ativa. Cadastre a loja em Loja → Lojas e caixas.</p>
      ) : (stores.data ?? []).map((s) => (
        <label key={s.id} className="flex items-center gap-2 text-sm">
          <input type="checkbox" checked={selected.has(s.id)} onChange={(e) => onChange(toggle(selected, s.id, e.target.checked))} className="size-4 accent-primary" />
          {s.name} <span className="text-muted-foreground">({s.code})</span>
        </label>
      ))}
    </fieldset>
  )
}

function PinFields({ pin, confirmation, onPin, onConfirmation }: { pin: string; confirmation: string; onPin: (v: string) => void; onConfirmation: (v: string) => void }) {
  return (
    <>
      <Field label="PIN inicial" hint="6 números. O operador troca por um só dele na primeira entrada no caixa.">
        {(p) => <input {...p} inputMode="numeric" autoComplete="off" maxLength={6} value={pin} onChange={(e) => onPin(e.target.value.replace(/\D/g, ''))} className={inputClass} />}
      </Field>
      <Field label="Repita o PIN">
        {(p) => <input {...p} inputMode="numeric" autoComplete="off" maxLength={6} value={confirmation} onChange={(e) => onConfirmation(e.target.value.replace(/\D/g, ''))} className={inputClass} />}
      </Field>
    </>
  )
}

/**
 * "Novo usuário de operação" (PDV-01 CA 12, ADR 0049): quem só usa o caixa, sem e-mail e sem senha. Nome, grupo (sem ação reservada), lojas e o PIN
 * inicial. Depois de criar, oferece criar outro (propositivo).
 */
export function OperationUserDialog({ onCreated }: { onCreated: () => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const groups = useListGroups({ query: { enabled: open } })
  const [name, setName] = useState('')
  const [groupIds, setGroupIds] = useState<Set<string>>(new Set())
  const [storeIds, setStoreIds] = useState<Set<string>>(new Set())
  const [pin, setPin] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [created, setCreated] = useState<string | null>(null)

  const reset = () => { setName(''); setPin(''); setConfirmation(''); setError(null) }

  const submit = async () => {
    const problem = name.trim().length < 2 ? 'Informe o nome do operador.' : groupIds.size === 0 ? 'Escolha o grupo do operador.' : storeIds.size === 0 ? 'Escolha ao menos uma loja.' : pinProblem(pin, confirmation)
    if (problem) { setError(problem); return }
    setBusy(true)
    setError(null)
    try {
      await createOperationUser({ name: name.trim(), groupIds: [...groupIds], storeIds: [...storeIds], pin, pinConfirmation: confirmation })
      await onCreated()
      setCreated(name.trim())
      reset()
    } catch (e) {
      setError(errorOf(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog.Root open={open} onOpenChange={(o) => { setOpen(o); if (!o) { setCreated(null); reset() } }}>
      <Dialog.Trigger className={secondary}>Novo usuário de operação</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
        <Dialog.Content className={content}>
          <Dialog.Title className="text-base font-semibold">Novo usuário de operação</Dialog.Title>
          <Dialog.Description className="mt-1 text-sm text-muted-foreground">Para quem só usa o caixa: sem e-mail e sem senha. Entra por PIN nos caixas registrados das lojas escolhidas e não acessa a retaguarda.</Dialog.Description>
          {created ? (
            <div className="mt-3 space-y-3">
              <p role="status" className="rounded-md border border-ok-soft bg-ok-soft px-3 py-2 text-sm text-ok-ink">{created} já pode entrar no caixa com o PIN inicial.</p>
              <div className="flex justify-end gap-2">
                <Dialog.Close className={secondary}>Fechar</Dialog.Close>
                <button type="button" onClick={() => setCreated(null)} className={button}>Criar outro</button>
              </div>
            </div>
          ) : (
            <form onSubmit={(e) => { e.preventDefault(); void submit() }} noValidate className="mt-3 space-y-3">
              <FormError message={error ?? undefined} />
              <Field label="Nome">{(p) => <input {...p} value={name} maxLength={200} onChange={(e) => setName(e.target.value)} className={inputClass} />}</Field>
              <fieldset className="space-y-1">
                <legend className="text-sm font-medium">Grupo<Dica nome="Grupo" texto="Grupo com ação reservada (aprovar, pagar, ver dado sensível) não aceita usuário de operação. Use, por exemplo, Operador de caixa." /></legend>
                {(groups.data ?? []).filter((g) => g.isActive && !g.isSystem).map((g) => (
                  <label key={g.id} className="flex items-center gap-2 text-sm">
                    <input type="checkbox" checked={groupIds.has(g.id)} onChange={(e) => setGroupIds((s) => toggle(s, g.id, e.target.checked))} className="size-4 accent-primary" />
                    {g.name}
                  </label>
                ))}
              </fieldset>
              <StorePicker selected={storeIds} onChange={setStoreIds} enabled={open} />
              <PinFields pin={pin} confirmation={confirmation} onPin={setPin} onConfirmation={setConfirmation} />
              <div className="flex justify-end gap-2 pt-2">
                <Dialog.Close className={secondary}>Voltar</Dialog.Close>
                <button type="submit" disabled={busy} className={button}>{busy ? 'Criando…' : 'Criar usuário de operação'}</button>
              </div>
            </form>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

/** Ações do usuário de operação (identity.create): as lojas e um PIN novo (o anterior deixa de valer; o novo pede a troca no primeiro uso). */
export function OperationUserActions({ user, refresh }: { user: UserRow; refresh: () => Promise<void> }) {
  const [mode, setMode] = useState<'stores' | 'pin' | null>(null)
  const [storeIds, setStoreIds] = useState<Set<string>>(() => new Set(user.operationStoreIds ?? []))
  const [pin, setPin] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const close = () => { setMode(null); setPin(''); setConfirmation(''); setError(null) }

  const save = async () => {
    const problem = mode === 'stores' ? (storeIds.size === 0 ? 'Escolha ao menos uma loja.' : null) : pinProblem(pin, confirmation)
    if (problem) { setError(problem); return }
    setBusy(true)
    setError(null)
    try {
      if (mode === 'stores') await updateOperationUser(user.id, { name: user.name, storeIds: [...storeIds] })
      else await resetOperationUserPin(user.id, { pin, pinConfirmation: confirmation })
      await refresh()
      notifySuccess(mode === 'stores' ? 'Lojas salvas' : 'PIN novo criado')
      close()
    } catch (e) {
      setError(errorOf(e))
    } finally {
      setBusy(false)
    }
  }

  if (user.status !== 'Active') return null
  return (
    <>
      <button type="button" onClick={() => { setStoreIds(new Set(user.operationStoreIds ?? [])); setMode('stores') }} className="text-xs font-semibold text-primary">Lojas</button>
      <button type="button" onClick={() => setMode('pin')} className="text-xs font-semibold text-primary">Novo PIN</button>
      <Dialog.Root open={mode !== null} onOpenChange={(o) => { if (!o) close() }}>
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-40 bg-foreground/30" />
          <Dialog.Content className={content}>
            <Dialog.Title className="text-base font-semibold">{mode === 'stores' ? `Lojas de ${user.name}` : `Novo PIN para ${user.name}`}</Dialog.Title>
            <Dialog.Description className="mt-1 text-sm text-muted-foreground">
              {mode === 'stores' ? 'O PIN vale só nos caixas destas lojas. Se ele estiver num caixa de uma loja que saiu, a sessão cai.' : 'O PIN anterior deixa de valer e a sessão no caixa cai. Na próxima entrada, ele troca o PIN novo por um só dele.'}
            </Dialog.Description>
            <form onSubmit={(e) => { e.preventDefault(); void save() }} noValidate className="mt-3 space-y-3">
              <FormError message={error ?? undefined} />
              {mode === 'stores'
                ? <StorePicker selected={storeIds} onChange={setStoreIds} enabled={mode === 'stores'} />
                : <PinFields pin={pin} confirmation={confirmation} onPin={setPin} onConfirmation={setConfirmation} />}
              <div className="flex justify-end gap-2 pt-2">
                <Dialog.Close className={secondary}>Voltar</Dialog.Close>
                <button type="submit" disabled={busy} className={button}>{mode === 'stores' ? 'Salvar lojas' : 'Criar PIN novo'}</button>
              </div>
            </form>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </>
  )
}
