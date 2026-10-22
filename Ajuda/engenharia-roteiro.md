---
titulo: Roteiro — etapas
rota: /app/engenharia/roteiros/$id
aba: etapas
padrao: true
modulo: engineering
---

## Para que serve
Ver e editar as etapas do roteiro: nome, letra, centro de trabalho, horas padrão, qualidade, aprovação e checklist.

## Como usar
- Edite as etapas na tabela. A 400 px cada etapa vira um cartão, com **Remover etapa** no próprio cartão.
- **Letra:** uma letra de A a Z, sem repetir no roteiro. É o código da etapa no quadro da produção.
- **Horas padrão:** horas de trabalho esperadas para uma unidade nesta etapa. O planejamento usa este número.
- **Começa sem BOM:** a primeira etapa pode começar antes de a BOM ser liberada; as seguintes, só com a BOM liberada (a partir da ENG-02).
- Ao **Salvar roteiro**, o sistema avisa: as OS já criadas não mudam, só as próximas usam o roteiro novo.
- No topo: **Duplicar**, **Inativar roteiro** (ou **Ativar roteiro**) e **Excluir roteiro**.
- A **Expedição** do exemplo é "preparar e embalar"; a baixa do estoque é feita no romaneio.

## Perguntas frequentes
**Apareceu "O roteiro mudou desde que você abriu".** Outra pessoa salvou antes. Clique em **Recarregar** e refaça a mudança.

**Por que não consigo excluir?** O roteiro já foi usado por alguma OS. Inative-o: ele some dos seletores, mas as OS continuam com a cópia delas.

**Não vejo o botão Salvar roteiro.** Seu grupo só tem a permissão de ver. Peça a permissão Editar em Engenharia ao administrador.