# LGPD-01b — Direitos do titular: anonimização e eliminação que preservam os documentos com movimento

| Campo | Valor |
|---|---|
| ID | LGPD-01b |
| Título | Anonimização de terceiro PF, contato, funcionário ou usuário a pedido do titular, pelo Proprietário, com 2FA de novo e confirmação digitada: troca de cada dado pessoal por um marcador em todos os módulos, preservando títulos, notas, movimentos, folha e auditoria; prévia do que muda e do que fica retido pela tabela de retenção pré-configurada e editável; uma transação por módulo, idempotente e retomável; eliminação atendida por anonimização |
| Tamanho | M (quebra da LGPD-01) |
| Status | Ready (pré-condição externa) |
| Requisitos | PRD §6 LGPD · ADR 0018 (anonimização que mantém os documentos com movimento, trocando o dado pessoal por um marcador; retenção por tipo de dado) · R-SEC-5 · R-PRP-7 |
| Dependências | LGPD-01a (pedido, identidade conferida, `IPersonalDataProvider`, inventário) |
| Bloqueia | — |
| Ordem de execução | 10 (Marco 6.3 "LGPD") |

## User story
Como **proprietário**, quero **anonimizar os dados de um titular que pediu, sem quebrar notas, títulos e folhas antigas**, para **cumprir o pedido sem perder o histórico que a lei me obriga a guardar**.

## Contexto e referências
- **ADR 0018**: retenção (terceiro PF e contato: enquanto ativo + prazo legal dos documentos, fiscal e contábil 5 anos após o exercício; funcionário desligado: 5 anos após o desligamento como padrão e mais para os documentos que a contadora indicar; campos sem obrigação legal podem ser anonimizados antes); "anonimização que mantém os documentos com movimento, trocando o dado pessoal por um marcador"; auditoria imutável sem dado pessoal em claro (o conflito é resolvido na origem); expurgo controlado da ADR 0016 só para dado que chegou à auditoria por engano.
- **ADR 0037** (`docs/adr/0037-execucao-lgpd.md`: provedores, inventário, expurgos, backups, uma transação por módulo); **ADR 0008** (adendo de 21/10/2026, D6: 2FA de novo para anonimizar titular); ADR 0042 (valores pré-configurados "a confirmar").
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D6, tabela 3.3 pontos 36 a 39, §4.2 (riscos de migração: a anonimização apaga dado de propósito, coberta pela ADR 0037), §4.4 padrão 7 (confirmação digitada quando apaga dado).
- `CLAUDE.md`: documentos com movimento (estoque, financeiro, fiscal) nunca são apagados.
- LGPD-01a (`IPersonalDataProvider`, pedido, inventário).

## Critérios de aceite
**Tabela de retenção**
1. **Dado** um membro do Proprietário, **quando** abre Configurações → Privacidade → Retenção (`/app/configuracoes/privacidade/retencao`), **então** vê a tabela de retenção por campo e tipo de titular, pré-configurada pela regra da ADR 0018 e editável: podem ser anonimizados a qualquer momento telefone, e-mail pessoal, localização do ponto e ciência, dependentes sem obrigação legal; dados bancários depois do último pagamento; endereço depois do prazo trabalhista; dados trabalhistas e previdenciários 5 anos depois do desligamento; nome e documento nas notas e títulos 5 anos depois do fim do exercício; cada linha tem o chip "A confirmar com a contadora" (e com o jurídico) e "Marcar como conferido" (quem e quando auditados); cada mudança é auditada com antes e depois.

**Prévia**
2. **Dado** um pedido de Anonimização ou Eliminação `Open` com a identidade conferida (LGPD-01a CA 5), **quando** o Proprietário clica "Prever anonimização", **então** cada módulo responde pelo contrato (`IPersonalDataProvider.PlanAnonymizationAsync(subject, today)`) com: campos que serão trocados por marcador, campos retidos (com o motivo e a data a partir da qual podem ser anonimizados, pela tabela do CA 1) e documentos que continuam existindo (ex.: "12 títulos a receber, 3 notas fiscais: mantidos, com o nome trocado"); o pedido de Eliminação mostra "A eliminação é atendida por anonimização: documentos com movimento não podem ser apagados."
3. **Dado** um funcionário ativo ou desligado há menos que o prazo de retenção, **quando** a prévia é montada, **então** só os campos sem obrigação legal entram, e o resto aparece como "Retido até {data}: obrigação trabalhista e previdenciária"; sem desligamento, nada trabalhista é anonimizado; terceiro PF com documento fiscal no prazo fica com nome e CPF retidos nas notas (o cadastro é anonimizado só no que não aparece nelas).

