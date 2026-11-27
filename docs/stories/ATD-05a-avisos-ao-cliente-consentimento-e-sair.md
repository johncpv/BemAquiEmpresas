# ATD-05a — Avisos automáticos ao cliente, consentimento e SAIR

| Campo | Valor |
|---|---|
| ID | ATD-05a |
| Título | Os avisos do BemAqui Empresas (pedido confirmado, pagamento confirmado, boleto emitido ou vencido, pedido cancelado e, com a VND-02b, separado, nota emitida, enviado e entregue) viram mensagem ao cliente no canal oficial da conversa do pedido, só com aceite do próprio telefone (pergunta separada, texto versionado, prova), estado relido e destino reconferido na hora, idempotentes por (pedido, situação); **SAIR, PARAR, CANCELAR ou STOP** desligam todos os avisos automáticos daquele telefone, do Conecta e do BemAqui Empresas |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) · [INT-02](INT-02-webhooks-de-saida.md) (catálogo, CA 5 e 9) · [INT-04](INT-04-ajustes-avisos-e-conexao.md) (CA 1 e 3) · [Plano](../integracoes/00-plano-integracoes.md) §3.3 · ADR 0041 (referência) · decisões do PO de 27/11/2026 (4) |
| Dependências | ATD-01, ATD-02, ATD-03a (`ErpOrderLink` e reconciliação); **VND-02b** para separado, nota pelo pedido, enviado e entregue; a propagação do SAIR ao BemAqui Empresas usa a rota da ATD-06a quando ela estiver no ar |
| Bloqueia | ATD-05b, ATD-06b (lista de supressão e detector de SAIR) |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`, `bemaqui-conecta`). ERP: nada além da INT-04 e da ATD-06a |
| Ordem | 4 (F2, depois da ATD-03a) |

## User story
Como **dono da loja**, quero **que o cliente receba no WhatsApp "pagamento confirmado" e "pedido enviado" sem a equipe lembrar, e que ele pare de receber quando pedir**, para **o cliente não precisar perguntar e a loja nunca insistir com quem não quer**.

## Contexto
- A ATD-01 recebe e deduplica os avisos; a ATD-03a liga pedido ↔ conversa e reconcilia a cada 10 min. Esta story é quem fala com o cliente.
- O aviso do ERP não tem dado pessoal e a ordem não é garantida: o Conecta acha o contato pelo pedido ou pelo `party_id` e relê o recurso antes de avisar.
- Hoje o Conecta não trata "SAIR" para mensagens automáticas, e o nó `send_message_to_contact` manda pelo canal mais recente do tenant (não serve).

## Critérios de aceite

### Configuração
1. **Dado** Configurações → Integrações → BemAqui Empresas → **Avisos ao cliente** (`admin`), **quando** abro, **então** vejo os avisos, **todos desligados até serem configurados**, cada um com chip **Ligado**, **Desligado** ou **"Falta a mensagem pronta do WhatsApp"**, texto e mensagem pronta com **prévia completa** (como o cliente vê): "Pedido confirmado" (`sales_order.confirmed`), "Pagamento confirmado" (`sales_order.paid`), "Boleto emitido" (`billing_slip.issued`, só quando a cobrança não foi mandada pelo atendente ou pela IA), "Boleto vencido" (`billing_slip.cancelled` com `expired`), "Pedido cancelado" (`sales_order.cancelled`) e, marcados **"(com a VND-02b)"** e indisponíveis até lá, "Pedido separado" (`sales_order.picked`), "Nota emitida" (`sales_order.invoiced`), "Pedido enviado" (`.shipped`) e "Pedido entregue" (`.delivered`). "Nota emitida" por `invoice.authorized` **não** é oferecido (sem o pedido, não se sabe a conversa).
2. **Dado** a tela, **quando** abro, **então** vejo "12 de 40 clientes aceitaram receber avisos" e **"Enviar um aviso de teste para o meu número"** (o telefone do `admin` com opt-in do próprio usuário, como hoje nas tarefas).

### Consentimento
3. **[Segurança]** **Dado** um telefone sem resposta de consentimento, **quando** o pedido é confirmado (ou o atendente clica "Perguntar sobre avisos"), **então** o Conecta manda, **separada do resumo**, a pergunta com o texto versionado: **"Posso te avisar por aqui quando o pagamento for confirmado, a nota for emitida e o pedido for enviado? Responda SIM. Para parar, responda SAIR."** (botões "SIM" e "Não, obrigado" no canal oficial); a resposta do **próprio cliente** grava `ErpNotificationConsent` **por telefone** (HMAC do E.164 com chave, ATD-02): situação (Aceitou, Recusou), versão do texto, data, a mensagem e o `wamid` como prova; sem resposta fica "Sem resposta" (diferente de "Recusou"), e nada automático sai.
4. **Dado** o consentimento, **quando** o texto da pergunta muda de versão, **então** os aceites antigos continuam valendo para os avisos que o texto antigo citava; aviso novo que o texto antigo não cobria pede aceite de novo.

### Envio
5. **[Segurança]** **Dado** um aviso ligado e um evento processado, **quando** vai sair, **então** o Conecta: (a) acha o contato pelo `ErpOrderLink` (ou pelo `party_id` com identificação, ATD-02); (b) **relê** o pedido ou o título e só manda se a situação atual ainda corresponde (pago e depois cancelado: não manda "pago"; entregue antes de enviado: só "entregue"); (c) confere o **consentimento e a supressão** naquele momento (CA 8); (d) **reconfere o destino**: HMAC do E.164 do destinatário = o da conversa do pedido e o cliente continua identificado; (e) manda **só pelo canal oficial** (Meta) da conversa do pedido — **Evolution e outros canais não oficiais não recebem aviso automático na v1**; (f) grava a mensagem na conversa com o rótulo "Aviso automático" e mostra o aviso enviado **no cartão do pedido**.
6. **Dado** o cliente que falou nas últimas 24 h, **quando** o aviso sai, **então** vai o texto; fora disso, a mensagem pronta aprovada pelo WhatsApp; sem ela, o aviso fica "Não enviado: o cliente não falou nas últimas 24 h e falta a mensagem pronta" com "Criar a mensagem pronta".
7. **[Segurança]** **Dado** o conteúdo, **quando** montado, **então** só primeiro nome, nome da empresa, número do pedido, situação e data prevista; nunca documento, endereço, linha digitável, Pix, link de boleto ou valor (valor fica fora na v1).
8. **Dado** o horário, **quando** o aviso fica pronto fora da janela da conta (padrão todos os dias, 8 h às 20 h, `America/Sao_Paulo`), **então** é agendado e, ao sair, passa de novo pelo CA 5 (b) a (d).
9. **Dado** o mesmo pedido na mesma situação vindo de aviso repetido, "Tentar de novo", reconciliação (ATD-03a CA 15) ou job duplo, **quando** o aviso iria sair, **então** sai **uma vez**, pela chave única **(pedido, situação, telefone)** em `erp_notifications`; entre dois avisos do mesmo contato, 30 s.
10. **Dado** **Avisos enviados** (`supervisor` e `admin`), **quando** abro, **então** cada aviso com contato, pedido, tipo e situação (Agendado, Enviado, Entregue, Lido, Não enviado com o motivo, Falhou); falha da Meta com **ação simples** ("Número sem WhatsApp: confirme o telefone com o cliente", "Mensagem pronta pausada pelo WhatsApp: revise a mensagem") e "Tentar de novo" só se a situação ainda corresponder.

### SAIR (decisão do PO 4)
11. **[Segurança]** **Dado** uma mensagem do cliente que seja só **SAIR**, **PARAR**, **CANCELAR** ou **STOP** (sem diferença de caixa, acento e pontuação), **quando** chega, **então** o telefone entra na **lista de supressão** do Conecta (`ContactSuppression` por HMAC do telefone, data, mensagem de prova) e param **todos os avisos automáticos daquele telefone**: avisos desta story, automações com gatilho de evento (ATD-05b), mensagens do BemAqui Empresas pelo Conecta (ATD-06b), e as automáticas do próprio Conecta (campanhas, reengajamento, retomada e automações); o Conecta informa o BemAqui Empresas (`POST /integration/whatsapp-opt-outs`, ATD-06a; enquanto ela não existir, fica em fila); a resposta **confirma o que foi desligado**: "Pronto. Você não vai mais receber mensagens automáticas da {empresa} por aqui (avisos de pedido, cobrança e novidades). Se quiser falar com alguém, é só escrever. Para voltar a receber, responda VOLTAR."; a conversa com o atendente continua normal.
12. **Dado** "VOLTAR", **quando** o cliente responde, **então** sai da supressão e volta a valer o consentimento anterior; o retorno também é registrado com a mensagem de prova.
13. **[Segurança]** **Dado** qualquer envio automático do Conecta (campanha, reengajamento, retomada, automação, aviso), **quando** vai sair, **então** consulta a supressão **na hora** (teste: telefone suprimido não recebe campanha nem reengajamento).

## Regras de negócio e técnicas
- **Avisos consumidos (catálogo v1 + INT-04):** `sales_order.confirmed|paid|cancelled` e, com a VND-02b, `.picked|.invoiced|.shipped|.delivered` (`sales.view`); `billing_slip.issued|cancelled` (`finance.view`, `party_id` e `resource` da INT-04).
- **Releitura:** `GET /orders/{id}` (`sales.view`), `GET /charges/{id}` (`finance.view`).
- **Job:** `ErpNotificationJob` (Hangfire, `tenantId` e ids), idempotente pela chave do CA 9.
- **Retenção:** `erp_notifications` (ids, tipo, situação, motivo) 12 meses; consentimento e supressão enquanto o contato existir, apagados ou pseudonimizados com o pedido do titular.

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ErpNotificationSettings`, `ErpNotification`, `ErpNotificationConsent`, `ContactSuppression`; migration aditiva; `ErpNotificationJob`; detector de SAIR/VOLTAR no `ReceiveMessageCommandHandler`; consulta da supressão em `SendCampaignCommand`, `ReengagementJob`, `RetomadaConversaJob` e no `AutomationEngine`; fila do opt-out ao BemAqui Empresas.
**Conecta (`bemaqui-atende-web`):** "Avisos ao cliente", "Avisos enviados", aviso no cartão do pedido, rótulo na conversa.
**Conecta (`bemaqui-conecta`, app):** rótulo "Aviso automático" e aviso no cartão.

