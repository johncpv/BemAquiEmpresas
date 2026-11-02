# Manual de Compras e Almoxarifado

**Grupos:**
- **Compras** (criar no sistema): `purchasing.view`, `purchasing.create`, `purchasing.edit`; cadastros de fornecedores; ver estoque e projetos.
- **Almoxarifado** (do sistema): `inventory.view`, `inventory.create`, `inventory.edit`, `inventory.adjust`; `purchasing.view`, `purchasing.receive`; `fiscal.view`, `fiscal.import_incoming`; ver produção e patrimônio.

Se a mesma pessoa compra e recebe, ela fica nos dois grupos. O sistema não deixa receber o pedido que ela mesma pediu ou aprovou: esse recebimento fica com outra pessoa.

## Rotina de Compras

### Todo dia

| Tarefa | Tela | Ajuda |
|---|---|---|
| Ver o que precisa ser comprado e para quando (falta da BOM, estoque mínimo, manual) | Fábrica → Compras → Solicitações `/app/compras/solicitacoes` (abre em **Aguardando você**) | [compras-solicitacoes](../../../Ajuda/compras-solicitacoes.md) · [compras-solicitacao](../../../Ajuda/compras-solicitacao.md) |
| Pedir preço a vários fornecedores (aba **Por item** → **Cotar**) | Compras → Cotações `/app/compras/cotacoes` | [compras-cotacoes](../../../Ajuda/compras-cotacoes.md) · [compras-cotacao-nova](../../../Ajuda/compras-cotacao-nova.md) · [compras-cotacao](../../../Ajuda/compras-cotacao.md) |
| Escolher o vencedor e gerar os pedidos | Cotação → **Comparativo** | [compras-cotacao-comparativo](../../../Ajuda/compras-cotacao-comparativo.md) |
| Enviar o pedido para aprovação e, aprovado, ao fornecedor | Compras → Pedidos `/app/compras/pedidos` | [compras-pedidos](../../../Ajuda/compras-pedidos.md) · [compras-pedido](../../../Ajuda/compras-pedido.md) · [compras-pedido-documentos](../../../Ajuda/compras-pedido-documentos.md) |

### Toda semana

| Tarefa | Tela | Ajuda |
|---|---|---|
| Cobrar os pedidos atrasados do fornecedor | Pedidos (previsão de entrega) | [compras-pedidos](../../../Ajuda/compras-pedidos.md) |
| Encerrar saldo de pedido que não vai chegar | Pedido → **Encerrar saldo do pedido** | [compras-pedido](../../../Ajuda/compras-pedido.md) |
| Manter os códigos do fornecedor e as conversões de unidade | Estoque → Itens → item → abas Fornecedores e Conversões | [estoque-item-fornecedores](../../../Ajuda/estoque-item-fornecedores.md) · [estoque-item-conversoes](../../../Ajuda/estoque-item-conversoes.md) |

## Rotina do Almoxarifado

### Todo dia

| Tarefa | Tela | Ajuda |
|---|---|---|
| Receber pela nota do fornecedor (XML; sem XML, o PDF ou **Digitar a nota**) | Financeiro → Fiscal → Notas de entrada → Importar `/app/fiscal/entrada/importar` | [fiscal-entrada-importar](../../../Ajuda/fiscal-entrada-importar.md) · [fiscal-entrada-importar-pdf](../../../Ajuda/fiscal-entrada-importar-pdf.md) · [fiscal-nota-entrada](../../../Ajuda/fiscal-nota-entrada.md) · [fiscal-nota-entrada-receber](../../../Ajuda/fiscal-nota-entrada-receber.md) |
| Receber sem nota (manual, contra o pedido) | Pedido → **Registrar recebimento** `/app/compras/pedidos/$id/recebimento` | [compras-recebimento-novo](../../../Ajuda/compras-recebimento-novo.md) |
| Separar e baixar o material pedido pelo operador | Estoque → Requisições `/app/estoque/requisicoes` | [estoque-requisicoes](../../../Ajuda/estoque-requisicoes.md) · [estoque-requisicao](../../../Ajuda/estoque-requisicao.md) |
| Ver o que falta para as OS | Estoque → Faltas `/app/estoque/faltas` | [estoque-faltas](../../../Ajuda/estoque-faltas.md) |
| Confirmar o romaneio de expedição | Produção → Expedição `/app/producao/expedicao` | [producao-expedicao](../../../Ajuda/producao-expedicao.md) · [producao-romaneio](../../../Ajuda/producao-romaneio.md) |

### Todo mês (ou quando a contagem não bate)

| Tarefa | Tela | Ajuda |
|---|---|---|
| Inventário por local (contagem e divergências) | Estoque → Inventários `/app/estoque/inventarios` | [estoque-inventarios](../../../Ajuda/estoque-inventarios.md) · [estoque-inventario-contagem](../../../Ajuda/estoque-inventario-contagem.md) · [estoque-inventario-divergencias](../../../Ajuda/estoque-inventario-divergencias.md) |
| Ajuste de sobra ou falta, com motivo | Estoque → Saldos → **Lançar ajuste** | [estoque-ajustes](../../../Ajuda/estoque-ajustes.md) · [estoque-ajuste](../../../Ajuda/estoque-ajuste.md) |
| Conferir saldos, custo médio e estoque mínimo | Estoque → Saldos `/app/estoque/saldos` | [estoque-saldos](../../../Ajuda/estoque-saldos.md) |
| Rastrear lote ou série de um elevador entregue | Estoque → Rastreabilidade `/app/estoque/rastreabilidade` | [estoque-rastreabilidade](../../../Ajuda/estoque-rastreabilidade.md) |

## Entre as telas

- **Da falta ao estoque, sem digitar de novo:** BOM liberada → falta → solicitação (sozinha) → cotação → pedido aprovado (vira previsão no caixa) → nota do fornecedor → recebimento (estoque, custo médio e contas a pagar juntos).
- **Receber pela nota é melhor que receber à mão.** A quantidade, o lote, o frete e o IPI vêm da nota, e os títulos a pagar nascem com os vencimentos das duplicatas.
- **Preço diferente do pedido:** o recebimento não muda o preço. Peça a correção a quem aprova em Compras.
- **Material para projeto:** enquanto não é baixado para a OS, ele aparece no projeto como **Comprometido**. Ele vira custo realizado na requisição.
- **Ajuste e lançamento manual acima de R$ 1.000,00** (ou com custo 20% fora do médio) vão para a aprovação de outra pessoa. O saldo só muda depois.

## Os 5 erros mais comuns

1. **Receber à mão o que veio com XML.** Perde a conferência da nota e digita duas vezes. Peça sempre o XML autorizado (`-procNFe.xml`) ao fornecedor.
2. **Comprar fora da solicitação.** O sistema não liga a compra à OS, e a falta continua aparecendo. Compre a partir da solicitação ou da cotação.
3. **Fracionar o pedido para caber na alçada.** O sistema soma os pedidos do mesmo fornecedor em 30 dias. Faça um pedido só.
4. **Saída avulsa em vez de requisição da OS.** O material não entra no custo do projeto certo. Para OS, use a requisição.
5. **Esquecer o lote no item que controla lote.** Sem lote, não há rastreabilidade do elevador entregue. O recebimento pede; não invente número.
