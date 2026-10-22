import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { KeyRound, LogOut, Tablet } from 'lucide-react'
import { useState } from 'react'
import { createShopfloorPin, deleteMyShopfloorPin, getGetShopfloorDeviceQueryKey, registerShopfloorDevice, setShopfloorDeviceTheme } from '@/api/generated/bemaqui'
import type { DeviceTheme, ShopfloorDeviceContext } from '@/api/generated/model'
import { ApiError } from '@/api/http'
import { FloorButton, FloorConfirm, FloorDica, FloorSheet, NumericKeypad } from '@/components/shopfloor/kit'
import { notifySuccess } from '@/components/ui/messages'
import { formatInstantDate } from '@/lib/format'
import { DEVICE_THEMES, FAB_ENTRY_TEXT, isPinComplete, PIN_LENGTH } from './device'
import { applyDeviceTheme, endFloorSession, useFloorDevice, useFloorSession } from './floor-session'

/**
 * "Este aparelho" no cabeçalho do /fab (PRD-03b CA 4, 9, 11 e 12): tema do tablet (Automático, Claro, Escuro), "Registrar este
 * aparelho" e "Criar PIN" (só com login completo), "Apagar meu PIN deste aparelho" e "Sair".
 */
export function DeviceButton() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <FloorButton tone="secondary" className="px-3" aria-label="Este aparelho: tema, PIN e sair" onClick={() => setOpen(true)}>
        <Tablet aria-hidden="true" className="size-6" /><span className="max-sm:sr-only">Aparelho</span>
      </FloorButton>
      <DeviceSheet open={open} onOpenChange={setOpen} />
    </>
  )
}

function DeviceSheet({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const device = useFloorDevice()
  const { pin: pinSession } = useFloorSession()
  const [creating, setCreating] = useState(false)
  const [erasing, setErasing] = useState(false)
  const data = device.data
  const go = (to: string) => void navigate({ to })
  const refresh = (next: ShopfloorDeviceContext) => {
    client.setQueryData(getGetShopfloorDeviceQueryKey(), next)
    applyDeviceTheme(next)
  }

  const theme = async (value: DeviceTheme) => {
    refresh(await setShopfloorDeviceTheme({ theme: value }))
  }

  return (
    <>
      <FloorSheet open={open} onOpenChange={onOpenChange} title={data?.registered ? `Este aparelho: ${data.name}` : 'Este aparelho'}
        description={data?.registered && data.expiresAt ? `Registrado até ${formatInstantDate(data.expiresAt)}.` : data && !data.registered ? FAB_ENTRY_TEXT.notRegistered : undefined}
        actions={(
          <div className="flex gap-[16px]">
            <FloorButton tone="secondary" className="flex-1" onClick={() => onOpenChange(false)}>Voltar</FloorButton>
            <FloorButton tone="secondary" className="flex-1" onClick={() => void endFloorSession(go)}><LogOut aria-hidden="true" className="size-6" />Sair</FloorButton>
          </div>
        )}>
        {device.isPending ? <p>Carregando…</p> : device.isError ? <p role="alert">Não foi possível ver o aparelho. Tente de novo.</p> : data && !data.registered ? (
          <RegisterForm context={data} onRegistered={refresh} />
        ) : data ? (
          <div className="space-y-5">
            <fieldset className="space-y-2">
              <legend className="font-semibold">Tema do app neste tablet</legend>
              <div className="flex flex-wrap gap-[8px]">
                {DEVICE_THEMES.map((t) => (
                  <FloorButton key={t.value} tone={data.theme === t.value ? 'primary' : 'secondary'} aria-pressed={data.theme === t.value} onClick={() => void theme(t.value)}>{t.label}</FloorButton>
                ))}
              </div>
              <p className="text-base text-muted-foreground">Vale para todos os operadores deste tablet.</p>
            </fieldset>
            {data.hasMyPin ? (
              <FloorButton tone="secondary" className="w-full" onClick={() => setErasing(true)}><KeyRound aria-hidden="true" className="size-6" />Apagar meu PIN deste aparelho</FloorButton>
            ) : data.canCreatePin ? (
              <FloorButton className="w-full" onClick={() => setCreating(true)}><KeyRound aria-hidden="true" className="size-6" />Criar PIN para este aparelho</FloorButton>
            ) : data.pinUnavailableReason ? <p className="rounded-md border border-border bg-muted p-4 text-base">{data.pinUnavailableReason}</p> : null}
          </div>
        ) : null}
      </FloorSheet>
      <CreatePinSheet open={creating} onOpenChange={setCreating} onCreated={(next) => { refresh(next); onOpenChange(false) }} />
      <FloorConfirm open={erasing} onOpenChange={setErasing} title="Apagar meu PIN deste aparelho?" verb="Apagar meu PIN" tone="danger"
        impact={<p>O seu nome sai da lista deste tablet{pinSession ? ' e você sai agora' : ''}. Para voltar a usar PIN aqui, entre com e-mail e senha e crie outro.</p>}
        onConfirm={async () => {
          await deleteMyShopfloorPin()
          notifySuccess('PIN apagado')
          if (pinSession) {
            await endFloorSession(go)
          } else {
            await client.invalidateQueries({ queryKey: getGetShopfloorDeviceQueryKey() })
          }
        }} />
    </>
  )
}

/** "Registrar este aparelho" (CA 4): nome até 40 letras; só com login completo de quem tem production.edit + shopfloor.create. */
function RegisterForm({ context, onRegistered }: { context: ShopfloorDeviceContext; onRegistered: (next: ShopfloorDeviceContext) => void }) {
  const [name, setName] = useState('Tablet ')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  if (!context.canRegister) {
    return <p className="rounded-md border border-border bg-muted p-4 text-base">{context.registerUnavailableReason ?? 'Este aparelho não pode ser registrado por você.'}</p>
  }

  const submit = async () => {
    setBusy(true)
    setError(null)
    try {
      const next = await registerShopfloorDevice({ name: name.trim() })
      notifySuccess('Aparelho registrado')
      onRegistered(next)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível registrar. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); void submit() }}>
      <label htmlFor="aparelho-nome" className="block font-semibold">Nome do aparelho</label>
      <input id="aparelho-nome" value={name} maxLength={40} onChange={(e) => setName(e.target.value)} placeholder="Tablet Linha 1"
        className="block min-h-[56px] w-full rounded-md border-2 border-input bg-card px-4 text-[18px]" aria-describedby="aparelho-ajuda" />
      <p id="aparelho-ajuda" className="text-base text-muted-foreground">Até 40 letras. O nome aparece na entrada do tablet e em Configurações → Segurança. O registro vale 180 dias.</p>
      {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 font-semibold text-crit-ink">{error}</p> : null}
      <FloorButton type="submit" className="w-full" disabled={busy || name.trim().length === 0}>{busy ? 'Registrando…' : 'Registrar este aparelho'}</FloorButton>
    </form>
  )
}

