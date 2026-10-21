# WPP-01 — Notificações por WhatsApp (cobrança e status do projeto)

| Campo | Valor |
|---|---|
| ID | WPP-01 |
| Título | Envio de mensagens por WhatsApp ao contato do cliente, por um provedor atrás de `IWhatsAppSender`, com credenciais por tenant cifradas: lembrete de vencimento e de atraso (régua de cobrança) e mudança de etapa do projeto, só com modelos aprovados, consentimento (opt-in) por contato, descadastro, fila com tentativas, webhook de status com assinatura e histórico por envio |
| Tamanho | G |
| Status | Draft |
| Requisitos | Backlog WPP-01 ("Notificações por WhatsApp (cobrança, status do projeto)") · R-FIN-9 (régua de cobrança) · PRD §3.2 (WhatsApp pós-MVP) · PRD §6 (LGPD, Observabilidade) · R-SEC-5 |
| Dependências | FIN-09 (régua de cobrança, Fase 5, D), BNK-01 (boleto), POR-01b/01c (links do portal), CAD-01a (contatos), FND-11 (segredos), FND-12 (alertas de integração), FND-08 (padrão de fila e templates) |
| Bloqueia | — |
| Ordem de execução | 5 (Marco 8.4 "Comunicação") |

## User story
Como **financeiro**, quero **que o cliente receba no WhatsApp o lembrete do boleto e o aviso de atraso**, e como **cliente**, quero **saber pelo WhatsApp quando o meu projeto muda de etapa**, para **receber mais rápido e o cliente ficar informado sem ligar**.

## Contexto e referências
- Backlog Fase 8 (WPP-01); PRD §3.2 (WhatsApp no pós-MVP); R-FIN-9 (régua de cobrança por e-mail, FIN-09, D).
- ADR 0007 (segredos por tenant e tipo, write-only, nunca em log; um novo tipo `whatsapp-credentials`), ADR 0009 (webhooks como rota anônima com assinatura validada), FND-08 (fila, tentativas, log de envio), FND-12 (alerta de integração).
- LGPD: o telefone do contato é dado pessoal; o envio de mensagem ativa pede base legal e opt-in conforme as regras do provedor (Pontos em aberto 2).

## Critérios de aceite
**Configuração**
1. **Dado** `settings.edit`, **quando** abro Configurações → WhatsApp, escolho o provedor (lista do ponto em aberto 1), informo as credenciais e o número remetente e clico "Testar conexão", **então** as credenciais são gravadas cifradas (`SecretKind.WhatsAppCredentials`, write-only, "configurado em dd/mm/aaaa") e o teste mostra o resultado traduzido (credencial recusada, número não verificado, rede); sem configuração, nenhuma mensagem é enviada e as telas mostram "WhatsApp não configurado".
2. **Dado** os modelos de mensagem, **quando** abro a lista, **então** vejo os modelos do sistema (proposta: "Boleto vence em {n} dias", "Boleto vencido", "Etapa do projeto mudou") com o texto e as variáveis (só nome do cliente, número do título ou do projeto, valor, vencimento, etapa e o link do portal ou do boleto), e a situação de aprovação no provedor; modelo não aprovado não é enviado.

**Consentimento**
3. **[Segurança]** **Dado** um contato do cliente, **quando** o usuário com `parties.edit` marca "Aceita receber WhatsApp" e informa a origem do consentimento (verbal, e-mail, portal) e a data, **então** o consentimento é gravado com quem registrou; sem ele, nenhuma mensagem sai para o contato; no portal (POR-01a), o próprio contato pode ligar e desligar (proposta).
4. **Dado** uma resposta "SAIR" ou o descadastro pelo provedor, **quando** o webhook chega, **então** o consentimento é revogado na hora e registrado.

**Envio**
5. **Dado** a régua de cobrança (FIN-09) com o canal WhatsApp ligado no parâmetro, **quando** o job da régua roda, **então** enfileira, para cada título elegível, uma mensagem por contato com "Recebe cobrança" e consentimento, pelo modelo aprovado, com o link do boleto ou do portal; idempotente por título, etapa da régua e dia (`dedup_key`), teste de execução dupla e de dois tenants.
6. **Dado** a mudança de etapa do projeto (eventos `ProjectFabricated`, `ProjectDelivered` etc.), **quando** o parâmetro "Avisar etapa por WhatsApp" está ligado, **então** os contatos do cliente com acesso ao portal e consentimento recebem a mensagem da etapa com o link do projeto no portal.
7. **Dado** a fila (`whatsapp`, poucos workers), **quando** o provedor falha, **então** tenta 3 vezes com backoff, depois marca `Failed` com o motivo e conta para o alerta de integração (`whatsapp:{provedor}`, FND-12); argumentos do job só com ids.
8. **[Segurança]** **Dado** a mensagem, **quando** montada, **então** não leva CPF, CNPJ completo, dado de outro cliente, observação interna nem custo; o texto é gerado só do modelo aprovado com as variáveis permitidas (nada de texto livre do usuário nem do cliente no modelo).

