---
titulo: Caixas
rota: /app/loja/caixas
aba:
padrao: true
modulo: sales
---

## Para que serve
Acompanhar os turnos dos caixas das lojas: quem abriu, quando fechou, a contagem cega do operador e a **quebra de caixa** (sobra ou falta) de cada forma de pagamento. Aqui o gerente aprova os fechamentos que passaram da tolerância, confere os Pix contra o extrato e reabre o último caixa quando é preciso.

## Como usar
- Filtre por loja, caixa e situação. O chip **Em conferência** mostra quantos fechamentos esperam o gerente; toque nele para ver só esses.
- Toque em **Abrir** num turno para ver o quadro: o contado por forma e, para quem aprova vendas, o **esperado** e a **diferença**; as vendas, as canceladas e as feitas sem rede; o fundo de troco, as sangrias, os suprimentos e o recolhimento ao cofre.
- **Aprovar fechamento:** no fechamento em conferência, informe o motivo e aprove. Quem operou o caixa não aprova o próprio fechamento.
- **Pix conferido pelo operador:** confira cada Pix (hora, valor, operador) no extrato do banco e marque **Conferido** ou **Não localizado**. O não localizado vira quebra da forma Pix. Confira antes de aprovar: no caixa já fechado, reabra para mudar a conferência. Quem operou ou contou o caixa, ou atestou o Pix, não o confere.
- **Reabrir caixa:** só o último turno do caixa, já fechado, com motivo. O caixa volta a vender, o recolhimento volta para a gaveta e o operador conta de novo. O Proprietário recebe um aviso. Quem operou o caixa não reabre o próprio fechamento.
- **Via do operador** e **Via do gerente** baixam o relatório do fechamento em PDF. A via do operador não traz o esperado nem a diferença.
- **Exportar** baixa a lista em planilha (CSV).

## Regras
- **Conferência cega:** o operador conta sem ver o esperado. O esperado e a diferença só aparecem para quem tem a permissão de aprovar vendas.
- **Tolerância de quebra:** por loja (padrão R$ 5,00), aplicada **em cada forma**: sobra no dinheiro não compensa falta no cartão. Acima dela, ou com sangria acima do dinheiro calculado, o fechamento fica **Em conferência**.
- **Aguardando envio das vendas:** o caixa foi contado, mas ainda falta chegar venda do aparelho. A quebra é calculada quando todas chegam.
- Nada entra num caixa depois do "Fechar caixa": venda atrasada vai para as Pendências do PDV.
- Quem não aprova vendas vê o caixa contado como **Enviado**, esteja ele fechado ou em conferência.

## Perguntas frequentes
**Onde mudo a tolerância de quebra?** Em Loja → Lojas e caixas, na loja, no campo "Tolerância de quebra por forma".

**O caixa ficou "Aguardando envio das vendas" e o aparelho quebrou.** Confira as Pendências do PDV. Se as vendas não vão chegar, quem aprova vendas pode aprovar o fechamento assim mesmo, com o motivo.

**O que o fechamento lança no financeiro?** Na seção **No financeiro** do caixa fechado (para quem vê o Financeiro): o dinheiro das vendas entra na conta do caixa; cada cartão vira um recebível contra a maquininha, por bandeira e data do crédito, com o bruto, a taxa prevista e o líquido (o crédito parcelado tem um por parcela); o Pix fica a conciliar na conta de destino; o Pix "Não localizado" e a falta ou a sobra do dinheiro vão para a conta **Quebra de caixa**. Com o fundo de troco e o recolhimento, a conta do caixa fica só com o fundo que ficou para o próximo turno.

**E se eu reabrir o caixa?** Os lançamentos do fechamento são estornados e voltam no próximo fechamento. Se a maquininha já depositou e alguém conciliou um recebível deste caixa, a reabertura é recusada: "Este caixa já tem recebimento conciliado. Estorne a baixa antes."
