# RH-01b — Ponto: lançamento manual, pedido de ajuste, abono de ausência e aprovação

| Campo | Valor |
|---|---|
| ID | RH-01b |
| Título | Lançamento manual de marcações pelo RH e pedido de ajuste pelo funcionário (incluir marcação esquecida ou desconsiderar uma errada), ambos com aprovação; abono de ausência com motivo da lista única de ausências (a mesma dos afastamentos) e documento opcional protegido por `hr.view_health`; aprovação ou reprovação com `hr.approve`, sem aprovar o próprio pedido (exceção do Proprietário único, com a marca); a marcação original nunca é apagada |
| Tamanho | M (quebra da RH-01) |
| Status | Ready |
| Requisitos | R-RH-4 (parte: lançamento manual pelo RH, ajustes com justificativa e aprovação) · PRD §6 LGPD · R-SEC-5 · R-COM-2 · R-COM-3 · R-PRP-2 · R-PRP-5 · R-PRP-6 (D) |
| Dependências | RH-01a (marcações, "Meu ponto"), CAD-04c1 (afastamentos e motivos), DSH-04 (`IAgendaSource`), FND-09e; Fase 0 (FND-06c, FND-07 anexos e notificações) |
| Bloqueia | RH-01c (o espelho usa só marcações válidas e abonos aprovados) |
| Ordem de execução | 7 (Marco 6.1 "Ponto") |

## User story
Como **funcionário**, quero **pedir o ajuste de uma marcação esquecida ou errada e justificar uma ausência**, como **RH**, quero **lançar marcações quando o funcionário não pôde usar o app**, e como **proprietário**, quero **aprovar cada ajuste sabendo o motivo**, para **o espelho e a folha usarem horas verdadeiras sem ninguém mexer no ponto às escondidas**.

## Contexto e referências
- PRD R-RH-4 ("ajustes com justificativa e aprovação"; "lançamento manual pelo RH").
- `ModuleCatalog`: `hr.approve` ("ajustes de ponto, revisão da conta de salário e fechamento da folha", RH-02c); é ação reservada ao Proprietário para conceder (ADR 0008). No seed, só o Proprietário tem `hr.approve`. Ação **`hr.view_health`** (adendo de 21/10/2026 à ADR 0003): ver motivo de saúde e atestado, separado de salário (`hr.view_sensitive`).
- **Decisão do PO de 22/10/2026:** a RH-01b usa a `hr.view_health` entregue pela **CAD-04c1b** (catálogo, seed patch `hr-view-health` no grupo RH, reserva ao Proprietário e a regra dos afastamentos de Doença e Acidente de trabalho). A RH-01b não cria a ação nem o seed patch de novo; o CA 1 passa a conferir o que já existe e a acrescentar só a perda do PIN, quando o PIN existir (PRD-03b).
- `03-modelo-dados` → `hr.time_entries` (`justification`, `approved_by`); RH-01a (marcação imutável, `status`).
- `04-fluxos-estados` §3.6 (regra do solicitante: quem pede não aprova, com a exceção do Proprietário único da D6) como referência da segregação de função.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.3 pontos 6 a 9, §4.3 (transversal: anexos autorizados por registro), §4.4 padrão 8; DSH-04 (Minha pauta) já entregue na Fase 4.
- **ADR 0035** (autoatendimento `/api/v1/me/hr/*`); ADR 0003 (adendo de 21/10/2026, `hr.view_health`); ADR 0026 (adendo de 21/10/2026: perfil sensível do PIN); ADR 0018 (retenção).
- ADR 0019 §2, §3, §7 e §18 (concorrência, idempotência, anexos por dono, notificação síncrona no módulo), ADR 0021 (mensagens), ADR 0016 (`SensitiveRead`).
- LGPD art. 11: atestado médico é dado de saúde (dado pessoal sensível). A CAD-04c1 já proíbe CID no motivo do afastamento.

