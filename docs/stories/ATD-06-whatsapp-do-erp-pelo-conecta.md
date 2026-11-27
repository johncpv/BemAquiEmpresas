# ATD-06 — WhatsApp do ERP pelo Conecta (transporte da WPP-01, fecha o P-29)

| Campo | Valor |
|---|---|
| ID | ATD-06 |
| Título | O ERP ganha o `IWhatsAppSender` com o adaptador **"via Conecta"**: a mensagem nasce no ERP (fila, idempotência, consentimento e janela da WPP-01), o ERP avisa o Conecta por um aviso assinado sem dado pessoal (`whatsapp_message.requested`), o Conecta busca a mensagem numa rota da própria conexão, manda pelo número oficial da empresa com o modelo aprovado e devolve a situação; o "SAIR" no Conecta derruba o opt-in no ERP; a tela Configurações → Integrações → WhatsApp passa a mostrar "Pelo Conecta" no lugar do parceiro BSP |
| Tamanho | P (só o transporte; os gatilhos de negócio continuam na WPP-01) |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §2 e Decisões do PO item 4 · [ADR 0053](../adr/0053-aplicativos-conectados.md) §4 (rotas da própria conexão) · [INT-02](INT-02-webhooks-de-saida.md) · [WPP-01](WPP-01-notificacoes-whatsapp.md) · ADR 0041 · P-29 (`docs/06-pendencias-riscos.md`) |
| Dependências | ATD-01; o canal oficial da Meta (Embedded Signup) ativo na conta do Conecta e os modelos aprovados; para mandar aos **clientes**, a WPP-01 (opt-in no portal, POR-01a; régua, FIN-09; etapas, POR-01b) |
| Bloqueia | WPP-01 (passa a usar este transporte) |
| Repositórios | **ERP** (contrato, adaptador, aviso novo, rotas da conexão, tela) e **Conecta** (envio, mapeamento de modelos, situação, "SAIR") |
| Ordem | 7 (F2) |

## User story
Como **Proprietário**, quero **que os avisos de cobrança e de etapa do projeto saiam pelo WhatsApp oficial que a empresa já usa no Conecta**, para **não contratar outro parceiro (BSP) e ver a mensagem na mesma caixa de entrada do atendimento**.

## Contexto
- **ERP:** a WPP-01 (Ready, não implementada) previa API oficial da Meta por um BSP contratado pelo tenant (P-29), credenciais em Configurações → Integrações → WhatsApp, modelos aprovados, opt-in do próprio contato no portal, mensagem só com o aviso e o link do portal, dias úteis das 8 h às 18 h, fila com tentativas e webhook da Meta. Não existe `IWhatsAppSender` no código.
- **Decisão do PO (ADR 0052 §2):** o ERP sempre recomenda o Conecta como WhatsApp oficial; o WPP-01 sai pelo Conecta, sem BSP separado, e o P-29 fica resolvido.
- **Conecta:** já tem a API oficial da Meta com Embedded Signup (`MetaCloudApiService`, `WhatsAppSignupController`), modelos com situação na Meta (`MessageTemplate.MetaStatus`) e envio de modelo (`SendTemplateAsync`, `SendTemplateWithNamedParamsAsync`), e recebe a situação das mensagens pelo webhook da Meta (`Message.DeliveryStatus`).
- **Por que puxar, e não empurrar:** o aviso da INT-02 não pode levar dado pessoal (CA 5 de lá), e o ERP não tem credencial para chamar o Conecta. Então o ERP avisa "há mensagem" e o Conecta busca o telefone numa rota autenticada pelo token da própria conexão.

## Critérios de aceite

