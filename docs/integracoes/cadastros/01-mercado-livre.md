# Cadastro no Mercado Livre (1º canal)

Antes, veja os [pré-requisitos](00-pre-requisitos.md). Confiança: **média**. O portal bloqueou a leitura automática, então confira os campos marcados.

## Onde e como

1. Entre em uma **conta do Mercado Livre da empresa** (pessoa jurídica, CNPJ do BemAqui). Se não houver, crie uma.
2. Acesse **https://developers.mercadolivre.com.br** → **Minhas aplicações** → **Criar aplicação**.
3. Preencha com as respostas abaixo e salve. O portal gera o **App ID** e a **Secret Key**.
4. Não cole a Secret Key em e-mail nem em chat. Ela entra só pela tela de configuração do ERP quando o MKT-01 estiver pronto, como fazemos com a chave do Emissor.

## Respostas do formulário

| Campo | Resposta |
|---|---|
| Nome | BemAqui Empresas |
| Nome curto | bemaqui-empresas (confirmar no portal) |
| Descrição | ERP do BemAqui para pequenas empresas. Importa os pedidos do Mercado Livre, mantém o estoque igual em todos os canais, emite a NF-e e envia ao Mercado Livre, e lança o repasse e as tarifas no financeiro do vendedor. |
| Logo | Ícone do BemAqui Empresas (512×512) |
| URI de redirect | `https://integracoes.bemaquiapp.com.br/oauth/mercadolivre/callback` |
| URL de retorno de notificações | `https://integracoes.bemaquiapp.com.br/webhooks/mercadolivre` |
| Escopos | Leitura, escrita e **offline_access** (necessário para o *refresh token*) |
| Tópicos de notificação | **orders_v2** (pedidos), **shipments** (envios), **items** (anúncios e estoque) e **payments** (pagamentos). Depois: **questions** e **messages**, quando o Conecta responder perguntas (confirmar no portal os nomes disponíveis no Brasil) |
| Unidades de negócio / site | Mercado Livre Brasil (MLB) |

## Depois do cadastro

- **Certificação de integrador:** existe um programa (https://developers.mercadolivre.com.br/en_us/certification-program). Não conseguimos ler os requisitos. A certificação não bloqueia o começo: o aplicativo funciona sem ela. Vamos pedir quando o MKT-01 estiver em produção com clientes.
- **Inatividade:** o Mercado Livre penaliza aplicativo sem chamadas por 4 meses. Não deixe o cadastro parado depois que o MKT-01 subir.
- **Usuários de teste:** a API permite criar usuários de teste. Serão usados no desenvolvimento, além de uma conta real de vendedor.

## Detalhes técnicos (para o MKT-01)

- Login OAuth 2.0 *authorization code*. O token de acesso dura cerca de 6 h. O *refresh token* é de uso único: cada renovação devolve um novo, e só o último vale. Por isso é guardado cifrado e trocado de forma atômica.
- As notificações chegam todas no endereço central. Ele encaminha para a empresa certa pelo `user_id` do vendedor e **sempre consulta o recurso na API** antes de agir (ADR 0031).
