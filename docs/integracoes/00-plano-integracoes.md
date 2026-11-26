# Plano de integrações: BemAqui Atende/Conecta e marketplaces

Proposta de 26/11/2026, para decisão do PO. Nada aqui está decidido até virar ADR. Itens em aberto na seção 7.

## 1. Resumo

Três frentes, com uma base em comum:

1. **ERP ↔ BemAqui Atende (e o app Conecta), conectados com um clique.** O Atende passa a vender pelo ERP: consulta produto, preço e estoque, cria o pedido, manda o Pix ou o boleto, acompanha o pedido e avisa o cliente sozinho em cada etapa.
2. **Marketplaces:** Mercado Livre primeiro, depois Shopee e Amazon. Sugerimos Magalu e TikTok Shop na sequência, e Nuvemshop e Tray para loja própria (ECM-07, já no backlog do PDV).
3. **API pública e MCP**, para empatar com concorrentes como o Nibo e para clientes ligarem os próprios sistemas e agentes de IA.

**A base é a mesma para as três:** um **pedido de venda** no ERP, mais **aplicativos conectados** (autorização OAuth com escopos, revogável) e **webhooks de saída** assinados. Construída uma vez, ela serve ao Atende, aos marketplaces e à API pública.

## 2. O que existe hoje

| | ERP (BemAqui Empresas) | BemAqui Atende |
|---|---|---|
| Venda | Módulo `Sales` com canais (`Pos`, `Online`, `ServiceOrder`), linhas, pagamentos e `SaleCompleted` com baixa de estoque (VND-01). **Não existe pedido em aberto** (aguardando pagamento, separação, envio) nem rota para criar venda de fora. Pedidos estão no ECM-04 (Draft). | **Não tem catálogo, carrinho nem pedido.** O "catálogo" é um JSON no agente de IA. O que mais se parece com venda é o `Deal` do funil do CRM. |
| Autenticação entre sistemas | **Não existe.** Só sessão de pessoa (JWT), PIN de aparelho e token de TV. O ERP só é cliente (EmissorFiscal, Inter). | Chave de API por empresa (`X-Api-Key`, guardada só em hash), com leitura de contatos e conversas. |
| Webhooks | Entrada genérica `/webhooks/{provedor}/{tenantKey}` com validação de origem obrigatória, deduplicação e nova consulta ao provedor (ADR 0031). **Sem webhook de saída.** O outbox é interno. | Entrada para sistemas externos (`contact_sync`, `purchase_confirmed`, `repurchase_trigger`) ligada ao contato por `ExternalRef`. **Sem webhook de saída.** |
| Segredos de integração | Cifrados por empresa (Data Protection, ADR 0007), só de escrita, troca com 2FA. | Cifrados para a chave de IA (`IEncryptionService`). **Sem entidade de credencial de integração por empresa.** |
| WhatsApp | Planejado (WPP-01), à espera da escolha de um BSP e dos modelos de mensagem (P-29). | **Pronto:** API oficial da Meta com Embedded Signup, Evolution, Instagram, Messenger e Telegram. |
| Precedente | — | Pedido pelo agente de IA no Yampi, com link de pagamento e consulta periódica do pagamento. É só para um cliente (Curamedic) e com regex no texto, mas valida o fluxo. |

Observação: **"Conecta" é o app de celular dos atendentes do Atende.** A integração é feita no Atende (API e web), e o Conecta mostra o que o Atende expuser.

## 3. Frente A: ERP ↔ Atende, com um clique

### 3.1 Como o usuário vê

1. No ERP, em **Configurações → Integrações → BemAqui Atende**, o Proprietário clica em **Conectar**. Também dá para começar pelo Atende, em Integrações → BemAqui Empresas.
2. Abre a tela do Atende, já logado ou pedindo login, para escolher a conta do Atende.
3. Volta ao ERP numa tela de consentimento: *"BemAqui Atende quer: ver produtos, preços e estoque; criar e acompanhar pedidos; ver e enviar cobranças; ver e atualizar clientes."* O Proprietário confirma com o código do autenticador (2FA).
4. Pronto. Os dois lados mostram **Conectado em dd/mm por Fulano**, com o botão **Desconectar**.