**Anonimização**
4. **Dado** a prévia, **quando** o Proprietário digita o nome do titular para confirmar (padrão 7), informa o código do 2FA de novo (TOTP recente, D6; sem ele, 403 `mfa_step_up_required`) e clica "Anonimizar", **então** a anonimização roda num job com `tenantId`, **uma transação por módulo** com o resultado por módulo gravado no pedido: cada campo da prévia é trocado pelo marcador `Titular anonimizado {número do pedido}` (texto) ou nulo (data, documento, telefone, e-mail, localização), o terceiro fica inativo e marcado "Anonimizado", o funcionário fica marcado "Anonimizado" (some dos seletores), o usuário do sistema fica bloqueado com o e-mail trocado, e o pedido registra data, quem fez e o resumo; a mensagem de sucesso diz "Dados anonimizados".
5. **Dado** um módulo que falha no meio, **quando** a anonimização roda, **então** o pedido mostra "Anonimização parcial: {módulos ok} / {módulo com erro}" e "Tentar de novo" (com 2FA de novo) refaz só os módulos que faltam; cada provedor é idempotente pelo número do pedido (rodar duas vezes dá o mesmo resultado, teste).
6. **[Segurança]** **Dado** a anonimização, **quando** termina, **então** nenhum valor original fica no banco fora da auditoria (teste de canário: grava valores conhecidos, anonimiza, varre as tabelas do tenant, o payload do outbox, os argumentos de job e o log), a auditoria continua íntegra (o diff da anonimização registra só "anonimizado", sem o valor anterior, porque o anterior já era mascarado) e documentos com movimento continuam com totais, datas e ids.
7. **[Segurança]** **Dado** os arquivos anexados do titular (documentos do funcionário, atestados, holerites, foto), **quando** a prévia os lista como não retidos, **então** a anonimização apaga o conteúdo do armazenamento (`IFileStorage`, prefixo do tenant) e mantém só a linha do anexo com o nome trocado pelo marcador; arquivo retido continua, com acesso só pela ação do dono (`hr.view_sensitive` ou `hr.view_health`).
8. **[Segurança]** **Dado** a anonimização, **quando** confirmada, **então** é irreversível: não existe "desfazer"; o `ConfirmDialog` diz isso no texto, lista o impacto por módulo e o botão usa o estilo `destructive`; só o Proprietário executa (403 para os demais).
9. **Dado** e-mails, notificações e cópias já gravadas do titular, **quando** anonimizo, **então** o destinatário em `shared.email_logs` e em `commercial.quote_dispatches` e o texto das notificações são trocados pelo marcador, o modelo (`model` jsonb) é limpo do que for dado do titular, e o `SubjectAnonymized` faz os módulos que guardam cópia (retrato do favorecido na OP, `payee_snapshot`) trocarem o dado pelo marcador; os registros de expurgo do inventário (LGPD-01a CA 6) não precisam de ação.
10. **Dado** um backup anterior à anonimização, **quando** existir, **então** a ajuda e o pedido registram "Os backups expiram em até 30 dias (FND-10b) e não são restaurados com dado anonimizado sem refazer a anonimização"; o runbook de restauração ganha o passo "reaplicar os pedidos de anonimização concluídos depois da data do backup".

**Permissões e auditoria**
11. **Dado** qualquer chamada desta story, **quando** o usuário não é membro do Proprietário, **então** 403; a prévia, a anonimização, as retentativas e as mudanças da tabela de retenção ficam no histórico com quem, quando e o resumo por módulo.

## Regras de negócio e técnicas
- **Contrato:** `IPersonalDataProvider` ganha `PlanAnonymizationAsync(subject, today)` e `AnonymizeAsync(subject, requestNumber)`; cada módulo decide o que é retido pela tabela de retenção (CA 1), lida pelo contrato de Configurações.
- **Uma transação por módulo** (ADR 0037), etapas idempotentes e retomáveis; o orquestrador grava o resultado de cada etapa no pedido.
- **Marcador** único por pedido; campos únicos (CPF, documento) ficam nulos para não violar índice.
- **Transição pela entidade rastreada** (ADR 0019 §2); a auditoria grava a ação `Anonymized` sem valores.
- **Retenção por funcionário:** a data base é o desligamento; sem desligamento, nada trabalhista é anonimizado.
- **Runbook:** `docs/operacao/restauracao.md` ganha o passo do CA 10.

