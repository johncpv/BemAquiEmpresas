# ATD-02 — Painel do cliente na conversa: cliente identificado, pedidos, cobranças, notas, 2ª via e status do projeto

| Campo | Valor |
|---|---|
| ID | ATD-02 |
| Título | No painel da conversa (web e app), o atendente vê o cliente do BemAqui Empresas **identificado pelo telefone** da conversa (ou por **código de 6 dígitos** mandado ao contato já cadastrado no ERP), com cartão-resumo, pedidos, cobranças em aberto, notas e projeto, e manda a 2ª via com um clique; o destino é reconferido na hora do envio; mensagens com linha digitável, Pix ou DANFE ficam fora da IA; nada do ERP é guardado além da identificação e do registro de quem viu |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §2 · [ADR 0053](../adr/0053-aplicativos-conectados.md) · [Plano](../integracoes/00-plano-integracoes.md) §3.3 e §9 · [INT-03](INT-03-api-integracao-v1.md) (CA 7 a 9, 15, 17 a 20 e a nota "Contrato com a ATD-01") · [INT-04](INT-04-ajustes-avisos-e-conexao.md) (CA 11 a 14) · ADR 0037 · decisões do PO de 27/11/2026 (1) |
| Dependências | ATD-01, INT-04; INT-03 (Done); **VND-02b** para a nota do pedido (até lá, `GET /orders/{id}/invoice` responde 404) |
| Bloqueia | ATD-03a, ATD-03b, ATD-04, ATD-05a |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`, `bemaqui-conecta`). ERP: nada além da INT-04 |
| Ordem | 2 (F2) |

## User story
Como **atendente do Conecta**, quero **ver na conversa os pedidos, as cobranças e as notas do cliente que está falando comigo e mandar a 2ª via sem sair dali**, para **responder "cadê meu boleto?" em segundos, sem abrir o BemAqui Empresas e sem mostrar dado de outra pessoa**.

## Contexto
- Painel atual: `bemaqui-atende-web/src/components/inbox/ContactPanel.vue` (contato, etiquetas, negócios); no app, `bemaqui-conecta/app/(app)/conversation/[id].tsx` e `contact/[id].tsx`. `Contact.Phone` em E.164 sem `+`; `Contact.ExternalRef` é dos webhooks `contact_sync` de outros sistemas.
- INT-03: busca por telefone devolve nome, documento mascarado, cidade, UF, `canBill`, `canInvoice`; por documento, só primeiro nome e iniciais. INT-04: `phoneSetByApp` e o código de identidade (`identity-challenges`).
- **Decisão do PO 1:** telefone que não bate (ou cadastro sem telefone) → código de 6 dígitos ao e-mail ou telefone **já** no cadastro do ERP, repetido pelo cliente na conversa; sem contato no ERP, só `supervisor` libera, registrado; telefone gravado a partir da conversa **nunca** é prova.

## Critérios de aceite

### Identificar o cliente
1. **[Segurança]** **Dado** uma conversa com o BemAqui Empresas conectado e `parties.view`, **quando** o painel abre e o contato não tem identificação, **então** o Conecta só busca se a conversa tem **mensagem recebida do cliente** (inbound real) naquele telefone, e busca `GET /customers?phone=<E.164>`: **um** resultado com `phoneSetByApp = false` → grava `ContactErpLink` "identificado pelo telefone" com o telefone conferido; resultado com `phoneSetByApp = true` → trata como não conferido (CA 3); **mais de um** → não identifica sozinho: lista os candidatos com nome, cidade/UF e os 3 últimos dígitos do documento, o atendente escolhe e segue o CA 3; nenhum → "Não achei este cliente no BemAqui Empresas pelo telefone desta conversa" com "Procurar por CPF/CNPJ" e "Cadastrar no BemAqui Empresas" (ATD-03a).
2. **[Segurança]** **Dado** "Procurar por CPF/CNPJ", **quando** o atendente informa um documento válido, **então** vê o primeiro nome e as iniciais ("Maria S. O."); "É este cliente" leva ao CA 3. Limites do Conecta: 10 buscas por documento por atendente por hora e 3 por conversa por dia; toda busca vai ao `erp_access_log` **inclusive sem resultado**; mais de 20 buscas sem resultado por atendente em um dia notifica o `supervisor`.
3. **[Segurança]** **Dado** um cliente escolhido sem telefone conferido, **quando** o atendente clica "Confirmar identidade", **então** o Conecta pede ao ERP o código (`POST /customers/{id}/identity-challenges`, canal escolhido entre os que o ERP oferecer) e mostra "Código enviado para m***@g***.com. Peça ao cliente para repetir aqui na conversa."; o código digitado **pelo cliente na conversa** é conferido pelo ERP (`…/verify`); confere → "Cliente identificado pelo CPF/CNPJ (código confirmado)", válido enquanto o telefone da conversa não mudar, com nova confirmação a cada 90 dias; errado ou vencido → a mensagem do ERP e "Mandar outro código"; sem contato válido no ERP (`no_verified_contact`) → "Este cliente não tem e-mail nem telefone confirmados no BemAqui Empresas" e só `supervisor` libera ("Liberar sem código", com motivo obrigatório, registrado).
4. **[Segurança]** **Dado** um cliente identificado só pelo que veio da própria conversa (cadastro feito no Conecta ou telefone completado pelo Conecta), **quando** o painel monta, **então** mostra apenas o que **nasceu nesta conversa**: pedidos com `ErpOrderLink` desta conversa e as cobranças e notas desses pedidos; o resto do histórico do cliente fica escondido com "Para ver outros pedidos e cobranças, confirme a identidade" (CA 3).
5. **[Segurança]** **Dado** a identificação, **quando** o painel vai ler dados, **então** confere: telefone atual do contato igual ao conferido (`verified_phone_hash` = **HMAC-SHA256 com chave** do E.164, nunca hash simples); conferência pelo telefone com menos de 24 h (reconferida **em segundo plano**, sem esvaziar o painel, usando a cota reservada da ATD-01); telefone do contato mudou → perde a identificação na hora. O painel mostra "identificado por último em dd/mm".
6. **[Segurança]** **Dado** um contato identificado, **quando** alguém tenta editar o telefone dele no Conecta, **então** só `supervisor` e `admin` editam, e editar **desfaz a identificação**; o `contact_sync` por `X-Api-Key` **não** cria nem altera telefone, nome ou `ExternalRef` de contato identificado (registra o evento como "Ignorado: cliente identificado no BemAqui Empresas"; teste).
7. **Dado** "Desfazer identificação" (`supervisor` ou `admin`), **quando** confirmado, **então** apaga o vínculo e registra quem e quando.

### O que o painel mostra
8. **Dado** um cliente identificado, **quando** o painel abre, **então** o topo é um **cartão-resumo**: nome, documento mascarado como veio, cidade/UF, o texto da identificação ("Cliente identificado pelo telefone" ou "…pelo CPF/CNPJ (telefone diferente)"), o alerta de título vencido ("1 boleto vencido de R$ 120,00 desde 20/11") com **"Mandar 2ª via"** visível, e o último pedido (número, situação); embaixo, Pedidos, Cobranças, Notas e Projetos, **como abas só quando há mais de um item**; chips "Pode receber boleto"/"Pode receber nota" com a `<Dica>` do que falta.
9. **Dado** `sales.view`, **quando** abro Pedidos, **então** vejo até os 20 mais recentes (`GET /customers/{id}/orders`; a rota não pagina: o Conecta corta e mostra "Ver os demais no BemAqui Empresas"): número, data, situação em linguagem simples, total, origem; vazio "Nenhum pedido no BemAqui Empresas." com "Novo pedido" (ATD-03a).
10. **Dado** `finance.view`, **quando** abro Cobranças, **então** vejo até 20 títulos em aberto (vencidos primeiro): número, vencimento, valor, saldo, "Vencido"; só o boleto mais recente; vazio "Nenhuma cobrança em aberto.".
11. **[Segurança]** **Dado** "Mandar 2ª via", **quando** o atendente clica, **então** o Conecta lê `GET /charges/{id}?customerId=` (`no-store`), mostra a **prévia** e, ao confirmar "Mandar", **reconfere o destino na hora**: HMAC do E.164 do destinatário real da conversa = `verified_phone_hash` e o cliente ainda identificado; divergência cancela com "O telefone desta conversa mudou. Confirme a identidade de novo." Manda valor, vencimento, linha digitável e Pix em texto, e o PDF como documento (CA 15); idempotente por **(título, conversa, 10 min)** (dois cliques não mandam duas). Se o cliente **não** falou nas últimas 24 h, o painel diz "O cliente não falou com você nas últimas 24 h. Mande a mensagem pronta para ele responder." e usa a mensagem pronta (CA 14); quando ele responde, o painel mostra **"Cliente respondeu: mandar a 2ª via"**.
12. **Dado** `fiscal.view`, **quando** um pedido tem nota, **então** número, série, situação e "Mandar DANFE" (prévia, reconferência do destino e PDF como no CA 11); até a VND-02b, "A nota aparece aqui quando o BemAqui Empresas emitir.".
13. **Dado** `projects.view`, **quando** abro Projetos, **então** número, nome, etapa (letra e nome), situação ("No prazo", "Atrasado", "Entregue", "Cancelado") e data prevista; até 20.

### Mensagens, documentos e LGPD
14. **Dado** a mensagem pronta da 2ª via, **quando** o `admin` configura, **então** escolhe uma mensagem pronta **aprovada pelo WhatsApp** do canal oficial, só com primeiro nome, nome da empresa e "responda esta mensagem para receber a 2ª via" (sem valor, linha digitável ou Pix).
15. **[Segurança]** **Dado** um PDF do ERP, **quando** vai ao cliente, **então** pelo canal oficial sobe direto na Meta (*upload* de mídia) sem guardar; nos outros canais, sobe num contêiner **privado**, nome aleatório de 128 bits, link assinado de **5 a 15 min**, apagado logo depois do envio (e por job de segurança em até 1 h), URL fora do log.
16. **[Segurança]** **Dado** uma mensagem com linha digitável, Pix copia-e-cola ou DANFE, **quando** é gravada na conversa, **então** fica marcada **sensível**: no histórico enviado à IA, no `ContactMemory` e nos *embeddings* ela entra só como "[2ª via enviada]" (teste canário com linha digitável e Pix).
17. **[Segurança]** **Dado** leituras do ERP, **quando** acontecem, **então** os dados ficam só em memória (cache de até 60 s, invalidado por aviso do mesmo cliente), nunca em tabela, `ContactMemory` ou *embedding*; o `erp_access_log` é **só de inclusão** (append-only), com retenção de 12 meses: atendente, contato, ação (viu cobranças, mandou 2ª via, buscou documento, identidade confirmada ou negada, liberação do `supervisor`), recurso, IP e data; o Conecta manda o atendente ao ERP no `BemAqui-On-Behalf-Of` (INT-04 CA 14); o `supervisor` vê o relatório "Quem viu o quê" por período.
18. **Dado** um aviso do ERP de pedido, cobrança ou nota de um cliente identificado, **quando** processado, **então** o SignalR manda **só "invalidar o painel do contato X"**, só para quem vê aquela conversa; o painel relê (nada de dado no evento).
19. **Dado** a exclusão ou anonimização do contato no Conecta (pedido do titular), **quando** acontece, **então** apaga identificação e PDFs pendentes; o `erp_access_log` fica com o contato pseudonimizado. A retenção de cada tabela nova fica declarada no inventário de dados do Conecta.

### Permissões
20. **Dado** os papéis do Conecta, **quando** usam o painel, **então** quem vê a conversa vê o painel; "Mandar 2ª via" e "Mandar DANFE": `agent`, `supervisor`, `admin`; "Desfazer identificação", "Liberar sem código" e editar telefone de contato identificado: `supervisor` e `admin`; a conta pode tirar Cobranças do `agent`. Quem não pode agir vê o texto "Peça a um supervisor para…".

## Regras de negócio e técnicas
- **Rotas do ERP:** `GET /customers?phone=`/`?document=`, `GET /customers/{id}`, `POST /customers/{id}/identity-challenges` e `…/verify` (`parties.view`); `PATCH /customers/{id}` (`parties.edit`, só campo vazio, com `Idempotency-Key`; o telefone completado assim não vale como prova); `GET /customers/{id}/orders`, `GET /orders/{id}` (`sales.view`); `GET /customers/{id}/charges`, `GET /charges/{id}?customerId=`, `GET /charges/{id}/pdf` (`finance.view`); `GET /orders/{id}/invoice`, `GET /invoices/{id}`, `GET /invoices/{id}/danfe` (`fiscal.view`); `GET /customers/{id}/projects` (`projects.view`).
- **Avisos usados (só para invalidar):** `sales_order.*`, `billing_slip.*`, `invoice.*` (pelo `party_id`, INT-04 CA 3).
- **Cota de buscas** (2.000/dia por empresa no ERP): 70% para o atendente, 20% reconferência, 10% IA (ATD-01); acabou a parte do atendente, "Limite de buscas do dia atingido. Tente amanhã ou peça ao BemAqui Empresas para aumentar.".

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ContactErpLink` (`contact_id`, `erp_connection_id`, `erp_customer_id`, `method` telefone|código|supervisor|conversa, `linked_by`, `linked_at`, `phone_verified_at`, `verified_phone_hash` com HMAC, `code_verified_at`), `ErpAccessLog` (só inclusão), `ErpIdentityChallenge` (id do desafio, atendente, conversa); marcação `IsSensitive` em `Message`; filtro das mensagens sensíveis no montador do histórico da IA, no `GenerateContactMemoryJob` e no `IndexConversationEmbeddingJob`; trava de edição do telefone; regra no `SyncExternalContactCommand`; consultas do painel, 2ª via, DANFE, limpeza de PDFs; evento `ErpCustomerInvalidated`.
**Conecta (`bemaqui-atende-web`):** bloco no `ContactPanel.vue` (cartão-resumo, listas, identidade, prévia), relatório "Quem viu o quê", mensagem pronta da 2ª via.
**Conecta (`bemaqui-conecta`, app):** cartão-resumo como **barra recolhida acima do teclado** na conversa, que abre a folha com as listas; "Mandar 2ª via" e "Mandar DANFE" abrem folha com a prévia; botões de 56 px.

