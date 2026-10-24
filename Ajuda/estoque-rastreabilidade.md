---
titulo: Rastreabilidade
rota: /app/estoque/rastreabilidade
aba: rastreabilidade
padrao: true
modulo: inventory
---

## Para que serve
Responder rápido a uma reclamação ou a um recall de fornecedor: a partir de uma **série**, ver de qual OS, projeto e cliente ela é e de quais lotes e fornecedores veio o material; a partir de um **lote**, ver em quais OS, equipamentos e clientes ele foi parar.

## Como usar
- Digite a **série** ou o **número do lote** em **Série ou lote** e clique em **Rastrear**. Vale o número inteiro ou o começo dele. A busca mostra até 50 séries e 50 lotes.
- A tela reconhece a série pelo formato código do item, ano e sequência (`ELV-0001-2026-00001`) e mostra as séries primeiro. Com um só resultado, o rastro abre direto.
- **Série:** a árvore mostra o item e a situação (**Em estoque**, **Expedido**), a **OS** de origem com a data de conclusão, o **projeto**, o **cliente**, o **romaneio** de expedição e os **lotes de matéria-prima consumidos**, com fornecedor, validade e o recebimento (número e data).
- **Lote:** a árvore mostra as **entradas**, o **saldo atual** por local, o **consumo nas OS** (projeto, cliente, data e quantidade) com as séries produzidas e o romaneio de cada série expedida, e as **outras saídas e ajustes**.
- A linha **Conta fechada** mostra a conta do lote: recebido − consumido + outras = saldo. **Diferença na conta** pede conferência dos movimentos do lote.
- Clique nos links para abrir a OS, o projeto, o cliente, o romaneio, o recebimento ou o movimento. **Ver destino do lote** leva do lote consumido pela série para onde mais ele foi usado.
- Use as setas do teclado na árvore: para cima e para baixo movem, direita abre, esquerda fecha.
- **Exportar CSV** (no lote) baixa o destino completo para a planilha. A exportação fica registrada no histórico do lote. No celular, o botão fica em **Mais ações**.
- No celular, a árvore vira uma lista com abrir e fechar, e a quantidade aparece embaixo de cada linha.

## Perguntas frequentes
**Por que a série mostra os lotes da OS inteira?** A OS baixa o material para a OS toda, e não por unidade. Uma OS de 3 unidades mostra a observação "Lotes da OS ... (3 unidades): a OS não separa o material por unidade.".

**Por que o cliente aparece só com as iniciais?** Cliente pessoa física aparece por extenso só para quem tem acesso a terceiros. Sem esse acesso, aparecem as iniciais ("J. M. R."). CPF e CNPJ nunca aparecem aqui nem no CSV.

**O que é "Sem série: OS concluída antes do registro de séries."?** A OS foi concluída antes de o sistema gravar séries do produto acabado. O consumo do lote aparece, mas não há série para mostrar.

**A busca aceita % ou _?** Esses caracteres são procurados como texto, não como curinga.
