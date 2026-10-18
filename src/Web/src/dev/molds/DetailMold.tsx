import { useSearch } from '@tanstack/react-router'
import { useState } from 'react'
import { ApiError } from '@/api/http'
import { Field } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { RowsEditor } from '@/components/form/rows-editor'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { NextSteps } from '@/components/ui/next-steps'
import { SidePanel } from '@/components/ui/side-panel'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { toastDone } from '@/components/ui/toast'
import { useUrlTab } from '@/components/ui/url-tab'
import { UrlTabs } from '@/components/ui/url-tabs'
import { PageHeading } from '@/shell/PageHeading'
import { MoldNav } from './MoldNav'

interface Contact { name: string; email: string }
const isEmail = (v: string) => v === '' || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v)
const focusFirstInvalid = (tab: string) => setTimeout(() => document.querySelector<HTMLElement>(`#painel-${tab} [aria-invalid="true"]`)?.focus(), 0)

/**
 * Molde 2 — detalhe com abas (FND-09d CA 2, 10, 16 e 17): cabeçalho com nome, chips e ações; abas ligadas à URL e à
 * ajuda; contador de erros por aba e foco no primeiro erro; "Histórico de alterações" sempre por último.
 */
export default function DetailMold() {
  const search = useSearch({ strict: false }) as Record<string, unknown>
  const [steps, setSteps] = useState(search.novo === '1')
  const [, setTab] = useUrlTab('dados')
  const [name, setName] = useState('WSP Equipamentos por Elevação Ltda')
  const [email, setEmail] = useState('financeiro@konebras.com.br')
  const [contacts, setContacts] = useState<Contact[]>([{ name: 'Ana Souza', email: 'ana@konebras.com.br' }])
  const [tried, setTried] = useState(false)
  const [address, setAddress] = useState(false)
  const [active, setActive] = useState(true)

  const dataErrors = (name.trim().length < 2 ? 1 : 0) + (isEmail(email) ? 0 : 1)
  const contactErrors = contacts.filter((c) => !c.name.trim() || !isEmail(c.email)).length
  const save = () => {
    setTried(true)
    const tab = dataErrors > 0 ? 'dados' : contactErrors > 0 ? 'contatos' : null
    if (tab) {
      setTab(tab)
      focusFirstInvalid(tab)
      return
    }
    toastDone('Terceiro salvo')
  }

  const header = (
    <PageHeading title={name || 'Terceiro'}>
      <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-1.5">
          <StatusChip tone={active ? 'ok' : 'neutral'}>{active ? 'Ativo' : 'Inativo'}</StatusChip>
          <Tag>Cliente</Tag>
          <Tag>Fornecedor</Tag>
          <Tag title="Ref. no Dolibarr: CLI0042">Importado do Dolibarr</Tag>
          <span className="text-sm text-muted-foreground tabular-nums">47.225.005/0001-86</span>
        </div>
        <div className="flex flex-wrap gap-2">
          <ConfirmDialog
            verb="Excluir"
            done="Terceiro excluído"
            destructive
            title="Excluir este terceiro?"
            description="Ele some das listas e dos seletores. Dá para restaurar depois."
            onConfirm={async () => { await new Promise((r) => setTimeout(r, 300)); throw new ApiError(409, { detail: 'Este terceiro tem orçamentos e não pode ser excluído.', code: 'in_use' }) }}
            recovery={{ verb: 'Inativar', done: 'Terceiro inativado', when: (code) => code === 'in_use', onSelect: async () => setActive(false) }}
            trigger={<button type="button" className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Excluir</button>}
          />
          <button type="button" onClick={save} className="rounded-md bg-primary px-3 py-2 text-sm font-semibold text-primary-foreground">Salvar terceiro</button>
        </div>
      </div>
    </PageHeading>
  )

  return (
    <>
      <MoldNav current="detalhe" />
      {header}
      {steps ? (
        <div className="mb-4">
          <NextSteps onDismiss={() => setSteps(false)} steps={[
            { label: 'Adicionar endereço de obra', onSelect: () => { setTab('enderecos'); setAddress(true) } },
            { label: 'Adicionar contato', onSelect: () => setTab('contatos') },
            { label: 'Criar orçamento', onSelect: () => toastDone('Abriria o orçamento com este cliente (COM-01)') },
          ]} />
        </div>
      ) : null}

      <UrlTabs label="Seções do terceiro" fallback="dados" help={false} tabs={[
        {
          id: 'dados', label: 'Dados', keepMounted: true, errors: tried ? dataErrors : 0, content: (
            <div className="grid max-w-3xl gap-3 sm:grid-cols-2">
              <Field label="Razão social" required error={tried && name.trim().length < 2 ? 'Informe o nome ou a razão social.' : undefined}>{(p) => <input {...p} value={name} onChange={(e) => setName(e.target.value)} className={inputClass} />}</Field>
              <Field label="E-mail" error={tried && !isEmail(email) ? 'Informe um e-mail válido.' : undefined}>{(p) => <input {...p} value={email} onChange={(e) => setEmail(e.target.value)} className={inputClass} />}</Field>
              <p className="text-xs text-muted-foreground sm:col-span-2">Para ver o contador de erros por aba: apague a razão social, ou erre um e-mail na aba Contatos, e clique em "Salvar terceiro".</p>
            </div>
          ),
        },
        {
          id: 'enderecos', label: 'Endereços', content: (
            <div className="max-w-3xl space-y-2">
              <div className="rounded-md border border-border bg-card p-3 text-sm"><Tag>Cobrança · padrão</Tag><p className="mt-1">Rua das Indústrias, 120 — Distrito Industrial, Campinas/SP — 13069-000</p></div>
              <button type="button" onClick={() => setAddress(true)} className="rounded-md border border-border px-3 py-2 text-sm font-semibold">Adicionar endereço</button>
            </div>
          ),
        },
        {
          id: 'contatos', label: 'Contatos', keepMounted: true, errors: tried ? contactErrors : 0, content: (
            <div className="max-w-3xl">
              <RowsEditor noun="contato" rows={contacts} onChange={setContacts} newRow={() => ({ name: '', email: '' })} headers={['Nome', 'E-mail']} summary={`${contacts.length} ${contacts.length === 1 ? 'contato' : 'contatos'}`}
                renderRow={(row, i, update) => [
                  <input key="n" aria-label={`Nome do contato ${i + 1}`} aria-invalid={tried && !row.name.trim()} value={row.name} onChange={(e) => update({ name: e.target.value })} className={inputClass} />,
                  <input key="e" aria-label={`E-mail do contato ${i + 1}`} aria-invalid={tried && !isEmail(row.email)} value={row.email} onChange={(e) => update({ email: e.target.value })} className={inputClass} />,
                ]} />
            </div>
          ),
        },
        {
          id: 'historico', label: 'Histórico de alterações', content: (
            <ol className="max-w-3xl space-y-2 text-sm">
              <li className="rounded-md border border-border bg-card p-3"><b className="tabular-nums">18/10/2026 14:02</b> · John · Papéis: Cliente → Cliente, Fornecedor</li>
              <li className="rounded-md border border-border bg-card p-3"><b className="tabular-nums">18/10/2026 13:40</b> · Importação Dolibarr · Terceiro criado (ref. CLI0042)</li>
            </ol>
          ),
        },
      ]} />

      <SidePanel open={address} onOpenChange={setAddress} title="Novo endereço" description="Endereço de obra ou de entrega.">
        <p className="text-sm text-muted-foreground">O formulário do painel segue o molde 3.</p>
      </SidePanel>
    </>
  )
}
