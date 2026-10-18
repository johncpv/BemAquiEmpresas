import { describe, expect, it } from 'vitest'
import type { HelpDocument } from '@/api/generated/model'
import { suggestedQuestions } from './resolve'

const doc = (faq: string): HelpDocument => ({ key: 'x', title: 'X', route: '/app', tab: null, isDefault: true, module: null, sections: [{ title: 'Perguntas frequentes', body: faq }] })

describe('perguntas sugeridas (FND-15, propositivo)', () => {
  it('vêm das perguntas frequentes da tela, até 3', () => {
    expect(suggestedQuestions(doc('**A?** Sim.\n\n**B?** Não.\n\n**C?** Talvez.\n\n**D?** Nunca.'))).toEqual(['A?', 'B?', 'C?'])
  })

  it('ignoram negrito que não é pergunta e tela sem ajuda', () => {
    expect(suggestedQuestions(doc('Clique em **Salvar**. **Funciona offline?** Não.'))).toEqual(['Funciona offline?'])
    expect(suggestedQuestions(undefined)).toEqual([])
  })
})
