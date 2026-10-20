# FND-09e — Componentes e convenções compartilhados da Fase 2

| Campo | Valor |
|---|---|
| ID | FND-09e |
| Título | Mensagens no topo do conteúdo (ADR 0021), `ConfirmDialog` grande com formulário e erro por campo, `applyServerErrors` com caminhos aninhados, `RowsEditor` com modelo de colunas, `DataTable` com seleção, totais e linha expansível, tokens de gráfico com Recharts, menu do Financeiro em dois grupos e as convenções de concorrência, idempotência, deduplicação de notificações e fronteira de contratos (ADR 0019) |
| Tamanho | M (nova, revisão pré-Fase 2 §3) |
| Status | Ready |
| Requisitos | PRD §6 (Usabilidade, Acessibilidade) · R-PRP-2 · R-PRP-7 · R-COM-2 · R-SEC-5 |
| Dependências | FND-09c (DataTable, estados, formulário), FND-09d (`ConfirmDialog`, `RowsEditor`, `NextSteps`), FND-09b (menu), FND-07 (notificações, auditoria); ADR 0019 e ADR 0021 aceitas |
| Bloqueia | Todas as stories da Fase 2 com tela ou documento (a começar pela FIN-01) |
| Ordem de execução | 0 (Fase 0, antes da FIN-01) |

## User story
Como **desenvolvedor das telas e dos documentos da Fase 2**, quero **os componentes e as convenções que todas elas usam prontos e testados**, para **nenhuma tela improvisar a sua versão de mensagem, diálogo financeiro, editor de linhas, tabela com totais, gráfico, concorrência ou idempotência**.

## Contexto e referências
- Revisão pré-Fase 2 (`docs/revisoes/2026-10-20-revisao-pre-fase2.md`) §3 "Pré-requisitos compartilhados", §2.1 (C2, A2, M8, M10, M11) e §2.3 (padrões de design 1 a 13).
- ADR 0021 (mensagens e próximos passos: resolve a P-18); ADR 0019 (concorrência com `xmin`, `Idempotency-Key`, `dedup_key`, Contracts × Contracts, posse de tabela).
- Código atual: `src/Web/src/components/ui/toast.ts` (`toastDone` com `nextStep`, sonner no rodapé), `confirm-dialog.tsx` (com `nextStep` e `recovery`), `form/form-utils.ts` (`applyServerErrors` só com caminho plano), `form/rows-editor.tsx`, `data-table/DataTable.tsx`; `BuildingBlocks/Notifications/Notification.cs` (sem `dedup_key`).
- **Fora desta story:** o filtro `RequireTwoFactor` e o 2FA por ação, adiados pelo PO (P-19). O 2FA continua exigido no login de quem aprova ou paga (FND-06c CA 12).
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
**Mensagens (ADR 0021)**
1. **Dado** uma ação concluída com sucesso, **quando** a tela chama `notifySuccess("Terceiro salvo")`, **então** a mensagem aparece numa região `role="status"` no topo da área de conteúdo (abaixo da barra superior, nunca atrás do dock), sem roubar o foco, visível por no mínimo 5 s e enquanto o ponteiro ou o foco estiverem sobre ela, com o botão "Fechar"; até 3 mensagens se empilham, a mais nova em cima; um teste E2E a 400 px confere que a mensagem fica visível acima do dock.
2. **Dado** uma ação que falha fora de diálogo (ação direta numa lista, falha de rede), **quando** a tela chama `notifyError(...)`, **então** a mensagem aparece na mesma região como `role="alert"`, não some sozinha e oferece "Tentar de novo" quando a ação pode ser repetida.
3. **Dado** o código do front, **quando** o lint e o build rodam, **então** `toastDone`, `toastError` e o parâmetro `nextStep` do `ConfirmDialog` não existem mais; as telas das Fases 0 e 1 usam `notifySuccess`/`notifyError` com os mesmos textos e o próximo passo que estava no toast vai para o `NextSteps` da tela (troca mecânica, sem mudar texto).

