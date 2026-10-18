# CAD-04a — Cargos, jornadas, sindicatos e custo-hora

| Campo | Valor |
|---|---|
| ID | CAD-04a |
| Título | Cargos com CBO, faixa e salário de referência (sensíveis, por rota própria) e custo-hora calculado (salário × (1 + % encargos) ÷ horas mensais) ou ajustado; jornadas com seed "44 h seg–sex" e sindicatos |
| Tamanho | M (quebra da CAD-04: o cadastro de funcionários foi para a CAD-04b e a CAD-04c) |
| Status | Ready |
| Requisitos | R-RH-2 · R-RH-1 (jornada e sindicato como cadastros de apoio) · R-PRD-4 (preparação) · R-SEC-5 · R-SEC-10 (`hr.view_sensitive`) · PRD §6 LGPD |
| Dependências | CAD-06 (% de encargos e `PayrollChargesChanged`), CAD-03 (centro de trabalho padrão), CAD-01a (`BrazilianDocuments`, para o CNPJ do sindicato), FND-09d (`RowsEditor`, `MoneyInput`, `CboInput`); Fase 0 |
| Bloqueia | CAD-04b, PRD-04, COM-01 (horas por centro no custo do orçamento) |
| Ordem de execução | 12 (Marco C — base de custo) |

## User story
Como **RH e proprietário**, quero **cadastrar os cargos com a faixa salarial e ver o custo-hora de cada um já com os encargos**, para **que toda hora apontada na fábrica vire custo real no projeto, sem planilha à parte**.

## Contexto e referências
- PRD R-RH-2 (cargos com faixa salarial e custo-hora = salário + encargos estimados), R-PRD-4.
- `docs/04-fluxos-estados.md` §3.2: `custo_hora_cargo = (salário × (1 + %encargos)) / horas_mensais_contratuais`; snapshot gravado no apontamento.
- `docs/03-modelo-dados.md` → `hr.job_positions` (com `reference_salary`, `monthly_hours`, `hourly_cost_manual`, `is_active`), `hr.work_schedules` (+ `is_active`, `seed_key`), `hr.unions`.
- Decidido em 18/10/2026 (tabela 1.1): salário de referência do cargo, com padrão na média da faixa; horas mensais = semanais × 5, editável; custo-hora do **cargo** visível com `hr.view`; custo por funcionário não é exposto; seed da jornada "44 h seg–sex"; sindicato em tabela simples.
- **ADR 0016** (dados sensíveis por rota `/sensitive`, `SensitiveRead`, comandos separados com `UnmappedMemberHandling.Disallow`, ordenar/filtrar por sensível ignorado, histórico de HR só com `hr.view_sensitive`, `no-store`, allowlist e canário), revisão pré-Fase 1 §2.5 (CAD-04a e CAD-04b).
- ADR 0003 e `ModuleCatalog`: `hr` com `view`, `create`, `edit`, `approve`, `delete` e `view_sensitive`. O grupo RH do seed tem `view`, `create`, `edit` e `view_sensitive` (ADR 0010 item 3). O campo `Phase` do catálogo é só informativo (ADR 0016): o `hr` vale pela liberação do tenant.
- Padrões comuns a todos os cadastros: `docs/stories/README.md`. Cargo, jornada e sindicato usam o **painel lateral**.

