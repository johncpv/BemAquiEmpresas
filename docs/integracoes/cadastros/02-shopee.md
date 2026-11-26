# Cadastro na Shopee Open Platform (2º canal)

Antes, veja os [pré-requisitos](00-pre-requisitos.md). Confiança: **baixa**. O portal bloqueou a leitura automática; o fluxo vem de guias de integradores. **Comece já:** a conta de desenvolvedor e o aplicativo passam por aprovação, e o prazo não é informado.

## Onde e como

1. Acesse **https://open.shopee.com** → **Sign up** e crie a conta de desenvolvedor como **empresa** (Brasil, CNPJ do BemAqui). Ela passa por verificação.
2. Aprovada a conta, vá em **Console → App Management → App List → Create App** e escolha o tipo **ERP System**. É o tipo para sistema que atende vários vendedores.
3. O aplicativo nasce em **teste**, com Partner ID e Partner Key de teste. Desenvolvemos nesse ambiente.
4. Pronto o MKT-02, pedimos **Go Live**. A Shopee revisa e, aprovado, gera o **Partner ID e a Partner Key de produção**.

## Respostas do formulário (o portal é em inglês)

| Campo | Resposta |
|---|---|
| Company name | BEM AQUI SOLUCOES TECNOLOGICA |
| Company registration (CNPJ) | 65.273.540/0001-94 |
| Developer type | Company / Third-party partner — **ERP System** |
| Website | `https://empresas.bemaquiapp.com.br` (precisa estar no ar com HTTPS e TLS 1.2 ou superior) |
| App name | BemAqui Empresas |
| App description | BemAqui Empresas is a cloud ERP for Brazilian small businesses. For Shopee sellers it imports orders, keeps stock in sync across all sales channels, issues the Brazilian NF-e and uploads it to Shopee, prints shipping labels and reconciles Shopee payouts and fees in the seller's finance module. |
| Redirect URL (domain) | `https://integracoes.bemaquiapp.com.br/oauth/shopee/callback` |
| Push / webhook URL | `https://integracoes.bemaquiapp.com.br/webhooks/shopee` (confirmar no portal onde se configura) |
| APIs needed | Shop, Product (item and stock), Order, Logistics, Payment (escrow/income) |
| Test account (for review) | Conta de vendedor de teste com login para os revisores. A Shopee pede isso no Go Live |
| Contact | E-mail institucional e responsável (ver pré-requisitos) |

## Detalhes técnicos (para o MKT-02)

- O vendedor autoriza por um link **assinado com HMAC** (Partner Key), que devolve o `code` e o `shop_id`. O `shop_id` identifica a empresa no roteador central.
- Prazos de token (da memória dos guias, **confirmar**): acesso de cerca de 4 h e *refresh* de cerca de 30 dias. Ler sempre o `expire_in` da resposta.
