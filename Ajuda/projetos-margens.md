---
titulo: Orçado × realizado
rota: /app/projetos/margens
aba:
padrao: true
modulo: projects
---

## Para que serve
Ver, projeto a projeto e no total, quanto foi orçado, quanto já foi gasto de verdade em cada natureza de custo e quanto a margem vai fechar, para agir nas obras que estão comendo o lucro enquanto ainda dá.

## Como usar
- No topo fica o **consolidado** dos projetos do filtro: receita contratada, orçado, realizado, projetado e a **margem orçada → projetada**. As margens do total são ponderadas pela receita: somamos as receitas e os custos de todos os projetos (Σ ÷ Σ), sem fazer média simples das margens.
- **Por natureza** mostra o gráfico "Orçado × projetado por natureza" (orçado em coluna vazada, projetado em coluna cheia, com o valor em cada coluna) e a tabela com orçado, realizado, comprometido, projetado e desvio. **Ver os valores em tabela** abre os números do gráfico.
- Clique em um valor de **Orçado**, **Realizado** ou **Comprometido** para abrir o painel com os projetos que somam esse valor. Na aba Custos de cada projeto, o mesmo clique mostra os lançamentos.
- A lista vem do **pior para o melhor desvio** da margem (projetada menos orçada, em pontos percentuais). Clique no cabeçalho para ordenar pelo número, pela receita ou pela margem projetada.
- O chip **Margem {x} pp abaixo** marca o projeto cuja margem projetada caiu; **Prejuízo projetado** (vermelho) quando a projetada fica negativa. O chip abre a aba Custos do projeto com a natureza que mais desviou destacada.
- Filtre pela situação (o padrão é **Em andamento**: planejado, em produção, fabricado e instalando), pelo período de assinatura e por **Só com margem abaixo da orçada**. Clique no nome do cliente para filtrar por ele; o responsável fica na linha expansível e também filtra. Os filtros ficam no endereço da página, para compartilhar.
- No celular, a lista mostra o número, o cliente, a margem orçada → projetada e o chip; receita, orçado, realizado e projetado ficam na linha expansível.

## Perguntas frequentes
**O que entra no realizado?** O material baixado para as OS (menos as devoluções), as horas apontadas (horas × custo-hora do cargo), os títulos a pagar confirmados com o projeto (instalador, frete, outros e o material entregue direto na obra) e o imposto das notas fiscais autorizadas do projeto.

**O que é o comprometido?** O que já foi contratado e ainda não foi gasto: o título previsto (por exemplo, o do instalador) e o material comprado para o projeto que ainda não foi baixado para a OS. No material, é títulos de compra menos baixas, nunca abaixo de zero: se a baixa usou material de estoque anterior, o comprometido fica em zero e não vira crédito.

**Como é o imposto projetado?** Até a primeira nota fiscal do projeto, é o imposto do orçamento aprovado (marca **Imposto orçado**). Com nota, é o imposto das notas mais o orçado da parte da receita ainda não faturada.

**A guia de imposto entra?** Não. DARF, GARE e DAS são da empresa: entram no DRE, não no projeto.

**Por que não vejo este relatório?** Custo e margem são sigilo comercial: só quem tem a permissão **Ver custo e margem** do projeto vê a tela e o item no menu.
