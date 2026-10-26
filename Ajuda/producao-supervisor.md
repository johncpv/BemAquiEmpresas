---
titulo: Painel do supervisor
rota: /app/producao/supervisor
aba:
padrao: true
modulo: production
---

## Para que serve
Começar o dia sabendo onde agir primeiro: numa tela só, a qualidade que espera decisão, o material que vai parar a fábrica, as OS atrasadas e em risco, a ocupação das próximas semanas e os indicadores de parada e de não conformidade. Cada bloco leva à ação que resolve.

## Como usar
- **Linha:** o filtro no topo vale para todos os blocos e fica no endereço (dá para mandar o link). **Todas** mostra a fábrica inteira. A linha da OS é a do centro de trabalho da etapa atual, como no Kanban.
- **Qualidade:** as etapas de qualidade prontas para decidir que **você pode decidir**. **Aprovar** e **Reprovar** abrem a inspeção, com o checklist, o impacto e a não conformidade. Logo abaixo, as **não conformidades abertas**, da mais antiga para a mais nova, com a OS, a etapa, o responsável e há quantos dias estão abertas; clique no número para tratar a NC.
- **Faltas de material:** o material que falta nas OS abertas. As que **bloqueiam** vêm primeiro: o chip **Bloqueia em {n} dias** quer dizer que a próxima etapa da OS está prevista para começar em até 5 dias úteis (pelo calendário padrão) e o material ainda não chegou. A coluna da compra mostra **Sem solicitação**, **Solicitada {SC}**, **Em cotação** ou **Pedido {PC} previsto para {data}**. Quem pode criar solicitação de compra vê **Gerar solicitação** na linha sem solicitação.
- **Quadro:** as colunas do Kanban por etapa, com quantas OS e as horas restantes. Só as OS **atrasadas** (faixa vermelha) e **em risco** (faixa âmbar) aparecem abertas. Os chips **{n} atrasadas** e **{n} em risco** abrem o Kanban já filtrado. O bloco se atualiza sozinho quando alguém mexe numa OS.
- **Capacidade:** a ocupação da semana atual e das próximas 4 de cada centro, com **Folga** (abaixo de 60%), **Lotado** (de 95% a 100%) e **Acima da capacidade**, e até quando o centro está ocupado. **Abrir** leva à Ocupação do Planejamento.
- **Indicadores (últimos 30 dias):** as **horas paradas** por motivo (Material, Máquina e Outro) e a **taxa de NC** de cada etapa de qualidade. Abra o motivo para ver as OS que mais pararam.
- **Atualizar** lê de novo a capacidade e os indicadores (pode ser usado uma vez a cada 10 segundos). A qualidade e as faltas recarregam sozinhas a cada minuto e quando você volta para a tela.
- Cada bloco mostra **Atualizado às {hh:mm}** e o link **Abrir** para a tela completa.

## Como calculamos
- **Horas paradas:** soma dos minutos das pausas apontadas no app do operador nos últimos 30 dias, dividida por 60, separada pelo motivo. Intervalo e fim de turno não contam como parada. Pausa ainda aberta conta até agora.
- **Taxa de NC:** reprovações da etapa de qualidade divididas pelo total de decisões (aprovações mais reprovações) nos últimos 30 dias, com uma casa.
- **Bloqueia em {n} dias:** dias úteis entre hoje e o início previsto da próxima etapa da OS no Planejamento. Hoje ou já passou é **Bloqueia hoje**.

## No celular
Os blocos ficam um embaixo do outro, nesta ordem: Qualidade, Faltas de material, Quadro (só os chips e a lista de atrasadas e em risco), Capacidade e Indicadores.

## Perguntas frequentes
**Por que uma etapa de qualidade não aparece para mim?** Porque você apontou alguma etapa da tentativa atual daquela OS. Quem fez o trabalho não aprova a qualidade dele: outra pessoa com a ação de qualidade decide.

**O que é Autoinspeção?** Quando você fez parte do trabalho e não há outro aprovador possível, você pode aprovar mesmo assim. A aprovação leva a marca **Autoinspeção** e o Proprietário é avisado.

**Por que só vejo "{n} etapas aguardam a qualidade"?** Porque você não tem a ação **Aprovar ou reprovar a qualidade**. A contagem mostra o que está na fila; as não conformidades continuam visíveis.

**Por que não vejo o bloco Faltas de material?** Ele é do Estoque: aparece para quem vê o Estoque.

**Vejo a situação da compra sem acesso a Compras?** Sim. O painel mostra a situação da compra só para leitura; abrir a solicitação ou o pedido exige acesso a Compras.

**Qual etapa "consome" o material?** A BOM ainda não diz em qual etapa cada item é usado, então o painel considera a próxima etapa da OS que ainda não terminou.
