# ATD-02 — Painel do cliente na conversa: vínculo com o ERP, pedidos, cobranças, notas, 2ª via e status do projeto

| Campo | Valor |
|---|---|
| ID | ATD-02 |
| Título | No painel lateral da conversa (web e app do Conecta), o atendente vê o cliente do ERP ligado ao contato pelo telefone da conversa, com documento mascarado, os pedidos, as cobranças em aberto (só o boleto mais recente), as notas com o DANFE e, na fábrica, o status do projeto; manda a 2ª via na conversa com um clique; nada do ERP é guardado no Conecta além do vínculo |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §2 · [ADR 0053](../adr/0053-aplicativos-conectados.md) · [Plano](../integracoes/00-plano-integracoes.md) §3.3 e §9 · [INT-03](INT-03-api-integracao-v1.md) (CA 7 a 9, 15, 17 a 20 e a nota "Contrato com a ATD-01") · ADR 0037 (LGPD) |
| Dependências | ATD-01; INT-03 (Done); **VND-02b** para a nota do pedido (`GET /orders/{id}/invoice` responde 404 até lá) |
| Bloqueia | ATD-03a, ATD-03b, ATD-04 (usam o vínculo) |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`, `bemaqui-conecta`). ERP: nenhuma mudança |
| Ordem | 2 (F2) |

## User story
Como **atendente do Conecta**, quero **ver na conversa os pedidos, as cobranças e as notas do cliente que está falando comigo, e mandar a 2ª via sem sair dali**, para **responder "cadê meu boleto?" em segundos sem abrir o ERP**.

## Contexto
- Hoje o painel lateral da conversa (`bemaqui-atende-web/src/components/inbox/ContactPanel.vue`) mostra dados do contato, etiquetas e os negócios (`Deal`) do funil; no app, `bemaqui-conecta/app/(app)/conversation/[id].tsx` e `contact/[id].tsx`. O `Contact` tem `Phone` (E.164 sem `+`) e um `ExternalRef` genérico, usado pelos webhooks `contact_sync` de outros sistemas.
- A INT-03 entrega as leituras com dado mínimo e exige: **o Conecta confere o telefone da conversa contra o telefone do cliente no ERP antes de ler cobranças ou notas** (INT-03, Regras). A busca por telefone devolve nome, documento mascarado, cidade, UF, `canBill` e `canInvoice`; a busca por documento devolve só o primeiro nome e as iniciais.
- O link `pdfUrl` da cobrança é uma rota da API (exige o token do Conecta): não serve para o cliente abrir. A 2ª via manda a linha digitável e o Pix copia-e-cola em texto e o PDF como documento.

## Critérios de aceite

### Vínculo do contato com o cliente do ERP
1. **[Segurança]** **Dado** uma conversa aberta com o ERP conectado e `parties.view` concedido, **quando** o painel abre e o contato **não tem vínculo**, **então** o Conecta busca `GET /api/v1/integration/customers?phone=<E.164 do contato>`; achando **um** cliente, grava o vínculo `ContactErpLink` (contato, conexão, `erp_customer_id`, "pelo telefone", data) e mostra o cliente; achando nenhum, mostra "Cliente não encontrado no ERP pelo telefone desta conversa" com "Procurar por CPF/CNPJ" e "Cadastrar no ERP" (ATD-03a).
2. **[Segurança]** **Dado** "Procurar por CPF/CNPJ", **quando** o atendente informa um documento válido, **então** vê só o primeiro nome e as iniciais ("Maria S. O."); ao clicar "É este cliente", o vínculo é gravado como "pelo documento, por {atendente}", mas **sem telefone conferido**: o painel mostra o cliente e esconde pedidos, cobranças, notas e projetos com "O telefone desta conversa não é o do cadastro no ERP" e as saídas: "Completar o telefone no ERP" (só quando o cadastro do ERP não tem telefone: `PATCH /customers/{id}` com `parties.edit`, que só preenche campo vazio, INT-03 CA 11) ou "Peça ao ERP para trocar o telefone do cliente".
3. **[Segurança]** **Dado** um vínculo, **quando** o painel vai ler pedidos, cobranças, notas ou projetos, **então** o telefone precisa estar **conferido**: a busca por telefone do contato devolveu o mesmo `erp_customer_id` nas **últimas 24 h** (`phone_verified_at`); vencido, confere de novo antes de ler; o telefone do contato mudou no Conecta, o vínculo perde a conferência na hora. Sem conferência, nada financeiro nem fiscal é lido (teste: duas pessoas com o mesmo CPF digitado não veem cobranças uma da outra).
4. **Dado** o limite de buscas do ERP (30/min por conexão e 2.000/dia por tenant), **quando** muitas conversas abrem ao mesmo tempo, **então** o Conecta busca por telefone **só** no primeiro vínculo e na reconferência de 24 h; recebendo 429, mostra "O ERP pediu uma pausa. Tentando de novo em {n} s." e não insiste antes do `Retry-After`.
5. **Dado** "Desfazer vínculo" (supervisor ou `admin`), **quando** confirmado, **então** o vínculo é apagado e registrado (quem, quando); o contato volta ao CA 1.

### O que o painel mostra
6. **[Segurança]** **Dado** um vínculo conferido, **quando** o bloco **Cliente no ERP** aparece, **então** mostra nome, documento **mascarado** como veio do ERP (`***.456.789-**`), cidade e UF, e os chips "Pode receber boleto" (`canBill`) e "Pode receber nota" (`canInvoice`) com a `<Dica>` do que falta; nunca endereço, e-mail ou outro telefone (a API nem devolve).
7. **Dado** `sales.view`, **quando** abro **Pedidos**, **então** vejo os pedidos do cliente (`GET /customers/{id}/orders`): número, data, situação em linguagem simples, total, origem ("Pelo Conecta", "No ERP"), e ao abrir um, as linhas, a cobrança (situação) e a nota (situação); vazio "Nenhum pedido no ERP." com "Novo pedido" (ATD-03a, se houver `sales.create`).
8. **Dado** `finance.view`, **quando** abro **Cobranças**, **então** vejo os títulos em aberto (`GET /customers/{id}/charges`): número, vencimento, valor, saldo e "Vencido" em destaque; cada título mostra só o boleto mais recente; vazio "Nenhuma cobrança em aberto.".
9. **[Segurança]** **Dado** um título com boleto emitido, **quando** o atendente clica "Mandar 2ª via", **então** o Conecta lê `GET /charges/{id}?customerId=<vinculado>` (`no-store`) e manda **na conversa**, como mensagem do atendente: valor, vencimento, a linha digitável e o Pix copia-e-cola; o PDF (`GET /charges/{id}/pdf`) vai como documento pelo canal; a prévia aparece antes de enviar ("Mandar"); fora da janela de 24 h do WhatsApp oficial, o envio usa o modelo aprovado de 2ª via (CA 13) ou mostra "Fora da janela de 24 h: use o modelo aprovado de 2ª via."; a linha digitável e o Pix **não** ficam no painel depois de enviados (ficam só na mensagem da conversa).
10. **Dado** `fiscal.view`, **quando** um pedido tem nota (`GET /orders/{id}/invoice`), **então** o painel mostra número, série, situação e "Mandar DANFE" (`GET /invoices/{id}/danfe` como documento na conversa, com prévia); até a VND-02b, o bloco mostra "A nota do pedido aparece aqui quando o ERP emitir." sem erro.
11. **Dado** `projects.view` (tenant de fábrica), **quando** abro **Projetos**, **então** vejo `GET /customers/{id}/projects`: número, nome, etapa atual (letra e nome), situação ("No prazo", "Atrasado", "Entregue", "Cancelado") e a data prevista; sem valor nem custo (o DTO já não traz).
12. **Dado** um escopo que o ERP não concedeu (ou `insufficient_scope` em tempo de uso), **quando** o painel monta, **então** o bloco daquele escopo não aparece; com nenhum escopo útil, o painel mostra "O ERP não liberou informações de clientes para o Conecta.".

### Envio de documentos e LGPD
13. **Dado** o modelo de 2ª via para fora da janela, **quando** o administrador configura em Configurações → Integrações → BemAqui Empresas → "Modelo da 2ª via", **então** só escolhe um `MessageTemplate` com `MetaStatus = approved` do canal oficial; o modelo leva só primeiro nome, nome da empresa e "responda esta mensagem para receber a 2ª via" (sem valor, linha digitável nem Pix no modelo): a resposta do cliente abre a janela e o atendente manda a 2ª via pelo CA 9.
14. **[Segurança]** **Dado** um PDF do ERP (boleto ou DANFE), **quando** o Conecta precisa de URL para o canal, **então** sobe o arquivo no armazenamento com nome aleatório e **link assinado de até 1 h**, apagado em até 24 h por job; o PDF nunca fica público nem indexável; onde o canal aceitar *upload* de mídia (API oficial da Meta), usa o *upload* direto e não guarda o arquivo.
15. **[Segurança]** **Dado** qualquer leitura do ERP pelo painel, **quando** acontece, **então** os dados ficam só em memória (cache de até 60 s por conversa, invalidado por aviso do ERP do mesmo cliente), nunca em tabela do Conecta, nunca no `ContactMemory` nem nos *embeddings* da conversa; o Conecta registra **quem viu e quem mandou** (`erp_access_log`: atendente, contato, ação "viu cobranças", "mandou 2ª via", id do recurso, data), porque a auditoria do ERP só sabe "Integração Conecta (autorizada por Fulano)".
16. **Dado** um aviso do ERP (ATD-01) de pedido, cobrança ou nota de um cliente vinculado a um contato com conversa aberta, **quando** é processado, **então** o painel aberto daquele contato atualiza por SignalR ("Pagamento confirmado agora") sem recarregar.

### Permissões no Conecta
17. **Dado** os papéis do Conecta, **quando** um usuário abre o painel, **então** vale a visibilidade da conversa de hoje (quem vê a conversa vê o painel); "Mandar 2ª via" e "Mandar DANFE": `agent`, `supervisor` e `admin` da conversa; "Desfazer vínculo": `supervisor` e `admin`; a conta pode desligar o bloco **Cobranças** para `agent` em Configurações (ponto em aberto 1).

## Regras de negócio e técnicas
- **Rotas do ERP usadas (contrato `integration-v1`):** `GET /customers?phone=`/`?document=` (`parties.view`), `GET /customers/{id}` (`parties.view`), `PATCH /customers/{id}` (`parties.edit`, só campo vazio, com `Idempotency-Key`), `GET /customers/{id}/orders` e `GET /orders/{id}` (`sales.view`), `GET /customers/{id}/charges`, `GET /charges/{id}?customerId=`, `GET /charges/{id}/pdf` (`finance.view`), `GET /orders/{id}/invoice`, `GET /invoices/{id}/danfe` (`fiscal.view`), `GET /customers/{id}/projects` (`projects.view`).
- **Avisos consumidos (só para atualizar o painel):** `sales_order.*`, `billing_slip.issued|paid|cancelled`, `invoice.authorized|cancelled|rejected` (pelo `party_id` → vínculo).
- **Erros do ERP:** como a ATD-01 CA 12; 404 de recurso vira "Não encontrado no ERP" (o ERP responde 404 igual para outro tenant, inexistente ou de outro cliente).
- **Telefone:** comparação sempre em E.164 normalizado; o Conecta não manda telefone em log.
- **Vínculo próprio:** `ContactErpLink` é tabela nova (não reaproveita `Contact.ExternalRef`, que é dos webhooks de outros sistemas e não tem conferência de telefone).

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ContactErpLink` (`ITenantEntity`, `contact_id`, `erp_connection_id`, `erp_customer_id`, `linked_by` usuário ou "telefone", `linked_at`, `phone_verified_at`, `verified_phone_hash`) e `ErpAccessLog`; migration aditiva; `CustomerPanelQuery` (monta o painel com os escopos), comandos "Vincular", "Desfazer vínculo", "Completar telefone", "Mandar 2ª via", "Mandar DANFE"; job de limpeza dos PDFs; evento SignalR `ErpCustomerUpdated` no `InboxHub`.
**Conecta (`bemaqui-atende-web`):** bloco "BemAqui Empresas" no `ContactPanel.vue` com abas Pedidos, Cobranças, Notas, Projetos; prévia da 2ª via; modelo da 2ª via nas Configurações.
**Conecta (`bemaqui-conecta`, app):** o mesmo painel em `conversation/[id].tsx` (folha deslizante), com "Mandar 2ª via"; botões de pelo menos 44 px.
**ERP:** nenhuma mudança.

