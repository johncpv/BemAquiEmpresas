---
titulo: Nova solicitação de compra
rota: /app/compras/solicitacoes/nova
aba:
padrao: true
modulo: purchasing
---

## Para que serve
Pedir à mão o que precisa ser comprado: material para um projeto ou para o estoque geral.

## Como usar
- Em cada linha, escolha o **item**, a **quantidade** e a **necessidade** (quando o material precisa estar na fábrica). A quantidade pode ir na unidade de estoque ou noutra com conversão cadastrada; o sistema mostra quanto dá na unidade de estoque.
- A necessidade vem preenchida com hoje mais o prazo padrão de compra, com a tag **Sugerido**. Mude se precisar; a data não pode ser antes de hoje.
- Escolha o **destino**: um projeto em andamento ou **Estoque geral**.
- Clique em **Enviar solicitação**. A solicitação nasce aberta, com o número SC-AAAA-0001, e aparece para quem compra.
- Vindo da falta de uma OS (**Gerar solicitação de compra** na aba Material ou na lista Faltas), o item, a quantidade que falta, o destino e a necessidade já chegam preenchidos.

## Perguntas frequentes
**Posso pedir um serviço?** Não. Serviço não entra na solicitação de compra: lance um título a pagar manual.

**Clicar duas vezes cria duas solicitações?** Não. O mesmo envio é reconhecido e devolve a mesma solicitação.

**Onde mudo o prazo padrão de compra?** Em Configurações → Parâmetros, seção Compras (padrão 15 dias).