## Telas e estados (Conecta)
- Carregando (esqueleto); sem conexão ("O BemAqui Empresas não está conectado. Peça ao administrador."); sem identificação (CA 1); candidatos (CA 1); confirmar identidade (CA 3); conteúdo só da conversa (CA 4); "Não consegui falar com o BemAqui Empresas. Tentar de novo"; suspensa/desconectada (textos da ATD-01).
- **Verbos:** "Confirmar identidade" → "Identidade confirmada"; "Mandar 2ª via" → "2ª via enviada"; "Mandar DANFE" → "DANFE enviado"; "Desfazer identificação" → "Identificação desfeita".
- **Propositivo:** vencido primeiro, com a ação à vista; cliente não encontrado oferece o cadastro já com nome e telefone.

## Plano de testes
- **Unidade:** decisão da identificação (um, vários, `phoneSetByApp`, nenhum); HMAC do telefone; reconferência; corte em 20; idempotência da 2ª via; montagem do histórico da IA sem mensagem sensível (canário).
- **Integração (Conecta, ERP falso):** telefone com vários clientes; código certo, errado, vencido e `no_verified_contact` com liberação do supervisor; cliente só da conversa vê só os pedidos da conversa; mesmo CPF digitado por outra pessoa não vê cobranças; telefone mudado no meio cancela a 2ª via; `contact_sync` não altera contato identificado; PDF com link curto apagado; SignalR só para quem vê a conversa; `erp_access_log` sem `UPDATE`/`DELETE`; dois tenants.

