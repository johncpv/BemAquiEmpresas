---
titulo: Notas de saída — a emitir
rota: /app/fiscal/saida
aba: a-emitir
padrao: true
modulo: fiscal
---

## Para que serve
Aqui ficam os **romaneios expedidos que ainda não têm NF-e**. A nota do equipamento sai do romaneio, no dia da entrega: a receita e o imposto do projeto contam pela **data da nota**, não pela parcela do contrato.

## Como usar
1. Veja a fila: projeto, cliente, data da expedição, dias desde a expedição e o valor estimado pelo contrato. O que está parado há mais de 1 dia útil aparece em destaque, primeiro.
2. Clique em **Emitir NF-e** no romaneio. A nota nasce em rascunho, já preenchida: o cliente do projeto, os itens do romaneio com as séries, o valor do contrato, o transporte, o endereço da obra e os tributos pela regra fiscal.
3. Confira o rascunho e clique em **Emitir NF-e** na nota.

## Situações
- **Fila vazia**: nenhum romaneio esperando nota. Use **Ver romaneios** para ir à expedição.
- A mesma fila aparece na **Minha pauta** como "Notas a emitir", para quem emite notas.

## Perguntas frequentes
**Por que o romaneio, e não o projeto?** Só o romaneio baixa o estoque. A nota só referencia o romaneio e nunca baixa de novo. Para faturar o que ainda não saiu, faça antes o romaneio (botão **Faturar** no projeto).

**E a instalação?** Serviço não entra na NF-e: sai em NFS-e, no aceite técnico.

**Quem pode emitir?** Quem tem a ação de criar no Fiscal. Editar o rascunho exige a ação de editar.
