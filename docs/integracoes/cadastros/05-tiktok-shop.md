# Cadastro no TikTok Shop Partner Center (5º canal, sob demanda)

Antes, veja os [pré-requisitos](00-pre-requisitos.md). Confiança: **baixa a média**. Não conseguimos ler o Partner Center; o fluxo vem de guias de integradores. Pela decisão do PO, este canal entra **quando houver cliente do segmento** (moda, beleza, acessórios). O cadastro pode ser feito antes, porque a revisão do aplicativo público leva **mais de 3 semanas**, segundo relatos dos EUA e do Reino Unido.

## Onde e como

1. Crie a conta no **TikTok Shop Partner Center** escolhendo a região **Brasil**. A região fica fixa na criação; conta de outra região não serve.
2. Vá em **App & Service → Create app & service** e escolha **Public**. O aplicativo público aparece na loja de aplicativos do TikTok Shop e atende qualquer vendedor. Dá para começar como **Custom**, só para vendedores escolhidos, e converter depois.
3. Preencha os dados, ative **Enable API** e envie para revisão.

## Respostas do formulário

| Campo | Resposta |
|---|---|
| App name | BemAqui ERP |
| Category / service type | ERP / Order management |
| Description (EN) | BemAqui ERP is a cloud ERP for Brazilian small businesses. For TikTok Shop sellers it imports orders, keeps stock in sync across channels, issues the Brazilian NF-e and reconciles payouts and fees in the seller's finance module. |
| Redirect URL | `https://integracoes.bemaquiapp.com.br/oauth/tiktokshop/callback` |
| Webhook URL | `https://integracoes.bemaquiapp.com.br/webhooks/tiktokshop` |
| Permissions | Products, inventory, orders, fulfillment/logistics, finance |
| Empresa | Razão social, CNPJ e contato (ver pré-requisitos) |

## Detalhes técnicos (para quando o canal entrar)

- Login OAuth com `code`, que dá o token de acesso e o *refresh token*. As fontes divergem sobre a validade, então leia `expires_in` e `refresh_expires_in` da resposta.
- Os webhooks chegam pelo menos uma vez, com 4 novas tentativas e assinatura no cabeçalho `Authorization`. Existe um aviso de **status de nota fiscal exclusivo do Brasil**, útil para a NF-e.
