import { useState } from 'react'
import { ChipGroup, ToggleGroup } from '@/components/form/choice'
import { Field } from '@/components/form/form'
import { CboInput, CepInput, DateInput, DocumentInput, MoneyInput, NcmInput, PercentInput, PhoneInput, QuantityInput, TimeInput } from '@/components/form/inputs'
import { RowsEditor } from '@/components/form/rows-editor'
import { StatusChip } from '@/components/ui/status-chip'
import { Tag } from '@/components/ui/tag'
import { TreeTable } from '@/components/ui/tree-table'
import { compareDecimal, formatBrDecimal, sumDecimal } from '@/lib/decimal'
import { documentKind, isValidDocument } from '@/lib/masks'
import { PageHeading } from '@/shell/PageHeading'
import { MoldNav } from './MoldNav'
import { ACCOUNTS, ROLES } from './sample'

interface Installment { days: string; percent: string | null }

/** Componentes da FND-09d nos estados vazio, preenchido, erro e aviso (galeria de aprovação). */
export default function ComponentsMold() {
  const [doc, setDoc] = useState('12ABC34501DE35')
  const [cep, setCep] = useState('13069000')
  const [phone, setPhone] = useState('11987654321')
  const [ncm, setNcm] = useState('84281000')
  const [cbo, setCbo] = useState('')
  const [money, setMoney] = useState<string | null>('1234.56')
  const [pct, setPct] = useState<string | null>('250')
  const [qty, setQty] = useState<string | null>('15.7')
  const [date, setDate] = useState('2026-10-18')
  const [time, setTime] = useState('08:00')
  const [mode, setMode] = useState<'markup' | 'margem'>('markup')
  const [roles, setRoles] = useState<string[]>([])
  const [rows, setRows] = useState<Installment[]>([{ days: '30', percent: '33.3333' }, { days: '60', percent: '33.3333' }, { days: '90', percent: '33.3334' }])
  const total = sumDecimal(rows.map((r) => r.percent), 4)
  const exact = compareDecimal(total, '100') === 0

  return (
    <>
      <MoldNav current="componentes" />
      <PageHeading title="Componentes de formulário">
        <p className="mt-2 text-muted-foreground">Cada campo mostra o valor que vai para a API (normalizado, sem máscara).</p>
      </PageHeading>
      <div className="grid max-w-5xl gap-6">
        <section aria-labelledby="c-mascaras" className="grid gap-3 rounded-md border border-border bg-card p-4 sm:grid-cols-2">
          <h2 id="c-mascaras" className="font-semibold sm:col-span-2">Máscaras</h2>
          <Field label="CPF ou CNPJ" hint={`Valor: ${doc || '—'} · ${documentKind(doc) ?? 'incompleto'}`} error={doc.length >= 11 && !isValidDocument(doc) ? 'Documento inválido. Confira os dígitos.' : undefined}>
            {(p) => <DocumentInput {...p} value={doc} onChange={setDoc} />}
          </Field>
          <Field label="CEP" hint={`Valor: ${cep || '—'}`}>{(p) => <CepInput {...p} value={cep} onChange={setCep} />}</Field>
          <Field label="Telefone" hint={`Valor: ${phone || '—'}`}>{(p) => <PhoneInput {...p} value={phone} onChange={setPhone} />}</Field>
          <Field label="NCM" hint={`Valor: ${ncm || '—'}`} dica="Nomenclatura Comum do Mercosul: a classificação fiscal do produto, com 8 dígitos.">{(p) => <NcmInput {...p} value={ncm} onChange={setNcm} />}</Field>
          <Field label="CBO" hint="Vazio, para ver o estado sem valor.">{(p) => <CboInput {...p} value={cbo} onChange={setCbo} />}</Field>
        </section>

        <section aria-labelledby="c-numeros" className="grid gap-3 rounded-md border border-border bg-card p-4 sm:grid-cols-3">
          <h2 id="c-numeros" className="font-semibold sm:col-span-3">Números, data e hora</h2>
          <Field label="Valor" hint={`Valor: ${money ?? '—'}`}>{(p) => <MoneyInput {...p} value={money} onChange={setMoney} />}</Field>
          <Field label="Encargos" hint={`Valor: ${pct ?? '—'}`} error={pct && compareDecimal(pct, '200') > 0 ? 'Use um valor de 0 a 200%.' : undefined}>{(p) => <PercentInput {...p} value={pct} onChange={setPct} />}</Field>
          <Field label="Quantidade" hint={`Valor: ${qty ?? '—'}`}>{(p) => <QuantityInput {...p} value={qty} onChange={setQty} />}</Field>
          <Field label="Data" hint={`Valor: ${date || 'incompleta'}`}>{(p) => <DateInput {...p} value={date} onChange={setDate} />}</Field>
          <Field label="Hora" hint={`Valor: ${time || 'incompleta'}`}>{(p) => <TimeInput {...p} value={time} onChange={setTime} />}</Field>
        </section>

        <section aria-labelledby="c-escolhas" className="space-y-3 rounded-md border border-border bg-card p-4">
          <h2 id="c-escolhas" className="font-semibold">Escolhas</h2>
          <div className="space-y-1">
            <span className="block text-sm font-medium">Modo de precificação</span>
            <ToggleGroup label="Modo de precificação" value={mode} onChange={setMode} options={[{ value: 'markup', label: 'Markup sobre o custo' }, { value: 'margem', label: 'Margem sobre o preço' }]} />
          </div>
          <ChipGroup legend="Papéis" required value={roles} onChange={setRoles} error={roles.length === 0 ? 'Escolha ao menos um papel.' : undefined} options={ROLES.map((r) => ({ ...r }))} />
        </section>

        <section aria-labelledby="c-parcelas" className="space-y-3 rounded-md border border-border bg-card p-4">
          <h2 id="c-parcelas" className="font-semibold">Editor de linhas: parcelas da condição 30/60/90</h2>
          <RowsEditor noun="parcela" rows={rows} onChange={setRows} min={1} reorder newRow={() => ({ days: '', percent: null })} headers={['Dias', 'Percentual']}
            summary={<span className={exact ? 'text-ok-ink' : 'text-warn-ink'}>Soma: {formatBrDecimal(total, 4)}%{exact ? '' : ' (precisa dar 100%)'}</span>}
            renderRow={(row, i, update) => [
              <input key="d" aria-label={`Dias da parcela ${i + 1}`} inputMode="numeric" value={row.days} onChange={(e) => update({ days: e.target.value.replace(/\D/g, '') })} className="w-full rounded-md border border-input bg-card px-3 py-2 text-sm tabular-nums" />,
              <PercentInput key="p" aria-label={`Percentual da parcela ${i + 1}`} scale={4} value={row.percent} onChange={(v) => update({ percent: v })} />,
            ]} />
        </section>

        <section aria-labelledby="c-arvore" className="space-y-3">
          <h2 id="c-arvore" className="font-semibold">Árvore (treegrid): plano de contas</h2>
          <p className="text-sm text-muted-foreground">Clique numa linha e use as setas: ↑ e ↓ movem, → expande, ← recolhe ou sobe ao pai.</p>
          <TreeTable nodes={ACCOUNTS} label="Plano de contas" renderLabel={(n) => <span><span className="mr-2 text-muted-foreground tabular-nums">{n.code}</span>{n.label}</span>} columns={[{ header: 'Tipo', cell: (n) => <Tag>{n.kind}</Tag> }]} />
        </section>

        <section aria-labelledby="c-chips" className="space-y-2 rounded-md border border-border bg-card p-4">
          <h2 id="c-chips" className="font-semibold">Situação × atributo</h2>
          <div className="flex flex-wrap gap-2">
            <StatusChip tone="ok">Ativo</StatusChip>
            <StatusChip tone="neutral">Inativo</StatusChip>
            <StatusChip tone="warn">Em risco</StatusChip>
            <StatusChip tone="crit">Atrasado</StatusChip>
            <Tag>Cliente</Tag>
            <Tag>Sem documento</Tag>
            <Tag>Importado do Dolibarr</Tag>
          </div>
        </section>
      </div>
    </>
  )
}