### ERP: contrato e adaptador
1. **Dado** a story, **quando** entra, **então** nasce `IWhatsAppSender` (BuildingBlocks, como o `IEmailSender`) com `EnqueueAsync(destino, modeloDoSistema, variáveis, dedupKey, relacionado)`, e a tabela `shared.whatsapp_logs` da WPP-01 (`to` como `[PersonalData]`, modelo, `related_type`, `related_id`, `dedup_key` único, `status`, `provider_message_id`, tentativas, erro, `scheduled_for`, `not_after`); o adaptador desta story é **`ConectaWhatsAppSender`**; o adaptador de BSP da WPP-01 deixa de existir (adendo à ADR 0041).
2. **[Segurança]** **Dado** uma mensagem enfileirada, **quando** o envio fica pronto (dentro da janela de envio da WPP-01), **então** o ERP grava, na mesma transação, o aviso público **`whatsapp_message.requested`** (novo no catálogo v1 da INT-02, aditivo) com `data = {id, template, not_after}`, **sem telefone, nome ou variáveis**; o aviso só vai para a inscrição da conexão ativa do Conecta com o WhatsApp ligado (CA 6); sem conexão ativa, a mensagem fica `Failed` "Conecta desconectado" e conta no alerta `whatsapp:{tenant}` (FND-12).
3. **[Segurança]** **Dado** o Conecta com o aviso, **quando** chama `GET /api/v1/integration/whatsapp-messages/{id}` (rota **da própria conexão**, `RequireConnectedAppSelf()` + módulo `conecta`, ADR 0053 §4), **então** recebe `{id, to (E.164), template, language, variables{first_name, company_name, link}, not_after}` com `Cache-Control: no-store`, **só** se a mensagem é desta conexão, está `Pending` e o opt-in ainda vale no momento da leitura; senão 404 (outra conexão, inexistente) ou 410 `message_not_sendable` (cancelada, vencida ou opt-in caiu); cada leitura grava auditoria com o termo em hash.
4. **Dado** `POST /api/v1/integration/whatsapp-messages/{id}/status` (mesma rota da conexão, `Idempotency-Key`), **quando** o Conecta informa `sent` (com `provider_message_id`), `delivered`, `read` ou `failed` (com o motivo da Meta), **então** o `whatsapp_logs` anda só para a frente (`Pending → Sent → Delivered → Read`, ou `Failed`), sem voltar; `failed` entra na regra de tentativas da WPP-01 (3 com espera crescente, depois `Failed` e alerta).
5. **Dado** uma mensagem `Pending` sem `sent` em 15 min, **quando** o varredor roda, **então** o aviso é reenviado uma vez e, passado `not_after`, a mensagem vai a `Failed` "O Conecta não confirmou o envio"; o Conecta também varre `GET /api/v1/integration/whatsapp-messages?status=pending` a cada 5 min para não depender só do aviso.
6. **[Segurança]** **Dado** Configurações → Integrações → **WhatsApp** no ERP, **quando** o Proprietário abre, **então** vê "WhatsApp oficial pelo Conecta" com a situação (Conecta conectado e com WhatsApp oficial ativo, ou o que falta: "Conecte o Conecta", "Ative o WhatsApp oficial no Conecta") e liga **"Enviar avisos pelo WhatsApp do Conecta"** com `settings.edit` e **2FA de novo** (é a empresa falando com clientes em seu nome; mesma regra do D6 para credenciais); a lista de números de teste e "Testar envio" da WPP-01 continuam e passam por este transporte.
7. **Dado** `POST /api/v1/integration/whatsapp-opt-outs` (rota da conexão, `Idempotency-Key`) com `{to (E.164), reason: "SAIR", occurred_at}`, **quando** o Conecta informa, **então** o ERP derruba o opt-in de **todos** os contatos com aquele telefone (`whatsapp_opt_out_at`, motivo "Pediu para sair pelo WhatsApp"), sem guardar o texto da mensagem (WPP-01 CA 7).

