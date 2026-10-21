# RH-01b — Ponto: lançamento manual, pedido de ajuste, abono de ausência e aprovação

| Campo | Valor |
|---|---|
| ID | RH-01b |
| Título | Lançamento manual de marcações pelo RH, pedido de ajuste pelo funcionário (incluir marcação esquecida ou desconsiderar uma errada) e abono de ausência com documento, sempre com justificativa; aprovação ou reprovação com `hr.approve` ("ajustes de ponto"), sem aprovar o próprio pedido; a marcação original nunca é apagada |
| Tamanho | M (quebra da RH-01) |
| Status | Draft |
| Requisitos | R-RH-4 (parte: lançamento manual pelo RH, ajustes com justificativa e aprovação) · PRD §6 LGPD · R-SEC-5 · R-COM-2 · R-COM-3 · R-PRP-2 · R-PRP-5 · R-PRP-6 (D) |
| Dependências | RH-01a (marcações, "Meu ponto"), CAD-04c (afastamentos e dono de anexo do funcionário como referência), FND-09e; Fase 0 (FND-06c, FND-07 anexos e notificações) |
| Bloqueia | RH-01c (o espelho usa só marcações válidas e abonos aprovados) |
| Ordem de execução | 2 (Marco 6.1 "Ponto") |

## User story
Como **funcionário**, quero **pedir o ajuste de uma marcação esquecida ou errada e justificar uma ausência**, como **RH**, quero **lançar marcações quando o funcionário não pôde usar o app**, e como **proprietário**, quero **aprovar cada ajuste sabendo o motivo**, para **o espelho e a folha usarem horas verdadeiras sem ninguém mexer no ponto às escondidas**.

## Contexto e referências
- PRD R-RH-4 ("ajustes com justificativa e aprovação"; "lançamento manual pelo RH").
- `ModuleCatalog`: `hr.approve` tem o significado "ajustes de ponto"; é ação reservada ao Proprietário para conceder (ADR 0008). No seed, só o Proprietário tem `hr.approve`.
- `03-modelo-dados` → `hr.time_entries` (`justification`, `approved_by`); RH-01a (marcação imutável, `status`).
- `04-fluxos-estados` §3.6 (regra do solicitante: quem pede não aprova, com a exceção do Proprietário único da D6) como referência da segregação de função.
- ADR 0019 §2, §3, §7 e §18 (concorrência, idempotência, anexos por dono, notificação síncrona no módulo), ADR 0021 (mensagens), ADR 0016 (`SensitiveRead`).
- LGPD art. 11: atestado médico é dado de saúde (dado pessoal sensível). A CAD-04c já proíbe CID no motivo do afastamento.

## Critérios de aceite
**Lançamento manual pelo RH**
1. **Dado** `hr.create`, **quando** abro o funcionário → aba Ponto → "Lançar marcação", informo data, hora, tipo e justificativa (10 a 200 caracteres) e clico "Lançar marcação", **então** nasce um pedido de ajuste `Pending` do tipo "Incluir marcação", com origem Manual e o lançador como solicitante; a mensagem de sucesso diz "Marcação enviada para aprovação".
2. **Dado** um dia sem nenhuma marcação, **quando** clico "Lançar o dia pela jornada", **então** o diálogo vem preenchido com as quatro marcações da jornada do funcionário naquele dia (CAD-04a), editáveis, e uma justificativa única; confirmar cria um pedido por marcação, no mesmo comando (propositivo, R-PRP-1).
3. **Dado** uma data futura ou anterior ao período mais antigo em aberto (RH-01c), **quando** lanço, **então** 422 "Data fora do período em aberto." ou "Não é possível lançar marcação no futuro."

**Pedido de ajuste pelo funcionário**
4. **Dado** "Meu ponto" → "Minhas marcações", **quando** toco "Pedir ajuste" e escolho "Incluir marcação esquecida" (data, hora, tipo) ou "Desconsiderar esta marcação" (numa marcação minha), com justificativa obrigatória, **então** nasce o pedido `Pending` com o funcionário como solicitante; o funcionário vê o pedido e a situação ("Aguardando aprovação") e pode "Cancelar pedido" enquanto está pendente.
5. **[Segurança]** **Dado** o pedido do funcionário, **quando** chega à API, **então** o funcionário vem da sessão (nunca do corpo) e "Desconsiderar" só aceita marcação do próprio funcionário (404 para a de outro, IDOR).

