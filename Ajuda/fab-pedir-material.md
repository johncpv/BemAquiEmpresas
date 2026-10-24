---
titulo: App do operador — pedir material
rota: /fab/etapa/$stepId
aba: pedir-material
padrao: false
modulo: shopfloor
---

## Para que serve
Pedir ao almoxarifado o material que falta na etapa, sem sair do app. O pedido não tira nada do estoque: quem separa e baixa é o almoxarifado.

## Como usar
- Na tela da etapa, toque em **Pedir material**. A folha mostra os itens da BOM da OS, com o que a BOM pede, o que já está reservado e o que já foi baixado.
- Toque em **+** e **−** ou digite a quantidade de cada item que você precisa. Só os itens com quantidade entram no pedido.
- Toque em **Pedir material** para enviar. A mensagem **Pedido enviado ao almoxarifado** aparece no topo e o almoxarifado recebe o aviso com a OS, a etapa e o seu nome.
- Embaixo do botão ficam os pedidos da etapa: **Pedido enviado ao almoxarifado**, **Material separado** ou **Recusado: {motivo}**.
- Depois de pausar por **Falta de material**, o pedido aparece em destaque como próximo passo.

## Perguntas frequentes
**Apareceu "Esta OS ainda não tem BOM liberada. Avise o supervisor."** A lista de material da OS ainda não foi liberada pela Engenharia. Fale com o supervisor.

**Posso escolher o lote?** Não. O almoxarifado escolhe o lote quando separa o material.

**Não vejo a seção Material.** O Estoque não está liberado na versão do sistema da empresa. Fale com o supervisor.