## Critérios de aceite
**Permissão de saúde**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `hr-view-health` roda, **então** `hr.view_health` entra no `ModuleCatalog` (ADR 0003, adendo de 21/10/2026) e o grupo RH do seed a recebe, uma vez, com o `perm_version` dos membros incrementado; quem ganha `hr.view_health` perde o PIN do tablet, como quem ganha `hr.view_sensitive` (adendo à ADR 0026).

**Lançamento manual pelo RH**
2. **Dado** `hr.create`, **quando** abro o funcionário → aba Ponto → "Lançar marcação", informo data, hora, tipo e justificativa (10 a 200 caracteres) e clico "Lançar marcação", **então** nasce um pedido de ajuste `Pending` do tipo "Incluir marcação", com origem Manual e o lançador como solicitante; o lançamento do RH também passa por aprovação; a mensagem de sucesso diz "Marcação enviada para aprovação".
3. **Dado** um dia sem nenhuma marcação, **quando** clico "Lançar o dia pela jornada", **então** o diálogo vem preenchido com as quatro marcações da jornada do funcionário naquele dia (CAD-04a), editáveis, e uma justificativa única; confirmar cria um pedido por marcação, no mesmo comando (propositivo, R-PRP-1).
4. **Dado** uma data futura ou anterior ao período mais antigo em aberto (RH-01c), **quando** lanço, **então** 422 "Data fora do período em aberto." ou "Não é possível lançar marcação no futuro."

**Pedido de ajuste pelo funcionário**
5. **Dado** "Meu ponto" → "Minhas marcações" (login completo), **quando** toco "Pedir ajuste" e escolho "Incluir marcação esquecida" (data, hora, tipo) ou "Desconsiderar esta marcação" (numa marcação minha), com justificativa obrigatória, **então** nasce o pedido `Pending` com o funcionário como solicitante, por `POST /api/v1/me/hr/time-adjustments` (ADR 0035); o funcionário vê o pedido e a situação ("Aguardando aprovação") e pode "Cancelar pedido" enquanto está pendente.
6. **[Segurança]** **Dado** o pedido do funcionário, **quando** chega à API, **então** o funcionário vem da sessão (nunca do corpo) e "Desconsiderar" só aceita marcação do próprio funcionário (404 para a de outro, IDOR); as rotas `/api/v1/me/hr/time-adjustments` estão na lista versionada de autoatendimento e fora do `[PinSessionAllowed]`.

**Abono de ausência**
7. **Dado** `hr.create` ou o próprio funcionário, **quando** registro "Abonar ausência" para um dia ou um intervalo de horas, escolho o motivo da **lista única de ausências** do HR (a mesma dos afastamentos da CAD-04c1: Atestado médico, Acidente de trabalho, Maternidade, Paternidade, Militar, Declaração de comparecimento, Folga compensada pelo banco de horas, Falta justificada e Falta não justificada, esta só registro; cada motivo diz se vale para abono, afastamento ou os dois e se é **de saúde**), informo a justificativa e anexo o documento opcional (PDF, PNG ou JPG até 10 MB), **então** nasce um pedido `Pending` do tipo "Abono"; aprovado, o espelho (RH-01c) conta as horas abonadas como justificadas.
8. **[Segurança]** **Dado** o documento do abono (ex.: atestado), **quando** é anexado, **então** usa o dono de anexo `TimeAdjustmentDocument`, com leitura e escrita por `hr.view_health` (o próprio funcionário pode anexar e ver o que ele mesmo enviou), `OwnerExists`, autorização pelo registro (o anexo precisa ser do pedido da rota), ClamAV, storage com prefixo do tenant e `SensitiveRead` no download (ADR 0019 §7); a justificativa mostra "Não informe CID nem diagnóstico." e nunca vai para log, evento ou notificação.
9. **[Segurança]** **Dado** um abono com motivo de saúde, **quando** alguém sem `hr.view_health` vê o pedido, o espelho ou a aba Ponto, **então** vê só "Ausência justificada" e "Documento anexado", sem o motivo, a justificativa nem o arquivo (fora do JSON); o motivo, a justificativa e o documento ficam com a retenção do funcionário (ADR 0018).

