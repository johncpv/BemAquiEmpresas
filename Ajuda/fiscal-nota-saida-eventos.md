---
titulo: Nota de saída — eventos
rota: /app/fiscal/saida/$id
aba: eventos
padrao: false
modulo: fiscal
---

## Para que serve
Acompanhar a nota na SEFAZ: envio, tentativas, autorização ou rejeição, com protocolo e data.

## Como usar
1. Veja a tentativa atual, o protocolo de autorização e a chave de acesso.
2. Rejeição: o código, o motivo e a ação sugerida ficam aqui e no topo da nota.

## Situações
- Cancelamento e carta de correção entram nesta aba numa próxima versão.

## Perguntas frequentes
**O que é a tentativa?** Cada nova emissão depois de uma rejeição é uma tentativa nova, com outra chave de envio. O número gasto pela rejeição vai para "Números a inutilizar".