## Tarefas
- [ ] Conecta API: identificação, código, painel, 2ª via, DANFE, sensíveis, log, SignalR, trava de telefone, `contact_sync`.
- [ ] Conecta web e app: painel, prévias, relatório.
- [ ] Manual do usuário e inventário de dados do Conecta.
- [ ] Fluxo do repositório do Conecta.

## Dev Notes
- **Conecta:** `components/inbox/ContactPanel.vue`, `ChatWindow.vue`, `composables/useInboxSocket.ts`; `API/Hubs/InboxHub.cs`; `Infrastructure/Channels/MetaCloudApiService.cs` (`SendMediaAsync`, `SendTemplateAsync`); `IFileStorageService`/`AzureBlobFileStorageService.cs`; `Domain/Entities/ContactMemory.cs`, `ConversationEmbedding.cs`; `Infrastructure/Jobs/GenerateContactMemoryJob.cs`, `IndexConversationEmbeddingJob.cs`; `Application/Features/AiAgents/Commands/ProcessAiMessageCommandHandler.cs` (montagem do histórico); `Application/Features/ExternalWebhooks/Commands/` (`SyncExternalContactCommand`).
- **ERP:** INT-03 Change Log itens 1, 4, 5, 6 e 13; INT-04 CA 11 a 14.

