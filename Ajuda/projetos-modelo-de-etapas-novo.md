---
titulo: Novo modelo de etapas
rota: /app/projetos/modelos/novo
aba:
padrao: true
modulo: projects
recurso: projects.service_execution
---

## Para que serve
Criar um modelo de etapas para um tipo de serviço.

## Como usar
- O modelo já vem com uma etapa de **Execução**, a **Entrega** e o **Aceite do cliente**, e com os marcos 50% na assinatura e 50% no aceite. Ajuste ao seu jeito.
- **Nome** é obrigatório e não pode repetir entre os modelos ativos. **Segmento** em branco vale para toda a empresa.
- Em cada etapa: nome, **letra** (sugerida pela inicial; é o código da etapa no indicador do projeto), **papel**, **horas por unidade**, **cargo sugerido**, **Exige aprovação** e o **checklist** (até 30 itens).
- Regras do papel: ao menos uma etapa de Execução; no máximo uma Entrega e um Aceite; o Aceite vem depois da Entrega e é a última etapa; depois da Entrega só pode vir o Aceite.
- **Marcos sugeridos**: a soma tem de dar 100%. O gatilho **Ao concluir a etapa** só aceita letras de etapas de Execução; a Entrega e o Aceite têm gatilhos próprios.
- No celular, cada etapa vira um cartão. Use **Mover para cima** e **Mover para baixo** para mudar a ordem.
- **Salvar modelo** grava e abre o modelo com os próximos passos.

## Perguntas frequentes
**Preciso de etapa de Aceite?** Não. Sem ela, o projeto termina na Entrega. Com ela, o Aceite do cliente vem por último.
