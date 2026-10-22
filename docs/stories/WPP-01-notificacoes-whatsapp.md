# WPP-01 — Notificações por WhatsApp (cobrança e status do projeto)

| Campo | Valor |
|---|---|
| ID | WPP-01 |
| Título | Avisos por WhatsApp ao contato do cliente pela API oficial da Meta, por um parceiro (BSP) contratado pelo tenant, atrás de `IWhatsAppSender`: lembrete da régua de cobrança e mudança de etapa do projeto, só com o aviso e o link do portal; opt-in pelo próprio contato no portal, que cai quando o número muda; modelos aprovados com prévia; envio em dias úteis das 8 h às 18 h; fila com tentativas; webhook com assinatura pela FND-16; tela Configurações → Integrações → WhatsApp com credenciais só de escrita e "Testar envio" |
| Tamanho | G |
| Status | Ready (pré-condição externa) |
| Requisitos | Backlog WPP-01 ("Notificações por WhatsApp (cobrança, status do projeto)") · R-FIN-9 (régua de cobrança) · PRD §3.2 (WhatsApp) · PRD §6 (LGPD, Observabilidade) · R-SEC-5 |
| Dependências | FIN-09 (régua de cobrança, antes), POR-01a (opt-in no portal, host e "Preferências de aviso"), POR-01b (link do projeto, avisos de marco), POR-01c (financeiro do portal), CAD-01a (contatos), CAD-03 (`IBusinessCalendar`), FND-16 (webhooks de entrada), FND-11 (segredos), FND-12 (alertas de integração), FND-08 (padrão de fila e templates) |
| Bloqueia | GOL-01a (marco final "Go-live") |
| Ordem de execução | 5 (Marco 8.4 "Comunicação") |

## User story
Como **financeiro**, quero **que o cliente receba no WhatsApp o lembrete do pagamento**, e como **cliente**, quero **saber pelo WhatsApp quando o meu projeto muda de etapa**, para **receber mais rápido e o cliente ficar informado sem ligar**, sempre pelo portal e só se o cliente pediu.

## Contexto e referências
- Backlog Fase 8 (WPP-01); PRD §3.2; R-FIN-9 (régua de cobrança por e-mail, FIN-09, com os passos −3, 0, +3 e +10 dias).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D6 (2FA de novo ao salvar credencial de integração), D17; tabela 3.5, pontos 17 a 21; §4.3 transversal (jobs com `tenantId`, rate limit, webhooks); §4.4 padrão 6 e os achados "prévia da mensagem do WhatsApp e lista de falhas reenviáveis".
- **ADR 0041** (`docs/adr/0041-whatsapp-provedor-consentimento.md`): API oficial da Meta por BSP, custo do tenant, opt-in pelo próprio contato, troca de número derruba o opt-in, conteúdo mínimo, janela de envio.
- **ADR 0007, adendo de 21/10/2026** (`SecretKind.WhatsAppCredentials`, só de escrita); **ADR 0031** (recepção de webhooks); **ADR 0042** (telas de integração em Configurações, segredos só de escrita); ADR 0009, adendo (rotas `/webhooks/*`); ADR 0034 (nada real no `konebras` antes da virada).
- LGPD: o telefone do contato é dado pessoal; a base legal é o consentimento do próprio contato, registrado com a versão do texto.

## Critérios de aceite
**Configuração (Configurações → Integrações → WhatsApp)**
1. **Dado** `settings.edit`, **quando** abro `/app/configuracoes/integracoes/whatsapp`, informo o parceiro (BSP), as credenciais (token de acesso, id do número, id da conta do WhatsApp Business e segredo do app) e o número remetente, e clico "Salvar credenciais", **então** o sistema pede o **2FA de novo** (TOTP recente, D6), grava as credenciais cifradas como `SecretKind.WhatsAppCredentials`, **só de escrita** (nunca devolvidas pela API; a tela mostra "configurado em dd/mm/aaaa por {usuário}"), e "Testar conexão" mostra o resultado traduzido (credencial recusada, número não verificado, rede) pelos estados do padrão 6; sem configuração, nada é enviado e as telas mostram "WhatsApp não configurado".
2. **Dado** a tela, **quando** a abro, **então** vejo o aviso "O custo das mensagens é cobrado pelo parceiro na conta da {empresa}.", o ambiente (teste ou produção) no `EnvironmentBanner`, a URL do webhook e o token de verificação (mostrado **uma vez** ao gerar, guardado só o hash), e a lista "Números de teste" (até 5, E.164).
3. **Dado** os modelos, **quando** clico "Atualizar modelos", **então** o sistema lê do parceiro os modelos aprovados e mostra os do sistema ("Aviso de pagamento" e "Aviso do projeto") com a situação (Aprovado, Pendente, Recusado) e a **prévia** com dados de exemplo ("Olá, Maria. A {empresa} tem um aviso sobre um pagamento. Veja no portal: {link}"); modelo não aprovado não é enviado.
4. **Dado** um modelo aprovado, **quando** clico "Testar envio" e escolho um número da lista de teste, **então** a mensagem de exemplo vai para esse número pela fila, com os estados do padrão 6; número fora da lista recusa com 422.