## Telas e estados (Conecta)
- **Painel:** carregando (esqueleto); sem conexão com o ERP ("Conecte o BemAqui Empresas para ver pedidos e cobranças", só para `admin`, com o caminho do ERP); sem vínculo (CA 1); telefone não conferido (CA 2); ERP fora do ar ("O ERP não respondeu. Tentar de novo."); suspensa ou desconectada (texto da ATD-01).
- **Propositivo:** título vencido aparece primeiro, com "Mandar 2ª via"; pedido "Aguardando pagamento" mostra "Mandar a cobrança de novo"; cliente sem cadastro oferece "Cadastrar no ERP" já preenchido com nome e telefone da conversa (ATD-03a).
- **Verbos:** "Vincular" → "Vinculado"; "Mandar 2ª via" → "2ª via enviada"; "Mandar DANFE" → "DANFE enviado"; "Desfazer vínculo" → "Vínculo desfeito".
- **Largura:** no app, a folha ocupa a tela; na web, o painel lateral rola por aba.

## Plano de testes
- **Unidade:** regra de conferência do telefone (vencida, telefone trocado, vínculo por documento); montagem da mensagem da 2ª via; mapeamento de escopo para bloco.
- **Integração (Conecta, ERP falso):** vínculo pelo telefone (CA 1); por documento sem conferência esconde o financeiro (CA 2, 3); reconferência de 24 h e 429 respeitado (CA 4); 2ª via dentro e fora da janela (CA 9, 13); PDF com link de 1 h e limpeza (CA 14); nada do ERP gravado em tabela, memória ou *embedding* (CA 15); `erp_access_log` gravado; dois tenants do Conecta não se cruzam.
- **Manual (piloto):** cliente real do tenant `bemaqui` no ERP SaaS com boleto do Inter de homologação.

