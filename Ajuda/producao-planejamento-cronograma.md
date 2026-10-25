---
titulo: Planejamento — cronograma
rota: /app/producao/planejamento
aba: cronograma
padrao: false
modulo: production
---

## Para que serve
Ver numa linha do tempo quando cada etapa de cada projeto acontece e como cada centro de trabalho está carregado, para enxergar o que empurra a entrega e onde está o gargalo. O cronograma só mostra: as datas são do plano, e nada se arrasta nem se edita aqui.

## Como usar
- Escolha **Por projeto** ou **Por centro** no alto da aba.
- **Escala**: **Dia**, **Semana** (padrão) ou **Mês**. **Período**: **12 semanas** (padrão, de 7 dias antes de hoje a 12 semanas depois), **6 meses** ou **12 meses**.
- A linha **Hoje** fica sempre marcada. **Ir para hoje** rola o cronograma até ela. A rolagem para o lado acontece só dentro do cronograma; a coluna dos nomes fica fixa.
- Acima do cronograma aparece o resumo, por exemplo: "12 projetos no período; 3 em risco. O mais atrasado é PRJ-2026-0003 · Ed. Monte Castelo, 7 dias depois do prometido."
- No alto aparece **Plano de {hora}**: o cronograma lê o último cálculo do plano.

## Por projeto
- Uma linha por projeto da **fila**, na ordem da aba **Fila**. A faixa do projeto vai do início da primeira etapa até a **data prevista** de entrega.
- O traço vertical **Prometido {data}** marca a data combinada com o cliente.
- O chip de **folga** mostra quantos dias sobram ("+3 dias") ou faltam ("−7 dias") entre o previsto e o prometido.
- Projeto **em risco** (previsto depois do prometido) tem a faixa lateral âmbar; **atrasado** (prometido já passou), vermelha.
- Clique na seta ao lado do projeto para abrir as OS, e na seta da OS para abrir as etapas. Com mais de 50 projetos, todos abrem recolhidos.

## Como ler as barras
- **Contínua e cheia**: etapa concluída, do início ao fim **reais**.
- **Tracejada**: o que o plano prevê e ainda não aconteceu.
- **Tracejada com parte preenchida**: etapa em andamento; a parte preenchida é o que já foi feito (horas apontadas ÷ horas padrão).
- **Hachurada**: **Instalação (datas da OS)**. A OS de instalação aparece com as datas digitadas nela, só para leitura. Para mudar, edite a própria OS de instalação.
- **Contorno âmbar com "Define a entrega"**: no projeto em risco ou atrasado, a etapa com o maior fim previsto. É ela que empurra a data. Para trazer a entrega para antes, antecipe o projeto na **Fila** ou libere horas no centro dessa etapa.
- A letra de cada etapa aparece no começo da barra, como no indicador de etapa.
- Etapa sem previsão não tem barra: a linha diz o motivo, e **Ver o motivo** leva à aba **Sem previsão**.

## Por centro
- Uma linha por centro de trabalho ativo, com as etapas alocadas nele na ordem do tempo. Quando duas etapas caem no mesmo dia, uma vai para uma sub-linha abaixo.
- O fundo de cada dia mostra a ocupação: **Folga** abaixo de 60%, **Lotado** de 95% para cima e **Acima da capacidade** quando as horas passaram do que o centro tem (o calendário mudou depois do plano). Passe o ponteiro no dia para ler o número.
- Fim de semana, **feriado** e **parada** aparecem sombreados, com o nome da exceção do calendário.

## Detalhe e teclado
- Passe o ponteiro, foque com o teclado ou toque na barra para ver OS, projeto, etapa, centro, horas restantes ÷ padrão e as datas previstas e reais.
- Clique na barra ou aperte **Enter** para abrir a OS. No toque, o primeiro toque mostra o detalhe e o botão **Abrir OS**.
- Pelo teclado: **Tab** chega às barras; as **setas** para a esquerda e para a direita andam no tempo; para cima e para baixo vão para a linha de cima ou de baixo; **Home** e **End**, primeira e última barra da linha.

## Ver como tabela
**Ver como tabela** mostra as mesmas linhas com as datas em texto (projeto, OS, etapa, centro, início, fim, prometido e folga), navegável pelas setas: para baixo e para cima mudam de linha; direita abre, esquerda recolhe.

## Filtros
- **Projeto**, **Cliente**, **Linha**, **Centro** e **Só em risco** filtram o cronograma. Os filtros, a visão, a escala e o período ficam no endereço da página: dá para salvar o link ou mandar para alguém.
- O filtro de linha ou de centro mostra os projetos que têm etapa naquele centro, com todas as etapas do projeto.

## Tempo real
O cronograma aberto se atualiza sozinho quando a fábrica muda (apontamento, etapa concluída, plano recalculado), agrupando os avisos a cada 5 segundos, sem piscar a tela.

## No celular
- **Por projeto** vira lista: projeto, previsto, prometido e folga. Toque no projeto para ver as etapas com as datas em texto e uma barra simples na largura da tela.
- **Por centro** mostra um centro por vez: escolha no seletor ou pelas setas.

## Perguntas frequentes
**Apareceu "Nenhuma etapa planejada no período."** O plano ainda não datou nenhuma etapa no período. Veja a fila e as OS sem previsão, ou escolha um período maior.

**Dá para mudar uma data arrastando a barra?** Não. As datas são calculadas pelo plano. Para mudar a ordem dos projetos, use a aba **Fila**.

**Por que a instalação não muda quando o plano recalcula?** Porque as datas da OS de instalação são as digitadas na OS, combinadas com o instalador.