**Consentimento**
5. **[Segurança]** **Dado** um contato com acesso ativo ao portal (POR-01a), **quando** ele abre "Preferências de aviso" e liga "Avisos por WhatsApp" para o telefone do seu contato (mostrado mascarado, com "Este não é meu número? Fale com a {empresa}."), lendo o texto do consentimento, **então** o opt-in é gravado com o telefone normalizado (E.164), a versão do texto, a data, o IP e o user-agent; **só o próprio contato** liga o opt-in (o backoffice vê a situação, mas não consegue ligar: a API recusa com 403); ele pode desligar a qualquer momento.
6. **[Segurança]** **Dado** o telefone do contato trocado (no cadastro do terceiro, CAD-01a, ou por qualquer caminho), **quando** gravado, **então** o opt-in **cai na hora** (`whatsapp_opt_out_at` com o motivo "Número trocado"), e nenhuma mensagem sai para o número novo até um opt-in novo do próprio contato.
7. **Dado** uma resposta "SAIR" (ou "PARAR") ou o descadastro pelo parceiro, **quando** o webhook chega, **então** o opt-in é revogado na hora e registrado; o conteúdo da mensagem recebida não é guardado.

**Envio**
8. **Dado** a régua de cobrança (FIN-09) com o canal WhatsApp ligado no passo (−3, 0, +3 ou +10 dias), **quando** o job da régua roda, **então** enfileira, para cada título elegível, uma mensagem por contato com "Recebe cobrança", acesso ativo ao portal e opt-in, pelo modelo "Aviso de pagamento" com o link de `/portal/financeiro`; idempotente por título, passo da régua e contato (`dedup_key`), com teste de execução dupla e de dois tenants; título pago, cancelado ou com a régua pausada não gera mensagem.
9. **Dado** um marco público do projeto (os mesmos da POR-01b CA 4), **quando** o parâmetro "Avisar etapa por WhatsApp" está ligado, **então** os contatos do cliente com acesso ativo ao portal e opt-in recebem o modelo "Aviso do projeto" com o link do projeto no portal; idempotente pelo evento.
10. **[Segurança]** **Dado** a mensagem, **quando** montada, **então** leva **só o aviso e o link do portal**: as variáveis permitidas são o primeiro nome do contato, o nome fantasia da empresa e o link no host do portal; nunca valor, vencimento, número do título, CPF, CNPJ, link de boleto, observação interna nem texto livre de usuário ou cliente (teste da montagem com canário).
11. **Dado** o horário, **quando** uma mensagem fica pronta fora de **dia útil, das 8 h às 18 h** (`America/Sao_Paulo`, `IBusinessCalendar`), **então** é agendada para o início da próxima janela, sem perder a idempotência.
12. **Dado** a fila (`whatsapp`, poucos workers), **quando** o parceiro falha, **então** tenta 3 vezes com backoff, depois marca `Failed` com o motivo e conta para o alerta de integração (`whatsapp:{tenant}`, FND-12); jobs recebem `tenantId` e só ids, e conferem o módulo liberado antes de enviar.
13. **Dado** o tenant `konebras` antes da GOL-01b, **quando** o envio roda, **então** só os números da lista de teste recebem mensagem; o envio aos contatos dos clientes liga na virada (checklist da GOL-01b); no tenant `demo`, só números de teste.

**Webhook, histórico e permissões**
14. **[Segurança]** **Dado** o webhook do parceiro (`/webhooks/whatsapp/{tenantKey}`, provedor `whatsapp` no registro da FND-16), **quando** chega um `POST`, **então** a assinatura `X-Hub-Signature-256` (HMAC SHA-256 do corpo cru com o segredo do app) é validada em tempo constante antes de qualquer leitura (sem assinatura válida, 401 sem detalhe), é idempotente pelo id da mensagem e atualiza o envio (Enviado, Entregue, Lido, Falhou); o `GET` de verificação da Meta (`hub.mode`, `hub.verify_token` conferido pelo hash em tempo constante, devolve `hub.challenge`) é a única exceção ao "só POST" da FND-16, declarada pelo provedor (ADR 0031/0041).
15. **Dado** um título ou projeto, **quando** abro o histórico de comunicação, **então** vejo os envios por WhatsApp (data, contato, modelo, situação) junto com os e-mails; o telefone aparece mascarado para quem não tem `parties.edit`.
16. **Dado** a tela de configuração, **quando** abro "Falhas de envio", **então** vejo as mensagens `Failed` dos últimos 30 dias com o motivo e "Reenviar" (só enquanto o título ou o marco ainda é elegível e o opt-in vale, sem duplicar pela `dedup_key`), com o impacto em lista no reenvio em lote.
17. **Dado** o módulo dono não liberado (`finance` para cobrança, `projects` para etapa) ou o WhatsApp não configurado, **quando** o job roda, **então** não envia; os endpoints de configuração exigem `settings.edit` (403 `forbidden` sem a ação; 403 `module_not_released` na camada 1) e todos têm `RequireModule` e `perm:`.
18. **Dado** credenciais, consentimentos e envios, **quando** consulto a auditoria, **então** vejo quem e quando; as credenciais só como "alterado" (`[ProtectedSecret]`, nunca no diff) e o telefone mascarado (`[PersonalData]`); canário: credencial, token de verificação e telefone fora do log e do `error_log` do nginx.