/** "Criar PIN" (CA 12): o PIN duas vezes no teclado grande; depois, a entrada do tablet pelo nome. */
export function CreatePinSheet({ open, onOpenChange, onCreated }: { open: boolean; onOpenChange: (open: boolean) => void; onCreated: (next: ShopfloorDeviceContext) => void }) {
  const navigate = useNavigate()
  const [first, setFirst] = useState('')
  const [second, setSecond] = useState('')
  const [step, setStep] = useState<'first' | 'second' | 'done'>('first')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const reset = () => {
    setFirst('')
    setSecond('')
    setStep('first')
    setError(null)
  }

  const submit = async () => {
    setBusy(true)
    setError(null)
    try {
      const next = await createShopfloorPin({ pin: first, confirmation: second })
      notifySuccess('PIN criado')
      onCreated(next)
      setStep('done')
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível criar o PIN. Tente de novo.')
      setFirst('')
      setSecond('')
      setStep('first')
    } finally {
      setBusy(false)
    }
  }

  const title = step === 'done' ? 'PIN criado' : step === 'first' ? 'Criar PIN para este aparelho' : 'Digite o PIN de novo'
  return (
    <FloorSheet open={open} onOpenChange={(o) => { if (!o) reset(); onOpenChange(o) }} title={title}
      description={step === 'done' ? 'Na próxima vez, toque no seu nome na entrada do tablet e digite o PIN.' : step === 'first' ? 'Escolha 6 números fáceis para você e difíceis para os outros.' : 'Para conferir, digite o mesmo PIN.'}
      actions={step === 'done' ? (
        <FloorButton className="w-full" onClick={() => { reset(); onOpenChange(false); void endFloorSession((to) => void navigate({ to })) }}>Sair e entrar pelo PIN</FloorButton>
      ) : (
        <div className="flex gap-[16px]">
          <FloorButton tone="secondary" className="flex-1" onClick={() => (step === 'second' ? (setSecond(''), setStep('first')) : (reset(), onOpenChange(false)))} disabled={busy}>Voltar</FloorButton>
          {step === 'first' ? (
            <FloorButton className="flex-1" disabled={!isPinComplete(first)} onClick={() => setStep('second')}>Continuar</FloorButton>
          ) : (
            <FloorButton className="flex-1" disabled={busy || !isPinComplete(second)} onClick={() => void submit()}>{busy ? 'Criando…' : 'Criar PIN'}</FloorButton>
          )}
        </div>
      )}>
      {step === 'done' ? null : (
        <>
          <p className="flex items-center gap-1 text-base text-muted-foreground">Evite números repetidos, sequências e a sua data de nascimento.<FloorDica nome="PIN" texto={FAB_ENTRY_TEXT.pinDica} /></p>
          {step === 'first'
            ? <NumericKeypad label="Novo PIN" mask max={PIN_LENGTH} value={first} onChange={(v) => { setFirst(v); setError(null) }} />
            : <NumericKeypad label="Confirme o PIN" mask max={PIN_LENGTH} value={second} onChange={setSecond} />}
          {error ? <p role="alert" className="rounded-md border border-crit-soft bg-crit-soft p-4 text-[18px] font-semibold text-crit-ink">{error}</p> : null}
        </>
      )}
    </FloorSheet>
  )
}