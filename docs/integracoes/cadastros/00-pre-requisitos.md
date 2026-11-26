# Cadastro do BemAqui como integrador nos marketplaces: pré-requisitos

Vale para os cinco canais (Mercado Livre, Shopee, Amazon, Magalu, TikTok Shop). Plano: [00-plano-integracoes.md](../00-plano-integracoes.md) · [ADR 0052](../../adr/0052-integracoes-conecta-e-marketplaces.md). Levantamento de 26/11/2026. Os portais do Mercado Livre e da Shopee bloquearam a leitura automática, então os campos marcados **(confirmar no portal)** vêm de guias de terceiros.

## Dados da empresa (iguais em todos os cadastros)

| Campo | Valor |
|---|---|
| Razão social | BEM AQUI SOLUCOES TECNOLOGICA (confirmar a grafia completa no cartão CNPJ) |
| CNPJ | 65.273.540/0001-94 |
| Nome do produto / do aplicativo | **BemAqui Empresas** |
| Tipo de cadastro | Desenvolvedor **público** / integrador (ISV) que atende vários vendedores. Não é integração própria de um vendedor |
| País | Brasil |
| Contato técnico e de segurança | E-mail institucional (sugestão: `integracoes@bemaquiapp.com.br`) e o responsável. Use e-mail do domínio, não pessoal |
| Logo | Ícone do BemAqui Empresas em PNG quadrado (512×512) |

## O que precisa existir ANTES de enviar

| # | Item | Por quê | Quem |
|---|---|---|---|
| 1 | **Site público do BemAqui Empresas** (por exemplo `https://empresas.bemaquiapp.com.br`), sem login, com HTTPS válido, descrevendo o ERP e as integrações | A Amazon recusa site com login ou aviso de segurança, e a Shopee pede a URL do produto no ar. Hoje `bemaquiapp.com.br` abre o BemAqui Empregos | Eu faço a página; o DNS fica com você |
| 2 | **Política de privacidade** e **termos de uso** do ERP (`/privacidade` e `/termos` do site acima), com dados de compradores de marketplace, finalidade, retenção (NF-e: 5 anos), compartilhamento, direitos do titular (LGPD) e contato do encarregado | Obrigatórias na Amazon (fase 1 dos papéis restritos), no Magalu (`--privacy-term`, `--terms-of-use`) e na Shopee | Eu redijo a minuta; a revisão jurídica fica com você |
| 3 | **Domínio central de integrações** `https://integracoes.bemaquiapp.com.br` | Cada canal aceita **um endereço de retorno e um de avisos por aplicativo**. Como o ERP tem uma empresa por domínio, esse endereço central recebe tudo e encaminha para a empresa certa pelo `state` assinado (login) e pelo id do vendedor (avisos) | DNS com você; o roteador fica comigo (MKT-00) |
| 4 | **Conta da empresa em cada canal**, no CNPJ acima (Mercado Livre PJ, ID Magalu PJ, Seller Central ou Solution Provider Portal da Amazon, Open Platform da Shopee, Partner Center do TikTok Shop) | Cada cadastro de desenvolvedor nasce de uma conta | Você |
| 5 | **Conta de vendedor para teste** (do BemAqui ou de um cliente piloto) | A Shopee pede credencial de teste na revisão; o Mercado Livre e os outros precisam de vendedor real para validar | Você |

## Endereços usados nos formulários

| Uso | Endereço |
|---|---|
| Site do produto | `https://empresas.bemaquiapp.com.br` |
| Política de privacidade | `https://empresas.bemaquiapp.com.br/privacidade` |
| Termos de uso | `https://empresas.bemaquiapp.com.br/termos` |
| Retorno do login (OAuth redirect) | `https://integracoes.bemaquiapp.com.br/oauth/<canal>/callback` |
| Avisos (webhook / notificações) | `https://integracoes.bemaquiapp.com.br/webhooks/<canal>` |

`<canal>` = `mercadolivre`, `shopee`, `amazon`, `magalu`, `tiktokshop`.

## Textos-base

**Descrição curta (PT):** ERP do BemAqui para pequenas empresas. Importa os pedidos do marketplace, mantém o estoque igual em todos os canais, emite a NF-e e lança o repasse e as tarifas no financeiro.

**Descrição curta (EN):** BemAqui Empresas is a cloud ERP for Brazilian small businesses. It imports marketplace orders, keeps inventory in sync across sales channels, issues the Brazilian electronic invoice (NF-e) and reconciles marketplace payouts and fees in the seller's finance module.

**Como protegemos o dado (resumo verdadeiro, usado nos questionários).** São controles que **já existem**:
- banco de dados separado por empresa;
- tráfego só em HTTPS;
- credenciais de integração cifradas (ASP.NET Data Protection, com chaves protegidas por certificado) e só de escrita;
- tokens guardados apenas em hash;
- acesso por grupos e permissões em três camadas;
- 2FA obrigatório para quem aprova ou paga;
- auditoria de quem fez o quê, com valores antes e depois;
- segredos mascarados nos logs;
- servidores na Hostinger.

**Lacunas a fechar antes da Amazon com papel restrito:**
- backup externo do ambiente SaaS, que hoje é só o dump antes de cada deploy;
- cifragem em repouso do dado pessoal do comprador;
- eliminação do dado pessoal após o prazo;
- plano de resposta a incidente por escrito.

Nada disso pode ser declarado como pronto antes de existir.
