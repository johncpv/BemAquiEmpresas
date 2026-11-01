---
titulo: Nota de saída — dados
rota: /app/fiscal/saida/$id
aba: dados
padrao: true
modulo: fiscal
---

## Para que serve
Conferir e emitir a NF-e do equipamento expedido: destinatário, operação, transporte, informações complementares e os totais. Tudo vem preenchido do romaneio, do projeto e da regra fiscal.

## Como usar
1. Confira o destinatário (vem do cadastro do cliente). Para corrigir documento, IE ou endereço, corrija no cadastro do cliente e descarte e refaça o rascunho.
2. Ajuste frete, outras despesas, as informações complementares e o envio ao cliente, e clique em **Salvar rascunho**.
3. Clique em **Pré-visualizar** para ver o DANFE com a faixa "Prévia, sem valor fiscal" (não gasta número).
4. Clique em **Emitir NF-e**. O diálogo mostra o destinatário, os itens, os totais, os impostos, o que entra no custo do projeto e o ambiente. Se faltar algo (integração, configuração, certificado, regra, endereço com IBGE), o diálogo lista cada item com o link para resolver.
5. Depois do envio: "Nota enviada à SEFAZ. Aguardando a autorização…". Se a SEFAZ demorar mais de 30 segundos, a tela avisa e a conferência automática segue. Nunca emita de novo enquanto processa.

## Situações
- **Rejeitada**: o motivo e a ação sugerida aparecem em destaque. Use **Corrigir e emitir de novo**: a nota volta a rascunho com os dados; o número gasto vai para "Números a inutilizar".
- **Denegada**: a SEFAZ denegou por irregularidade do destinatário. Fale com a contadora; a nota não volta.
- **Autorizada**: o XML e o DANFE ficam em Documentos e vão para o e-mail de cobrança do cliente (se "Enviar ao cliente" estiver marcado).

## Perguntas frequentes
**Posso digitar o imposto?** Não. Os tributos saem da regra fiscal. Mude a regra do item (entre as ativas que valem) ou a quantidade, o valor e o desconto, e o cálculo refaz na hora.

**A nota cria título a receber?** Não. A parcela do contrato é só cobrança; a receita conta pela data da nota.

**Descartar apaga?** Não. O rascunho descartado sai da fila e fica registrado, sem número. Nota com número só é cancelada ou inutilizada.