### Conecta: envio e situação
8. **Dado** o aviso `whatsapp_message.requested` validado (ATD-01), **quando** processado, **então** o Conecta busca a mensagem (CA 3), confere `not_after`, acha o **modelo mapeado** para o código do sistema ("Aviso de pagamento", "Aviso do projeto") com `MetaStatus = approved` no **canal oficial escolhido** e manda por `SendTemplateWithNamedParamsAsync` só com as variáveis recebidas; **uma vez por `id`** (`erp_whatsapp_relays` com `id` único); responde a situação (CA 4) com o `wamid`.
9. **Dado** Configurações → Integrações → BemAqui Empresas → **WhatsApp do ERP** no Conecta (`admin`), **quando** abro, **então** escolho o canal oficial (só `MetaCloudApi`; Evolution, Instagram, Messenger e Telegram não aparecem) e o modelo aprovado de cada código do sistema, com a prévia; sem modelo aprovado, o código mostra "Crie e aprove o modelo na Meta" com o atalho para Modelos de mensagem já preenchido com o texto da WPP-01 ("Olá, {{first_name}}. A {{company_name}} tem um aviso sobre um pagamento. Veja no portal: {{link}}"); faltando canal ou modelo, a mensagem responde `failed` com o motivo ("modelo não aprovado", "canal oficial inativo").
10. **Dado** a situação da mensagem vinda da Meta (`delivered`, `read`, `failed`) pelo webhook que o Conecta já recebe, **quando** chega para um `wamid` de mensagem do ERP, **então** o Conecta repassa ao ERP (CA 4), uma vez por situação.
11. **Dado** a mensagem enviada, **quando** sai, **então** aparece na conversa do contato com aquele telefone no canal escolhido (cria a conversa se não houver, resolvida e sem IA), com o rótulo "Aviso do ERP", para o atendente ver o que o cliente recebeu; a resposta do cliente abre a conversa normal.
12. **Dado** "SAIR" ou "PARAR" de um contato que recebeu aviso do ERP (o mesmo detector da ATD-05 CA 6), **quando** chega, **então** o Conecta chama o CA 7 e confirma ao cliente.

## Regras de negócio e técnicas
- **Quem decide o quê:** o ERP decide **se** e **quando** manda (opt-in, janela de dias úteis das 8 h às 18 h, idempotência por `dedup_key`, conteúdo mínimo com só primeiro nome, nome fantasia e link do portal; WPP-01 CA 5 a 11). O Conecta só transporta, com o modelo aprovado e o número oficial; não muda texto nem variáveis.
- **Rotas novas (ERP, `integration-v1`, aditivas):** `GET /api/v1/integration/whatsapp-messages?status=pending`, `GET /api/v1/integration/whatsapp-messages/{id}`, `POST /api/v1/integration/whatsapp-messages/{id}/status`, `POST /api/v1/integration/whatsapp-opt-outs`; todas `RequireConnectedAppSelf()` (sem escopo de negócio, ponto em aberto 1), módulo `conecta`, `app-api`, `no-store`; códigos novos `message_not_sendable`.
- **Aviso novo (INT-02, catálogo v1, aditivo):** `whatsapp_message.requested` com `data = {id, template, not_after}`; entregue só à conexão com o WhatsApp ligado (sem escopo de negócio); entra no `DefaultEvents`.
- **Custo:** a mensagem é cobrada pela Meta na conta do WhatsApp do Conecta da empresa (a mesma do atendimento). A tela do ERP diz isso: "As mensagens são cobradas pela Meta na conta do WhatsApp que a {empresa} usa no Conecta."
- **Ambientes:** a regra da WPP-01 CA 13 vale (só números de teste no `konebras` antes da virada e no `demo`).

## O que muda em cada repositório
**ERP:** `IWhatsAppSender`, `ConectaWhatsAppSender`, `shared.whatsapp_logs` (migration aditiva), tradutor do aviso `whatsapp_message.requested`, rotas do CA 3, 4, 5 e 7, varredor, tela "WhatsApp oficial pelo Conecta" com 2FA, ajuda `Ajuda/configuracoes-integracoes-whatsapp.md`; **adendos:** ADR 0041 (transporte pelo Conecta, sem BSP), ADR 0053 §4 (rotas de mensagem da própria conexão); **WPP-01** atualizada (CA 1, 2, 3 e 14 trocados por este transporte; modelos aprovados vêm do Conecta; `SecretKind.WhatsAppCredentials` e o provedor `whatsapp` da FND-16 deixam de ser necessários); **P-29** fechado em `docs/06-pendencias-riscos.md`; OpenAPI `integration-v1` e o arquivo versionado.
**Conecta (`bemaqui-atende-api`):** `ErpWhatsAppRelay` (`id` do ERP único, `wamid`, situação), mapeamento de modelos por código do sistema, envio, repasse de situação a partir do webhook da Meta, "SAIR", varredor de 5 min.
**Conecta (`bemaqui-atende-web`):** "WhatsApp do ERP" nas configurações; rótulo na conversa.

