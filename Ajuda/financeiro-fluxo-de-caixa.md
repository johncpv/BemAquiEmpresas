---
titulo: Fluxo de caixa
rota: /app/financeiro/fluxo-de-caixa
aba:
padrao: true
modulo: finance
---

## Para que serve
Ver quanto entra, quanto sai e qual será o saldo dia a dia, para antecipar falta de caixa antes que ela aconteça. Além da data combinada, o sistema mostra o caixa considerando que cada cliente costuma pagar com o atraso de sempre.

## Como usar
- O **saldo atual** é o saldo inicial das contas mais tudo o que já entrou e saiu até hoje.
- Até ontem, a tabela mostra o **realizado** (baixas, pagamentos e lançamentos). De hoje em diante, o **projetado**: títulos em aberto pelo saldo, no vencimento, e as despesas recorrentes que ainda não viraram título.
- Os **vencidos em aberto** entram no dia de hoje, numa linha destacada.
- Escolha o **cenário** acima do gráfico: **Os dois** (padrão), **Base** ou **Ajustado**.
  - **Base:** cada título entra no vencimento combinado.
  - **Ajustado:** cada recebimento entra com o **atraso médio** do cliente nos últimos 12 meses, em dias corridos (sem pular fim de semana nem feriado). Cliente que ainda não tem pagamento registrado usa a **média da empresa**. Recebimento vencido e ainda em aberto entra no dia mais tarde entre hoje e o vencimento mais o atraso. Pagamentos, recorrentes e o realizado ficam iguais aos da base.
- No gráfico, o realizado é a linha contínua, a base é a tracejada fina e o ajustado é a tracejada grossa com pontos. Cada linha tem o nome no fim, e há as marcas **Hoje** e **Saldo zero**. O texto acima do gráfico diz o menor saldo de cada cenário. Clique em **Ver como tabela** para ver os mesmos valores numa tabela.
- O quadro de destaques mostra o **menor saldo previsto** no ajustado e na base, com a data, e a **diferença causada por atrasos** (menor saldo da base menos o menor saldo do ajustado).
- **Risco de não receber no período:** quando a inadimplência do cliente passa do limite (20,00% no começo, em Configurações → Parâmetros), o ajustado conta só a parte que provavelmente entra. O quadro mostra o total e os clientes, com os links para a **Inadimplência** e para os títulos do cliente.
- **Deslocado para depois de:** recebimentos que vencem no período, mas que, com o atraso de sempre, entram depois do fim do horizonte.
- Troque a **visão** (diária, semanal ou mensal), o **horizonte** (30, 60 ou 90 dias) e a **conta** nos filtros do topo.
- Na visão diária, clique no dia para ver o que compõe o valor, com o link de cada título. No ajustado, cada recebimento mostra o vencimento original, a data ajustada e o motivo, e os que saíram do dia aparecem à parte. O total do painel é o mesmo da tabela.
- No celular, a tabela mostra o período e os saldos; toque em **Entradas e saídas** para ver o resto da linha.

## Perguntas frequentes
**Por que o caixa aparece negativo?** A soma do que vence é maior que o saldo. O alerta vermelho mostra o primeiro dia negativo; no ajustado, o alerta diz "Considerando os atrasos de sempre". Clique no botão do alerta para ver o que entra e sai nesse dia.

**Como é calculada a inadimplência do cliente?** O que ele deve vencido há mais de 30 dias, dividido pelo que ele faturou nos últimos 12 meses, até 100%. Os títulos da implantação contam na dívida, mas não no faturado. Quem não faturou nada e tem dívida vencida fica com 100%.

**Como é calculado o atraso médio?** Pelos títulos quitados nos últimos 12 meses: dias entre o vencimento e a baixa, pesando pelo valor. Pagar antes do vencimento conta como zero. Os títulos da implantação não entram no cálculo, mas são deslocados como os outros. No deslocamento, o atraso é arredondado para dias inteiros.

**Por que o ajustado está igual à base?** Ainda não há histórico de pagamento na empresa. A tela avisa: "Ainda não há histórico de pagamento: o ajustado é igual à base."

**Por que, filtrando por conta, não aparece o previsto nem o ajustado?** Os títulos ainda não têm conta definida. A projeção, na base e no ajustado, é só no consolidado.

**O que é "Estimado"?** Um título com data estimada, como o pagamento do instalador antes da OS terminar.

**Onde estão a folha e as compras?** A folha entra pelos títulos do fechamento e pela folha prevista; as compras, pelos títulos previstos dos pedidos aprovados.

**O que é "A receber sem data"?** Parcelas que dependem de um marco, como a entrega. Elas entram no fluxo quando o marco acontecer, na base e no ajustado.

**O que é "Estimativa do 13º"?** Desde janeiro, as duas parcelas do 13º de cada funcionário ativo aparecem no fluxo de caixa como estimativa, nos meses das datas limite (novembro e dezembro): o salário atual × os avos projetados do ano, metade em cada parcela. Quando a folha da parcela fecha, a estimativa sai e entra a folha real. O RH desliga em Configurações → Parâmetros → RH ("13º no fluxo de caixa").

**O que é "Folha prevista"?** Os próximos meses da folha (3, por padrão), com o 13º e as férias, como títulos "Estimado". Quando a folha real do mês fecha, a previsão daquele mês sai e entra a folha real, sem contar duas vezes. Quem não aprova nem paga no Financeiro vê só o total de cada folha, sem o salário de cada pessoa.
