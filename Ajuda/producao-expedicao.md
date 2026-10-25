---
titulo: Produção — expedição
rota: /app/producao/expedicao
aba:
padrao: true
modulo: production
---

## Para que serve
Ver o produto acabado que está pronto para sair da fábrica, montar o romaneio de cada projeto e acompanhar os romaneios até a expedição.

## Como usar
- **Prontos para expedir** lista os projetos com produto acabado no estoque: o que entrou pela conclusão das OS e ainda não foi expedido. Cada projeto mostra o **disponível** (físico menos o reservado). Abra **Itens, físico e reservado** para ver o físico, o reservado, as séries e o que já está em romaneio aberto.
- **Novo romaneio** (quem cria na produção) abre o romaneio do projeto já preenchido com as séries e as quantidades, o endereço da obra e a transportadora sugerida.
- **Romaneios** lista número, projeto, cliente, itens, data e situação: **Rascunho**, **Aguardando confirmação do estoque**, **Expedido**, **Cancelado** e **Estornado**. A tag **Estoque geral** marca o romaneio com produto acabado sem projeto.
- Quem aprova no Estoque abre a lista em **Aguardando você**: os romaneios com produto do estoque geral que outra pessoa montou e que você confirma. Use **Todos** para ver o resto.
- O romaneio não é nota fiscal: é a lista do que sai da fábrica, com as séries, para a obra.

## Perguntas frequentes
**Por que o projeto não aparece em Prontos para expedir?** Nenhuma OS do projeto foi concluída com o produto no estoque, ou tudo já está expedido. Veja as OS prontas para concluir.

**Expedir baixa o estoque?** Sim. A confirmação do romaneio tira o produto acabado do estoque na hora e as séries passam a **Expedido**. A nota fiscal de saída, na Fase 5, só vai referenciar o romaneio e não baixa de novo.

**A expedição entra no custo do projeto?** Não. O custo do equipamento já entrou pelo material baixado e pelas horas apontadas.
