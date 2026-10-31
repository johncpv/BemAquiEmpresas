---
titulo: Regra fiscal
rota: /app/fiscal/regras/$id
aba:
padrao: true
modulo: fiscal
---

## Para que serve
Mostra uma regra fiscal em seções: operação e CFOP, condições (UF de destino, tipo de cliente, NCM), ICMS (com ICMS-ST, FCP e a marca DIFAL), IPI, PIS e COFINS, ISS (na prestação de serviço), IBS e CBS, e os tributos que entram no custo do projeto.

## Como usar
1. Ajuste os campos e clique em **Salvar regra fiscal**. A regra fica em rascunho e o selo volta para **A confirmar com a contadora**.
2. Numa regra **ativa**, salvar cria a **nova versão** em rascunho; a ativa continua valendo até a nova ser ativada. As versões aparecem acima do formulário e a nota guarda a versão usada.
3. O **CFOP fora do estado** é sugerido a partir do de dentro (5101 → 6101): clique em "Confirme" para usar.
4. **Ativar regra** fica liberado depois de abrir o simulador. Quem editou a versão não a ativa, a não ser que seja o único Proprietário (a ativação fica marcada como "Autoaprovado").
5. **Marcar como conferida** registra quem conferiu e quando. **Inativar regra** pede o motivo.
6. O **Histórico de alterações** mostra o antes e o depois de cada campo, quem editou, quem ativou e quem conferiu.

## Simular
O botão **Simular** abre o painel lateral com a operação, o cliente (UF, tipo e consumidor final) e os itens (NCM, origem, quantidade, valor, desconto, frete, outras despesas e a alíquota do IPI da TIPI). O resultado mostra, por item, a regra, o CFOP, cada tributo com base, alíquota e valor, e os totais com o que **entra no custo do projeto**. A simulação usa esta versão, mesmo em rascunho; nada é gravado e o Emissor Fiscal não é chamado. O painel abre com a última simulação feita neste navegador.

## Perguntas frequentes
**Como o ICMS interestadual é escolhido?** Pela UF de destino: 12% para Sul e Sudeste (menos o ES), 7% para as demais UFs e o ES, e 4% para mercadoria importada (origem 1, 2, 3 ou 8).

**O IPI entra na base do ICMS?** Só na venda a não contribuinte ou a consumidor final.

**O que é a marca DIFAL?** Na venda a não contribuinte de outro estado, o cálculo mostra a diferença para a alíquota interna do destino, mas a emissão é bloqueada: o Emissor Fiscal ainda não emite nota com DIFAL. Fale com a contadora para emitir essa venda.