## Critérios de aceite
**Cargos**
1. **Dado** `hr.create`, **quando** cadastro um cargo com nome (único entre os não excluídos), CBO (6 dígitos, exibido `9999-99`) e centro de trabalho padrão (opcional, ativo, via `IWorkCenters`) e clico "Salvar cargo", **então** ele é gravado, auditado e o toast diz "Cargo salvo".
2. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** informo a faixa salarial (mínimo ≤ máximo, `numeric(18,2)`) no comando próprio "Salvar faixa salarial", **então** o salário de referência vem sugerido como a média da faixa enquanto eu não o editar, e os dois são gravados.
3. **Dado** o % de encargos de 80,00% (CAD-06), salário de referência de R$ 3.300,00 e jornada de 44 h semanais (220 h mensais = 44 × 5), **quando** salvo, **então** o custo-hora calculado é R$ 27,00 (`3.300 × 1,80 ÷ 220`), gravado em `numeric(18,6)` e exibido com 2 casas.
4. **Dado** as horas mensais contratuais, **quando** escolho a jornada do cargo, **então** elas vêm como semanais × 5 e posso editar no cargo (ex.: 200 h); o custo-hora usa o valor do cargo.
5. **Dado** `hr.edit`, **quando** clico "Ajustar custo-hora" e informo um valor manual, **então** o cargo fica marcado "Ajustado manualmente" (com `<Dica>` explicando que não acompanha mudança de encargos) e "Voltar ao calculado" restaura a fórmula.
6. **Dado** o evento `PayrollChargesChanged` (CAD-06), **quando** é processado, **então** os cargos não ajustados à mão são recalculados; os ajustados não mudam; o processamento é idempotente (inbox por handler) e cada recálculo fica no histórico do cargo.
7. **Dado** o diálogo de confirmação do % de encargos da CAD-06 e um usuário com `hr.view`, **quando** ele abre, **então** mostra "{n} cargos serão recalculados", lido de `GET /api/v1/hr/positions/summary` (contagem, sem valores); sem `hr.view`, mostra o texto genérico.
8. **Dado** Production (PRD-04) ou Commercial (COM-01), **quando** precisam do custo-hora, **então** usam `IHourlyCost` de `HR.Contracts`, só **por cargo**, sem exigir permissão de RH do usuário; não existe custo-hora por funcionário no contrato; o teste de arquitetura impede outra referência.
9. **Dado** um cargo usado por funcionário ativo (CAD-04b), **quando** tento excluir, **então** a API responde 409 "O cargo tem funcionários. Inative o cargo ou mude o cargo deles." e o diálogo oferece "Inativar"; sem uso, `hr.delete` exclui logicamente.

**Dados de salário do cargo (LGPD)**
10. **[Segurança]** **Dado** `hr.view` sem `hr.view_sensitive`, **quando** chamo a lista ou `GET /api/v1/hr/positions/{id}`, **então** a resposta não traz faixa salarial nem salário de referência (campos ausentes no JSON bruto); o custo-hora do cargo aparece; a faixa só vem em `GET /api/v1/hr/positions/{id}/sensitive`, que exige `hr.view_sensitive`, responde com `Cache-Control: no-store` e registra `SensitiveRead` (ADR 0016).
11. **[Segurança]** **Dado** `hr.edit` sem `hr.view_sensitive`, **quando** chamo o comando de faixa salarial, **então** a API responde 403 `forbidden`; um `PUT` dos dados gerais com campos de salário no corpo responde 400 (`UnmappedMemberHandling.Disallow`) e nunca anula a faixa gravada.
12. **[Segurança]** **Dado** um usuário sem `hr.view_sensitive`, **quando** pede a lista ordenada ou filtrada por faixa salarial, **então** a ordenação e o filtro são ignorados (sem erro e sem vazar a ordem).
13. **[Segurança]** **Dado** o histórico de alterações de cargo, jornada ou sindicato, **quando** um usuário sem `hr.view_sensitive` abre, **então** recebe 403 `forbidden`; com a permissão, a faixa e o salário de referência aparecem como `***` (`[PersonalData]`).
14. **[Segurança]** **Dado** o teste de arquitetura com allowlist e o teste de canário, **quando** rodam, **então** toda propriedade de entidade auditável do HR é `[PersonalData]` ou está na lista de não sensíveis, nenhum DTO de resumo ou lista tem propriedade sensível, e valores canário de salário não aparecem em `audit.audit_log` nem no log.

