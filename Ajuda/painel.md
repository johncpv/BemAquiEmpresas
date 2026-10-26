---
titulo: Painel do proprietário
rota: /app/painel
aba:
padrao: true
modulo: dashboard
---

## Para que serve
Ver a empresa em um minuto: quanto há em caixa, como ele fica nas próximas semanas, quanto foi faturado contra a meta, quem está devendo, o que espera a sua decisão, quanto os projetos estão rendendo, quanto falta produzir e faturar, como está a fábrica e quais entregas vão atrasar. Cada número leva ao relatório de onde ele veio.

## Como usar
- **Cabeçalho:** a saudação, a data e uma frase-resumo montada com os cards que você vê. O botão **Atualizar** lê todos os números de novo; ele pode ser usado uma vez a cada 10 segundos.
- **Atenção:** até 3 alertas, nesta ordem: caixa ajustado negativo nos próximos 90 dias, o projeto que mais vai atrasar em relação à data prometida ao cliente ("vai atrasar 7 dias — previsão e prometido"), decisões aguardando você e o maior título vencido. Clique no alerta para ir direto à ação. Sem alerta, a faixa não aparece.
- **Saldo em contas:** o saldo de hoje de todas as contas ativas, somado e por conta, o mesmo de Financeiro → Contas e caixas.
- **Faturado no mês:** a receita bruta do mês contra a meta, com o medidor "% da meta" e os dias úteis que faltam no mês (pelo calendário padrão da produção). Sem meta, aparece o link **Definir meta** para quem edita as Configurações. O chip **Provisório** quer dizer que o faturamento ainda é contado pelos títulos emitidos, porque as notas ainda não são emitidas pelo sistema.
- **A receber vencido:** o total vencido, separado por faixa de atraso (0–30, 31–60, 61–90 e mais de 90 dias), os 5 maiores clientes e o maior atraso, como em Financeiro → Inadimplência.
- **Caixa projetado, próximas 12 semanas:** o saldo no fim de cada semana em dois cenários. **Base** usa os vencimentos combinados; **Ajustado** desloca cada recebimento pelo atraso médio do cliente. Escolha **Os dois**, **Base** ou **Ajustado**; o card mostra o menor saldo de cada cenário e a diferença causada pelos atrasos. "Ver origem" abre o fluxo de caixa no cenário escolhido.
- **Aguardando sua decisão:** ordens de pagamento, aditivos, qualidade, pedidos de compra e ajustes de ponto que esperam você. Cada item abre o documento, onde a decisão é tomada.
- **Comercial:** orçamentos dos últimos 90 dias: o valor em aberto, a conversão, os que vencem em 7 dias e o funil. Clique numa barra para ver os orçamentos daquela situação.
- **Margem dos projetos:** a **margem projetada (com custos reais)** dos projetos em andamento: receita contratada menos o custo projetado (o já gasto mais o que falta do orçado ou o já contratado), contra a margem orçada, com a diferença em pontos percentuais. Abaixo, a **margem realizada** dos projetos entregues ou encerrados no ano e os 5 piores projetos pelo desvio, cada um até a aba Custos com a natureza que mais desviou. As margens são ponderadas pela receita e são as mesmas do Orçado × realizado. Só aparece para quem vê o custo dos projetos.
- **Projetos em andamento:** até 8 projetos, os atrasados e os em risco primeiro, com a etapa, a entrega ("Atrasado n dias", "+n dias" quando a previsão passou do prometido, ou "No prazo"), o contratado e, conforme o acesso, a margem orçada → projetada e o % recebido. No celular, contratado, margem e recebido ficam em "Valores".
- **Produção:** projetos em andamento, OS atrasadas e em risco (cada número abre o Kanban filtrado) e a ocupação da fábrica na semana atual e nas 4 seguintes, como em Planejamento → Ocupação.
- **Carteira:** **a produzir** é a receita contratada (contrato mais aditivos) dos projetos ainda não fabricados; **a faturar** é o contratado dos contratos ativos menos o que já virou receita desses projetos. O chip **Provisório** aparece enquanto a receita é contada pelos títulos emitidos.
- **Entregas do mês:** previstas no mês (ainda não entregues), entregues no mês, em risco (previsão depois da data prometida) e atrasadas (data prometida já passou), com o projeto que mais vai atrasar e o link para o cronograma.
- **Resultado por projeto:** o link no card de margem abre a receita × custos diretos de cada projeto por período.
- **"Como calculamos":** o **?** ao lado do nome de cada card explica a regra do número.
- **"Atualizado às":** cada card mostra a hora em que o número foi calculado.

## Perguntas frequentes
**Por que não vejo algum card?** Cada card só aparece para quem tem acesso ao módulo de onde vem o número: Caixa e pagamentos para os cards de dinheiro, Orçamentos para o Comercial, Contratos e projetos para projetos, carteira e entregas (e o custo dos projetos para a margem) e Produção para a fábrica. Sem nenhum, o painel mostra "Você ainda não tem acesso aos indicadores. Fale com o administrador."

**O número do painel é diferente do relatório?** Não deveria: o painel usa o mesmo cálculo do relatório de origem. O número pode ter até 1 minuto de atraso; um recebimento, uma ordem de pagamento, um contrato novo, uma previsão de entrega recalculada, uma baixa de material ou um apontamento fazem o painel recalcular. Use **Atualizar** para ler tudo de novo.

**O que é "Provisório" no faturamento?** Até as notas fiscais serem emitidas pelo sistema, o faturamento é a data de emissão dos títulos a receber. Com as notas, passa a ser pela data da nota.

**Onde defino a meta?** Em Configurações → Parâmetros → **Metas de faturamento**, um valor para cada mês do ano.

**Por que caio no painel quando entro?** O painel é a primeira tela de quem é do grupo Proprietário. Os demais começam no Início; quem tem acesso ao painel o encontra no dock, em Visão geral.
