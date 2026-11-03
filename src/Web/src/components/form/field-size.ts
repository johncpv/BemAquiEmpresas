/**
 * Tamanho do campo pelo conteúdo (ADR 0044, docs/ux/padrao-layout.md §C). Trilhas na grade de 6 (contêiner ≥ 448 px) e de 12
 * (contêiner ≥ 768 px); abaixo de 448 px todo campo ocupa a linha:
 * - `xs` 2 · 2: UF, número do endereço, %, parcelas, dígito;
 * - `sm` 3 · 3: CPF, CEP, data, telefone, dinheiro, quantidade, NCM, CBO, código, matrícula, PIS, RG;
 * - `md` 3 · 4: CNPJ, CPF/CNPJ, select curto, complemento, bairro, cidade, agência/conta;
 * - `lg` 6 · 6: e-mail, nome fantasia, select longo, senha (padrão);
 * - `xl` 6 · 8: nome completo, razão social, logradouro, descrição curta;
 * - `full` 6 · 12: observação, textarea, upload, editor de linhas, grupo de opções.
 */
export type FieldSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'full'

/**
 * Trilhas explícitas, só quando a linha não fecha com os tamanhos (ex.: CEP 2 · Logradouro 8 · Nº 2). `md`: grade de 6;
 * `lg`: grade de 12. A exceção fica comentada na tela que a usa.
 */
export interface FieldSpan {
  md?: 1 | 2 | 3 | 4 | 5 | 6
  lg?: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12
}

// Classes escritas por extenso: o Tailwind só gera o que encontra no código.
const MD_SPAN = { 1: '@md:col-span-1', 2: '@md:col-span-2', 3: '@md:col-span-3', 4: '@md:col-span-4', 5: '@md:col-span-5', 6: '@md:col-span-6' } as const
const LG_SPAN = {
  1: '@3xl:col-span-1', 2: '@3xl:col-span-2', 3: '@3xl:col-span-3', 4: '@3xl:col-span-4', 5: '@3xl:col-span-5', 6: '@3xl:col-span-6',
  7: '@3xl:col-span-7', 8: '@3xl:col-span-8', 9: '@3xl:col-span-9', 10: '@3xl:col-span-10', 11: '@3xl:col-span-11', 12: '@3xl:col-span-12',
} as const
const SIZE_TRACKS: Record<FieldSize, { md: keyof typeof MD_SPAN; lg: keyof typeof LG_SPAN }> = {
  xs: { md: 2, lg: 2 },
  sm: { md: 3, lg: 3 },
  md: { md: 3, lg: 4 },
  lg: { md: 6, lg: 6 },
  xl: { md: 6, lg: 8 },
  full: { md: 6, lg: 12 },
}

/** Classes de coluna de um item da FormGrid pelo tamanho (padrão `lg`) e, se houver, pelas trilhas explícitas. */
export function fieldSpanClass(size: FieldSize = 'lg', span?: FieldSpan) {
  const tracks = SIZE_TRACKS[size]
  return `col-span-full ${MD_SPAN[span?.md ?? tracks.md]} ${LG_SPAN[span?.lg ?? tracks.lg]}`
}
