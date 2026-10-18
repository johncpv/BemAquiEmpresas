import { z } from 'zod'
import { pt } from 'zod/v4/locales'

/** Mensagens de validação em PT-BR (CLAUDE.md), centralizadas para todos os formulários (FND-09c). */
z.config(pt())

export { z }