**Aprovação**
10. **Dado** `hr.approve`, **quando** abro RH → Ponto → "Ajustes para aprovar", **então** vejo os pedidos pendentes com funcionário, dia, o que muda ("Incluir saída às 17:50", "Desconsiderar entrada às 07:12", "Abonar 4 h: {motivo, conforme o CA 9}"), a justificativa (conforme o CA 9), quem pediu e quando, e o espelho do dia antes e depois (horas trabalhadas do dia).
11. **Dado** um pedido `Pending`, **quando** clico "Aprovar ajuste" e confirmo "Aprovar", **então** na mesma transação: "Incluir" cria a marcação `Valid` com origem Manual, `approved_by` e o id do pedido; "Desconsiderar" passa a marcação original a `Voided`, com o pedido e o motivo (nada é apagado); "Abono" fica valendo para o espelho; o pedido fica `Approved` e o solicitante é notificado ("Seu ajuste de {dia} foi aprovado."); a mensagem de sucesso diz "Ajuste aprovado".
12. **Dado** um pedido `Pending`, **quando** clico "Reprovar" e informo o comentário obrigatório, **então** o pedido fica `Rejected`, nada muda nas marcações e o solicitante é notificado com o comentário.
13. **[Segurança]** **Dado** o aprovador, **quando** o pedido é dele mesmo (solicitante) ou é de ajuste das próprias marcações, **então** 403 "Você não pode aprovar um ajuste seu." (segregação de função); com um só membro com `hr.approve` (Proprietário único), ele aprova as próprias marcações com a marca "autoaprovado" visível no pedido e no espelho, como a D6 do Financeiro.
14. **[Segurança]** **Dado** dois aprovadores ao mesmo tempo, **quando** decidem o mesmo pedido, **então** só uma decisão grava (transição pela entidade rastreada com `xmin`); a outra recebe 409 `concurrency_conflict` com "Recarregar".
15. **Dado** vários pedidos selecionados, **quando** clico "Aprovar selecionados" (D, R-PRP-6), **então** o diálogo lista o impacto por pedido e cada pedido é decidido na sua transação; os que falharem (próprio, já decidido, período fechado) aparecem com o motivo e os outros são aprovados.
16. **Dado** um período fechado (RH-01c), **quando** tento lançar, pedir, aprovar ou reprovar um ajuste dentro dele, **então** 409 "O período {mês} está fechado. Peça a reabertura ao RH."

**Notificações e pauta**
17. **Dado** um pedido novo, **quando** é gravado, **então** quem tem `hr.approve` recebe a notificação "Ajuste de ponto de {funcionário} para aprovar" com link para o pedido (síncrona, ADR 0019 §18, `dedup_key` `time-adjustment:{id}`), sem a justificativa nem o motivo no texto; o HR implementa o `IAgendaSource` da Minha pauta (DSH-04) com a categoria "Ajustes de ponto aguardando você" para quem tem `hr.approve`.

**Permissões e auditoria**
18. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; lançar exige `hr.create`, aprovar e reprovar `hr.approve`, ver os pedidos dos outros `hr.view`, motivo de saúde e documento `hr.view_health`; sem a ação, 403 `forbidden` e o botão some. Pedido do próprio funcionário pelo autoatendimento (CA 5, 6). `RequireModule` e `perm:`; IDOR em `/employees/{id}/time-adjustments/{adjId}`.
19. **Dado** qualquer pedido, decisão ou marcação desconsiderada, **quando** abro o histórico, **então** vejo antes/depois, usuário, data e IP; a marcação desconsiderada continua visível no espelho riscada, com "Desconsiderada por {aprovador} em {data}: {motivo}" para quem tem `hr.view`.

