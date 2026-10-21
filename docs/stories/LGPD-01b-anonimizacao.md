# LGPD-01b — Direitos do titular: anonimização e eliminação que preservam os documentos com movimento

| Campo | Valor |
|---|---|
| ID | LGPD-01b |
| Título | Anonimização de terceiro PF, contato ou funcionário a pedido do titular, pelo Proprietário: troca de cada dado pessoal por um marcador em todos os módulos, preservando títulos, notas, movimentos, folha e auditoria; prévia do que muda e do que fica retido por obrigação legal; irreversível, com confirmação forte e registro no pedido |
| Tamanho | M (quebra da LGPD-01) |
| Status | Draft |
| Requisitos | PRD §6 LGPD · ADR 0018 (anonimização que mantém os documentos com movimento, trocando o dado pessoal por um marcador; retenção por tipo de dado) · R-SEC-5 · R-PRP-7 |
| Dependências | LGPD-01a (pedido, `IPersonalDataProvider`), CAD-01a, CAD-04b/04c, RH-01a a RH-02c |
| Bloqueia | — |
| Ordem de execução | 9 (Marco 6.3 "LGPD") |

## User story
Como **proprietário**, quero **anonimizar os dados de um titular que pediu, sem quebrar notas, títulos e folhas antigas**, para **cumprir o pedido sem perder o histórico que a lei me obriga a guardar**.

## Contexto e referências
- **ADR 0018**: retenção (terceiro PF e contato: enquanto ativo + prazo legal dos documentos, fiscal e contábil 5 anos após o exercício; funcionário desligado: 5 anos após o desligamento como padrão e mais para os documentos que a contadora indicar; campos sem obrigação legal podem ser anonimizados antes); "anonimização que mantém os documentos com movimento, trocando o dado pessoal por um marcador"; auditoria imutável sem dado pessoal em claro (o conflito é resolvido na origem); expurgo controlado da ADR 0016 só para dado que chegou à auditoria por engano.
- `CLAUDE.md`: documentos com movimento (estoque, financeiro, fiscal) nunca são apagados.
- LGPD-01a (`IPersonalDataProvider`, pedido).

## Critérios de aceite
**Prévia**
1. **Dado** um pedido de Anonimização ou Eliminação `Open` com titular identificado, **quando** o Proprietário clica "Prever anonimização", **então** cada módulo responde pelo contrato (`IPersonalDataProvider.PlanAnonymizationAsync`, proposta) com: campos que serão trocados por marcador, campos retidos por obrigação legal (com o motivo e a data a partir da qual podem ser anonimizados, pela tabela de retenção da ADR 0018 e do ponto em aberto 1) e documentos que continuam existindo (ex.: "12 títulos a receber, 3 notas fiscais: mantidos, com o nome trocado").
2. **Dado** um funcionário ativo ou desligado há menos que o prazo de retenção, **quando** a prévia é montada, **então** só os campos sem obrigação legal entram (proposta da lista no ponto em aberto 1: telefone, e-mail pessoal, endereço depois do prazo da contadora, localização do ponto, dados bancários depois do último pagamento), e o resto aparece como "Retido até {data}: obrigação trabalhista e previdenciária"; terceiro PF com documento fiscal no prazo fica com nome e CPF retidos nas notas (o cadastro é anonimizado só no que não aparece nelas).

**Anonimização**
3. **Dado** a prévia, **quando** o Proprietário digita o nome do titular para confirmar e clica "Anonimizar", **então** numa transação por módulo (proposta, com o resultado por módulo gravado no pedido) cada campo da prévia é trocado pelo marcador `Titular anonimizado {número do pedido}` (texto) ou nulo (data, documento, telefone, e-mail, localização), o terceiro fica inativo e marcado "Anonimizado", o funcionário fica marcado "Anonimizado" (some dos seletores), e o pedido registra data, quem fez e o resumo; a mensagem de sucesso diz "Dados anonimizados".
4. **Dado** um módulo que falha no meio, **quando** a anonimização roda, **então** o pedido mostra "Anonimização parcial: {módulos ok} / {módulo com erro}" e "Tentar de novo" refaz só os módulos que faltam (cada provedor é idempotente pelo número do pedido).
5. **[Segurança]** **Dado** a anonimização, **quando** termina, **então** nenhum valor original fica no banco fora da auditoria (teste de canário: grava valores conhecidos, anonimiza, varre as tabelas do tenant e o log), a auditoria continua íntegra (o diff da anonimização registra só "anonimizado", sem o valor anterior, porque o anterior já era mascarado) e documentos com movimento continuam com totais, datas e ids.
6. **[Segurança]** **Dado** os arquivos anexados do titular (documentos do funcionário, atestados, foto), **quando** a prévia os lista como não retidos, **então** a anonimização apaga o conteúdo do armazenamento (`IFileStorage`) e mantém só a linha do anexo com o nome trocado pelo marcador; arquivo retido continua, com acesso só por `hr.view_sensitive`.
7. **[Segurança]** **Dado** a anonimização, **quando** confirmada, **então** é irreversível: não existe "desfazer"; o diálogo diz isso no texto e o botão usa o estilo `destructive`; só o Proprietário executa (403 para os demais).
8. **Dado** e-mails já enviados ao titular, **quando** anonimizo, **então** o destinatário em `shared.email_logs` e em `commercial.quote_dispatches` é trocado pelo marcador; o corpo do e-mail não é guardado em claro (FND-08), e o modelo (`model` jsonb) é limpo do que for dado do titular.
9. **Dado** um backup anterior à anonimização, **quando** existir, **então** a ajuda e o pedido registram "Os backups expiram em até 30 dias (FND-10b) e não são restaurados com dado anonimizado sem refazer a anonimização"; o runbook de restauração ganha o passo "reaplicar os pedidos de anonimização concluídos depois da data do backup" (proposta).