## Tarefas
- [ ] Conecta API: vínculo, conferência, consulta do painel, 2ª via, DANFE, log de acesso, SignalR.
- [ ] Conecta web: bloco no `ContactPanel.vue` e configurações do modelo.
- [ ] Conecta app: painel na conversa.
- [ ] Textos no manual do usuário do Conecta (`docs/MANUAL_USUARIO.md`).
- [ ] Fluxo do repositório do Conecta (branch, revisão do mantenedor, merge e deploy).

## Dev Notes
- **Conecta:** `bemaqui-atende-web/src/components/inbox/ContactPanel.vue`, `ChatWindow.vue`, `composables/useInboxSocket.ts`; `bemaqui-atende-api/src/BemAquiAtende.API/Hubs/InboxHub.cs`; `Infrastructure/Channels/MetaCloudApiService.cs` (`SendMediaAsync`, `SendTemplateAsync`; mensagem fora da janela de 24 h falha com `FailureReason`); `Application/Common/Interfaces/IFileStorageService.cs` e `Infrastructure/Services/AzureBlobFileStorageService.cs`; `Domain/Entities/ContactMemory.cs` e `ConversationEmbedding.cs` (o que **não** pode receber dado do ERP).
- **ERP:** INT-03 Change Log itens 1, 4, 5, 6 e 13 (endereço não volta; leitura de qualquer pedido do tenant com `sales.view`; `/charges/{id}` só de cliente, com `customerId` para amarrar; DANFE só de nota de produção; situações do projeto).

## Fora do escopo
- Criar cliente e pedido e gerar cobrança (ATD-03a); pedido de orçamento (ATD-03b); IA (ATD-04); avisos automáticos (ATD-05).
- Endereço completo do cliente (INT-03 decidiu não devolver).
- Histórico financeiro, crédito e títulos pagos.

## Pontos em aberto (para o PO)
1. **Atendente (`agent`) vê cobranças?** **Recomendação:** sim por padrão (é o caso de uso principal: 2ª via), sempre com o telefone conferido, e a conta pode restringir a `supervisor` e `admin`.
2. **Vínculo pelo documento sem telefone conferido:** mostrar ao menos os pedidos? **Recomendação:** não; nada além do nome abreviado até o telefone bater, porque quem tem o CPF de outra pessoa não pode ver os pedidos dela.
3. **Guardar o PDF do boleto no armazenamento do Conecta**, mesmo por 24 h: **recomendação:** só quando o canal não aceitar *upload* direto, com link assinado de 1 h e limpeza em 24 h.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da INT-03 implementada e da leitura do painel de conversa do Conecta (web e app). | Claude (planejamento) |
