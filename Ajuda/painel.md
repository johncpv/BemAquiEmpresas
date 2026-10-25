---
titulo: Painel do proprietário
rota: /app/painel
aba:
padrao: true
modulo: dashboard
---

## Para que serve
Ver a empresa em um minuto: quanto há em caixa, como ele fica nas próximas semanas, quanto foi faturado contra a meta, quem está devendo e o que espera a sua decisão. Cada número leva ao relatório de onde ele veio.

## Como usar
- **Cabeçalho:** a saudação, a data e uma frase-resumo montada com os cards que você vê. O botão **Atualizar** lê todos os números de novo; ele pode ser usado uma vez a cada 10 segundos.
- **Atenção:** até 3 alertas, nesta ordem: caixa ajustado negativo nos próximos 90 dias, decisões aguardando você e o maior título vencido. Clique no alerta para ir direto à ação. Sem alerta, a faixa não aparece.
- **Saldo em contas:** o saldo de hoje de todas as contas ativas, somado e por conta, o mesmo de Financeiro → Contas e caixas.
- **Faturado no mês:** a receita bruta do mês contra a meta, com o medidor "% da meta" e os dias úteis que faltam no mês (pelo calendário padrão da produção). Sem meta, aparece o link **Definir meta** para quem edita as Configurações. O chip **Provisório** quer dizer que o faturamento ainda é contado pelos títulos emitidos, porque as notas ainda não são emitidas pelo sistema.
- **A receber vencido:** o total vencido, separado por faixa de atraso (0–30, 31–60, 61–90 e mais de 90 dias), os 5 maiores clientes e o maior atraso, como em Financeiro → Inadimplência.
- **Caixa projetado, próximas 12 semanas:** o saldo no fim de cada semana em dois cenários. **Base** usa os vencimentos combinados; **Ajustado** desloca cada recebimento pelo atraso médio do cliente. Escolha **Os dois**, **Base** ou **Ajustado**; o card mostra o menor saldo de cada cenário e a diferença causada pelos atrasos. "Ver origem" abre o fluxo de caixa no cenário escolhido.
- **Aguardando sua decisão:** ordens de pagamento, aditivos, qualidade, pedidos de compra e ajustes de ponto que esperam você. Cada item abre o documento, onde a decisão é tomada.
- **Comercial:** orçamentos dos últimos 90 dias: o valor em aberto, a conversão, os que vencem em 7 dias e o funil. Clique numa barra para ver os orçamentos daquela situação.
- **"Como calculamos":** o **?** ao lado do nome de cada card explica a regra do número.
- **"Atualizado às":** cada card mostra a hora em que o número foi calculado.

## Perguntas frequentes
**Por que não vejo algum card?** Cada card só aparece para quem tem acesso ao módulo de onde vem o número: Caixa e pagamentos para os cards de dinheiro e Orçamentos para o Comercial. Sem nenhum, o painel mostra "Você ainda não tem acesso aos indicadores. Fale com o administrador."

**O número do painel é diferente do relatório?** Não deveria: o painel usa o mesmo cálculo do relatório de origem. O número pode ter até 1 minuto de atraso; um recebimento, uma ordem de pagamento ou um contrato novo fazem o painel recalcular. Use **Atualizar** para ler tudo de novo.

**O que é "Provisório" no faturamento?** Até as notas fiscais serem emitidas pelo sistema, o faturamento é a data de emissão dos títulos a receber. Com as notas, passa a ser pela data da nota.

**Onde defino a meta?** Em Configurações → Parâmetros → **Metas de faturamento**, um valor para cada mês do ano.

**Por que caio no painel quando entro?** O painel é a primeira tela de quem é do grupo Proprietário. Os demais começam no Início; quem tem acesso ao painel o encontra no dock, em Visão geral.