**Permissões e auditoria**
10. **Dado** qualquer chamada desta story, **quando** o usuário não é membro do Proprietário, **então** 403; a prévia e a anonimização ficam no histórico do pedido com quem, quando e o resumo por módulo.

## Regras de negócio e técnicas
- **Contrato:** `IPersonalDataProvider` ganha `PlanAnonymizationAsync(subject, today)` e `AnonymizeAsync(subject, requestNumber)`; cada módulo decide o que é retido pela regra de retenção dele, a partir dos parâmetros da tabela da ADR 0018.
- **Marcador** único por pedido; campos únicos (CPF, documento) ficam nulos para não violar índice.
- **Transição pela entidade rastreada** (ADR 0019 §2); a auditoria grava a ação `Anonymized` sem valores.
- **Retenção por funcionário:** a data base é o desligamento; sem desligamento, nada trabalhista é anonimizado.
- **Runbook:** `docs/operacao/restauracao.md` ganha o passo do CA 9.

## Permissões, eventos e tabelas
- **Permissões:** membro do Proprietário.
- **Eventos:** `SubjectAnonymized` (`{ subjectType, subjectId, requestNumber }`), sem dado pessoal, para os módulos que guardam cópia (ex.: retrato do favorecido na OP) reagirem.
- **Tabelas:** `settings.privacy_requests` (+ `anonymization_summary` jsonb sem dado pessoal, `anonymized_at`); `parties.parties` e `hr.employees` (+ `anonymized_at`).

## Telas e estados
- No pedido (LGPD-01a): "Prever anonimização" (prévia por módulo, em três blocos: "Será anonimizado", "Fica retido até…", "Documentos mantidos"), "Anonimizar" com confirmação digitada.
- No terceiro e no funcionário: faixa "Cadastro anonimizado em {data} (pedido {número})", sem ações de edição.
- **Estados:** prevendo; anonimizando; parcial (CA 4); concluído.
- **400 px:** prévia em seções empilhadas.
- **Ajuda:** atualizar `Ajuda/configuracoes-privacidade-pedido.md` ("Anonimização" e "O que fica retido"); `<Dica>` em "Retido".

## Plano de testes
- **Unidade:** regras de retenção por tipo de dado e data (CA 2).
- **Integração (Testcontainers):** prévia por módulo (CA 1, 2); anonimização completa e parcial com retentativa (CA 3, 4); canário em todas as tabelas e no log (CA 5); arquivos apagados e retidos (CA 6); só o Proprietário e sem desfazer (CA 7, 10); e-mails (CA 8); evento consumido pelo Finance (retrato da OP).
- **E2E (Playwright):** anonimizar um terceiro PF de teste com títulos pagos e conferir que os títulos continuam com o marcador.

## Fora do escopo
- Expurgo de dado pessoal que chegou à auditoria por engano: migration controlada da ADR 0016.
- Anonimização automática ao fim do prazo de retenção, sem pedido: depois do MVP (Pontos em aberto 3).

## Pontos em aberto
1. **Tabela de retenção por campo** (pré-condição, contadora + jurídico): quais campos do funcionário e do terceiro podem ser anonimizados antes do fim do prazo e quais documentos a contadora manda guardar por mais de 5 anos (ADR 0018 deixa a lista com a contadora).
2. **Eliminação × anonimização** (jurídico): o pedido de eliminação é atendido pela anonimização (proposta), já que a exclusão física quebraria documentos com movimento.
3. Anonimização automática quando a retenção vence, com aviso ao Proprietário.
4. Transação única entre módulos ou uma por módulo com retentativa (arquiteto; proposta: uma por módulo).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, canário em todas as tabelas) e E2E passando.
- [ ] Migration criada (não destrutiva: só colunas novas).
- [ ] Provedores de todos os módulos com dado pessoal.
- [ ] Telas com estados; ajuda atualizada; runbook atualizado.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PlanAnonymizationAsync` e `AnonymizeAsync` em cada provedor; orquestrador no pedido.
- [ ] Consumidor do `SubjectAnonymized` no Finance; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect, cybersecurity (canário, irreversibilidade), @qa.
- Gate: CA 2, 5, 6 e 7 obrigatórios.

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

- **Correções feitas na validação:** retenção pela tabela da ADR 0018, com a lista de campos como pré-condição da contadora (ponto 1), sem decidir; documentos com movimento mantidos (CA 1, 5); backups no runbook (CA 9).
- **Pré-condições para `Ready`:** ponto em aberto 1; LGPD-01a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da LGPD-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