**Webhook e histórico**
9. **[Segurança]** **Dado** o webhook de status (`/webhooks/whatsapp/{tenantKey}`), **quando** chega, **então** a assinatura do provedor é validada antes de qualquer leitura (sem assinatura válida, 401 sem detalhe), é idempotente pelo id da mensagem do provedor e atualiza o envio (Enviado, Entregue, Lido, Falhou); conteúdo de mensagem recebida do cliente não é guardado, só o descadastro (proposta).
10. **Dado** um título ou projeto, **quando** abro o histórico de comunicação, **então** vejo os envios por WhatsApp (data, contato, modelo, situação) junto com os e-mails; o telefone aparece mascarado para quem não tem `parties.edit`.
11. **Dado** o módulo dono não liberado (`finance` para cobrança, `projects` para etapa) ou o WhatsApp não configurado, **quando** o job roda, **então** não envia; os endpoints de configuração exigem `settings.edit` e todos têm `RequireModule` e `perm:`.
12. **Dado** credenciais, consentimentos e envios, **quando** consulto a auditoria, **então** vejo quem e quando; as credenciais só como "alterado" (`[ProtectedSecret]`) e o telefone mascarado (`[PersonalData]`); canário: credencial e telefone fora do log.

## Regras de negócio e técnicas
- **`IWhatsAppSender`** (BuildingBlocks, como o `IEmailSender`), com um adaptador por provedor só com `HttpClient`, chave em cabeçalho, log do `HttpClient` desligado (como os provedores de IA, ADR 0014).
- **Tabelas (propostas):** `shared.whatsapp_settings` (provedor, número, `credentials_cipher`, `set_at`), `shared.whatsapp_templates` (código, texto, variáveis, situação no provedor), `shared.whatsapp_logs` (`to` `[PersonalData]`, modelo, `related_type`, `related_id`, `status`, `provider_message_id`, tentativas, erro), `parties.party_contacts` (+ `whatsapp_opt_in`, `whatsapp_opt_in_source`, `whatsapp_opt_in_at`, `whatsapp_opt_in_by`, `whatsapp_opt_out_at`).
- Telefone normalizado em E.164.

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (configuração), `parties.edit` (consentimento); os jobs pelos módulos donos.
- **Eventos:** consome `ProjectFabricated`, `ProjectDelivered`, `TechnicalAcceptance` e a régua da FIN-09.
- **Tabelas:** acima; segredo `whatsapp-credentials` (ADR 0007).

## Telas e estados
- **Configurações → WhatsApp** (`/app/configuracoes/whatsapp`), consentimento no contato do terceiro, histórico de comunicação no título e no projeto.
- **Estados:** não configurado; modelo não aprovado; falha do provedor com o motivo; vazio do histórico.
- **400 px:** configuração em seções empilhadas.
- **Ajuda:** `Ajuda/configuracoes-whatsapp.md`; atualizar `Ajuda/parties-terceiro.md` ("WhatsApp do contato"); `<Dica>` em "Consentimento" e "Modelo aprovado".

## Plano de testes
- **Unidade:** montagem da mensagem só com variáveis permitidas (CA 8); normalização do telefone.
- **Integração (Testcontainers, provedor falso):** configuração write-only e teste (CA 1); modelos (CA 2); consentimento e descadastro (CA 3, 4); régua idempotente com dois tenants (CA 5); etapa (CA 6); retentativas e alerta (CA 7); webhook com assinatura e idempotente (CA 9); histórico (CA 10); módulos e permissões (CA 11); canário (CA 12).
- **Manual:** envio real no ambiente de teste do provedor escolhido.

## Fora do escopo
- Atendimento ou conversa pelo WhatsApp (caixa de entrada, chatbot).
- Mensagens a funcionários (holerite, ponto).

## Pontos em aberto
1. **Provedor** (pré-condição, PO): API oficial do WhatsApp Business (Meta) ou um parceiro; custo por conversa e quem paga (tenant ou plataforma).
2. **Base legal e opt-in** (pré-condição, jurídico): consentimento registrado pelo backoffice basta, ou só pelo próprio contato (portal ou resposta ao primeiro contato).
3. **Textos dos modelos** (PO): aprovação no provedor antes do uso.
4. Horário permitido para envio (proposta: dias úteis, das 8 h às 18 h, America/Sao_Paulo).
5. Dependência da FIN-09 (régua por e-mail, D na Fase 5): sem ela, só a mensagem de etapa do projeto.

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade, integração (provedor falso) e o teste manual passando.
- [ ] Migration criada (não destrutiva); tipo de segredo novo com o teste golden da ADR 0007.
- [ ] Permissões só do `ModuleCatalog`; webhook na lista de rotas anônimas com assinatura.
- [ ] Telas com estados; ajuda; teste de cobertura passando.
- [ ] Alertas de integração ligados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/02-arquitetura.md` (integração) e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IWhatsAppSender` e o adaptador do provedor; job de envio; webhook; consentimento no contato; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (provedor e webhook), cybersecurity (segredo, assinatura, LGPD), @qa.
- Gate: CA 3, 5, 8 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** sem consentimento não há envio (CA 3); mensagem só de modelo aprovado e variáveis permitidas (CA 8); webhook com assinatura antes de ler (CA 9); provedor e base legal como pré-condições (pontos 1 e 2).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
