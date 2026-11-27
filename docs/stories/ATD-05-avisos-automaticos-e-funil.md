# ATD-05 — Avisos automáticos ao cliente por evento do ERP e etapa do funil

| Campo | Valor |
|---|---|
| ID | ATD-05 |
| Título | Os avisos do ERP (pedido confirmado, pago, cancelado, boleto emitido ou vencido, nota autorizada e, com a VND-02b, separado, faturado, enviado e entregue) viram mensagem ao cliente na conversa em que o pedido nasceu (texto dentro da janela de 24 h, modelo aprovado fora dela), com consentimento, horário e "SAIR"; o negócio do funil anda sozinho por evento; um gatilho novo "Evento do BemAqui Empresas" no editor de automações |
| Tamanho | P |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) · [INT-02](INT-02-webhooks-de-saida.md) (catálogo v1, CA 5 e 9) · [Plano](../integracoes/00-plano-integracoes.md) §3.3 (avisos e funil) · ADR 0041 (consentimento, janela e conteúdo mínimo, como referência) |
| Dependências | ATD-01 (recebimento dos avisos), ATD-03a (`ErpOrderLink`: pedido ↔ conversa ↔ negócio); **VND-02b** para `sales_order.picked|invoiced|shipped|delivered` |
| Bloqueia | — |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`). ERP: nenhuma mudança |
| Ordem | 6 (F2) |

## User story
Como **dono da loja**, quero **que o cliente receba no WhatsApp "pagamento confirmado", "nota emitida" e "pedido enviado" sem a equipe lembrar de mandar, e que o funil mostre em que pé cada venda está**, para **o cliente não precisar perguntar e eu ver o funil real**.

## Contexto
- A ATD-01 recebe e deduplica os avisos (`erp_inbound_events`) e os marca processados; esta story é quem reage.
- O aviso traz só ids, número, situação, valor e datas, **sem dado pessoal**, e a ordem não é garantida (INT-02 CA 5 e 9): o Conecta acha o contato pelo vínculo (`ErpOrderLink` ou `ContactErpLink` pelo `party_id`) e relê o recurso antes de avisar.
- **O que existe no Conecta:** automações (`AutomationFlow`, gatilhos `new_conversation`, `keyword`, `tag`, `deal_stage_changed`…, nós `send_message`, `move_deal`, `create_deal`, `send_message_to_contact`, `webhook`…), funil (`Deal`, `PipelineStage`, `MoveDealCommand`), modelos da Meta (`MessageTemplate` com `MetaStatus`) e envio por `IChannelService.SendTemplateAsync`. O nó `send_message_to_contact` usa o **canal mais recente do tenant** e texto livre: não serve para aviso de pedido (pode sair por outro número e falha fora da janela de 24 h). Não há tratamento de "SAIR" para avisos.

## Critérios de aceite

### Avisos ao cliente
1. **Dado** Configurações → Integrações → BemAqui Empresas → **Avisos ao cliente** (`admin`), **quando** abro, **então** vejo a lista de avisos prontos, **todos desligados até serem configurados**: "Pedido confirmado" (`sales_order.confirmed`), "Boleto emitido" (`billing_slip.issued`, só quando a cobrança **não** foi mandada pelo atendente ou pela IA), "Pagamento confirmado" (`sales_order.paid`), "Boleto vencido" (`billing_slip.cancelled` com `expired`), "Pedido cancelado" (`sales_order.cancelled`), "Nota emitida" (`invoice.authorized` ou `sales_order.invoiced`) e, com a VND-02b, "Pedido separado", "Pedido enviado" e "Pedido entregue"; cada um tem o texto (dentro da janela) e o modelo aprovado (fora da janela), com prévia.
2. **[Segurança]** **Dado** um aviso ligado, **quando** o evento é processado, **então** o Conecta: (a) acha o contato pelo pedido (`ErpOrderLink`) ou pelo `party_id` (`ContactErpLink` com telefone conferido, ATD-02 CA 3); sem vínculo conferido, não manda; (b) **relê** `GET /orders/{id}` (ou a cobrança, ou a nota) e só manda se o estado atual ainda corresponder ao aviso (pago e depois cancelado: não manda "pago"; entregue chegou antes de enviado: manda só "entregue"); (c) manda pelo **canal da conversa em que o pedido nasceu** (ou da última conversa do contato no mesmo número), nunca por outro número; (d) grava a mensagem na conversa (`SenderType = "system"`, rótulo "Aviso do ERP") para o atendente ver.
3. **Dado** a janela de 24 h do WhatsApp oficial, **quando** o cliente falou nas últimas 24 h, **então** sai o texto; fora dela, sai o modelo aprovado (`MetaStatus = approved`, categoria *utility*); sem modelo aprovado, o aviso fica "Não enviado: fora da janela e sem modelo aprovado" na lista do CA 9, sem tentar texto.
4. **[Segurança]** **Dado** o conteúdo, **quando** montado, **então** as variáveis permitidas são primeiro nome do contato, nome da empresa, número do pedido, situação e data prevista; **nunca** documento, endereço, linha digitável, Pix ou link de boleto no aviso automático (a 2ª via é pedida pelo cliente e mandada pela ATD-02); valor do pedido só se a conta ligar "Mostrar valor nos avisos" (padrão desligado; ponto em aberto 2).
5. **[Segurança]** **Dado** o consentimento, **quando** o primeiro aviso automático de um contato vai sair, **então** precisa haver o aceite do contato para "avisos do pedido por aqui" (`ContactErpNotificationConsent`: data, texto aceito, conversa); o aceite é pedido no resumo do pedido (ATD-03a CA 6, "Quer receber os avisos deste pedido por aqui?" com botões "Sim"/"Não") ou pela IA (ATD-04) e gravado pela resposta **do próprio cliente**; sem aceite, nada sai e o atendente vê "Cliente não pediu avisos" (ponto em aberto 1).
6. **Dado** uma resposta do cliente "SAIR", "PARAR" ou "NÃO QUERO AVISOS" (sem diferença de caixa e acento), **quando** chega, **então** o consentimento é revogado na hora, sai uma confirmação ("Pronto, você não vai mais receber avisos automáticos de pedidos. Para voltar, responda AVISOS.") e a revogação fica registrada; a conversa segue normal com o atendente.
7. **Dado** o horário, **quando** o aviso fica pronto fora da janela configurada (padrão: todos os dias, das 8 h às 20 h, `America/Sao_Paulo`), **então** é agendado para o início da próxima janela, e ao sair passa de novo pelo CA 2 (b).
8. **Dado** o mesmo evento processado duas vezes (reentrega do ERP, "Reprocessar" ou execução dupla do job), **quando** o aviso iria sair, **então** sai **uma vez só**, pela chave `(id do evento, contato, tipo de aviso)` única em `erp_notifications`.
9. **Dado** a lista **Avisos enviados** (supervisor e `admin`), **quando** abro, **então** vejo cada aviso com contato, pedido, tipo, situação (Enviado, Entregue, Lido, Falhou com o motivo da Meta, Não enviado com o motivo, Agendado) e "Reenviar" para os que falharam (só se o estado do pedido ainda corresponder).

### Funil
10. **Dado** **Funil por evento** nas mesmas configurações, **quando** o `admin` liga e escolhe a etapa de cada evento (ex.: `sales_order.created` → "Pedido", `.paid` → "Pago", `.delivered` → "Entregue", `.cancelled` → "Perdido"), **então** o negócio ligado ao pedido (`ErpOrderLink.DealId`, criado na ATD-03a) vai à etapa pelo `MoveDealCommand` (que dispara as automações `deal_stage_changed` e as regras de tarefa), recebe o valor do total do ERP, fica `won` no evento marcado como "ganho" (padrão `.paid`) e `lost` no cancelado; o negócio nunca volta de etapa por um aviso atrasado (ordem das etapas do funil, conferida contra o estado relido).
11. **Dado** um pedido criado no ERP (não pelo Conecta) para um cliente vinculado, **quando** chega `sales_order.created`, **então** a opção "Criar negócio para pedidos feitos fora do Conecta" (padrão desligada) cria o negócio na etapa configurada; desligada, nada muda no funil.

### Automações
12. **Dado** o editor de automações, **quando** o `admin` cria uma automação, **então** há o gatilho **"Evento do BemAqui Empresas"** (`erp_event`) com o filtro do tipo de aviso; o contexto leva contato, conversa, negócio, tipo e número do pedido (sem dado pessoal); os nós de envio usados com esse gatilho mandam pelo canal da conversa e respeitam a janela, o consentimento e o "SAIR" desta story (o nó `send_message_to_contact` de hoje não é oferecido para esse gatilho).

## Regras de negócio e técnicas
- **Avisos do ERP consumidos (catálogo v1 da INT-02):** `sales_order.created|confirmed|paid|cancelled` e, com a VND-02b, `.picked|.invoiced|.shipped|.delivered` (`sales.view`); `billing_slip.issued|paid|cancelled` (`finance.view`); `invoice.authorized|cancelled|rejected` (`fiscal.view`). `invoice.*` só tem `party_id` (não o pedido): o aviso "Nota emitida" usa `sales_order.invoiced` quando a VND-02b existir e, até lá, `invoice.authorized` pelo cliente vinculado.
- **Rotas do ERP para reler:** `GET /orders/{id}` (`sales.view`), `GET /charges/{id}` (`finance.view`), `GET /orders/{id}/invoice` (`fiscal.view`). Escopo ausente: o aviso correspondente fica indisponível na tela.
- **Job:** `ErpNotificationJob` (Hangfire, com `tenantId` e o id do evento), idempotente pela chave do CA 8; espera entre avisos do mesmo contato de 30 s para não amontoar ("pago" e "nota emitida" juntos viram duas mensagens espaçadas).
- **Retenção:** `erp_notifications` guarda ids, tipo, situação e motivo; o texto enviado fica só na conversa.

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ErpNotificationSettings` (por tenant: avisos ligados, textos, modelos, janela, valor, funil), `ErpNotification`, `ContactErpNotificationConsent`; migration aditiva; `ErpNotificationJob`; reação aos avisos no `ProcessErpEventJob` (ATD-01); detecção de "SAIR"/"AVISOS" no recebimento de mensagens (`ReceiveMessageCommandHandler`); gatilho `erp_event` no `AutomationEngine` e no contexto (`AutomationContext` ganha `ErpEventType` e `ErpOrderNumber`).
**Conecta (`bemaqui-atende-web`):** "Avisos ao cliente", "Funil por evento" e "Avisos enviados" nas configurações; gatilho novo no `AutomationEditorView.vue`; rótulo "Aviso do ERP" na conversa.
**ERP:** nenhuma mudança.