**Abono de ausência**
6. **Dado** `hr.create` ou o próprio funcionário, **quando** registro "Abonar ausência" para um dia ou um intervalo de horas, escolho o tipo (lista do ponto em aberto 2), informo a justificativa e anexo o documento opcional (PDF, PNG ou JPG até 10 MB), **então** nasce um pedido `Pending` do tipo "Abono"; aprovado, o espelho (RH-01c) conta as horas abonadas como justificadas.
7. **[Segurança]** **Dado** o documento do abono (ex.: atestado), **quando** é anexado, **então** usa o dono de anexo `TimeAdjustmentDocument` (proposta), com leitura e escrita por `hr.view_sensitive` (o próprio funcionário pode anexar e ver o que ele mesmo enviou), `OwnerExists`, ClamAV e `SensitiveRead` no download (ADR 0019 §7); a justificativa mostra "Não informe CID nem diagnóstico." e nunca vai para log, evento ou notificação.

**Aprovação**
8. **Dado** `hr.approve`, **quando** abro RH → Ponto → "Ajustes para aprovar", **então** vejo os pedidos pendentes com funcionário, dia, o que muda ("Incluir saída às 17:50", "Desconsiderar entrada às 07:12", "Abonar 4 h: consulta médica"), a justificativa, quem pediu e quando, e o espelho do dia antes e depois (horas trabalhadas do dia).
9. **Dado** um pedido `Pending`, **quando** clico "Aprovar ajuste" e confirmo "Aprovar", **então** na mesma transação: "Incluir" cria a marcação `Valid` com origem Manual, `approved_by` e o id do pedido; "Desconsiderar" passa a marcação original a `Voided`, com o pedido e o motivo (nada é apagado); "Abono" fica valendo para o espelho; o pedido fica `Approved` e o solicitante é notificado ("Seu ajuste de {dia} foi aprovado."); a mensagem de sucesso diz "Ajuste aprovado".
10. **Dado** um pedido `Pending`, **quando** clico "Reprovar" e informo o comentário obrigatório, **então** o pedido fica `Rejected`, nada muda nas marcações e o solicitante é notificado com o comentário.
11. **[Segurança]** **Dado** o aprovador, **quando** o pedido é dele mesmo (solicitante) ou é de ajuste das próprias marcações, **então** 403 "Você não pode aprovar um ajuste seu." (segregação de função); a exceção do Proprietário único segue o ponto em aberto 3.
12. **[Segurança]** **Dado** dois aprovadores ao mesmo tempo, **quando** decidem o mesmo pedido, **então** só uma decisão grava (transição pela entidade rastreada com `xmin`); a outra recebe 409 `concurrency_conflict` com "Recarregar".
13. **Dado** vários pedidos selecionados, **quando** clico "Aprovar selecionados" (D, R-PRP-6), **então** cada pedido é decidido na sua transação, os que falharem (próprio, já decidido, período fechado) aparecem com o motivo e os outros são aprovados.
14. **Dado** um período fechado (RH-01c), **quando** tento lançar, pedir, aprovar ou reprovar um ajuste dentro dele, **então** 409 "O período {mês} está fechado. Peça a reabertura ao RH."

**Notificações e pauta**
15. **Dado** um pedido novo, **quando** é gravado, **então** quem tem `hr.approve` recebe a notificação "Ajuste de ponto de {funcionário} para aprovar" com link para o pedido (síncrona, ADR 0019 §18, `dedup_key` `time-adjustment:{id}`), sem a justificativa no texto; quando a Minha pauta (DSH-04) existir, o detector do HR lista "Ajustes de ponto aguardando você" (proposta).

**Permissões e auditoria**
16. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; lançar exige `hr.create`, aprovar e reprovar `hr.approve`, ver os pedidos dos outros `hr.view`; sem a ação, 403 `forbidden` e o botão some. Pedido do próprio funcionário pela regra de autoatendimento da RH-01a (ponto em aberto 1 de lá). `RequireModule` e `perm:`; IDOR em `/employees/{id}/time-adjustments/{adjId}`.
17. **Dado** qualquer pedido, decisão ou marcação desconsiderada, **quando** abro o histórico, **então** vejo antes/depois, usuário, data e IP; a marcação desconsiderada continua visível no espelho riscada, com "Desconsiderada por {aprovador} em {data}: {motivo}" para quem tem `hr.view`.