## Regras de negócio e técnicas
- **`IWhatsAppSender`** (BuildingBlocks, como o `IEmailSender`), com o adaptador da API oficial da Meta pelo parceiro (só `HttpClient`, URL base fixa do parceiro, token em cabeçalho, log do `HttpClient` desligado, como na ADR 0014); parceiro que exija outra API ganha adaptador próprio.
- **Tabelas:** `shared.whatsapp_settings` (parceiro, número, ambiente, `credentials_secret_id`, `verify_token_hash`, `set_at`, `set_by`, `test_numbers`), `shared.whatsapp_templates` (código, nome no parceiro, idioma, texto, variáveis, situação), `shared.whatsapp_logs` (`to` `[PersonalData]`, modelo, `related_type`, `related_id`, `dedup_key`, `status`, `provider_message_id`, tentativas, erro, `scheduled_for`), `parties.party_contacts` (+ `whatsapp_opt_in_phone`, `whatsapp_opt_in_at`, `whatsapp_opt_in_text_version`, `whatsapp_opt_in_ip`, `whatsapp_opt_in_user_agent`, `whatsapp_opt_out_at`, `whatsapp_opt_out_reason`).
- Telefone normalizado em E.164; a comparação do opt-in é com o telefone atual do contato (CA 6).
- Parâmetros: canal WhatsApp por passo da régua (FIN-09, Configurações → Cobrança → "Régua de cobrança") e "Avisar etapa por WhatsApp" (Configurações → Integrações → WhatsApp).

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (configuração, com 2FA de novo para credenciais); `parties.edit` (ver o telefone completo); o opt-in é do usuário do portal; os jobs pelos módulos donos.
- **Eventos:** consome os marcos do projeto e a régua da FIN-09; consome `PartyContactChanged` (POR-01a) para derrubar o opt-in.
- **Tabelas:** acima; segredo `WhatsAppCredentials` (ADR 0007).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (integração externa assíncrona: "Enviado / Aguardando / Demorando (30 s) / Confirmado / Recusado com motivo e ação", sem reenvio enquanto processa, `EnvironmentBanner` único); 5 (opt-in no portal com vocabulário leigo); 7 (reenvio em lote com o impacto em lista).
- **Configurações → Integrações → WhatsApp** (`/app/configuracoes/integracoes/whatsapp`): parceiro e credenciais (só de escrita), número, ambiente, webhook, números de teste, modelos com prévia, "Testar conexão", "Testar envio", "Avisar etapa por WhatsApp", "Falhas de envio".
- **Portal:** seção "Avisos por WhatsApp" em `/portal/preferencias`.
- **Backoffice:** situação do opt-in no contato do terceiro (sem botão de ligar); histórico de comunicação no título e no projeto.
- **Verbos:** "Salvar credenciais" / "Credenciais salvas"; "Testar conexão"; "Testar envio" / "Mensagem de teste enviada"; "Reenviar" / "Reenvio na fila".
- **Estados:** carregando; não configurado (primeira configuração com os campos e os modelos sugeridos); modelo não aprovado; falha do parceiro com o motivo; vazio do histórico e das falhas; erro.
- **400 px:** configuração em seções empilhadas; prévia da mensagem em largura total.
- **Ajuda:** `Ajuda/configuracoes-integracoes-whatsapp.md` (prevista); `Ajuda/portal-preferencias.md` ("Avisos por WhatsApp"); atualizar `Ajuda/cadastros-terceiro-contatos.md` ("WhatsApp do contato"); `<Dica>` em "Consentimento", "Modelo aprovado", "Números de teste" e "Token de verificação".