## Permissões, eventos e tabelas
- **Permissões:** membro do Proprietário, com 2FA de novo para anonimizar.
- **Eventos:** `SubjectAnonymized` (`{ subjectType, subjectId, requestNumber }`), sem dado pessoal, para os módulos que guardam cópia reagirem.
- **Tabelas:** `settings.retention_rules` (`subject_type`, `field_group`, `rule`, `years`, `base_date`, `confirmed_at`, `confirmed_by`, `seed_key`); `settings.privacy_requests` (+ `anonymization_summary` jsonb sem dado pessoal, `anonymized_at`, `anonymization_steps`); `parties.parties`, `hr.employees` e `identity.users` (+ `anonymized_at`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (confirmação digitada quando apaga dado, impacto em lista, estilo `destructive`, no `ConfirmDialog` criado na BNK-01b, reutilizado).
- No pedido (LGPD-01a): "Prever anonimização" (prévia por módulo, em três blocos: "Será anonimizado", "Fica retido até…", "Documentos mantidos"), "Anonimizar" com confirmação digitada e 2FA.
- **Configurações → Privacidade → Retenção:** tabela por tipo de titular, com os chips e "Marcar como conferido".
- No terceiro, no funcionário e no usuário: faixa "Cadastro anonimizado em {data} (pedido {número})", sem ações de edição.
- **Estados:** prevendo; anonimizando (assíncrono, com o estado de demora); parcial (CA 5); concluído; erro; primeira abertura da retenção com os valores sugeridos.
- **400 px:** prévia em seções empilhadas; diálogo em tela cheia; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** atualizar `Ajuda/configuracoes-privacidade-pedido.md` ("Anonimização", "O que fica retido" e "Eliminação"); `Ajuda/configuracoes-privacidade-retencao.md` (nova); `<Dica>` em "Retido" e "Marcador".

## Plano de testes
- **Unidade:** regras de retenção por tipo de dado e data (CA 1, 3).
- **Integração (Testcontainers):** tabela de retenção com o chip e auditoria (CA 1); prévia por módulo e identidade conferida (CA 2, 3); anonimização com 2FA, confirmação digitada, completa e parcial com retentativa e idempotência (CA 4, 5); canário em todas as tabelas, outbox, jobs e log (CA 6); arquivos apagados e retidos (CA 7); só o Proprietário e sem desfazer (CA 8, 11); e-mails, notificações e `SubjectAnonymized` consumido pelo Finance (CA 9).
- **E2E (Playwright):** anonimizar um terceiro PF de teste com títulos pagos e conferir que os títulos continuam com o marcador.

## Fora do escopo
- Expurgo de dado pessoal que chegou à auditoria por engano: migration controlada da ADR 0016.
- Anonimização automática ao fim do prazo de retenção, sem pedido: depois do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, D6 e tabela 3.3, pontos 36–39):
1. **Tabela de retenção por campo:** pré-configurada pela regra da ADR 0018 e editável em Configurações → Privacidade → Retenção, "A confirmar com a contadora" e com o jurídico (CA 1).
2. **Eliminação atendida por anonimização** (CA 2).
3. **Anonimização automática** fica para depois do MVP.
4. **Uma transação por módulo,** etapas idempotentes e retomáveis (CA 4, 5; ADR 0037).
5. **2FA de novo** para anonimizar (D6) e confirmação digitada (padrão 7) (CA 4, 8).

Decisões da revisão aplicadas: §4.2 (anonimização coberta pela ADR 0037; inventário da LGPD-01a, CA 9); §4.4 padrão 7.

**Pré-condição externa:** a contadora e o jurídico conferem a tabela de retenção (pré-configurada em Configurações → Privacidade → Retenção com o chip "A confirmar com a contadora"); não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, canário em todas as tabelas) e E2E passando.
- [ ] Migration criada (não destrutiva: só colunas e tabela novas).
- [ ] Provedores de todos os módulos com dado pessoal.
- [ ] Telas com estados; ajuda escrita e atualizada; runbook atualizado.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `RetentionRule` e seed; tela de retenção.
- [ ] `PlanAnonymizationAsync` e `AnonymizeAsync` em cada provedor; orquestrador por módulo, retomável.
- [ ] Consumidor do `SubjectAnonymized` no Finance; tela, ajuda e runbook.

## Quality gates
- Agentes previstos: @dev, @architect (orquestração por módulo), cybersecurity (canário, irreversibilidade, 2FA), @qa.
- Gate: CA 3, 4, 5, 6, 7 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: 2FA de novo, CA 4) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** retenção pela tabela da ADR 0018, com a lista de campos como pré-condição da contadora; documentos com movimento mantidos; backups no runbook.
- **Correções da revalidação (21/10/2026):** pontos 36 a 39 decididos: tabela de retenção pré-configurada e editável (CA 1, novo), eliminação por anonimização (CA 2), uma transação por módulo idempotente e retomável (CA 4, 5); D6: 2FA de novo para anonimizar e para tentar de novo (CA 4, 5); confirmação digitada com impacto em lista (padrão 7, CA 8); usuário do sistema como titular; notificações, outbox e jobs no canário (CA 6, 9); a dependência ficou só a LGPD-01a (que já traz o RH); ordem 10 do Marco 6.3; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência da tabela de retenção pela contadora e pelo jurídico.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da LGPD-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D6, tabela 3.3 pontos 36–39, ADR 0037, §4.2, §4.4 padrão 7); nova ordem (Marco 6.3, ordem 10); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
