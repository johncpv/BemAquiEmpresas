# Cadastro na Amazon (SP-API) como desenvolvedor público (3º canal)

Antes, veja os [pré-requisitos](00-pre-requisitos.md). Confiança: **alta**, pela documentação oficial. **Comece já:** é a revisão mais exigente.

## Onde e como

1. Acesse o **Solution Provider Portal** (https://solutionproviderportal.amazon.com) ou, a partir de uma conta Seller Central, **Apps and Services → Develop Apps**.
2. Preencha o **Developer Profile** como **Public Developer**, porque atendemos vários vendedores.
3. A Amazon revisa a funcionalidade, a política de uso aceitável (AUP), a política de proteção de dados (DPP) e as respostas de segurança. O prazo citado por terceiros é de 1 a 2 semanas. **Se a Amazon fizer perguntas, responda em até 5 dias**, ou o caso é fechado.
4. Aprovado o perfil, crie o **aplicativo** (fica em *Draft*) e teste com `version=beta`, com poucos vendedores autorizados. Para atender qualquer vendedor, o aplicativo precisa ser **publicado no Selling Partner Appstore**.

## Papéis (roles): em duas etapas

| Etapa | Papéis | Por quê |
|---|---|---|
| **1 – agora** | **Product Listing**, **Inventory and Order Tracking** (sem dado pessoal), **Pricing**, **Finance and Accounting** | Sincroniza estoque e preço, recebe pedidos sem dado do comprador e concilia o repasse |
| **2 – depois de fechar as lacunas** | **Direct-to-Consumer Shipping** (restrito) e **Tax Invoicing** (restrito) | Nome, endereço e CPF do comprador para emitir a NF-e e enviar. Exige verificação do negócio, avaliação de segurança e revisão de arquitetura |

A etapa 2 só sai depois das lacunas listadas nos pré-requisitos: backup externo, cifragem em repouso do dado pessoal, eliminação após o prazo e plano de incidente. A Amazon exige isso na DPP.

## Respostas do Developer Profile (em inglês)

| Field | Answer |
|---|---|
| Organization name | BEM AQUI SOLUCOES TECNOLOGICA |
| Organization website | `https://empresas.bemaquiapp.com.br` |
| Country | Brazil |
| Primary contact | Responsável e e-mail institucional (ver pré-requisitos) |
| Developer type | Public developer — we build a multi-seller application |
| Use cases | Order management, inventory synchronization across channels, pricing, invoice (NF-e) issuance, finance/payout reconciliation |
| Marketplace | Amazon.com.br (Brazil) |

**Perguntas de segurança.** O limite é de 500 caracteres cada. Escreva com as próprias palavras, sem copiar texto da Amazon, e **só declare o que já existe**. Rascunhos:

- **Network protection:** "Our ERP runs on dedicated containers behind a reverse proxy; only HTTPS (TLS 1.2+) is exposed. Admin access to servers is restricted to SSH keys. Each customer has its own isolated PostgreSQL database."
- **Access management:** "Role-based access with groups and permissions checked on every API call. Two-factor authentication is mandatory for users who approve or pay. Access is revoked when an employee is terminated. Every change is recorded in an audit log with before/after values."
- **Credential and secret storage:** "Integration credentials are encrypted with ASP.NET Data Protection (keys protected by an X.509 certificate) and are write-only in the UI. Tokens are stored only as hashes. Secrets are masked in logs."
- **Incident response:** (escrever só depois de o plano de incidente existir por escrito)
- **Data retention and deletion:** (escrever só depois de implementada a eliminação do dado pessoal após a entrega, respeitada a guarda fiscal da NF-e de 5 anos)

## Aplicativo

| Field | Answer |
|---|---|
| App name | BemAqui Empresas |
| OAuth Login URI | `https://integracoes.bemaquiapp.com.br/oauth/amazon/login` |
| OAuth Redirect URI | `https://integracoes.bemaquiapp.com.br/oauth/amazon/callback` |

## Detalhes técnicos (para o MKT-03)

- O código de autorização vale 5 minutos, e o fluxo inteiro precisa terminar em 10. O token de acesso LWA dura 1 h. O *refresh token* não tem validade fixa, e a Amazon recomenda reautorização anual.
- **Notificações não são webhook HTTP:** chegam por **Amazon SQS ou EventBridge**. O MKT-03 precisa de um consumidor de fila no endereço central, roteando pelo `selling_partner_id`.
- O dado pessoal vem só com **Restricted Data Token**, válido por 1 hora.