## Regras de negócio e técnicas
- **Tabela (proposta):** `hr.time_adjustments`: `employee_id`, `kind` (`AddEntry`/`VoidEntry`/`Absence`), `entry_id` (no `VoidEntry`), `at` e `type` (no `AddEntry`), `absence_date`, `absence_minutes` ou dia inteiro, `absence_kind`, `justification` (`[PersonalData]` no abono), `source` (Employee/HR), `requested_by`, `requested_at`, `status` (`Pending`/`Approved`/`Rejected`/`Cancelled`), `decided_by`, `decided_at`, `decision_comment`, `created_entry_id`; `xmin`.
- **Marcação:** `hr.time_entries.status` ganha `Voided`; a marcação criada por ajuste tem `source = Manual`, `approved_by` e `adjustment_id`.
- **Idempotência:** lançar e pedir exigem `Idempotency-Key` (ADR 0019 §3).
- **Segregação:** quem pede não aprova; o aprovador não aprova ajuste das próprias marcações.
- **Prazo do pedido:** só dentro de período em aberto (RH-01c).
- Datas: `absence_date` em `date`; `at` em `timestamptz`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.create` (lançar e abonar para outro), `hr.approve` (decidir), `hr.view` (ver pedidos), `hr.view_sensitive` (documento do abono); autoatendimento para o próprio pedido.
- **Eventos:** nenhum entre módulos (notificação síncrona dentro do HR).
- **Tabelas:** `hr.time_adjustments` (nova, proposta); `hr.time_entries` (+ `Voided`, `adjustment_id`); dono de anexo `TimeAdjustmentDocument`.

## Telas e estados
- **RH → Ponto → Ajustes para aprovar** (`/app/rh/ponto/ajustes`): lista com seleção, filtros por situação e funcionário na URL; detalhe em painel lateral com antes e depois do dia.
- **Aba Ponto do funcionário:** "Lançar marcação", "Lançar o dia pela jornada", "Abonar ausência"; pedidos do funcionário com chip de situação (Aguardando info, Aprovado ok, Reprovado crítico, Cancelado neutro).
- **"Meu ponto" → Pedir ajuste** e "Meus pedidos".
- **Verbos:** "Lançar marcação" / "Marcação enviada para aprovação"; "Aprovar ajuste" / "Ajuste aprovado"; "Reprovar" / "Ajuste reprovado"; "Cancelar pedido" / "Pedido cancelado".
- **Estados:** carregando; vazio ("Nenhum ajuste aguardando aprovação."); erro; período fechado (CA 14) com o atalho "Ver fechamento".
- **400 px:** pedidos em cartões; diálogo de decisão em tela cheia.
- **Ajuda:** `Ajuda/rh-ponto-ajustes.md`; atualizar `Ajuda/rh-funcionario-ponto.md` e `Ajuda/meu-ponto.md` ("Pedir ajuste"); `<Dica>` em "Desconsiderar" ("A marcação continua guardada, só deixa de contar.") e "Abono".

## Plano de testes
- **Unidade:** dia pela jornada (CA 2); regras de data (CA 3); segregação (CA 11).
- **Integração (Testcontainers):** lançamento e pedido com `Idempotency-Key` (CA 1, 4); IDOR do funcionário (CA 5); abono com anexo `TimeAdjustmentDocument`, `SensitiveRead` e justificativa fora do log (CA 6, 7); aprovar incluir, desconsiderar e abono na mesma transação (CA 9); reprovar (CA 10); próprio pedido (CA 11); concorrência (CA 12); lote parcial (CA 13); período fechado (CA 14); notificação com `dedup_key` e sem a justificativa (CA 15); três camadas (CA 16); histórico (CA 17).
- **E2E (Playwright):** funcionário pede ajuste no celular a 400 px; Proprietário aprova; o espelho do dia muda.

## Fora do escopo
- Espelho, banco de horas e fechamento: RH-01c.
- Afastamentos longos (doença, licenças): CAD-04c.
- Alçada por tipo de ajuste ou aprovação em dois níveis: fora do MVP.

## Pontos em aberto
1. **Lançamento manual pelo RH também exige aprovação** (PO): proposta sim, pela mesma regra do ajuste (o RH lança, o Proprietário aprova). A alternativa é o lançamento do RH valer na hora, com auditoria e notificação ao Proprietário.
2. **Tipos de abono** (PO + contadora): proposta Atestado médico, Declaração de comparecimento, Folga compensada (banco de horas), Falta justificada (outros) e Falta não justificada (só registro). A relação com os tipos de afastamento da CAD-04c (ponto em aberto 2 de lá) precisa ser única.
3. **Proprietário único** (PO): com um só membro com `hr.approve`, ele pode aprovar o ajuste das próprias marcações? Proposta: sim, com a marca "autoaprovado", como a D6 do Financeiro.
4. Retenção do documento do abono (atestado): proposta igual à do funcionário (ADR 0018), com acesso só por `hr.view_sensitive`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `TimeAdjustmentDocument` registrado com `OwnerExists`.
- [ ] Telas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Próximo passo e alertas acionáveis aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TimeAdjustment` (Domain) com as transições e a segregação.
- [ ] Endpoints `/api/v1/hr/employees/{id}/time-adjustments`, `/api/v1/hr/time-adjustments` (aprovar, reprovar, lote) e `/api/v1/hr/me/time-adjustments`.
- [ ] Dono de anexo, notificações; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação, IDOR, dado de saúde), @qa.
- Gate: CA 5, 7, 9, 11 e 12 obrigatórios.

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

- **Correções feitas na validação:** "desconsiderar" no lugar de editar ou apagar a marcação (CA 9, 17); abono com dono de anexo sensível e sem CID (CA 7); o aprovador não aprova ajuste das próprias marcações (CA 11); período fechado bloqueia o ajuste (CA 14).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; RH-01a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