## Telas e estados (Conecta)
- **Avisos ao cliente:** cada aviso com chip (Ligado, Desligado, "Falta modelo aprovado"), texto, modelo e prévia; vazio de modelos com o atalho para Modelos de mensagem; carregando e erro.
- **Avisos enviados:** filtros por tipo, situação e período; vazio "Nenhum aviso enviado ainda."; "Reenviar" com o impacto.
- **Verbos:** "Salvar avisos" → "Avisos salvos"; "Reenviar" → "Reenviado".
- **Propositivo:** ao conectar o ERP (ATD-01), o próximo passo sugere "Ligue o aviso de pagamento confirmado"; modelo faltando leva direto à criação do modelo já preenchido.

## Plano de testes
- **Unidade:** decisão por estado relido (pago → cancelado, entregue antes de enviado); montagem com as variáveis permitidas (canário); janela de horário; "SAIR" e "AVISOS".
- **Integração (Conecta, ERP falso, canal falso):** aviso dentro e fora da janela de 24 h, com e sem modelo (CA 3); sem consentimento (CA 5); "SAIR" (CA 6); fora do horário agendado e relido ao sair (CA 7); evento duplicado e job duplo (CA 8); funil por evento sem voltar etapa (CA 10); gatilho `erp_event` (CA 12); dois tenants.
- **Manual (piloto):** pedido pago no ERP SaaS chega como "Pagamento confirmado" no WhatsApp do número de teste.