Não há chave para copiar e colar. A conexão pode ser desfeita dos dois lados e fica registrada na auditoria.

### 3.2 Como funciona por dentro (proposta)

- **Aplicativos conectados no ERP (INT-01):** autorização OAuth 2.0 com *authorization code* e PKCE. O Atende é um aplicativo **primeira parte**, pré-cadastrado em toda empresa. O token de acesso é curto, o *refresh token* gira a cada uso, e o ERP guarda só o hash. A mesma peça depois serve à API pública (seção 5).
- **Escopos = módulo.ação do catálogo** (ADR 0003): `catalog.view`, `inventory.view`, `parties.view|create|edit`, `sales.view|create`, `finance.view` (cobranças) e `fiscal.view` (DANFE).
- **Ninguém concede o que não tem** (ADR 0008). O aplicativo recebe a interseção entre os escopos pedidos, as permissões de quem autorizou e os módulos liberados. As três camadas de acesso continuam valendo.
- **Na auditoria**, o autor aparece como *"Integração BemAqui Atende (autorizada por Fulano)"*.
- **Empresa pelo endereço:** o Atende guarda o endereço do ERP da empresa, por exemplo `erp.empresa.com.br`, e chama sempre por ele, como o resto do sistema.
- **Webhooks de saída (INT-02):** inscrição por evento, corpo assinado com HMAC-SHA256 sobre `{timestamp}.{corpo}`, nova tentativa com espera crescente pelo Hangfire, e registro de entregas em **Integrações → Eventos**, que já existe para a entrada. O segredo é gerado na conexão e gira com sobreposição de 24 h, como o do EmissorFiscal.
- **No Atende (ATD-01):** credencial de integração por empresa, cifrada com o `IEncryptionService`, e endpoint de recebimento validando a mesma assinatura. Toda mudança no Atende segue o fluxo combinado com o Thiago: branch, merge na main e depois deploy.

### 3.3 O que o Atende passa a fazer

| Capacidade | Quem usa | ERP | Atende/Conecta |
|---|---|---|---|
| Achar o cliente pelo telefone ou CPF e ligar o contato ao cadastro do ERP | Atendente e agente de IA | `parties` (busca e criação) | `Contact.ExternalRef` |
| Painel do cliente na conversa: pedidos, cobranças em aberto, notas, 2ª via | Atendente (web e Conecta) | Leitura de vendas, financeiro e fiscal | Painel lateral |
| Consultar produto, preço e estoque disponível | Atendente e agente de IA | `catalog` + saldo menos reserva | Busca na conversa |
| **Criar pedido** com reserva de estoque | Atendente e agente de IA | **Pedido de venda (VND-02)** | Formulário e ferramenta da IA |
| Cobrar por Pix ou boleto com Pix (Inter) e mandar o link | Atendente, ou automático | Bolepix existente | Mensagem com o link |
| Avisar o cliente sozinho: pagamento confirmado, nota emitida, pedido enviado ou entregue | Automação | Webhooks de saída | Automações e modelos de mensagem |
| Atualizar a etapa do negócio (Deal) no CRM conforme o pedido anda | Automação | Eventos | Funil do CRM |
| Para fábrica (Konebras): status do projeto e 2ª via de boleto | Atendente e agente de IA | `projects.view`, `finance.view` | Painel |

**Agente de IA:** trocar o padrão de texto `[PEDIDO]...[/PEDIDO]` lido por regex por **ferramentas (function calling)**: `buscar_produto`, `ver_estoque`, `criar_pedido`, `status_pedido` e `segunda_via`. A IA **sugere, mas não decide**: um pedido criado pela IA entra como "aguardando confirmação do cliente", e o efeito financeiro e de estoque só vale com o pagamento. O Yampi da Curamedic migra para esse padrão quando fizer sentido.

### 3.4 Bônus: o WhatsApp do ERP sai pelo Atende

O WPP-01 (avisos de cobrança e de status por WhatsApp) está parado à espera de um BSP (P-29). **O Atende já tem a API oficial da Meta.** Proposta: o `IWhatsAppSender` do ERP passa a ter um adaptador "via BemAqui Atende", que usa o número da empresa já conectado lá. Isso tira a contratação de um BSP separado e põe o histórico da mensagem na mesma caixa de entrada do atendimento.

