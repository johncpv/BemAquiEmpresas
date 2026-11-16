# Protótipos do PDV

Dois protótipos navegáveis, em HTML puro. Abra o arquivo no navegador. Os produtos e os preços são exemplos, e nada é gravado.

| Arquivo | Origem | O que mostra |
|---|---|---|
| [prototipo-por-tipo-de-comercio.html](prototipo-por-tipo-de-comercio.html) | Plano (15/11/2026) | Um PDV que muda conforme o tipo de comércio (balcão, mercado, padaria, lanchonete, restaurante, oficina), com as abas Venda, Pedidos (cozinha e painel do cliente), Retaguarda (nota de entrada e precificação) e Caixa (sangria e conferência cega); venda sem internet; NFC-e ou cupom não fiscal; divisão da conta |
| [telas-por-modo-de-venda.html](telas-por-modo-de-venda.html) | Trazido pelo PO (15/11/2026) | Quatro telas de venda, uma por modo: Caixa Rápido (teclado e leitor), Balcão Touch (toque), Mesas e Delivery, Oficina / OS |

## Leitura das quatro telas (15/11/2026)

As quatro telas não são alternativas entre si: cada uma é a tela certa para um jeito de vender. O plano já previa isso como **modo de venda** do caixa, escolhido pelo perfil do negócio. A recomendação é adotar as quatro como referência visual da tela de venda e completar o que elas ainda não mostram.

| Tela | Modo no plano | Segmentos | O que tem de melhor |
|---|---|---|---|
| Caixa Rápido | Leitor e teclado | Supermercado, padaria, conveniência, balcão com leitor | Campo único com foco permanente, `3*código` para quantidade, último item em destaque, total grande, atalhos F2 a F10 |
| Balcão Touch | Grade por toque | Lanchonete, hamburgueria, trailer, autoatendimento | Categorias na lateral, cartões grandes, combo com adicionais, senha e nome para o painel, comer aqui / viagem / delivery |
| Mesas e Delivery | Mesa e comanda | Restaurante, bar, pizzaria | Mapa de mesas com tempo e valor, "enviar cozinha", pré-conta, valor por pessoa, quadro de delivery por canal |
| Oficina / OS | Ordem de serviço | Oficina, assistência, serviços | Placa em destaque, etapas da OS, serviços e peças separados, orçamento por WhatsApp, garantia |

### O que falta nelas e está no plano

| Falta | Onde está no plano |
|---|---|
| Venda sem internet (indicador, fila de pendências, contingência) | OFF-01 a OFF-03 |
| Escolha entre NFC-e e cupom não fiscal (hoje o botão só diz "emitir NFC-e") | PP-05; IMP-01 |
| Divisão da conta por pessoa e por item (hoje só há várias formas na mesma conta) | PDV-03; fluxo de split no 04 |
| Tela da cozinha e painel "Na cozinha / Pedidos prontos" | Fase de food service no 05 |
| Abertura, sangria e fechamento do caixa com conferência cega | CX-01, CX-02 |
| Retaguarda (CPD): nota de entrada e precificação | Fase 12; CPD-01 |
| Supervisor (PIN) em desconto e cancelamento | PDV-04 |
| Peso lido da balança e etiqueta de balança | PDV-02; SPK-01 |
| Tela de celular (o arquivo só cabe a partir de tablet) | Trailer e garçom usam celular |

### Ajustes ao adotar

- **Uma identidade, quatro modos.** As cores mudam por tela no arquivo (azul, laranja, verde, cinza). No produto, a cor é da marca do BemAqui ou do lojista; o que muda por modo é a disposição.
- **Ícones do sistema** no lugar de emoji, como no restante do BemAqui.
- **Um mesmo cliente pode usar mais de um modo** (padaria: leitor no caixa e comanda no salão). O modo é do caixa, não da empresa.