## Plano de testes
- **Unidade:** montagem da mensagem só com as variáveis permitidas e canário (CA 10); normalização do telefone; janela de envio (CA 11).
- **Integração (Testcontainers, parceiro falso):** credenciais só de escrita com 2FA de novo e teste (CA 1, 2); modelos e prévia (CA 3); testar envio só para número de teste (CA 4); opt-in só pelo contato e recusado pelo backoffice (CA 5); troca de número derruba o opt-in (CA 6); "SAIR" pelo webhook (CA 7); régua idempotente com dois tenants (CA 8); etapa (CA 9); retentativas e alerta (CA 12); bloqueio no `konebras` antes da virada (CA 13); webhook com assinatura, idempotente, e o GET de verificação (CA 14); histórico (CA 15); falhas e reenvio (CA 16); módulos e permissões (CA 17); auditoria e canário (CA 18).
- **Manual:** envio real para um número de teste no ambiente do parceiro, no tenant `demo`.

## Fora do escopo
- Atendimento ou conversa pelo WhatsApp (caixa de entrada, chatbot).
- Mensagens a funcionários (holerite, ponto).
- Valores, boletos ou documentos na mensagem.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.5, pontos 17–21; D17, ADR 0041):
1. API oficial da Meta por um parceiro (BSP), custo do tenant, atrás do `IWhatsAppSender` (CA 1, 2).
2. Opt-in pelo próprio contato, no portal; troca de número derruba o opt-in (CA 5, 6).
3. Textos dos modelos aprovados pela Konebras e pela Meta, com prévia na tela (CA 3).
4. Dias úteis, das 8 h às 18 h (CA 11).
5. FIN-09 antes (dependência; CA 8).

Decisões da revisão aplicadas: D17 (mensagem só com o aviso e o link do portal, CA 10); D6 (2FA de novo ao salvar as credenciais, CA 1); novo requisito de Configurações (tela em Integrações, segredos só de escrita, ADR 0042); §4.3 transversal (jobs com `tenantId` e módulo conferido, CA 12; canário também no `error_log` do nginx, CA 18); §4.4 padrão 6 e os achados de prévia e de falhas reenviáveis (CA 3, 16); ADR 0034 (nada real no `konebras` antes da virada, CA 13).

**Pré-condição externa:** contrato do tenant com o parceiro (BSP), número verificado na Meta e aprovação dos modelos pela Meta (não bloqueia o `Ready`; o código é testado com o parceiro falso e no ambiente de teste do parceiro).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade, integração (parceiro falso) e o teste manual passando.
- [ ] Migration criada (não destrutiva); tipo de segredo novo com o teste golden da ADR 0007.
- [ ] Permissões só do `ModuleCatalog`; provedor `whatsapp` no registro da FND-16, com o GET de verificação documentado na ADR 0031/0041.
- [ ] Telas com estados; ajuda prevista escrita; teste de cobertura passando.
- [ ] Alertas de integração ligados; auditoria ligada.
- [ ] Item "ligar o envio aos clientes" no checklist da GOL-01b.
- [ ] OpenAPI e cliente Orval regenerados; `docs/02-arquitetura.md` (integração) e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IWhatsAppSender` e o adaptador da API oficial; job de envio com janela; provedor do webhook (POST com assinatura e GET de verificação).
- [ ] Opt-in no portal e queda pela troca de número; canal WhatsApp na régua da FIN-09.
- [ ] Tela Configurações → Integrações → WhatsApp, falhas reenviáveis e ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (parceiro e webhook), cybersecurity (segredo, assinatura, consentimento, LGPD), design (prévia e opt-in no portal), @qa.
- Gate: CA 1, 5, 6, 8, 10, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: POR-01a, FND-16 e CAD-03; sem a BNK-01b (a mensagem não leva boleto); bloqueia a GOL-01a) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** sem consentimento não há envio (CA 3); mensagem só de modelo aprovado e variáveis permitidas (CA 8); webhook com assinatura antes de ler (CA 9); provedor e base legal como pré-condições (pontos 1 e 2).
- **Correções da revalidação (21/10/2026):** pontos 17 a 21 decididos (API oficial por BSP com custo do tenant, CA 1, 2; opt-in só pelo próprio contato e queda na troca de número, CA 5, 6; modelos com prévia, CA 3; janela de envio, CA 11; FIN-09 antes); mensagem só com aviso e link do portal (D17, CA 10); tela Configurações → Integrações → WhatsApp com credenciais só de escrita, 2FA de novo e "Testar envio" (CA 1–4); webhook pela FND-16 com o GET de verificação (CA 14); falhas reenviáveis (CA 16); nada aos clientes do `konebras` antes da virada (CA 13); bloqueia a GOL-01a; ADRs 0031, 0034, 0041 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externas: contrato com o parceiro, número verificado e modelos aprovados (não bloqueiam).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D6, D17, tabela 3.5 pontos 17–21, §4.3 transversal, §4.4 padrões 5, 6 e 7 e achados de prévia e falhas, ADRs 0041 e 0042); nova ordem (Marco 8.4, ordem 5, antes do go-live; bloqueia a GOL-01a); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
