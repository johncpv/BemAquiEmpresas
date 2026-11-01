---
titulo: Notas de saída — a emitir
rota: /app/fiscal/saida
aba: a-emitir
padrao: true
modulo: fiscal
---

## Para que serve
Aqui ficam os **romaneios expedidos que ainda não têm NF-e** e as **instalações com aceite técnico que ainda não têm NFS-e**. A nota do equipamento sai do romaneio, no dia da entrega; a da instalação sai em NFS-e, no aceite técnico. A receita e o imposto do projeto contam pela **data da nota**, não pela parcela do contrato.

## Como usar
1. Veja a fila: projeto, cliente, a data (expedição ou aceite), os dias desde então e o valor estimado pelo contrato. O que está parado há mais de 1 dia útil aparece em destaque, primeiro.
2. Clique em **Emitir NF-e** no romaneio. A nota nasce em rascunho, já preenchida: o cliente do projeto, os itens do romaneio com as séries, o valor do contrato, o transporte, o endereço da obra e os tributos pela regra fiscal.
3. Clique em **Emitir NFS-e** na instalação. A NFS-e nasce em rascunho com os serviços do contrato ainda não faturados e o ISS pela regra de prestação de serviço. Enquanto o Emissor Fiscal não emite a NFS-e de Caçapava, a tela mostra o processo provisório pelo portal da prefeitura.
4. Confira o rascunho e emita (ou registre a nota emitida na prefeitura).

## Situações
- **Fila vazia**: nenhum romaneio nem instalação esperando nota. Use **Ver romaneios** para ir à expedição.
- A mesma fila aparece na **Minha pauta** como "Notas a emitir" e "NFS-e a emitir", para quem emite notas.

## Perguntas frequentes
**Por que o romaneio, e não o projeto?** Só o romaneio baixa o estoque. A nota só referencia o romaneio e nunca baixa de novo. Para faturar o que ainda não saiu, faça antes o romaneio (botão **Faturar** no projeto).

**E a instalação?** Serviço não entra na NF-e: sai em NFS-e, no aceite técnico da OS de instalação.

**Quem pode emitir?** Quem tem a ação de criar no Fiscal. Editar o rascunho exige a ação de editar.
