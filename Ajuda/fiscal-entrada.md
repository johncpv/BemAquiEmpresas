---
titulo: Notas de entrada
rota: /app/fiscal/entrada
aba: entrada
padrao: true
modulo: fiscal
---

## Para que serve
Lista as notas fiscais eletrônicas (NF-e) que os fornecedores mandaram, importadas pelo XML (ou, sem o XML, lidas do PDF da DANFE ou digitadas e conferidas campo a campo): número, fornecedor, emissão, total e a situação de cada uma. Daqui você confere a nota contra o pedido de compra antes de receber o material.

## Como usar
1. Clique em **Importar XML** e solte os arquivos `.xml` que o fornecedor enviou (até 20 de uma vez, 2 MB cada).
2. Abra a nota importada, ligue cada item ao item do estoque (o **de-para**) e escolha o pedido de compra.
3. Confira as diferenças de quantidade e de preço e clique em **Conferir nota**. Depois, o recebimento é feito pela nota.
4. Use o filtro de situação para achar as notas **Importadas** (ainda a conferir), **Conferidas**, **Recebidas** ou **Recusadas**.
5. Sem o XML, use **Importar PDF da DANFE** (o sistema lê o PDF e você confere cada campo) ou **Digitar a nota**. As leituras ainda não conferidas aparecem no topo da lista, em **Leituras e notas digitadas a conferir**.

## Situações
- **Importada**: o XML foi aceito; falta ligar os itens e o pedido.
- **Conferida**: os itens estão ligados e as divergências foram registradas; pode ser recebida.
- **Recebida**: o material deu entrada pelo recebimento da nota.
- **Recusada**: a nota foi recusada com o motivo; a mesma chave pode ser importada de novo.

## Perguntas frequentes
**Quem importa a nota?** Quem tem a permissão **Importar nota de entrada** do Fiscal (o Almoxarifado, no padrão). Emitir nota de saída não dá essa permissão.

**O sistema confere se a nota foi cancelada?** Não. Ele confere a assinatura e o protocolo de autorização, mas não consulta a SEFAZ depois. Em caso de dúvida, consulte a chave no portal da SEFAZ.

**O que quer dizer "Lida do PDF, sem XML"?** A nota foi lida do PDF da DANFE e conferida por alguém, mas o sistema não tem o XML (sem assinatura nem protocolo). "Digitada, sem XML" é a nota digitada. Quando o XML chegar, abra a nota e use **Substituir pelo XML**.
