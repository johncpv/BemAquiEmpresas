---
titulo: Importar PDF da DANFE
rota: /app/fiscal/entrada/importar-pdf
aba:
padrao: true
modulo: fiscal
---

## Para que serve
Recebe o PDF da DANFE quando o fornecedor não mandou o XML da nota. O sistema lê o PDF e abre a tela de conferência com os campos lidos. O XML continua sendo o melhor caminho: a nota lida do PDF não tem assinatura nem protocolo para conferir.

## Como usar
1. Arraste o PDF para a área indicada ou clique em **Escolher arquivo** (um arquivo `.pdf`, até 5 MB, de 1 a 10 páginas).
2. Clique em **Importar PDF**. A tela da leitura abre e mostra **Lendo o PDF…** até os campos ficarem prontos.
3. Sem um PDF que sirva, clique em **Digitar a nota**: o mesmo formulário abre vazio.

## Perguntas frequentes
**"Este PDF não pode ser lido com segurança."** O PDF tem JavaScript, arquivo embutido, formulário com ação ou senha. Peça o XML ao fornecedor.

**Posso importar um PDF escaneado?** Pode enviar, mas o sistema não lê imagem: a tela vai oferecer **Pedir o XML ao fornecedor** e **Digitar a nota**.

**Há limite de envios?** Sim: até 30 PDFs por hora por usuário.