## Regras de negócio e técnicas
- **Tabela:** `hr.time_adjustments`: `employee_id`, `kind` (`AddEntry`/`VoidEntry`/`Absence`), `entry_id` (no `VoidEntry`), `at` e `type` (no `AddEntry`), `absence_date`, `absence_minutes` ou dia inteiro, `absence_reason` (lista única), `justification` (`[PersonalData]`), `source` (Employee/HR), `requested_by`, `requested_at`, `status` (`Pending`/`Approved`/`Rejected`/`Cancelled`), `decided_by`, `decided_at`, `decision_comment`, `self_approved`, `created_entry_id`; `xmin`.
- **Lista única de ausências** (`AbsenceReason` no Domain do HR, com `applies_to` Abono/Afastamento/Ambos e `is_health`): substitui o tipo de afastamento da CAD-04c1, com migração dos valores atuais (Doença → Atestado médico; Outros → Falta justificada; Acidente de trabalho, Maternidade, Paternidade e Militar ficam iguais), sem perder dado.
- **Marcação:** `hr.time_entries.status` ganha `Voided`; a marcação criada por ajuste tem `source = Manual`, `approved_by` e `adjustment_id`.
- **Idempotência:** lançar e pedir exigem `Idempotency-Key` (ADR 0019 §3).
- **Segregação:** quem pede não aprova; o aprovador não aprova ajuste das próprias marcações (exceção do Proprietário único, com a marca).
- **Prazo do pedido:** só dentro de período em aberto (RH-01c).
- Datas: `absence_date` em `date`; `at` em `timestamptz`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.create` (lançar e abonar para outro), `hr.approve` (decidir), `hr.view` (ver pedidos), `hr.view_health` (motivo de saúde e documento; nova, seed patch `hr-view-health`); autoatendimento para o próprio pedido (ADR 0035).
- **Eventos:** nenhum entre módulos (notificação síncrona dentro do HR).
- **Contratos:** implementa `IAgendaSource` (DSH-04).
- **Tabelas:** `hr.time_adjustments` (nova); `hr.time_entries` (+ `Voided`, `adjustment_id`); motivo único de ausência no afastamento (CAD-04c1, migração); dono de anexo `TimeAdjustmentDocument`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 ("Meu ponto" → Pedir ajuste com o layout de autoatendimento; bloco "Dados protegidos" para motivo de saúde e documento).
- **RH → Ponto → Ajustes para aprovar** (`/app/rh/ponto/ajustes`): lista com seleção, filtros por situação e funcionário na URL; detalhe em painel lateral com antes e depois do dia.
- **Aba Ponto do funcionário:** "Lançar marcação", "Lançar o dia pela jornada", "Abonar ausência"; pedidos do funcionário com chip de situação (Aguardando info, Aprovado ok, Reprovado crítico, Cancelado neutro) e a marca "autoaprovado".
- **"Meu ponto" → Pedir ajuste** e "Meus pedidos".
- **Verbos:** "Lançar marcação" / "Marcação enviada para aprovação"; "Aprovar ajuste" / "Ajuste aprovado"; "Reprovar" / "Ajuste reprovado"; "Cancelar pedido" / "Pedido cancelado".
- **Estados:** carregando; vazio ("Nenhum ajuste aguardando aprovação."); erro; período fechado (CA 16) com o atalho "Ver fechamento"; "Dados protegidos" (CA 9).
- **400 px:** pedidos em cartões; diálogo de decisão em tela cheia; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/rh-ponto-ajustes.md`; atualizar `Ajuda/rh-funcionario-ponto.md` e `Ajuda/meu-ponto.md` ("Pedir ajuste"); `<Dica>` em "Desconsiderar" ("A marcação continua guardada, só deixa de contar."), "Abono" e "Motivo de saúde".