**Jornadas**
15. **Dado** `hr.create`, **quando** cadastro uma jornada com nome e, no `RowsEditor`, entrada, saída e intervalo de cada dia, e clico "Salvar jornada", **então** as horas semanais são calculadas (ex.: seg–sex 08:00–17:48 com 1 h de intervalo = 44,00 h), com o total ao vivo em `aria-live`; saída antes da entrada responde 400.
16. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe a jornada "44 h — seg a sex 08:00–17:48", com `seed_key`, sem recriar a excluída.

**Sindicatos**
17. **Dado** `hr.create`, **quando** cadastro um sindicato com nome, CNPJ (validado pelo `BrazilianDocuments`) e mês da data-base e clico "Salvar sindicato", **então** ele é gravado e pode ser escolhido no funcionário (CAD-04b); sindicato ou jornada em uso só inativa.

**Permissões (3 camadas)**
18. **Dado** o módulo `hr` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `hr`, 403 `forbidden`; com `hr.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:`.

**Auditoria**
19. **Dado** qualquer alteração de cargo, jornada ou sindicato, **quando** abro "Histórico de alterações" com `hr.view_sensitive`, **então** vejo antes/depois, usuário e data, com o salário mascarado (CA 13).

## Regras de negócio e técnicas
- **Fórmula** (04 §3.2) num só lugar (`HourlyCostCalculator`), com `decimal` em todas as etapas e arredondamento só no fim (6 casas).
- **Salário de referência** (`reference_salary`): padrão na média da faixa enquanto não editado.
- **Horas mensais contratuais** (`monthly_hours`): semanais da jornada × 5, editável no cargo.
- **Ajuste manual:** `hourly_cost_manual` (bool).
- **Comandos separados:** dados gerais (nome, CBO, centro, jornada, horas mensais), faixa salarial (sensível) e ajuste do custo-hora.
- **`SensitiveRead`:** `IAuditWriter.RecordReadAsync` nasce nesta story (primeira do HR) e é reaproveitado pela CAD-04b.
- **Jornada:** `days` JSON `[{ "weekday": 1, "start": "08:00", "end": "17:48", "break_minutes": 60 }]`; horas em `numeric(5,2)`.
- **Sindicato:** `hr.unions` (`name`, `document`, `base_month`, `is_active`).
- **Soft delete** em cargos, jornadas e sindicatos; com uso, só inativa (D10).
- **Ids:** Guid v7. **Auditoria:** `IAuditable`; `salary_min`, `salary_max` e `reference_salary` com `[PersonalData]`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `hr.view`, `hr.create`, `hr.edit`, `hr.delete`, `hr.view_sensitive`.
- **Eventos:** consome `PayrollChargesChanged` (CAD-06); publica `HourlyCostChanged` (`{ positionId }`, sem valores; sem consumidor nesta fase).
- **Tabelas:** `hr.job_positions` (+ `reference_salary`, `monthly_hours`, `hourly_cost_manual`, `is_active`); `hr.work_schedules` (+ `is_active`, `seed_key`); `hr.unions` (nova).

## Telas e estados
- **RH → Cargos** (`/app/rh/cargos`): h1 com a linha "Cargos da empresa e o custo de cada hora trabalhada."; DataTable com nome (link), CBO, centro padrão, custo-hora (`Money`, chip "Ajustado"), funcionários no cargo e situação; painel lateral com a seção "Faixa salarial" que, sem `hr.view_sensitive`, mostra "Dados protegidos".
- **RH → Jornadas** (`/app/rh/jornadas`) e **RH → Sindicatos** (`/app/rh/sindicatos`): listas com painel lateral; o editor da jornada mostra o total semanal e mensal ao vivo.
- **Verbos:** "Salvar cargo", "Salvar faixa salarial", "Ajustar custo-hora", "Voltar ao calculado", "Salvar jornada", "Salvar sindicato", "Inativar", "Excluir".
- **Estados:** carregando; vazio ("Cadastre os cargos da empresa para calcular o custo da hora trabalhada." + "Novo cargo"); vazio filtrado; erro com "Tentar de novo"; modo leitura; "Dados protegidos".
- **Propositivo:** salário de referência na média da faixa; prévia do custo-hora enquanto digita; depois de salvar o cargo, a faixa oferece "Cadastrar funcionário neste cargo" (CAD-04b).
- **400 px e teclado:** um CA de E2E para o editor da jornada.
- **Ajuda:** `Ajuda/rh-cargos.md` (aba `cargos`), `Ajuda/rh-jornadas.md` (aba `jornadas`), `Ajuda/rh-sindicatos.md` (aba `sindicatos`); `<Dica>` em "Custo-hora", "CBO", "Horas mensais" e "Dados protegidos".

