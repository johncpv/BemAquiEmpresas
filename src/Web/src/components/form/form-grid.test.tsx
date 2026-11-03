import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { fieldSpanClass } from './field-size'
import { Field, FormActions, FormGrid, FormSection } from './form'

const box = (label: string) => screen.getByLabelText(label).closest('div.space-y-1')!

describe('grade do formulário (ADR 0044)', () => {
  it('tamanho do campo vira trilhas: 1 coluna, 6 a partir de 448 px e 12 a partir de 768 px', () => {
    expect(fieldSpanClass('xs')).toBe('col-span-full @md:col-span-2 @3xl:col-span-2')
    expect(fieldSpanClass('sm')).toBe('col-span-full @md:col-span-3 @3xl:col-span-3')
    expect(fieldSpanClass('md')).toBe('col-span-full @md:col-span-3 @3xl:col-span-4')
    expect(fieldSpanClass('lg')).toBe('col-span-full @md:col-span-6 @3xl:col-span-6')
    expect(fieldSpanClass('xl')).toBe('col-span-full @md:col-span-6 @3xl:col-span-8')
    expect(fieldSpanClass('full')).toBe('col-span-full @md:col-span-6 @3xl:col-span-12')
    expect(fieldSpanClass()).toBe(fieldSpanClass('lg'))
    expect(fieldSpanClass('xl', { lg: 7 })).toBe('col-span-full @md:col-span-6 @3xl:col-span-7')
    expect(fieldSpanClass('sm', { md: 2, lg: 2 })).toBe('col-span-full @md:col-span-2 @3xl:col-span-2')
  })

  it('FormGrid consulta o contêiner e o Field dentro dela ganha a coluna do tamanho', () => {
    const { container } = render(
      <FormGrid>
        <Field label="CPF" size="sm">{(p) => <input {...p} />}</Field>
        <Field label="Nome">{(p) => <input {...p} />}</Field>
        <Field label="UF" size="xs">{(p) => <input {...p} />}</Field>
      </FormGrid>,
    )
    expect(container.firstElementChild!.className).toBe('@container')
    expect(container.querySelector('.grid')!.className).toContain('grid-cols-1 gap-4 @md:grid-cols-6 @3xl:grid-cols-12')
    expect(box('CPF').className).toContain('@3xl:col-span-3')
    expect(box('Nome').className).toContain('@3xl:col-span-6')
    expect(box('UF').className).toContain('@md:col-span-2')
  })

  it('FormSection sem grid mantém a grade antiga de 2 colunas e o Field sem classe de coluna (telas ainda não migradas)', () => {
    render(
      <FormSection title="Antiga">
        <Field label="Campo" size="sm">{(p) => <input {...p} />}</Field>
      </FormSection>,
    )
    const fieldset = screen.getByRole('group', { name: 'Antiga' })
    expect(fieldset.getAttribute('data-grid')).toBe('legacy')
    expect(fieldset.querySelector('.grid')!.className).toContain('sm:grid-cols-2')
    expect(box('Campo').className).toBe('space-y-1')
  })

  it('FormSection grid="form" usa a FormGrid; uma seção antiga dentro dela não herda as colunas', () => {
    render(
      <FormSection title="Nova" description="Descrição curta." grid="form">
        <Field label="CNPJ" size="md">{(p) => <input {...p} />}</Field>
      </FormSection>,
    )
    expect(screen.getByRole('group', { name: 'Nova' }).querySelector('.\\@container')).not.toBeNull()
    expect(box('CNPJ').className).toContain('@3xl:col-span-4')
    expect(screen.getByText('Descrição curta.').className).toContain('max-w-prose')

    render(
      <FormGrid>
        <FormSection title="Dentro">
          <Field label="Interno" size="xl">{(p) => <input {...p} />}</Field>
        </FormSection>
      </FormGrid>,
    )
    expect(box('Interno').className).toBe('space-y-1')
  })

  it('FormActions: no fim à direita no curto; presa acima do dock e com "Alterações não salvas" no longo', () => {
    const { rerender } = render(<FormActions><button type="submit">Salvar modelo</button></FormActions>)
    let bar = screen.getByRole('button', { name: 'Salvar modelo' }).parentElement!
    expect(bar.getAttribute('data-form-actions')).toBe('end')
    expect(bar.className).toContain('justify-end')

    rerender(<FormActions sticky dirty={false}><button type="submit">Salvar dados pessoais</button></FormActions>)
    bar = screen.getByRole('button', { name: 'Salvar dados pessoais' }).closest('[data-form-actions]')!
    expect(bar.getAttribute('data-form-actions')).toBe('sticky')
    expect(bar.className).toContain('sticky')
    expect(bar.className).toContain('bottom-[calc(var(--dock-offset)+0.5rem)]')
    expect(screen.queryByText('Alterações não salvas')).toBeNull()

    rerender(<FormActions sticky dirty><button type="submit">Salvar dados pessoais</button></FormActions>)
    expect(screen.getByText('Alterações não salvas').getAttribute('aria-live')).toBe('polite')
  })
})
