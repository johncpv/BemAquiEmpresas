---
titulo: Romaneio — itens
rota: /app/producao/expedicao/$id
aba: itens
padrao: true
modulo: production
---

## Para que serve
Conferir o romaneio, baixar o PDF e confirmar a saída do produto acabado para a obra.

## Como usar
- O cabeçalho mostra o número, a situação, o cliente e o projeto. A faixa de fatos mostra a entrega, a transportadora, a placa e o motorista, a data prevista, os volumes e o peso.
- A aba **Itens** mostra cada item com a quantidade, o local, o lote e as séries.
- **Baixar PDF** gera o romaneio com a empresa, o cliente, a entrega, o transporte, os itens com as séries e os campos de assinatura de quem expediu e de quem recebeu. O PDF não tem valor nem custo: não é nota fiscal.
- **Confirmar expedição** (quem edita na produção): o diálogo mostra, por item, a saída do estoque, o local, as séries que passam a **Expedido** e o físico antes e depois. Ao confirmar, o estoque baixa na hora, as séries ficam **Expedido** e o PDF fica guardado na aba **Documentos**. A mensagem é **Expedição confirmada**.
- Se alguém tirou o produto do estoque antes, a confirmação é recusada com o saldo atual dentro do diálogo e nada muda: o romaneio continua em rascunho.
- **Estoque geral**: com produto acabado sem projeto, a confirmação deixa o romaneio **Aguardando confirmação do estoque** e quem aprova no Estoque é avisado. Outra pessoa confirma em **Confirmar estoque geral** (a mensagem é **Estoque geral confirmado**). Quem montou não confirma, exceto o Proprietário que está sozinho no sistema: aí aparece a marca **Autoaprovado** e o Proprietário é avisado.
- **Cancelar romaneio** (rascunho ou aguardando): pede o motivo e libera as séries. **Estornar expedição** (quem aprova na produção): quando o produto não saiu de fato, pede o motivo, devolve o produto ao estoque e as séries voltam a **No estoque**.
- Depois de expedir, os próximos passos levam à **OS de instalação** do projeto ou a **Registrar entrega**. A NF-e de saída nasce do romaneio expedido, em Fiscal → saída (**A emitir**).

## Perguntas frequentes
**Por que não consigo confirmar o estoque geral?** Você montou ou pediu a confirmação deste romaneio, ou não tem a aprovação no Estoque. Peça a outra pessoa do estoque.

**O estorno da expedição muda o custo do projeto?** Não. A expedição nunca entra no custo do projeto, nem o estorno dela.

**Dá para estornar a conclusão da OS depois de expedir?** Não. O estorno da conclusão é recusado com "O produto já foi expedido.". Estorne a expedição antes, se o produto não saiu.