## Plano de testes
- **Unidade:** fórmula com o exemplo do CA 3 e bordas (encargos 0%, salário 0); horas da jornada (CA 15); horas mensais (CA 4); média da faixa (CA 2).
- **Integração (Testcontainers):** cargo calculado e manual (CA 3, 5); recálculo idempotente pelo evento entregue duas vezes (CA 6); resumo para o diálogo (CA 7); `IHourlyCost` só por cargo (CA 8); exclusão com uso (CA 9); JSON bruto sem salário, `/sensitive` com `SensitiveRead` e `no-store` (CA 10); 403 e 400 nos comandos (CA 11); ordenação ignorada (CA 12); histórico com 403 e máscara (CA 13, 19); canário (CA 14); seed da jornada em tenant novo e existente (CA 16); sindicato (CA 17); três camadas (CA 18).
- **Arquitetura:** allowlist de `[PersonalData]` e DTOs sem sensível (CA 14); HR só referencia `Identity.Contracts` e `Production.Contracts`; outros módulos só `HR.Contracts`.
- **E2E (Playwright):** cadastrar cargo e jornada e ver o custo-hora; usuário sem `hr.view_sensitive` não vê a faixa nem na resposta da rede; editor da jornada a 400 px e só pelo teclado.

## Fora do escopo
- Funcionários: CAD-04b (base) e CAD-04c (dependentes, banco/Pix, adicionais, afastamentos).
- Uso do custo-hora no apontamento: PRD-04. Folha: RH-02 e Fase 7.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1 e §2.5): salário de referência com padrão na média; horas mensais = semanais × 5, editável; custo-hora do cargo com `hr.view`, sem custo por funcionário; seed "44 h seg–sex"; sindicato em tabela.
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `hr`, não destrutiva) e seed da jornada.
- [ ] Permissões só do `ModuleCatalog` (inclui `hr.view_sensitive`).
- [ ] Telas com estados de carregando, vazio, erro e "Dados protegidos".
- [ ] Ajuda das três telas; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` nos campos de salário e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `JobPosition`, `WorkSchedule`, `LaborUnion`; `HourlyCostCalculator`.
- [ ] `IHrDb`, migration, seed da jornada, handler de `PayrollChargesChanged` com inbox.
- [ ] `IHourlyCost` em `HR.Contracts`; `IAuditWriter.RecordReadAsync` e o padrão `/sensitive`.
- [ ] Comandos separados com `UnmappedMemberHandling.Disallow`; filtro de ordenação sensível.
- [ ] Endpoints `/api/v1/hr/positions` (+ `/summary`, `/{id}/sensitive`, faixa, ajuste), `/schedules`, `/unions`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD, rotas sensíveis), @qa.
- Gate: CA 3 (fórmula), CA 6 (recálculo idempotente), CA 10 e 11 (salário fora da resposta e comandos separados) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** valores da tabela 1.1 nos CAs (salário de referência, horas mensais, custo-hora visível, seed); CAs de segurança da §2.5 (rota `/sensitive`, comandos separados, ordenação ignorada, histórico só com `hr.view_sensitive`, allowlist e canário); `IHourlyCost` só por cargo; contagem no diálogo da CAD-06; CA de encargos não configurados retirado (o seed garante 80,00%); verbos específicos; ordem 12 do Marco C.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-04) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, tabela 1.1) e CAs de segurança da revisão pré-Fase 1; ordem 12 do Marco C | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