**Diálogo de ação com efeito (padrão 1 e 2 da Fase 2)**
4. **Dado** o `ConfirmDialog` com `size="lg"`, **quando** abre, **então** tem cerca de 640 px de largura e ocupa a tela cheia a 400 px, aceita um formulário (React Hook Form) com erros por campo, um bloco de impacto (valores, conta, títulos afetados, saldo antes e depois) e o botão com o mesmo verbo do gatilho; o estilo `destructive` é usado só quando o verbo é "Estornar…" ou "Cancelar…".
5. **Dado** um diálogo com pré-condições (`preconditions: [{ ok, text, fixLink }]`), **quando** alguma falha, **então** o diálogo lista o que falta com o link para corrigir, o botão fica desabilitado e o motivo aparece no `aria-describedby` do botão; o 422 do servidor continua como defesa.
6. **Dado** o servidor recusando a ação com 422, **quando** o erro tem caminhos de campo, **então** cada erro aparece no seu campo dentro do diálogo e o foco vai para o primeiro; com erro de regra (409/422 sem campo), o bloco `role="alert"` aparece dentro do diálogo com o foco nele e a ação de `recovery` quando existe; com 409 `concurrency_conflict`, o bloco oferece "Recarregar".
7. **Dado** um usuário cuja sessão está na regra existente da FND-06c CA 12 (tem `*.approve` ou `*.pay` e ainda não configurou o 2FA), **quando** abre um diálogo de aprovar ou pagar, **então** o diálogo mostra o estado único "Para aprovar, ative a verificação em duas etapas." com "Ativar agora", que abre a configuração do 2FA e volta ao diálogo com o que já foi preenchido; nenhum código TOTP é pedido por ação (P-19).

**Formulário, editor de linhas e tabela**
8. **Dado** um ProblemDetails com `errors` em caminhos aninhados (`lines[2].unitCost`, `milestones[0].percent`), **quando** `applyServerErrors` é chamado, **então** cada erro vai para o campo da linha certa (editor de linhas e diálogos inclusive) e o contador de erros por aba conta os erros das linhas.
9. **Dado** o `RowsEditor` com um modelo de colunas (`columns: [{ key, label, input, width, hideBelow }]`), **quando** uso, **então** cada linha tem chave estável gerada no front (nunca o índice), erro por linha, "Remover {linha n}" com o foco indo para a linha seguinte (ou para "Adicionar" quando era a última), e a 400 px cada linha vira um cartão empilhado com os mesmos campos.
10. **Dado** o `DataTable` com `selectable`, `totals` e `expandable`, **quando** uso, **então** tem seleção por checkbox com "Selecionar todos desta página" e o total selecionado anunciado, rodapé de totais vindo do servidor (totais do filtro, não da página) e linha expansível por teclado (`aria-expanded`); os três recursos são opcionais e a tabela atual continua igual sem eles.

**Gráficos**
11. **Dado** o componente `Chart` (Recharts), **quando** desenha uma série, **então** usa só os tokens `--chart-1` a `--chart-6`, com valores próprios no tema claro e no escuro (lint de cor literal continua valendo), distingue realizado (traço contínuo) de projetado (tracejado) e traz a tabela equivalente acessível por teclado, ligada ao gráfico por `aria-describedby`.

**Navegação (D12)**
12. **Dado** a área Financeiro do dock, **quando** abro o menu, **então** ele tem dois grupos com título: "Caixa e pagamentos" (A receber, A pagar, Ordens de pagamento, Contas e extrato, Fluxo de caixa, Inadimplência) e "Configuração financeira" (Plano de contas, Centros de custo, Condições de pagamento, Recorrentes, Alçadas, Implantação financeira); cada item só aparece quando a story dele existe (entrada no `modules.ts`) e respeita as permissões; os itens que já existem (Plano de contas, Centros de custo, Condições, Contas) vão para o grupo certo.

