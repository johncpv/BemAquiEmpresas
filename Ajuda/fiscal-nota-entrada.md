---
titulo: Nota de entrada — itens e de-para
rota: /app/fiscal/entrada/$id
aba: itens
padrao: true
modulo: fiscal
---

## Para que serve
Liga cada item que o fornecedor faturou ao item do nosso estoque (o **de-para**), com a quantidade e o preço convertidos para a unidade de estoque. O de-para fica lembrado: na próxima nota do mesmo fornecedor, o item já vem ligado.

## Como usar
1. Se o fornecedor ainda não está cadastrado, clique em **Cadastrar fornecedor com os dados da nota**. Se está cadastrado sem o papel de fornecedor, clique em **Adicionar papel**.
2. Para cada item, veja a marca: **Lembrado** (de-para de uma nota anterior) ou **Sugerido** (mesmo NCM ou nome parecido). Aceite a sugestão, escolha outro item ou marque **Sem item de estoque** (serviço ou despesa, com a conta do plano).
3. Quando a unidade do fornecedor é diferente da nossa (caixa e unidade, por exemplo), informe o **fator de conversão**: quanto 1 unidade do fornecedor vale na unidade de estoque.
4. Vá para a aba **Pedido e divergências**.

## Receber pela nota
Depois de **Conferir nota**, quem registra recebimento em Compras clica em **Receber pela nota**: o recebimento do pedido abre preenchido com a quantidade, o lote, o frete, o seguro, as outras despesas e o IPI da nota, e os títulos a pagar nascem com as duplicatas. Recebida, a nota fica em modo leitura com o link do recebimento e dos títulos. Se o recebimento for estornado, a nota volta a **Conferida**.

## Crédito de impostos
O custo de entrada segue o parâmetro **Crédito de impostos na entrada** (Configurações → Fiscal, seção Entrada). O padrão é nenhum crédito: o ICMS, o IPI, o PIS e a COFINS da nota ficam no custo, até a contadora confirmar. A nota recebida mostra a regra usada.

## Perguntas frequentes
**O item não existe no estoque.** Quem pode cadastrar itens clica em **Cadastrar item** e volta para ligar. Ou marque **Sem item de estoque**, se não for material de estoque.

**Errei o de-para.** Enquanto a nota não foi conferida, escolha o item certo de novo. Depois de conferida, a nota não muda.

**A nota mostra "Lida do PDF, sem XML".** Ela foi lida do PDF da DANFE e conferida campo a campo. Segue igual: de-para, pedido, conferência e recebimento. No aviso do topo, **Pedir o XML ao fornecedor** e **Substituir pelo XML** quando ele chegar.
