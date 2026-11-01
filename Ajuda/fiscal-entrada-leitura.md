---
titulo: Leitura do PDF da DANFE e nota digitada
rota: /app/fiscal/entrada/leitura/$id
aba:
padrao: true
modulo: fiscal
---

## Para que serve
Quando o fornecedor mandou só o PDF da nota (a DANFE), o sistema lê a chave de acesso, a emissão, o emitente, o destinatário, os totais e os itens, e mostra tudo ao lado do PDF para você **conferir campo a campo**. Quando o PDF não serve, a mesma tela vira **Digitar a nota**. Nada entra no estoque nem no financeiro antes da conferência: a leitura é só uma sugestão.

## Como usar
1. Enquanto aparece **Lendo o PDF…**, aguarde alguns segundos. Se passar de meio minuto, a tela avisa; a leitura para sozinha e você pode **Digitar a nota**.
2. Cada campo mostra a situação em texto: **Lido**, **Leitura incerta: confira** ou **Não encontrado**. O cursor já vai para o primeiro campo a conferir.
3. Compare cada campo com a DANFE. Se estiver certo, marque **Conferido**; se não, corrija o valor (corrigir já marca como conferido).
4. Confira também os itens: código, descrição, NCM, unidade, quantidade, valor unitário e valor total. Use **Adicionar item** ou **Tirar item** se a leitura errou a quantidade de itens.
5. O rodapé mostra quantos campos faltam. **Conferir nota** só fica ativo com todos conferidos e com a soma dos itens igual ao total dos produtos.
6. Depois de conferir, a nota abre como as notas do XML: ligue os itens ao estoque, escolha o pedido e confira a nota. Aí o recebimento é feito pela nota.

## O que o sistema confere
- O PDF passa pelo antivírus e é recusado se tiver JavaScript, arquivo embutido, formulário com ação ou senha. A leitura acontece num leitor isolado, sem acesso à rede nem aos dados.
- A chave de acesso precisa ter 44 dígitos com o dígito verificador certo. Dela saem o número, a série e o CNPJ do emitente; se o texto do PDF mostrar outro CNPJ, vale o da chave.
- A data de emissão precisa ser do mês da chave, o destinatário precisa ser esta empresa e a mesma chave não pode já ter sido importada.

## Perguntas frequentes
**"Este PDF não tem texto para ler."** É um PDF escaneado (só imagem). O sistema não faz leitura de imagem: peça o XML ao fornecedor ou digite a nota.

**"Não conseguimos buscar o XML desta nota automaticamente."** O sistema ainda não baixa o XML pela chave. Clique em **Pedir o XML ao fornecedor**: o fornecedor recebe um e-mail com o número e a chave da nota.

**A nota fica marcada como "Lida do PDF, sem XML" ou "Digitada, sem XML".** A marca aparece em todas as telas e nos títulos a pagar. Quando o XML chegar, abra a nota e use **Substituir pelo XML**.

**"A soma dos itens não bate com o total da nota."** Algum valor de item ou o total dos produtos está diferente da DANFE. Corrija o valor que está errado.
