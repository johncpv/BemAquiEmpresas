---
titulo: Planejamento — ocupação da fábrica
rota: /app/producao/planejamento
aba: ocupacao
padrao: false
modulo: production
---

## Para que serve
Ver quanto da capacidade de cada centro de trabalho já está tomada em cada semana, para saber se dá para aceitar mais serviço, onde vai faltar gente e quando sobra tempo.

## Como usar
- A tabela mostra as **próximas 8 semanas** a partir da atual (de segunda a domingo; escolha até 26 em **Semanas**), uma linha por **centro de trabalho ativo** e a linha **Fábrica** no fim.
- Cada célula tem a **ocupação** (por exemplo, **31,3%**), as horas **comprometidas** e a **capacidade** ("10 de 32 h").
- **Comprometidas** são as horas que o plano já reservou no centro nos dias da semana, somando todas as etapas. **Capacidade** são as horas que o centro tem nos mesmos dias, pelo calendário, pelos feriados e pelas paradas, lidas agora. Férias de funcionário não reduzem a capacidade.
- Na **semana atual** contam só os dias **a partir de hoje**, porque o plano começa hoje.
- A linha **Fábrica** soma as comprometidas de todos os centros e divide pela soma das capacidades. Com um filtro de linha, ela vira o **Total** da linha.
- Centro sem capacidade na semana (por exemplo, todos os dias de feriado) mostra **—**.
- No alto aparece **Plano de {hora}**: a ocupação lê o último cálculo do plano, sem recalcular nada.

## Estados da célula
- **Folga** (azul): abaixo de 60%. Sobra tempo no centro; abra a célula e use **Ver a fila** para antecipar um projeto.
- Sem destaque: de 60% a menos de 95%.
- **Lotado** (âmbar): 95% ou mais. Abra a célula para ver as etapas da semana.
- **Acima da capacidade** (vermelho): as horas planejadas passaram da capacidade. Veja abaixo.
- O estado sempre aparece escrito junto da cor.

## Por que a ocupação não passa de 100%
O plano é **finito**: ele nunca põe num dia mais horas do que o centro tem. O que não cabe vai para as semanas seguintes. Por isso a ocupação pelo plano fica em até 100%, e um centro cheio mostra **Lotado** semana após semana, em vez de 150%.

## O quadro "Depois de"
- Como a ocupação não passa de 100%, a carga que ficou para frente aparece no quadro **Depois de {data}**: para cada centro, as horas já planejadas depois da última semana mostrada e o último dia planejado ("Corte ocupado até 19/02").
- Se toda a carga cabe nas semanas mostradas, o quadro diz isso.

## "Acima da capacidade"
- Só acontece quando a capacidade **caiu depois do último cálculo do plano**: um feriado ou uma parada novos no calendário num dia que já tinha etapas.
- Aparece a faixa vermelha com **Recalcular agora** (para quem pode editar a produção). O plano refaz as datas e a carga passa para os dias seguintes. Normalmente o plano se refaz sozinho em segundos depois da mudança no calendário.

## Ver as etapas da célula
- Clique na célula, ou vá até ela com as **setas** e aperte **Enter**. O painel lateral lista as etapas alocadas naquele centro e naquela semana: OS (com link), projeto, etapa, horas na semana e o início e o fim previstos.
- O **total da semana** no painel é o mesmo número da célula.
- Na tabela, **Home** e **End** vão para a primeira e a última semana da linha; **Ctrl+Home** e **Ctrl+End**, para o começo e o fim da tabela. A coluna dos centros fica fixa quando a tabela rola para o lado.

## Gráfico
- Escolha **Gráfico** para ver um ponto por semana com o percentual escrito ao lado e a linha de referência **Capacidade (100%)**. Sem filtro, o gráfico é da fábrica; escolhendo um **Centro**, é só dele.
- Acima do gráfico há o resumo ("Semana mais ocupada: S45 · 02/11, 53,1%. Corte ocupado até 19/02.") e, abaixo, **Ver os valores em tabela**.

## Filtros
- **Linha**, **Centro**, **Semanas** e a visão (Tabela ou Gráfico) ficam no endereço da página: dá para salvar o link ou mandar para alguém.

## No celular
Uma semana por vez: escolha a semana no seletor ou pelas setas e veja os centros em lista. Toque no centro para ver as etapas.

## Perguntas frequentes
**Apareceu "Nenhuma etapa planejada no período."** O plano ainda não datou nenhuma etapa. Veja a fila e as OS sem previsão.

**Apareceu "Cadastre os centros de trabalho para ver a ocupação."** Não há centro de trabalho ativo. Cadastre os centros em Fábrica → Produção → Centros de trabalho.

**Um centro inativo aparece?** Não. Um centro criado no meio do período tem capacidade 0 nos dias antes da criação.

**A manutenção de máquina entra?** Entra como parada no calendário do centro, e a capacidade cai nesses dias.
