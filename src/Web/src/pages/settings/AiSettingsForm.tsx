import { useEffect, useState } from 'react'
import { saveAiSettings, testAiSettings } from '@/api/generated/bemaqui'
import type { AiSettingsDto, AiSettingsRequest, AiTestResult } from '@/api/generated/model'
import { Field, FormError } from '@/components/form/form'
import { applyServerErrors, inputClass, useZodForm } from '@/components/form/form-utils'
import { StatusChip } from '@/components/ui/status-chip'
import { notifySuccess } from '@/components/ui/messages'
import { Dica } from '@/help/Dica'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'
import { z } from '@/lib/zod'

const OTHER = 'outro'
const schema = z
  .object({ provider: z.string().min(1, 'Escolha o provedor.'), model: z.string().min(1, 'Escolha o modelo.'), customModel: z.string(), apiKey: z.string() })
  .refine((v) => v.model !== OTHER || v.customModel.trim().length > 0, { path: ['customModel'], message: 'Informe o nome do modelo.' })
type Values = z.input<typeof schema>

const primary = 'rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground disabled:opacity-60'
const secondary = 'rounded-md border border-border px-3 py-2 text-sm font-semibold disabled:opacity-60'

/** Formulário do provedor de IA: "Testar conexão" não salva; depois de "Conexão OK", "Salvar configuração" ganha destaque. */
export function AiSettingsForm({ settings, onDone, onCancel }: { settings: AiSettingsDto; onDone: () => Promise<void>; onCancel: () => void }) {
  const initialProvider = settings.provider ?? settings.providers[0]?.code ?? ''
  const suggestions = (code: string) => settings.providers.find((p) => p.code === code)?.models ?? []
  const savedModel = settings.model ?? ''
  const form = useZodForm(schema, {
    defaultValues: {
      provider: initialProvider,
      model: savedModel ? (suggestions(initialProvider).includes(savedModel) ? savedModel : OTHER) : suggestions(initialProvider)[0] ?? OTHER,
      customModel: savedModel && !suggestions(initialProvider).includes(savedModel) ? savedModel : '',
      apiKey: '',
    },
  })
  const [replaceKey, setReplaceKey] = useState(!settings.configured)
  const [test, setTest] = useState<AiTestResult | null>(null)
  const [testing, setTesting] = useState(false)
  const provider = form.watch('provider')
  const model = form.watch('model')
  const keepsKey = settings.configured && settings.provider === provider && !replaceKey

  useEffect(() => {
    const sub = form.watch(() => setTest(null))
    return () => sub.unsubscribe()
  }, [form])

  const request = (v: Values): AiSettingsRequest | null => {
    if (!keepsKey && !v.apiKey.trim()) {
      form.setError('apiKey', { type: 'manual', message: settings.configured ? 'Informe a chave de API do novo provedor.' : 'Informe a chave de API.' }, { shouldFocus: true })
      return null
    }
    return { provider: v.provider, model: (v.model === OTHER ? v.customModel : v.model).trim(), apiKey: keepsKey ? null : v.apiKey.trim() }
  }

  const runTest = async () => {
    if (!(await form.trigger())) return
    const body = request(form.getValues())
    if (!body) return
    setTesting(true)
    try {
      const result = await testAiSettings(body)
      setTest(result)
    } catch (e) {
      applyServerErrors(form, e)
    } finally {
      setTesting(false)
    }
  }

  const submit = form.handleSubmit(async (v) => {
    const body = request(v)
    if (!body) return
    try {
      await saveAiSettings(body)
      await onDone()
      notifySuccess('Configuração salva')
    } catch (e) {
      applyServerErrors(form, e)
    }
  })

  return (
    <form onSubmit={submit} noValidate aria-label="Provedor de IA" className="max-w-xl space-y-4 rounded-md border border-border bg-card p-5">
      <FormError message={form.formState.errors.root?.server?.message} />
      <fieldset className="space-y-1">
        <legend className="text-sm font-medium">Provedor</legend>
        <div className="flex flex-wrap gap-2">
          {settings.providers.map((p) => (
            <label key={p.code} className="flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
              <input type="radio" value={p.code} {...form.register('provider', { onChange: () => form.setValue('model', suggestions(form.getValues('provider'))[0] ?? OTHER) })} className="accent-primary" />
              {p.name}
            </label>
          ))}
        </div>
      </fieldset>
      <Field label="Modelo" error={form.formState.errors.model?.message} hint="Os modelos mais leves respondem mais rápido e custam menos.">
        {(p) => (
          <select {...p} {...form.register('model')} className={inputClass}>
            {suggestions(provider).map((m) => <option key={m} value={m}>{m}</option>)}
            <option value={OTHER}>Outro (digitar o nome)</option>
          </select>
        )}
      </Field>
      {model === OTHER ? (
        <Field label="Nome do modelo" error={form.formState.errors.customModel?.message}>{(p) => <input {...p} {...form.register('customModel')} autoComplete="off" spellCheck={false} className={inputClass} />}</Field>
      ) : null}
      {keepsKey ? (
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <span className="tabular-nums">Chave configurada em {settings.keySetAt ? formatDateTime(settings.keySetAt) : '—'}</span>
          <button type="button" onClick={() => setReplaceKey(true)} className={secondary}>Substituir</button>
        </div>
      ) : (
        <Field label="Chave de API" error={form.formState.errors.apiKey?.message} hint="A chave é guardada cifrada e nunca é mostrada de novo.">
          {(p) => <input {...p} type="password" autoComplete="new-password" spellCheck={false} {...form.register('apiKey')} className={inputClass} />}
        </Field>
      )}
      <p className="text-xs text-muted-foreground">
        Só vão para o provedor a pergunta e os trechos de ajuda.<Dica texto="Nome da empresa, clientes, valores e usuários nunca entram na chamada. Se a pergunta tiver um dado assim, o sistema avisa antes de enviar." />
      </p>
      <div aria-live="polite">
        {test ? (test.ok ? <StatusChip tone="ok">{test.message}</StatusChip> : <p role="alert" className="text-sm text-crit-ink">{test.message}</p>) : null}
      </div>
      <div className="flex flex-wrap justify-end gap-2 pt-1">
        <button type="button" onClick={onCancel} className={secondary}>Voltar</button>
        <button type="button" onClick={() => void runTest()} disabled={testing} className={test?.ok ? secondary : primary}>{testing ? 'Testando…' : 'Testar conexão'}</button>
        <button type="submit" disabled={form.formState.isSubmitting} className={cn(test?.ok ? primary : secondary)}>Salvar configuração</button>
      </div>
    </form>
  )
}