## Telas e estados
- **Avisos ao cliente:** chips (CA 1), prévia completa, contagem de aceites, teste para o meu número; vazio de mensagens prontas com "Criar a mensagem pronta" já preenchida; carregando e erro.
- **Avisos enviados:** filtros; vazio "Nenhum aviso enviado ainda."; falhas com ação.
- **Verbos:** "Salvar avisos" → "Avisos salvos"; "Enviar aviso de teste" → "Aviso de teste enviado"; "Perguntar sobre avisos" → "Pergunta enviada"; "Tentar de novo".
- **Propositivo:** ao conectar, o próximo passo sugere "Ligue o aviso de pagamento confirmado"; o cartão do pedido mostra "Cliente aceitou avisos" ou "Perguntar sobre avisos".

## Plano de testes
- **Unidade:** estado relido; montagem com as variáveis permitidas (canário); SAIR/PARAR/CANCELAR/STOP/VOLTAR com variações; janela; chave (pedido, situação, telefone).
- **Integração (Conecta, ERP falso, canal falso):** sem consentimento, "Sem resposta" e "Recusou"; aceite com prova; dentro e fora das 24 h, com e sem mensagem pronta; Evolution não recebe; destino trocado cancela; fora do horário relido ao sair; duplicidade por aviso, reconciliação e job duplo; SAIR para avisos, campanha, reengajamento e automação, com o opt-out ao ERP em fila; VOLTAR; dois tenants.

