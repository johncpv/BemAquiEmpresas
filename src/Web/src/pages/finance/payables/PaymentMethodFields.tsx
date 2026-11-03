import { useListBanks } from '@/api/generated/bemaqui'
import { Field, FormGrid } from '@/components/form/form'
import { inputClass } from '@/components/form/form-utils'
import { DocumentInput } from '@/components/form/inputs'
import { type MethodDraft, PAYABLE_DICAS } from './payables'

interface Props { value: MethodDraft; onChange: (m: MethodDraft) => void; errors: Partial<Record<string, string>>; disabled?: boolean }

const PIX_TYPES = [
  { value: 'Cnpj', label: 'CNPJ' }, { value: 'Cpf', label: 'CPF' }, { value: 'Email', label: 'E-mail' }, { value: 'Phone', label: 'Telefone' }, { value: 'Random', label: 'Chave aleatória' },
]

/**
 * Forma de pagamento do título (FIN-02 CA 6): os campos mudam com o tipo; os erros do servidor vêm em "paymentDetails.*". Traz a
 * própria FormGrid (ADR 0044), porque também vive no SidePanel e na aba Forma de pagamento: a seção que o usa fica com grid="none".
 */
export function PaymentMethodFields({ value, onChange, errors, disabled }: Props) {
  const banks = useListBanks({ query: { enabled: value.kind === 'Ted' } })
  const set = (patch: Partial<MethodDraft>) => onChange({ ...value, ...patch })
  const err = (k: string) => errors['paymentDetails.' + k]
  // Titular: Nome 8 · CPF ou CNPJ 4.
  const holder = (
    <>
      <Field label="Nome do titular" required size="xl" error={err('holderName')} dica={PAYABLE_DICAS.titular}>{(p) => <input {...p} value={value.holderName} maxLength={150} onChange={(e) => set({ holderName: e.target.value })} className={inputClass} />}</Field>
      <Field label="CPF ou CNPJ do titular" required size="md" error={err('holderDocument')}>{(p) => <DocumentInput {...p} value={value.holderDocument} onChange={(holderDocument) => set({ holderDocument })} />}</Field>
    </>
  )
  return (
    <fieldset disabled={disabled} className="min-w-0">
      <FormGrid>
        <Field label="Forma de pagamento" size="md" dica={PAYABLE_DICAS.forma}>
          {(p) => (
            <select {...p} value={value.kind} onChange={(e) => set({ kind: e.target.value as MethodDraft['kind'] })} className={inputClass}>
              <option value="">Ainda não informada</option>
              <option value="Pix">Pix</option>
              <option value="Boleto">Boleto</option>
              <option value="Ted">TED</option>
              <option value="Other">Outro</option>
            </select>
          )}
        </Field>
        {value.kind === 'Pix' ? (
          <>
            {/* Tipo de chave 4 · Chave 8 */}
            <Field label="Tipo de chave" required size="md">{(p) => <select {...p} value={value.pixKeyType} onChange={(e) => set({ pixKeyType: e.target.value })} className={inputClass}>{PIX_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}</select>}</Field>
            <Field label="Chave Pix" required size="xl" error={err('pixKey')}>{(p) => <input {...p} value={value.pixKey} maxLength={77} onChange={(e) => set({ pixKey: e.target.value })} className={inputClass} />}</Field>
            {holder}
          </>
        ) : value.kind === 'Boleto' ? (
          <Field label="Linha digitável" required size="xl" error={err('barcode')} hint="Os 47 números do boleto (48 para contas de consumo).">
            {(p) => <input {...p} inputMode="numeric" value={value.barcode} maxLength={60} onChange={(e) => set({ barcode: e.target.value })} className={inputClass + ' font-mono tabular-nums'} />}
          </Field>
        ) : value.kind === 'Ted' ? (
          <>
            {/* Banco 6 · Tipo de conta 4 / Agência 4 · Conta 4 */}
            <Field label="Banco" required size="lg" error={err('bankCode')}>
              {(p) => <select {...p} value={value.bankCode} onChange={(e) => set({ bankCode: e.target.value })} className={inputClass}><option value="">Escolha</option>{(banks.data ?? []).map((b) => <option key={b.code} value={b.code}>{b.code} — {b.name}</option>)}</select>}
            </Field>
            <Field label="Tipo de conta" required size="md" error={err('accountType')}>
              {(p) => <select {...p} value={value.accountType} onChange={(e) => set({ accountType: e.target.value })} className={inputClass}><option value="Checking">Corrente</option><option value="Savings">Poupança</option></select>}
            </Field>
            <Field label="Agência" required size="md" error={err('agency')}>{(p) => <input {...p} value={value.agency} maxLength={10} onChange={(e) => set({ agency: e.target.value })} className={inputClass + ' tabular-nums'} />}</Field>
            <Field label="Conta" required size="md" error={err('account')}>{(p) => <input {...p} value={value.account} maxLength={20} onChange={(e) => set({ account: e.target.value })} className={inputClass + ' tabular-nums'} />}</Field>
            {holder}
          </>
        ) : value.kind === 'Other' ? (
          <Field label="Como paga" required size="xl" error={err('description')} hint="Ex.: débito em conta, cartão corporativo.">{(p) => <input {...p} value={value.description} maxLength={200} onChange={(e) => set({ description: e.target.value })} className={inputClass} />}</Field>
        ) : null}
      </FormGrid>
    </fieldset>
  )
}
