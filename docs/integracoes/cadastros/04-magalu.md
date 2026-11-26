# Cadastro no Magalu (Magalu Devs) (4º canal)

Antes, veja os [pré-requisitos](00-pre-requisitos.md). Confiança: **alta** na parte técnica, pela documentação oficial. Uma dúvida comercial precisa ser confirmada com o Magalu, no fim deste documento.

## Onde e como

O cadastro **não tem formulário web**: é feito por uma ferramenta de linha de comando. Eu executo os comandos com você, que faz o login.

1. Crie a conta **ID Magalu** da empresa (https://id.magalu.com), como pessoa jurídica.
2. Instale a ferramenta `idm` (https://github.com/luizalabs/id-magalu-cli) e rode `idm login`. O login abre o navegador e é feito por você.
3. Crie o aplicativo com `idm client create`, usando os valores abaixo. A ferramenta devolve o **Client ID** e o **Client Secret**. **O Client Secret não pode ser recuperado depois:** ele vai direto para a tela de configuração do ERP.
4. Alguns escopos precisam de aprovação. Acompanhe com `idm client list`: o status passa de `PENDING` para `AVAILABLE`.

## Valores do `idm client create`

| Opção | Valor |
|---|---|
| `--name` | BemAqui Empresas (é o nome que o vendedor vê no consentimento) |
| `--description` | ERP do BemAqui para pequenas empresas: importa os pedidos do Magalu, sincroniza estoque e preço, emite a NF-e e concilia o repasse no financeiro. |
| `--terms-of-use` | `https://empresas.bemaquiapp.com.br/termos` |
| `--privacy-term` | `https://empresas.bemaquiapp.com.br/privacidade` |
| `--redirect-uris` | `https://integracoes.bemaquiapp.com.br/oauth/magalu/callback` |
| `--scopes` | Pedidos, produtos/SKU, estoque, preço, entregas e financeiro do vendedor. Os nomes exatos estão na lista de escopos do Magalu Devs e são escolhidos na hora de criar |
| `--icon` | Ícone do BemAqui Empresas |
| `--reason` | Integração de ERP multi-vendedor para pequenas empresas brasileiras |

## Detalhes técnicos (para o canal Magalu)

- Login OAuth 2.0 *authorization code*. O vendedor entra com a credencial de pessoa jurídica. O código vale 10 minutos e o token de acesso até 2 h. O `state` identifica a empresa.
- **Webhooks** são configurados pela API, com HTTPS obrigatório e assinatura HMAC-SHA256 nos cabeçalhos `X-Signature-256` e `X-Timestamp`. O segredo gira com 1 h de tolerância, no mesmo padrão do nosso ADR 0031. O aviso só traz a URI do recurso, então sempre consultamos a API.

## A confirmar com o Magalu

Um guia de ERP de dez/2025 diz que o Magalu não liberava a API para ERPs fora do programa de parceiros. A Open API nova, que substitui a IntegraCommerce, sugere que hoje libera. Antes de desenvolver, confirme com o canal de parceiros do Magalu se um ERP pode usar a API para vários vendedores e se existe homologação ou certificação de integrador.