## Tarefas
- [ ] Conecta API: configurações, consentimento, job, supressão e SAIR global.
- [ ] Conecta web e app: telas e rótulos.
- [ ] Mensagens prontas sugeridas (*utility*): "pagamento_confirmado", "pedido_enviado", "nota_emitida", "pedido_cancelado", "boleto_vencido".
- [ ] Manual do administrador e inventário de dados.
- [ ] Fluxo do repositório do Conecta (o mantenedor revisa o SAIR nas campanhas e no reengajamento).

## Dev Notes
- **Conecta:** `Application/Features/Inbox/Commands/ReceiveMessageCommandHandler.cs`, `Application/Features/Campaigns/Commands/SendCampaignCommand.cs`, `Infrastructure/Jobs/ReengagementJob.cs`, `RetomadaConversaJob.cs`, `Infrastructure/Automations/AutomationEngine.cs` (`ExecuteSendMessageToContact` usa o canal mais recente: não reaproveitar), `Infrastructure/Channels/MetaCloudApiService.cs` (`SendTemplateWithNamedParamsAsync`, botões), `Domain/Entities/MessageTemplate.cs`, `Message.cs`, `User.cs` (`WhatsappOptIn` do usuário, para o teste do CA 2).

## Fora do escopo
- Funil por evento e gatilho de automação (ATD-05b); WhatsApp disparado pelo próprio BemAqui Empresas (ATD-06a/b, WPP-01); avisos de orçamento e projeto; valor no aviso.

## Pontos em aberto
Decididos (PO, 27/11/2026): aceite do próprio cliente na conversa, pergunta separada; sem valor no aviso; Evolution sem aviso automático na v1; janela 8 h às 20 h; SAIR global (decisão 4). Nada pendente.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Escrita como ATD-05 (avisos e funil). | Claude (planejamento) |
| 27/11/2026 | 0.2 | Revisões de 27/11/2026: arquitetura, segurança, UX; decisões do PO. ATD-05 dividida em 05a (avisos, consentimento, SAIR) e 05b (funil e gatilho). Consentimento por telefone, versionado, separado do resumo, com prova; SAIR/PARAR/CANCELAR/STOP global com supressão e propagação ao ERP; destino e estado reconferidos; idempotência (pedido, situação, telefone); Evolution fora; "Nota emitida" só com a VND-02b; chips, prévia, contagem de aceites, teste, falhas com ação, aviso no cartão. | Claude (planejamento) |
