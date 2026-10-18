import { forwardRef, type InputHTMLAttributes, useState } from 'react'
import { formatBrDecimal, parseBrDecimal } from '@/lib/decimal'
import { cboMask, cepMask, cnpjMask, cpfMask, documentMask, type MaskSpec, ncmMask, phoneMask } from '@/lib/masks'
import { cn } from '@/lib/utils'
import { inputClass } from './form-utils'

type BaseProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'defaultValue'>

export interface MaskedInputProps extends BaseProps {
  mask: MaskSpec
  /** Valor normalizado, sem máscara. */
  value?: string | null
  onChange?: (normalized: string) => void
}

/** Campo com máscara na digitação; entrega o valor normalizado (FND-09d CA 3 e 4). Funciona com Controller do RHF. */
export const MaskedInput = forwardRef<HTMLInputElement, MaskedInputProps>(function MaskedInput({ mask, value, onChange, className, ...rest }, ref) {
  return (
    <input
      ref={ref}
      {...rest}
      inputMode={mask.inputMode}
      maxLength={mask.maxLength}
      autoComplete="off"
      spellCheck={false}
      value={mask.format(value ?? '')}
      onChange={(e) => onChange?.(mask.normalize(e.target.value))}
      className={cn(inputClass, 'tabular-nums', className)}
    />
  )
})

type Fixed = Omit<MaskedInputProps, 'mask'>
export const DocumentInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={documentMask} {...p} />)
export const CpfInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={cpfMask} {...p} />)
export const CnpjInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={cnpjMask} {...p} />)
export const CepInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={cepMask} {...p} />)
export const PhoneInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={phoneMask} {...p} />)
export const NcmInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={ncmMask} {...p} />)
export const CboInput = forwardRef<HTMLInputElement, Fixed>((p, ref) => <MaskedInput ref={ref} mask={cboMask} {...p} />)

export interface DecimalInputProps extends BaseProps {
  /** Casas decimais: dinheiro 2, % 2 ou 4, quantidade 4. */
  scale: number
  /** String normalizada ("1234.56") ou null. */
  value?: string | null
  onChange?: (normalized: string | null) => void
  prefix?: string
  suffix?: string
}

/**
 * Número no padrão brasileiro (FND-09d CA 5): aceita "1.234,56", "1234,5" e colado "1234.5"; formata ao sair do campo;
 * entrega string normalizada, nunca number com perda.
 */
export const DecimalInput = forwardRef<HTMLInputElement, DecimalInputProps>(function DecimalInput({ scale, value, onChange, prefix, suffix, className, onBlur, onFocus, ...rest }, ref) {
  // Enquanto o campo tem foco, mostra o que foi digitado; fora dele, o valor formatado.
  const [text, setText] = useState<string | null>(null)
  const shown = text ?? formatBrDecimal(value, scale)

  return (
    <span className={cn('flex items-center rounded-md border border-input bg-card focus-within:outline-2 focus-within:outline-ring has-[[aria-invalid=true]]:border-crit', className)}>
      {prefix ? <span aria-hidden="true" className="pl-3 text-sm text-muted-foreground">{prefix}</span> : null}
      <input
        ref={ref}
        {...rest}
        inputMode="decimal"
        autoComplete="off"
        value={shown}
        onFocus={(e) => {
          setText(formatBrDecimal(value, scale))
          onFocus?.(e)
        }}
        onChange={(e) => {
          setText(e.target.value)
          onChange?.(parseBrDecimal(e.target.value, scale))
        }}
        onBlur={(e) => {
          setText(null)
          onBlur?.(e)
        }}
        className="w-full bg-transparent px-3 py-2 text-right text-sm tabular-nums outline-none"
      />
      {suffix ? <span aria-hidden="true" className="pr-3 text-sm text-muted-foreground">{suffix}</span> : null}
    </span>
  )
})

export const MoneyInput = forwardRef<HTMLInputElement, Omit<DecimalInputProps, 'scale' | 'prefix'>>((p, ref) => <DecimalInput ref={ref} scale={2} prefix="R$" {...p} />)
export const PercentInput = forwardRef<HTMLInputElement, Omit<DecimalInputProps, 'suffix' | 'scale'> & { scale?: number }>(({ scale = 2, ...p }, ref) => <DecimalInput ref={ref} scale={scale} suffix="%" {...p} />)
export const QuantityInput = forwardRef<HTMLInputElement, Omit<DecimalInputProps, 'scale'>>((p, ref) => <DecimalInput ref={ref} scale={4} {...p} />)

const isoFromBr = (digits: string) => {
  if (digits.length !== 8) return ''
  const [d, m, y] = [Number(digits.slice(0, 2)), Number(digits.slice(2, 4)), Number(digits.slice(4))]
  const date = new Date(Date.UTC(y, m - 1, d))
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d ? `${digits.slice(4)}-${digits.slice(2, 4)}-${digits.slice(0, 2)}` : ''
}

export interface DateInputProps extends BaseProps {
  /** "aaaa-mm-dd" (date, sem fuso) ou "". */
  value?: string | null
  onChange?: (iso: string) => void
}

/** Data de negócio (FND-09d CA 13): digita dd/mm/aaaa, entrega aaaa-mm-dd; data impossível entrega "". */
export const DateInput = forwardRef<HTMLInputElement, DateInputProps>(function DateInput({ value, onChange, className, ...rest }, ref) {
  const fromValue = (v: string | null | undefined) => (v ? v.slice(8, 10) + v.slice(5, 7) + v.slice(0, 4) : '')
  const [digits, setDigits] = useState(() => fromValue(value))
  // Valor trocado de fora (ex.: reset do formulário): ajusta durante o render, sem efeito.
  const [last, setLast] = useState(value)
  if (value !== last) {
    setLast(value)
    if (value) setDigits(fromValue(value))
  }
  const shown = digits.replace(/^(\d{2})(\d)/, '$1/$2').replace(/^(\d{2})\/(\d{2})(\d)/, '$1/$2/$3')
  return (
    <input
      ref={ref}
      {...rest}
      inputMode="numeric"
      placeholder="dd/mm/aaaa"
      maxLength={10}
      autoComplete="off"
      value={shown}
      onChange={(e) => {
        const next = e.target.value.replace(/\D/g, '').slice(0, 8)
        setDigits(next)
        onChange?.(isoFromBr(next))
      }}
      className={cn(inputClass, 'tabular-nums', className)}
    />
  )
})

export interface TimeInputProps extends BaseProps {
  /** "hh:mm" ou "". */
  value?: string | null
  onChange?: (time: string) => void
}

export const TimeInput = forwardRef<HTMLInputElement, TimeInputProps>(function TimeInput({ value, onChange, className, ...rest }, ref) {
  const [digits, setDigits] = useState((value ?? '').replace(':', ''))
  const shown = digits.replace(/^(\d{2})(\d)/, '$1:$2')
  return (
    <input
      ref={ref}
      {...rest}
      inputMode="numeric"
      placeholder="hh:mm"
      maxLength={5}
      value={shown}
      onChange={(e) => {
        const next = e.target.value.replace(/\D/g, '').slice(0, 4)
        setDigits(next)
        const ok = next.length === 4 && Number(next.slice(0, 2)) < 24 && Number(next.slice(2)) < 60
        onChange?.(ok ? `${next.slice(0, 2)}:${next.slice(2)}` : '')
      }}
      className={cn(inputClass, 'tabular-nums', className)}
    />
  )
})
