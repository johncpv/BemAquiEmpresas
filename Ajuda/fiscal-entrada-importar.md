---
titulo: Importar XML da nota de entrada
rota: /app/fiscal/entrada/importar
aba:
padrao: true
modulo: fiscal
---

## Para que serve
Recebe o XML da NF-e que o fornecedor mandou e cria a nota de entrada com o emitente, os itens, os impostos, os totais e as duplicatas. Cada arquivo é conferido sozinho: um arquivo com problema não impede os outros.

## Como usar
1. Arraste os arquivos `.xml` para a área indicada ou clique em **Escolher arquivos** (até 20 de uma vez, 2 MB cada).
2. Clique em **Importar XML**. Cada arquivo mostra o resultado: a nota importada (com o link) ou o motivo da recusa.
3. Abra cada nota importada para ligar os itens e o pedido.

## O que o sistema confere
- O arquivo passa pelo antivírus antes de ser lido e precisa ser um XML de NF-e válido no leiaute oficial 4.00.
- O XML precisa ter o **protocolo de autorização** da SEFAZ (o XML "autorizado", não o enviado para assinatura).
- A **assinatura digital** precisa ser do próprio emitente (certificado ICP-Brasil do mesmo CNPJ) e corresponder a esta nota: qualquer alteração no arquivo derruba a assinatura.
- A nota precisa ser **destinada a esta empresa** (o CNPJ do destinatário).
- A mesma chave não entra duas vezes, exceto se a nota anterior foi recusada.

## Perguntas frequentes
**"Este XML não tem o protocolo de autorização da SEFAZ."** Peça ao fornecedor o XML autorizado (o arquivo `-procNFe.xml`).

**"A assinatura não corresponde a esta nota."** O arquivo foi alterado depois de assinado ou não é o original. Peça ao fornecedor o XML de novo.

**Há limite de importações?** Sim: até 60 arquivos por hora por usuário.
