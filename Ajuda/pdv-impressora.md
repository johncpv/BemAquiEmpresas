---
titulo: Caixa (PDV) — impressora e cupom
rota: /pdv
aba: impressora
padrao: false
modulo: pos
---

## Para que serve
Instalar a impressora de cupom do caixa sem chamar técnico e imprimir o comprovante da venda para o cliente. O comprovante diz **"Não é documento fiscal"**: é um recibo para o cliente conferir o que comprou, não substitui a nota fiscal. A impressão funciona sem internet.

## Como usar
- Na tela do caixa, antes de entrar com o PIN, toque em **Impressora do caixa** e depois em **Instalar impressora**. São três passos:
  1. **Conexão:** por enquanto, **Pelo navegador** (a impressora instalada no computador ou no aparelho, por USB, rede ou Bluetooth do sistema). USB direto, rede e Bluetooth direto chegam numa próxima versão.
  2. **Largura e modelo:** bobina de **58 mm** (32 colunas) ou **80 mm** (48 colunas com a fonte pequena, a maioria; 42 com a fonte normal) e o modelo na lista.
  3. **Imprimir teste:** sai a régua de colunas, os acentos, um QR Code e o corte. Responda **Saiu certo?**. Com "Sim", a impressora é salva (pede o e-mail e a senha de quem registra aparelhos) e vira a padrão do caixa.
- A impressora é do caixa: os outros aparelhos do mesmo caixa recebem a configuração na próxima sincronização.
- Ao concluir a venda, o caixa faz o que a loja escolheu: **perguntar** ("Imprimir" ou "Não imprimir"), **imprimir sempre** ou **não imprimir** (o botão **Imprimir cupom** continua lá).
- Para reimprimir, toque em **Reimprimir** na tela de venda e escolha a venda. A reimpressão sai com **2ª VIA** e fica registrada com quem e quando.
- O CPF do cliente sai **mascarado** no cupom (***.456.789-**). O número inteiro fica só guardado, cifrado, para a devolução.
- O comprovante de sangria e de suprimento sai pela mesma impressora.

## O cupom saiu cortado ou estranho
- **Cortado à direita** (a régua não aparece inteira): no assistente, troque 48 por **42 colunas**, ou confira se a bobina é mesmo de 80 mm.
- **Letra muito pequena ou margem grande:** no diálogo de impressão do navegador, use escala **100%** e margens **Nenhuma**; escolha o papel do tamanho da bobina.
- **Acentos trocados por símbolos:** atualize o driver da impressora no aparelho.
- **O papel não corta sozinho:** o corte automático depende do driver; o corte e a gaveta comandados pelo caixa chegam com a impressão direta.

## Quando dá erro
Se aparecer **"Não foi possível imprimir."**, a venda já está gravada e não muda. Confira se a impressora está ligada, com papel e conectada e toque em **Tentar de novo**, escolha **Outra impressora** ou toque em **Seguir sem imprimir** (dá para reimprimir depois). O navegador nem sempre percebe papel acabado ou impressora desligada: se nada saiu, reimprima.

## Modelos testados
A lista de modelos do assistente ainda não foi testada com o kit de hardware da loja. Os resultados dos testes entram aqui.

## Perguntas frequentes
**Preciso de internet para imprimir?** Não. Só para salvar a impressora nova.

**Dá para enviar o cupom por e-mail ou WhatsApp?** Ainda não. Chega numa próxima versão, junto com o envio das vendas ao servidor.
