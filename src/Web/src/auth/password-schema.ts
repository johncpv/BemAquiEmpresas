import { z } from '@/lib/zod'

/** Mesma política do servidor (ADR 0010): 12 caracteres ou mais; o servidor ainda confere as senhas óbvias. */
export const newPasswordSchema = z
  .object({ password: z.string().min(12, 'A senha precisa ter pelo menos 12 caracteres. Uma frase fácil de lembrar funciona bem.'), confirm: z.string() })
  .refine((v) => v.password === v.confirm, { path: ['confirm'], message: 'As senhas não são iguais.' })