## Telas e estados
- **ERP, Configurações → Integrações → WhatsApp:** "Pelo Conecta" com a situação e o que falta, a chave "Enviar avisos pelo WhatsApp do Conecta" (2FA), números de teste, "Testar envio", "Falhas de envio" (WPP-01 CA 16); a recomendação "O WhatsApp oficial do BemAqui é o Conecta" (ADR 0052 §2).
- **Conecta, WhatsApp do ERP:** canal oficial, modelos com prévia e chip (Aprovado, Pendente, Recusado, Falta criar), carregando e erro.
- **Verbos:** "Ligar envio" → "Envio ligado"; "Testar envio" → "Mensagem de teste enviada".

## Plano de testes
- **ERP, unidade:** aviso sem dado pessoal (lista de campos proibidos); transições do `whatsapp_logs` só para a frente.
- **ERP, integração (Testcontainers, recebedor falso):** enfileirar → aviso → leitura → `sent` → `delivered` (CA 2 a 4); leitura por outra conexão (404) e depois do opt-out (410) (CA 3); varredor e `not_after` (CA 5); 2FA ao ligar (CA 6); opt-out pelo Conecta (CA 7); execução dupla; dois tenants; canário de telefone no log.
- **Conecta, integração:** uma mensagem por `id` com aviso repetido e varredor (CA 8); modelo não aprovado e canal não oficial (CA 9); repasse da situação da Meta (CA 10); conversa com o rótulo (CA 11); "SAIR" (CA 12).
- **Manual:** "Testar envio" do ERP SaaS para um número de teste pelo número oficial do BemAqui no Conecta (piloto 21/12).

## Tarefas
- [ ] ERP: adendos às ADRs 0041 e 0053; contrato, adaptador, tabela, aviso novo, rotas, varredor, tela, ajuda, OpenAPI.
- [ ] ERP: atualizar a WPP-01 (CAs trocados e dependências) e fechar o P-29.
- [ ] Conecta: relay, mapeamento de modelos, situação, "SAIR", tela.
- [ ] Fluxo: ERP primeiro (PR, CI e deploy), depois o Conecta.

## Dev Notes
- **ERP:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Webhooks/` (publicador e catálogo dos avisos, `OutboundSubscriptionService.cs`), `IEmailSender` (molde do contrato e da fila), `Jobs/TenantJob.cs`, `Observability/IntegrationHealth.cs`; a WPP-01 inteira para as regras de negócio.
- **Conecta:** `bemaqui-atende-api/src/BemAquiAtende.Infrastructure/Channels/MetaCloudApiService.cs`, `API/Controllers/WhatsAppSignupController.cs`, `API/Controllers/MessageTemplatesController.cs`, `API/Controllers/WebhookController.cs` (situação das mensagens da Meta), `Domain/Entities/MessageTemplate.cs`, `Domain/Entities/Message.cs`.

## Fora do escopo
- Régua de cobrança por WhatsApp, avisos de etapa do projeto, opt-in no portal e histórico de comunicação: continuam na WPP-01, que passa a depender desta story.
- Mensagens a funcionários; mensagens com valor, boleto ou documento (D17).
- Outro transporte que não o Conecta.

## Pontos em aberto (para o PO)
1. **Rota da conexão ou escopo novo:** a leitura da mensagem expõe o telefone do contato. **Recomendação:** rota da própria conexão (sem escopo novo e sem nova autorização), porque o ERP só expõe mensagens que **ele** decidiu mandar por aquela conexão, e o liga-desliga com 2FA fica no ERP; a alternativa (escopo novo, como `conecta.send_whatsapp`) exigiria "Autorizar de novo" em todas as conexões.
2. **2FA de novo para ligar o envio:** **recomendação:** sim (é a empresa falando com clientes em seu nome, mesma regra do D6).
3. **WPP-01 sem o portal:** sem a POR-01a, não há opt-in, e nenhum cliente recebe aviso do ERP; só os números de teste. **Recomendação:** manter a ADR 0041 (opt-in no portal) e não antecipar; os avisos de pedido do Conecta (ATD-05) cobrem o varejo enquanto isso.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da ADR 0052 §2, da WPP-01 e da leitura do WhatsApp oficial do Conecta; transporte por aviso + rota da própria conexão, porque o aviso não leva dado pessoal e o ERP não tem credencial para chamar o Conecta. | Claude (planejamento) |
