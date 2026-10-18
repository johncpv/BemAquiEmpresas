import { useState } from 'react'

/** Códigos de recuperação mostrados uma única vez (FND-05 CA 2): copiar, baixar e concluir. */
export function RecoveryCodes({ codes, onDone }: { codes: readonly string[]; onDone: () => void }) {
  const [copied, setCopied] = useState(false)
  const text = codes.join('\n')
  const download = () => {
    const url = URL.createObjectURL(new Blob([`Códigos de recuperação do BemAqui Empresas\nCada um vale uma vez.\n\n${text}\n`], { type: 'text/plain' }))
    const a = document.createElement('a')
    a.href = url
    a.download = 'codigos-de-recuperacao.txt'
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">Guarde estes códigos fora do computador. Cada um entra uma vez, se você perder o celular. Eles não aparecem de novo.</p>
      <ol aria-label="Códigos de recuperação" className="grid grid-cols-2 gap-2 rounded-md bg-muted p-3 font-mono text-sm tabular-nums">
        {codes.map((c) => <li key={c}>{c}</li>)}
      </ol>
      <div className="flex gap-2">
        <button type="button" onClick={async () => { await navigator.clipboard?.writeText(text); setCopied(true) }} className="flex-1 rounded-md border border-border px-3 py-2 text-sm font-semibold">
          {copied ? 'Copiados' : 'Copiar'}
        </button>
        <button type="button" onClick={download} className="flex-1 rounded-md border border-border px-3 py-2 text-sm font-semibold">Baixar</button>
      </div>
      <button type="button" onClick={onDone} className="w-full rounded-md bg-primary px-3 py-2.5 text-sm font-semibold text-primary-foreground">Concluir</button>
    </div>
  )
}
