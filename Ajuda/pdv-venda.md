---
titulo: Caixa (PDV) — tela de venda
rota: /pdv
aba: venda
padrao: false
modulo: pos
---

## Para que serve
Lançar os itens da venda tão rápido quanto você bipa ou toca. A tela trabalha só com os dados guardados neste aparelho: com ou sem internet, ela funciona do mesmo jeito; a única diferença é a faixa de estado no topo. Cada ação (item lançado, quantidade, remoção, espera, descarte) fica gravada no aparelho na hora: se a energia cair ou a aba fechar, ao abrir de novo a venda está como estava.

## Como usar
- **Abrir o caixa:** depois de entrar com o seu PIN, toque em **Abrir caixa**. Sem isso a tela mostra "Abra o caixa para começar a vender.".
- **Bipar:** passe o produto no leitor. Ele entra com quantidade 1 e o preço da tabela da loja. O código da caixa fechada (ex.: caixa com 12) já entra com a quantidade da embalagem. O leitor funciona mesmo com o cursor em outro campo.
- **Produto não encontrado:** o caixa apita e mostra "Produto não encontrado: 789…". A venda não muda; confira o código ou busque pelo nome.
- **Quantidade antes do código:** digite `3*` e bipe (ou digite o código): entram 3. Em produto pesável, use vírgula: `0,350*código`.
- **Buscar pelo nome:** digite 3 letras do nome (ex.: "arr" acha "Arroz"). No modo Teclado, escolha com as setas e Enter; no modo Toque, toque no produto.
- **Produto pesável:** pede a quantidade (até 3 casas, ex.: 1,250). Produto por unidade só aceita número inteiro.
- **Mesmo produto duas vezes:** soma na mesma linha. Em **Ajustes do caixa** dá para trocar para uma linha por lançamento.
- **Remover:** o último item lançado sai direto (bipou errado). Outra linha pede a permissão de remover itens no caixa. Toda remoção fica registrada.
- **Observação:** escolha a linha e use **Observação** (F4): ela sai no cupom.
- **Deixar em espera:** o cliente esqueceu algo? Use **Deixar em espera** (F7) e atenda o próximo. Em **Em espera** aparecem as vendas guardadas, com a hora, os itens e o total; **Retomar** traz a venda de volta.
- **Descartar a venda:** pede o motivo e fica registrado. A venda descartada ganha o número dela na hora.
- **Cliente:** opcional. Digite o CPF ou o CNPJ na nota; o caixa confere o dígito na hora. Com internet, também dá para buscar o cliente cadastrado pelo nome (o CPF vai no campo de documento da venda, não na busca); sem internet, aparece "Disponível com internet." e o CPF avulso continua funcionando.
- **Fechar o caixa:** se houver venda na tela ou em espera, o caixa pede para retomar ou descartar antes.

## Atalhos do modo Teclado
- **F2:** buscar (volta ao campo de código).
- **F3:** quantidade da linha escolhida.
- **F4:** observação da linha.
- **F6:** remover item.
- **F7:** deixar em espera (sem itens na tela: ver as vendas em espera).
- **F8:** cliente (CPF ou CNPJ).
- **F9:** descartar a venda.
- **F10:** pagar.
- **Setas para cima e para baixo:** escolher a linha (ou o produto na lista da busca).
- **`3*código`:** lançar 3 do código.

## Perguntas frequentes
**Modo Toque ou Teclado?** O padrão vem do cadastro do caixa (Loja → Lojas e caixas). No caixa, **Ajustes do caixa** troca o modo deste aparelho.

**O CPF do cliente fica guardado?** Só neste aparelho, cifrado, fora do registro da venda, para a nota e para achar a compra numa troca. Ele nunca aparece em log nem no endereço da página e pode ser apagado (**Tirar cliente**).

**Por que o produto aparece "sem preço"?** Ele não tem preço na tabela da loja. Cadastre o preço na retaguarda (Catálogo de produtos); o caixa recebe na próxima sincronização.

**E o pagamento?** O recebimento (dinheiro, Pix, cartão e troco) chega na próxima versão do caixa. Até lá, a venda fica guardada no aparelho.