## 4. Frente B: marketplaces

### 4.1 Mercado (dados de 2025–2026, estimativas de bancos)

| Canal | Participação estimada (GMV) | Observação |
|---|---|---|
| Mercado Livre | ~42–47% | Líder, API aberta, OAuth, notificações por tópico, envio de NF-e e Mercado Envios |
| Shopee | ~14–19% | O app "ERP System" passa por aprovação da Shopee: **pedir já** |
| Amazon | ~11–12% | SP-API: funções revisadas, dado pessoal só com função restrita e revisão de segurança: **pedir já** |
| Magalu | ~12% | Programa de integradores certificados e nova API (OpenAPI) |
| TikTok Shop | ~1,6% (2025), crescendo rápido | Projeção de ~R$ 20 bi anualizados em 2026 |
| Shein, Temu | Muito acesso, pouco GMV para o vendedor nacional | Esperar |

Loja própria: **Nuvemshop e Tray** (ECM-07), depois Loja Integrada, Shopify e WooCommerce.

**Sugestão de ordem:** Mercado Livre → Shopee → Amazon → Magalu → TikTok Shop, com Nuvemshop e Tray em paralelo, porque usam a mesma base. Frete: **Melhor Envio** para quem vende fora do Mercado Envios.

### 4.2 Piso de mercado (o que Bling, Tiny e Omie entregam)

Importar pedidos; **estoque único** sincronizado entre loja física, site e marketplaces (benchmark do PDV, item 35); preço por canal; NF-e emitida no ERP e enviada ao marketplace; etiqueta e frete; e **conciliação do repasse** (valor líquido, comissão e tarifa de frete viram lançamento financeiro). A conciliação é o nosso diferencial natural: **margem real por canal, já descontadas as tarifas**, que é o objetivo central do produto. Publicar anúncio pelo ERP fica para depois; nem o Omie faz isso bem.

### 4.3 Direto ou por hub (PP-21)

- **Hub** (Anymarket, Plugg.to, Ideris): entra mais rápido e eles cuidam da homologação de cada canal. Em troca, custo por lojista, dependência e mais uma camada para dar erro em estoque e fiscal. Preços não são públicos: pedir proposta.
- **Direto:** controle e margem, com aprovação e manutenção de cada API.
- **Recomendação:** **direto no Mercado Livre**, que tem a maior fatia e a API mais aberta, e **pedir já** as aprovações da Shopee e da Amazon, que levam tempo. Avaliar um hub só para a cauda (Magalu, Americanas, TikTok Shop), depois das propostas.

### 4.4 Como encaixa no ERP

Um módulo de **canais de venda** com um adaptador por marketplace (`IMarketplaceConnector`):

- O lojista autoriza a conta do marketplace por OAuth. O token fica cifrado num `SecretKind` novo.
- As notificações entram pelo `/webhooks/{provedor}/{tenantKey}`, que já existe, sempre consultando o marketplace de novo.
- Os jobs do Hangfire sempre recebem o `tenantId`.
- Um mapeamento **anúncio ↔ SKU** liga cada anúncio ao produto.
- O pedido importado entra como **pedido de venda (VND-02)**, com o canal e as tarifas.
- A NF-e sai pelo EmissorFiscal e é enviada ao marketplace.
- O repasse vira título a receber, com as tarifas como despesa do canal.

## 5. Frente C: API pública e MCP (comparação com o Nibo)

O Nibo é financeiro e contábil, não ERP de estoque. Os pontos fortes dele são a conciliação Open Finance multibanco (via Belvo), o portal do contador, o WhatsApp, a **API pública** e o **MCP para agentes de IA**.

Com a frente A pronta (aplicativos conectados, escopos e webhooks), a **API pública** sai quase de graça: OpenAPI publicado, um cadastro de aplicativo de terceiros pelo Proprietário e limites de uso. Por cima dela vem um **servidor MCP**, para o cliente usar o ERP a partir do próprio assistente de IA, com os mesmos escopos.

Outra lacuna em relação ao Nibo: **Open Finance multibanco**. Hoje temos só o Inter e o OFX. Fica registrada como pendência de produto, fora deste plano.