**Convenções de servidor (ADR 0019)**
13. **[Segurança]** **Dado** uma entidade que implementa `IConcurrencyVersioned`, **quando** duas requisições salvam a mesma versão, **então** a segunda recebe 409 ProblemDetails `code: "concurrency_conflict"` com a mensagem "{Documento} mudou desde que você abriu. Recarregue." (`DbUpdateConcurrencyException` convertida no middleware), o DTO de detalhe expõe `version` (o `xmin`) e o comando o recebe; teste com uma entidade de teste.
14. **Dado** um tipo `IAuditable` de documento, **quando** um handler muda o estado dele, **então** um teste de arquitetura reprova `ExecuteUpdate`/`ExecuteSqlRaw` sobre esse tipo fora de uma allowlist que exige `IAuditWriter` explícito na mesma transação.
15. **[Segurança]** **Dado** um endpoint marcado com `.RequireIdempotencyKey()`, **quando** chega sem o cabeçalho `Idempotency-Key`, **então** responde 422 "Envie o cabeçalho Idempotency-Key."; com a mesma chave e o mesmo corpo, devolve o resultado original sem gravar de novo; com a mesma chave e outro corpo, 422 `idempotency_key_reused`; o hook `useIdempotencyKey()` do front gera uma chave por abertura de formulário ou diálogo e o mutator a envia (teste com um comando de teste e dois envios paralelos).
16. **Dado** `INotificationService.NotifyAsync(userId, …, dedupKey)`, **quando** a mesma chave é enviada duas vezes para o mesmo usuário (inclusive em paralelo), **então** só uma notificação é gravada (`shared.notifications.dedup_key`, índice único parcial `(user_id, dedup_key)`).
17. **Dado** os projetos `*.Contracts`, **quando** os testes de arquitetura rodam, **então** um `*.Contracts` que referencia outro `*.Contracts` reprova; o teste de posse de tabela da ADR 0016 (cada assembly mapeia só tabelas do próprio schema no `TenantDbContext.Model`, e o SQL cru dos módulos não cita schema alheio) está implementado e passando.

## Regras de negócio e técnicas
- **Mensagens:** região única no shell (`MessageRegion`), alimentada por `notifySuccess`/`notifyError`; o `Toaster` do sonner sai do rodapé. Texto de sucesso = particípio do verbo.
- **Concorrência:** `xmin` mapeado como `IsRowVersion()` pelo Npgsql em toda entidade `IConcurrencyVersioned`; `version` como `uint` no DTO.
- **Idempotência:** coluna `client_request_id uuid` na tabela do documento, índice único parcial; o filtro guarda o hash do corpo para recusar a chave reaproveitada; a resposta original é reconstruída a partir do documento.
- **Notificações:** `dedup_key text null`, índice único parcial `(user_id, dedup_key) WHERE dedup_key IS NOT NULL`; `ON CONFLICT DO NOTHING`.
- **Tokens de gráfico:** `--chart-1` a `--chart-6` em `tokens.css` (claro e escuro), contraste mínimo 3:1 com o fundo (WCAG 1.4.11), conferido no teste de tokens da FND-09a.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Eventos:** nenhum.
- **Tabelas:** `shared.notifications` (+ `dedup_key`). A coluna `client_request_id` nasce em cada tabela de documento na story que a cria.

## Telas e estados
- Sem tela nova. A região de mensagens entra no shell (backoffice); as telas dedicadas (PWA e painel da TV) não a usam.
- **400 px e teclado:** E2E da região de mensagens, do `ConfirmDialog` grande (tela cheia, foco preso, Esc fecha com "Voltar") e do `RowsEditor` em cartões.
- **Ajuda:** as ajudas que citam o "aviso no canto" ou o toast passam a citar a mensagem no topo da tela (busca por "toast" e "canto" em `Ajuda/`); o teste de cobertura continua passando.