## Fora do escopo
- Criar cliente e pedido e cobrar (ATD-03a); orçamento (ATD-03b); IA (ATD-04); avisos automáticos (ATD-05a); endereço completo; títulos pagos e crédito.

## Pontos em aberto
Decididos (PO, 27/11/2026): `agent` vê cobranças por padrão, com opção de restringir; sem identidade confirmada, nada além do nome abreviado (e o que nasceu na conversa); PDF por *upload* direto, link curto nos outros canais; código de identidade (decisão 1).
Para o PO: **validade da identidade por código** — **recomendação:** enquanto o telefone da conversa não mudar, com nova confirmação a cada 90 dias.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da INT-03 e da leitura do painel do Conecta. | Claude (planejamento) |
| 27/11/2026 | 0.2 | Revisões de 27/11/2026: arquitetura, segurança, UX; decisões do PO. Código de identidade, conversa inbound, vários candidatos, conteúdo só da conversa para cadastro feito no Conecta; HMAC com chave, reconferência em segundo plano e no envio; mensagens sensíveis fora da IA; limites e log de busca por documento; log só de inclusão com relatório; trava do telefone e `contact_sync`; SignalR só invalida; PDF com link curto; cartão-resumo, abas condicionais, textos, barra no app. | Claude (planejamento) |