## 6. Fases e histórias (proposta)

| Fase | Histórias | Tamanho | Depende de |
|---|---|---|---|
| **F1 – Base no ERP** | **VND-02** Pedido de venda: estados (aberto → aguardando pagamento → pago → separado → faturado → enviado → entregue / cancelado), reserva, Pix ou boleto, NF-e; é o núcleo do ECM-04 | G | VND-01 |
| | **INT-01** Aplicativos conectados: OAuth com PKCE, escopos, consentimento com 2FA, revogação, auditoria | M | — |
| | **INT-02** Webhooks de saída: inscrição, assinatura, nova tentativa, registro de entregas | M | — |
| | **INT-03** API de integração v1: catálogo, estoque, clientes, pedidos, cobranças, notas | M | VND-02, INT-01 |
| **F2 – Atende** | **ATD-01** Conectar com um clique + credencial cifrada + recebimento de eventos | M | INT-01, INT-02 |
| | **ATD-02** Painel do cliente na conversa (web e Conecta) | M | INT-03 |
| | **ATD-03** Criar pedido e mandar a cobrança pelo atendente | M | INT-03 |
| | **ATD-04** Ferramentas do agente de IA (substituem o `[PEDIDO]` por regex) | M | ATD-03 |
| | **ATD-05** Automações por evento (pagamento, nota, envio) e etapa do funil | P | ATD-01 |
| | **ATD-06** WhatsApp do ERP via Atende (fecha o WPP-01 sem BSP) | P | ATD-01 |
| **F3 – Marketplaces** | **MKT-00** Base de canais: anúncio ↔ SKU, estoque por canal, pedido importado, tarifas, repasse | G | VND-02 |
| | **MKT-01** Mercado Livre (pedidos, estoque, NF-e, etiqueta do Mercado Envios, repasse) | G | MKT-00 |
| | **MKT-02** Shopee · **MKT-03** Amazon | M cada | MKT-00 + aprovação |
| | ECM-07 Nuvemshop/Tray (já planejada) · Magalu/TikTok (direto ou hub) | M cada | MKT-00 |
| **F4 – API pública** | **API-01** API pública + cadastro de aplicativo de terceiros · **API-02** Servidor MCP | M · M | INT-01..03 |

**Ordem sugerida:** F1 → F2 (primeiro valor, porque é tudo nosso) → F3, com as aprovações da Shopee e da Amazon pedidas **no início da F1** → F4. O ambiente SaaS do BemAqui serve de piloto da F2: o BemAqui usa o próprio Atende com o próprio ERP.

## 7. Decisões para o PO

1. **Onde o "um clique" começa:** pelos dois lados, com o ERP como principal? *Recomendado.*
2. **Perfis que vendem pelo Atende:** varejo, e-commerce e serviços criam pedido; fábrica (Konebras) só consulta, com status de projeto e 2ª via. Confere?
3. **Pagamento do pedido do Atende:** Pix e boleto com Pix do Inter primeiro; cartão depois, com gateway (PP-21)?
4. **WhatsApp do ERP pelo Atende** no lugar de um BSP separado (resolve o P-29)?
5. **Marketplaces:** direto no Mercado Livre, pedidos de aprovação da Shopee e da Amazon já, e hub só para a cauda depois de propostas? (PP-21)
6. **Plano comercial:** a integração com o Atende entra em todo plano ou é adicional? E cada marketplace?
7. **Prioridade:** isto entra antes ou depois das pendências do PDV (IMP-02, SPK-01 e INFRA-02, que aguardam loja parceira)?

## 8. Riscos

- **Aprovação de marketplace fora do nosso controle**, principalmente Shopee e Amazon. Por isso o pedido de aprovação vem primeiro.
- **Dado pessoal (LGPD):** o Atende recebe só o necessário do cliente. Na Amazon, o dado pessoal só vem com token restrito e por 1 hora.
- **Estoque vendido duas vezes entre canais:** a reserva no pedido e a sincronização por evento, não por planilha, são obrigatórias desde o MKT-00.
- **Dois produtos, dois times:** contrato de API versionado (`/api/v1`) e teste de contrato nos dois lados antes de cada deploy.