## Plano de testes
- **Unidade (Vitest):** região de mensagens (CA 1, 2), `ConfirmDialog` com pré-condições, erros e estado de 2FA (CA 4–7), `applyServerErrors` aninhado (CA 8), `RowsEditor` (CA 9), `DataTable` (CA 10), `Chart` com tabela equivalente (CA 11), menu em grupos (CA 12).
- **Integração (Testcontainers):** 409 de concorrência (CA 13); `Idempotency-Key` com envio repetido e paralelo (CA 15); `dedup_key` em paralelo (CA 16).
- **Arquitetura:** `ExecuteUpdate` em documento auditável (CA 14); Contracts × Contracts e posse de tabela (CA 17); lint sem `toastDone`/`nextStep` (CA 3).
- **E2E (Playwright):** mensagem visível a 400 px acima do dock (CA 1); diálogo grande em tela cheia e só pelo teclado (CA 4); editor em cartões (CA 9).

## Fora do escopo
- Filtro `RequireTwoFactor`, 2FA por ação, `amr`/TOTP recente e invalidação do security stamp ao conceder ação reservada: adiados (P-19).
- Componentes de valor (rótulo de %, "Voltar ao sugerido", "Faltam 10,00%"): entram com a COM-01a e a COM-01b, que são as primeiras a usá-los (Padrões comuns da Fase 2, itens 4 e 5).
- Anexos por dono e e-mail com anexo (ADR 0019 §7): FIN-03a e COM-03.

## Pontos em aberto
Nenhum. A P-18 foi decidida (ADR 0021) e o 2FA por ação foi adiado pelo PO (P-19).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva): `dedup_key` em `shared.notifications`.
- [ ] Nenhuma permissão nova.
- [ ] Telas das Fases 0 e 1 migradas para a região de mensagens, com os estados que já tinham.
- [ ] Ajudas que citavam o toast atualizadas; teste de cobertura passando.
- [ ] Próximos passos fora da mensagem de sucesso em todas as telas.
- [ ] Auditoria: allowlist do CA 14 documentada.
- [ ] OpenAPI e cliente Orval regenerados (campo `version` nos DTOs de teste, quando houver).
- [ ] `docs/03-modelo-dados.md` atualizado (`notifications.dedup_key`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `MessageRegion`, `notifySuccess`, `notifyError` (Web); remover `toastDone`/`toastError` e o `nextStep` do `ConfirmDialog`.
- [ ] `ConfirmDialog` com `size`, `preconditions`, formulário e estado de 2FA; `applyServerErrors` aninhado.
- [ ] `RowsEditor` com modelo de colunas; `DataTable` com `selectable`, `totals` e `expandable`; `Chart` e tokens `--chart-*`.
- [ ] Menu da área Financeiro em grupos (`modules.ts` com `group`).
- [ ] `IConcurrencyVersioned`, middleware de 409, `.RequireIdempotencyKey()` e `useIdempotencyKey()`, `dedup_key`.
- [ ] Testes de arquitetura (CA 14, 17).

## Quality gates
- Agentes previstos: @dev, design (região de mensagens, diálogo grande, 400 px), @qa.
- Gate: CA 1, 6, 13 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK (sem endpoint de negócio novo; concorrência e idempotência marcadas **[Segurança]**) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (20/10/2026):** o filtro `RequireTwoFactor` da revisão §3 saiu (adiado, P-19); o estado de 2FA no diálogo ficou só para a regra que já existe (CA 7); o menu do Financeiro em dois grupos (D12) entrou aqui, porque mexe no shell de todas as telas; os componentes de valor ficaram para a COM-01a e a COM-01b.
- **Pré-condições para `Ready`:** nenhuma pendente (ADR 0019 e ADR 0021 aceitas).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 20/10/2026 | 0.1 | Story nova (revisão pré-Fase 2 §3, ADR 0019 e 0021) | River (SM) |
| 20/10/2026 | 0.2 | Validação do PO: 10/10, `Ready` | Pax (PO) |