## Tarefas
- [ ] Conecta API: configurações, consentimento, job, reações, "SAIR", gatilho `erp_event`.
- [ ] Conecta web: telas de avisos, funil e enviados; gatilho no editor.
- [ ] Modelos sugeridos (texto *utility*) para a conta enviar à Meta: "pedido_pago", "pedido_enviado", "nota_emitida", "pedido_cancelado".
- [ ] Manual do administrador do Conecta.
- [ ] Fluxo do repositório do Conecta.

## Dev Notes
- **Conecta:** `bemaqui-atende-api/src/BemAquiAtende.Infrastructure/Automations/AutomationEngine.cs` (`ShouldRun`, `ExecuteSendMessageToContact` usa o canal mais recente: não reaproveitar; `ExecuteMoveDeal`), `Application/Common/Interfaces/IAutomationEngine.cs` (`AutomationContext`), `Application/Features/Pipeline/Commands/MoveDealCommand.cs`, `Application/Features/Inbox/Commands/ReceiveMessageCommandHandler.cs`, `Infrastructure/Channels/MetaCloudApiService.cs` (`SendTemplateAsync`, `SendTemplateWithNamedParamsAsync`), `Domain/Entities/MessageTemplate.cs`, `Domain/Entities/Message.cs` (`DeliveryStatus`, `FailureReason`), `Infrastructure/Jobs/RetomadaConversaJob.cs` e `ReengagementJob.cs` (como o Conecta já respeita janela e horário).
- **Canal não oficial (Evolution):** sem modelo e sem janela; a v1 só manda aviso automático por canal oficial (Meta) ou por Evolution **dentro** da janela de 24 h (ponto em aberto 3).

## Fora do escopo
- WhatsApp disparado pelo próprio ERP (régua de cobrança, etapa de projeto): ATD-06 e WPP-01.
- Avisos de orçamento e de projeto (não há `quote.*` nem `project.*` no catálogo v1).
- Campanhas de recompra a partir do ERP.

## Pontos em aberto (para o PO)
1. **Consentimento para os avisos automáticos:** a ADR 0041 (WPP-01) exige opt-in do próprio contato no portal; aqui o cliente está na conversa em que comprou. **Recomendação:** aceite do próprio cliente na conversa (botão "Sim" no resumo do pedido), com o texto versionado e "SAIR" a qualquer momento; sem aceite, só o atendente manda.
2. **Valor do pedido no aviso:** **recomendação:** desligado por padrão (o aviso diz o que aconteceu e o número; o cliente vê o valor no resumo que já recebeu).
3. **Avisos por canal não oficial (Evolution):** **recomendação:** só dentro da janela de 24 h; fora dela, não manda.
4. **Janela de horário:** **recomendação:** todos os dias, das 8 h às 20 h (aviso de pedido é transacional, diferente da régua de cobrança da WPP-01, que é de dias úteis das 8 h às 18 h).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir do catálogo da INT-02 e da leitura das automações e do funil do Conecta. | Claude (planejamento) |