## Plano de testes
- **Unidade:** dia pela jornada (CA 3); regras de data (CA 4); segregação e Proprietário único (CA 13); lista única de ausências e migração dos tipos da CAD-04c1 (CA 7).
- **Integração (Testcontainers):** seed patch `hr-view-health` uma vez e PIN apagado (CA 1); lançamento e pedido com `Idempotency-Key` (CA 2, 5); IDOR do funcionário e lista versionada (CA 6); abono com anexo `TimeAdjustmentDocument`, autorização pelo registro, `SensitiveRead` e justificativa fora do log (CA 7, 8); motivo de saúde fora do JSON sem `hr.view_health` (CA 9); aprovar incluir, desconsiderar e abono na mesma transação (CA 11); reprovar (CA 12); próprio pedido e autoaprovado (CA 13); concorrência (CA 14); lote parcial (CA 15); período fechado (CA 16); notificação com `dedup_key` e item da pauta (CA 17); três camadas (CA 18); histórico (CA 19).
- **E2E (Playwright):** funcionário pede ajuste no celular a 400 px; Proprietário aprova; o espelho do dia muda.

## Fora do escopo
- Espelho, banco de horas e fechamento: RH-01c.
- Afastamentos longos (doença, licenças): CAD-04c1 (usa a mesma lista de motivos).
- Alçada por tipo de ajuste ou aprovação em dois níveis: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 6–9):
1. **Lançamento manual do RH também passa por aprovação** (CA 2).
2. **Tipos de abono unificados com os afastamentos da CAD-04c1** numa lista única de ausências (CA 7).
3. **Proprietário único** aprova as próprias marcações, com a marca "autoaprovado" (CA 13).
4. **Atestado:** retenção igual à do funcionário (ADR 0018); ação `hr.view_health` separando atestado de salário (adendo à ADR 0003) (CA 1, 8, 9).

Decisões da revisão aplicadas: §4.3 (anexo autorizado pelo registro e storage com prefixo do tenant, CA 8); §4.4 padrão 8; pauta pelo `IAgendaSource` da DSH-04 (CA 17).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva; migração dos tipos de afastamento para a lista única sem perda).
- [ ] Permissões só do `ModuleCatalog` (`hr.view_health` com seed patch); dono de anexo `TimeAdjustmentDocument` registrado com `OwnerExists`.
- [ ] Telas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Próximo passo e alertas acionáveis aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TimeAdjustment` (Domain) com as transições e a segregação; `AbsenceReason` único.
- [ ] Endpoints `/api/v1/hr/employees/{id}/time-adjustments`, `/api/v1/hr/time-adjustments` (aprovar, reprovar, lote) e `/api/v1/me/hr/time-adjustments`.
- [ ] Seed patch `hr-view-health`; dono de anexo, notificações, `IAgendaSource`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação, IDOR, dado de saúde), @qa.
- Gate: CA 1, 6, 8, 9, 11, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: `hr.view_health`, CA 1, 8, 9) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** "desconsiderar" no lugar de editar ou apagar a marcação; abono com dono de anexo sensível e sem CID; o aprovador não aprova ajuste das próprias marcações; período fechado bloqueia o ajuste.
- **Correções da revalidação (21/10/2026):** pontos 6 a 9 decididos: lançamento do RH com aprovação (CA 2), lista única de ausências com a da CAD-04c1 e migração (CA 7), Proprietário único com "autoaprovado" (CA 13), `hr.view_health` nova com seed patch, documento e motivo de saúde só com ela e perda do PIN (CA 1, 8, 9); rotas `/api/v1/me/hr/*` (ADR 0035, CA 5, 6); anexo autorizado pelo registro (CA 8); pauta pelo `IAgendaSource` (CA 17), a DSH-04 já entregue; CAD-04c → CAD-04c1; ordem 7 do Marco 6.1; CAs renumerados (1 a 17 viraram 2 a 19).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (tabela 3.3 pontos 6–9, adendos às ADRs 0003 e 0026, ADR 0035, §4.3, §4.4 padrão 8); nova ordem (Marco 6.1, ordem 7); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
