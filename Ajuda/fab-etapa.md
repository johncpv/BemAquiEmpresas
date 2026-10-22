---
titulo: App do operador — etapa
rota: /fab/etapa/$stepId
aba:
padrao: true
modulo: shopfloor
---

## Para que serve
Apontar o seu trabalho numa etapa da OS: iniciar, pausar, retomar, sair e finalizar, com o cronômetro e o checklist.

## Como usar
- No topo: a OS, o produto, o projeto e o cliente, o display de andar com a etapa atual, as **horas padrão** e as horas já **apontadas** por todos os operadores, e quem está na etapa agora.
- Toque em **Iniciar etapa**. O cronômetro começa e a OS entra em produção.
- Marque os itens do **checklist** conforme faz. Cada marcação fica gravada com o seu nome e a hora.
- Os **desenhos e documentos** da OS abrem na tela (PDF, PNG e JPG). DWG e DXF só com **Baixar**.

## Pausar e retomar
- Toque em **Pausar** e escolha o motivo: Falta de material, Máquina parada, Intervalo, Fim do turno ou Outro. O motivo que você mais usa aparece primeiro. O texto é opcional.
- O cronômetro para e o cartão mostra "Pausada: {motivo}". **Falta de material** avisa o almoxarifado sozinha.
- Toque em **Retomar** para voltar. As pausas não contam como horas trabalhadas.

## Finalizar
- **Finalizar etapa** só libera com todos os itens do checklist marcados. Enquanto faltar, o botão mostra "Marque os {n} itens que faltam".
- A confirmação ocupa a tela toda: "Finalizar etapa · {h} h apontadas", com o que muda (a etapa fica concluída e a próxima fica pronta). Toque em **Finalizar etapa** de novo para confirmar, ou em **Voltar**.
- Depois, a próxima da fila aparece com **Iniciar próxima: {etapa} da {OS}**.
- Etapa marcada "Exige aprovação" fica **Aguardando aprovação** até o supervisor aprovar.

## Mais de uma pessoa na etapa
- Com outro operador na etapa, aparecem **Sair da etapa (só eu)**, que fecha só o seu apontamento sem pedir o checklist, e **Concluir etapa para todos**, que pede o checklist e encerra também o apontamento dos outros, com a marca "Encerrado por {seu nome}".

## Perguntas frequentes
**Toquei duas vezes em Iniciar. Abriu dois apontamentos?** Não. Cada pessoa tem um apontamento aberto por vez; o segundo toque não cria outro.

**Esqueci de finalizar ontem. E agora?** Fale com o supervisor: ele corrige o apontamento ou conclui a etapa pela tela da OS.

**Por que não consigo iniciar a etapa de qualidade?** A inspeção de qualidade é feita por quem tem a ação **Aprovar ou reprovar a qualidade**, na tela **Qualidade** do app (/fab/qualidade). Na fila do operador, a OS aparece como "Aguardando qualidade".

**A OS voltou para uma etapa que eu já tinha feito. Por quê?** A qualidade reprovou e abriu uma não conformidade. A etapa de retorno fica pronta de novo: refaça e finalize normalmente. As horas do retrabalho entram como um apontamento novo.