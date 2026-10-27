# RH-02a — Folha lançada: competência, rubricas, lançamento e importação dos valores da contadora

| Campo | Valor |
|---|---|
| ID | RH-02a |
| Título | Cadastro de rubricas (proventos, descontos e informativas) pré-cadastradas "A confirmar com a contadora", folha da competência no modo "lançada" (mensal, adiantamento, férias, 13º 1ª e 2ª parcela e rescisão), lançamento por funcionário com as horas do mês na referência, importação em CSV no layout padrão do sistema, bases de INSS, FGTS e IRRF, encargos da empresa por guia com órgão e vencimento padrão, conferência com o mês anterior; valores só com `hr.view_sensitive`; parâmetros do RH em Configurações → Parâmetros → RH |
| Tamanho | G (quebra da RH-02 em 02a lançamento, 02b holerite e envio, 02c fechamento com CP e previsão de caixa) |
| Status | Done (pré-condição externa: conferência da contadora) |
| Requisitos | R-RH-5 (parte: importar/lançar proventos e descontos vindos da contadora) · R-RH-1 · PRD §6 LGPD · R-SEC-5 · R-SEC-10 · R-PRP-1 · R-PRP-3 |
| Dependências | CAD-04b (funcionários, salário), CAD-04c1 (afastamentos e situação), CAD-04c2 (adicionais, vale-transporte, dependentes, conta), FND-09e; Fase 0 (FND-07) |
| Bloqueia | RH-02c, RH-02b, RH-03, RH-01c (horas do espelho fechado na folha), FLH-02a (reaproveita rubricas, folha e holerite) |
| Ordem de execução | 2 (Marco 6.2 "Folha lançada") |

## User story
Como **RH**, quero **lançar ou importar, para cada funcionário, os proventos e descontos que a contadora calculou**, para **ter a folha do mês no sistema, conferida, pronta para gerar holerites e os pagamentos**.

## Contexto e referências
- PRD R-RH-5 ("Folha lançada: importar/lançar proventos e descontos vindos da contadora, gerar holerite PDF, enviar por e-mail, gerar CP de salários"); R-RH-6 é a folha calculada (Fase 7); R-RH-8 e P-06 (layout padrão do sistema em CSV documentado, layouts adicionais depois como plug-ins).
- `03-modelo-dados` → `hr.payroll_runs` (`period`, `type` Monthly/Vacation/13th/Termination, `mode` Entered/Calculated, `status` Draft/Closed/Paid), `hr.payslips` (`gross`, `deductions`, `net`, `fgts_base`, `inss_base`, `irrf_base`), `hr.payslip_lines` (`code`, `description`, `reference`, `type` Earning/Deduction, `amount`).
- Backlog RH-02a, CA "holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido)".
- `06` Riscos: "Folha calculada completa… responsabilidade legal" → por isso a Fase 6 só **lança** o que a contadora calculou; nenhum cálculo legal aqui.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D14 e D15 (a Fase 6 começa pela folha; as horas vêm lançadas ou importadas, e o espelho do ponto entra depois, pela RH-01c), tabela 3.3 pontos 15 a 19, §4.4 padrões 3 e 8; novo requisito "tudo configurável em Configurações" (valores pré-configurados com o chip "A confirmar com a contadora", ADR 0042).
- **ADR 0036** (`docs/adr/0036-folha-no-financeiro.md`: um título por guia, com o órgão como terceiro); **ADR 0042** (`docs/adr/0042-configuracoes-fiscais-e-integracoes.md`: parâmetros pré-configurados "a confirmar" e "Marcar como conferido").
- ADR 0016 (dado sensível por rota e comando, `SensitiveRead`, allowlist, canário; adendo M6, parâmetros do módulo dono), ADR 0019 §2, §3, §9 (concorrência, idempotência, arredondamento), MIG-01a (proteção contra injeção de fórmula em CSV).

## Critérios de aceite
**Rubricas**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe o cadastro de rubricas com a lista padrão (Salário, Horas extras 50%, Horas extras 100%, DSR, Adicional de insalubridade, Adicional de periculosidade, Adicional noturno, Férias, 1/3 de férias, 13º salário, Salário-família, Faltas, Atrasos, INSS, IRRF, Vale-transporte, Adiantamento, Pensão alimentícia e FGTS, esta informativa), cada uma com código (até 10 caracteres, único), nome, tipo (Provento, Desconto ou Informativa) e as marcas informativas "Base do INSS", "Base do FGTS" e "Base do IRRF" (usadas pela Fase 7), com o chip "A confirmar com a contadora" até alguém com `hr.edit` clicar "Marcar como conferido" (quem e quando auditados, ADR 0042), sem recriar a que o tenant excluiu (`seed_key`, `IgnoreQueryFilters()`); o código pode ser trocado pelo da contadora sem perder o histórico.
2. **Dado** `hr.edit`, **quando** crio, edito, inativo ou excluo uma rubrica (excluir só sem uso, D10), **então** a operação é auditada; rubrica em uso só inativa (409 com "Inativar").

**Parâmetros do RH**
3. **Dado** `settings.edit`, **quando** abro Configurações → Parâmetros → RH (`/app/configuracoes/parametros?secao=rh`; `hr.parameters`, dono HR, ADR 0016 M6, criada nesta story), **então** vejo, pré-configurados e editáveis: o limite do alerta de variação do líquido (`hr.payroll_net_variation_alert_pct` = 10%), o vencimento padrão de cada guia (FGTS, INSS, IRRF: dia 20 do mês seguinte; contribuição sindical e outros: informado na folha; dia não útil antecipa) e o favorecido padrão de cada guia (terceiro do `IParties`, vazio até alguém escolher); o dia útil vem do calendário padrão pelo `IBusinessCalendar` (novo em `Production.Contracts`, criado nesta story por ser o primeiro consumidor na nova ordem; a RH-02c e a RH-01c reusam); os valores legais ficam com o chip "A confirmar com a contadora" e a ação "Marcar como conferido"; cada mudança é auditada com antes e depois.

**Folha da competência**
4. **Dado** `hr.create` + `hr.view_sensitive`, **quando** clico "Nova folha", escolho a competência (mês) e o tipo (Mensal, Adiantamento, Férias, 13º 1ª parcela, 13º 2ª parcela ou Rescisão, cada um com vencimento próprio no caixa) e confirmo "Criar folha", **então** nasce a folha `Draft`, modo `Entered`, com um holerite por funcionário ativo na competência (admitido até o último dia e não desligado antes do primeiro; em Férias e Rescisão, eu escolho os funcionários); o comando exige `Idempotency-Key` e uma folha do mesmo tipo e competência não se repete (409 "Já existe a folha {tipo} de {mês}.").
5. **Dado** a folha nova, **quando** abro um holerite, **então** ele vem preenchido de forma propositiva: na Mensal, a linha "Salário" com o salário base vigente (CAD-04b) e a referência "30 dias"; as horas do mês (extras, faltas, atrasos, noturnas) entram na referência das rubricas, lançadas à mão ou pelo CSV (CA 8), enquanto o ponto não existir (o espelho fechado entra pela RH-01c); com "Copiar do mês anterior", as rubricas e valores do holerite anterior do mesmo tipo entram como rascunho editável.
6. **Dado** um holerite `Draft`, **quando** incluo, altero ou removo linhas (rubrica, referência em texto até 20 caracteres, valor `numeric(18,2)` > 0) e informo as bases do INSS, do FGTS e do IRRF e o FGTS do mês, **então** os totais (proventos, descontos e líquido = proventos − descontos) são recalculados ao vivo e no servidor com as mesmas regras (ADR 0019 §9); líquido negativo responde 422 "O líquido não pode ser negativo."; uma linha informativa não entra no líquido.
7. **Dado** a folha, **quando** abro a aba "Encargos da empresa", **então** informo as guias da competência que a contadora calculou, **uma linha por guia** (FGTS, INSS, IRRF retido, contribuição sindical e "Outros", com descrição, valor, vencimento preenchido pelo padrão do CA 3 e o favorecido, o órgão como terceiro, preenchido pelo padrão do CA 3, com o atalho "Cadastrar órgão" que abre o cadastro de terceiro já com o nome sugerido), que a RH-02c usa para gerar um título a pagar por guia; a soma do FGTS dos holerites é mostrada ao lado da guia do FGTS, com o aviso âmbar quando diferem.

**Importação**
8. **Dado** `hr.edit` + `hr.view_sensitive` e um arquivo CSV no **layout padrão do sistema** `v1` (documentado na ajuda: `matricula_ou_cpf;codigo_rubrica;referencia;valor`, separador `;`, decimal com vírgula, UTF-8), **quando** envio o arquivo em "Importar lançamentos", **então** o sistema mostra a prévia antes de gravar: funcionários encontrados, linhas por rubrica, totais e a lista de erros por linha ("Linha 12: rubrica 'HE75' não existe.", "Linha 30: CPF não pertence a funcionário ativo."); só com zero erro o botão "Importar {n} lançamentos" fica habilitado.
9. **Dado** a prévia sem erros, **quando** confirmo "Importar", **então** as linhas dos funcionários presentes no arquivo substituem as linhas desses holerites, numa transação, e os holerites ausentes do arquivo ficam como estão; o diálogo avisa antes "Substitui os lançamentos de {n} funcionários."; o arquivo não é guardado depois da importação (só o resumo, sem valores, na auditoria).
10. **[Segurança]** **Dado** o arquivo enviado, **quando** é lido, **então** tem limite de 2 MB e 5 mil linhas, passa pela mesma proteção de entrada da MIG-01a (célula que começa com `=`, `+`, `-` ou `@` é tratada como texto), nunca é executado e o CPF do arquivo é usado só para achar o funcionário (não vai para log nem para a mensagem de erro, que mostra só a linha).

**Conferência**
11. **Dado** a folha, **quando** abro a aba "Conferência", **então** vejo os totais por rubrica (cada total leva à lista dos holerites que o compõem, com o mesmo total), o total de proventos, descontos e líquido, o número de holerites, e, por funcionário, a variação do líquido contra o mesmo tipo do mês anterior, com o chip de alerta acima do limite do CA 3 (10%), a lista "Sem lançamento" (holerite vazio) e as listas "Novos na folha" (primeira folha do funcionário) e "Conta alterada" (conta em quarentena, `IEmployees`, CAD-04c2 CA 7), que o fechamento (RH-02c) repete.

**LGPD e segurança**
12. **[Segurança]** **Dado** `hr.view` sem `hr.view_sensitive`, **quando** abro RH → Folha, **então** vejo só competência, tipo, situação, número de holerites e quem fechou; nenhum valor, base, rubrica com valor ou líquido existe no JSON (rota de resumo × rota `/sensitive`, ADR 0016); a leitura do holerite e da conferência registra `SensitiveRead` e responde com `no-store`.
13. **[Segurança]** **Dado** os testes de allowlist e de canário, **quando** rodam, **então** valores de linha, bases e líquido são `[PersonalData]` e não aparecem em `audit.audit_log` (só mascarados) nem no log.
14. **Dado** uma folha `Closed` (RH-02c), **quando** tento alterar linha, base, encargo ou importar, **então** 409 "A folha de {mês} está fechada."
15. **[Segurança]** **Dado** dois usuários editando o mesmo holerite, **quando** o segundo salva, **então** 409 `concurrency_conflict` com "Recarregar" (`xmin`).

**Permissões**
16. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; criar folha com `hr.create` + `hr.view_sensitive`, editar e importar com `hr.edit` + `hr.view_sensitive`, rubricas com `hr.edit`, parâmetros com `settings.edit`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/payroll-runs/{id}/payslips/{payslipId}` e `/payroll-runs/{id}/charges/{chargeId}`; o favorecido da guia é validado pelo `IParties`.

## Regras de negócio e técnicas
- **Nada de cálculo legal** nesta story: todo valor é digitado ou importado da contadora. A Fase 7 reaproveita as mesmas tabelas com `mode = Calculated`.
- **Horas do mês:** na referência das rubricas (lançada ou importada); quando o ponto existir, a RH-01c passa a mostrar o espelho fechado ao lado e oferece "Usar horas do espelho" (documentado na ajuda).
- **Tabelas (sobre o 03):** `hr.payroll_items` (rubricas: `code` único, `name`, `kind` Earning/Deduction/Information, `inss_base`, `fgts_base`, `irrf_base`, `is_active`, `seed_key`, `confirmed_at`, `confirmed_by`, soft delete); `hr.payroll_runs` (+ `type` com Monthly/Advance/Vacation/Thirteenth1/Thirteenth2/Termination, `number` opcional, `xmin`, `closed_at`, `closed_by`); `hr.payslips` (+ `fgts_amount`, `xmin`); `hr.payslip_lines` (+ `payroll_item_id`, `type` ganha `Information`); `hr.payroll_run_charges` (guias: `kind` Fgts/Inss/Irrf/Union/Other, `description`, `amount`, `due_date`, `payee_party_id`); `hr.parameters` (chave, valor, `confirmed_at`, `confirmed_by`).
- **Dinheiro** `numeric(18,2)`, `AwayFromZero`; totais somam as linhas.
- **Funcionários da folha:** pela situação e pelas datas de admissão e desligamento; o afastado entra na Mensal (o que ele recebe vem da contadora).
- **Rubricas no `ModuleCatalog`:** nenhuma ação nova.
- Guid v7; `IAuditable` em rubrica, folha, holerite, linha, guia e parâmetro.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view` (resumo), `hr.view_sensitive` (valores), `hr.create`, `hr.edit`, `settings.edit` (parâmetros do RH).
- **Eventos:** nenhum nesta story (o fechamento é da RH-02c).
- **Contratos:** `IBusinessCalendar` em `Production.Contracts` (criado na DSH-01a, Fase 4; esta story acrescenta os métodos que faltarem: `IsBusinessDayAsync`, `NthBusinessDayOfMonthAsync`, `PreviousBusinessDayAsync`, `ExceptionsAsync(from, to)` do calendário padrão da CAD-03, sem o HR ler tabela do Production); usa `IEmployees` (situação, `BankAccountStatus`) e `IParties` (órgão da guia).
- **Tabelas:** `hr.payroll_items`, `hr.payroll_runs`, `hr.payslips`, `hr.payslip_lines`, `hr.payroll_run_charges`, `hr.parameters`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 3 (Conferência como tabela analítica: total por rubrica com drill-down de mesmo total), 8 (bloco "Dados protegidos" padrão).
- **RH → Folha** (`/app/rh/folha`): lista de folhas (competência, tipo, situação, holerites; com `hr.view_sensitive`, o líquido total).
- **Folha** (`/app/rh/folha/$id`), abas na URL: Holerites, Encargos da empresa, Conferência, Histórico de alterações.
- **Holerite** em painel lateral (desktop) ou tela cheia (400 px), com o `RowsEditor` de linhas e o rodapé fixo de totais anunciado em `aria-live`.
- **RH → Rubricas** (`/app/rh/rubricas`): cadastro simples em painel lateral, com o chip "A confirmar com a contadora" e "Marcar como conferido".
- **Configurações → Parâmetros → RH** (`/app/configuracoes/parametros?secao=rh`): seção nova, com os chips e "Marcar como conferido".
- **Verbos:** "Criar folha" / "Folha criada"; "Salvar holerite" / "Holerite salvo"; "Importar lançamentos" / "{n} lançamentos importados"; "Copiar do mês anterior"; "Marcar como conferido" / "Conferido".
- **Estados:** carregando; vazio ("Nenhuma folha ainda. Crie a folha do mês para lançar os valores da contadora." + "Nova folha"); primeira configuração dos parâmetros com os valores sugeridos; prévia com erros; erro; "Dados protegidos" sem `hr.view_sensitive`; folha fechada em modo leitura.
- **Propositivo:** holerite preenchido com o salário e com o mês anterior; vencimento e órgão da guia pelo padrão; aviso de variação; "Cadastrar órgão" com o nome sugerido.
- **400 px e teclado:** E2E do holerite com o editor de linhas; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/rh-folha.md`, `Ajuda/rh-folha-detalhe.md` (uma por aba: `holerites`, `encargos`, `conferencia`, `historico`), `Ajuda/rh-rubricas.md`, com o layout do CSV `v1` descrito, e a seção RH de `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Base do INSS", "Informativa", "Folha lançada" e "A confirmar com a contadora".

## Plano de testes
- **Unidade:** totais e líquido (CA 6); leitura do CSV com casos de erro e de injeção (CA 8, 10); funcionários da competência (CA 4); vencimento padrão da guia com dia não útil (CA 3).
- **Integração (Testcontainers):** seed idempotente com o chip e "Marcar como conferido" (CA 1); rubrica em uso (CA 2); parâmetros do RH com auditoria (CA 3); criação com `Idempotency-Key` e duplicidade, os seis tipos (CA 4); preenchimento e cópia (CA 5); guias com vencimento e órgão padrão (CA 7); prévia e substituição (CA 8, 9); limites do arquivo e CPF fora do log (CA 10); conferência com drill-down, novos na folha e conta alterada (CA 11); JSON sem valor com `hr.view` (CA 12); canário (CA 13); folha fechada (CA 14); concorrência (CA 15); três camadas e IDOR (CA 16).
- **Arquitetura:** DTOs de resumo e lista da folha sem propriedade de valor.
- **E2E (Playwright):** criar a folha, importar o CSV de exemplo, conferir os totais; usuário só com `hr.view` não recebe valores na rede.

## Fora do escopo
- Holerite em PDF e envio: RH-02b. Fechamento, títulos a pagar e previsão de caixa: RH-02c.
- Horas do espelho do ponto na folha: RH-01c.
- Qualquer cálculo de INSS, IRRF, FGTS, férias, 13º ou rescisão: Fase 7.
- Leitura de PDF do holerite da contadora: fora do MVP.
- Leitor do arquivo próprio da contadora (plug-in de importação): story nova quando o arquivo existir (P-06).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 15–19):
1. **Rubricas:** a lista padrão pré-cadastrada, com o chip "A confirmar com a contadora"; os códigos podem ser trocados pelos da contadora (CA 1).
2. **Tipos de folha:** os seis (Mensal, Adiantamento, Férias, 13º 1ª e 2ª parcela, Rescisão), com vencimentos diferentes no caixa (CA 4).
3. **Encargos:** um título por guia, com o órgão como terceiro e vencimento padrão pré-configurado em Configurações → Parâmetros → RH (CA 3, 7; ADR 0036).
4. **Layout de importação:** o layout padrão `v1` do sistema; se a contadora tiver um arquivo próprio, ele vira um leitor adicional depois (P-06) (CA 8).
5. **Alerta de variação do líquido:** 10%, como parâmetro (CA 3, 11); o aviso "novos na folha / conta alterada" aparece na conferência e no fechamento (CA 11; RH-02c).

Decisões da revisão aplicadas: D14 e D15 (folha antes do ponto: a RH-02a não depende mais da RH-01c, as horas vêm lançadas ou importadas, CA 5); novo requisito de Configurações (parâmetros pré-configurados "a confirmar", ADR 0042, CA 3); §4.4 padrões 3 e 8.

**Pré-condição externa:** a contadora confere a lista de rubricas e os códigos, os vencimentos padrão das guias e, se existir, manda o arquivo de exportação do sistema dela; até lá valem os valores pré-configurados com o chip "A confirmar com a contadora" (não bloqueia o `Ready`).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [x] Migration criada (não destrutiva); seed das rubricas e dos parâmetros idempotente.
- [x] Permissões só do `ModuleCatalog`; contrato `IBusinessCalendar` do Production (DSH-01a) reusado, com os métodos novos.
- [x] Telas com estados, inclusive "Dados protegidos" e a primeira configuração dos parâmetros.
- [x] Ajuda da lista, de cada aba, da seção de parâmetros e do layout do CSV; teste de cobertura passando.
- [x] Padrões propositivos aplicados.
- [x] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `PayrollItem`, `PayrollRun`, `Payslip`, `PayslipLine`, `PayrollRunCharge`, `HrParameters` (Domain).
- [x] Métodos que faltarem no `IBusinessCalendar` do Production (criado na DSH-01a; calendário padrão da CAD-03).
- [x] Leitor do CSV com prévia (sem gravar) e aplicação; proteção de entrada.
- [x] Endpoints `/api/v1/hr/payroll-items`, `/api/v1/hr/payroll-runs` (+ `/sensitive`, holerites, guias, importação, conferência), `/api/v1/hr/parameters`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (métodos novos do `IBusinessCalendar` do Production), cybersecurity (corte de valores, CSV), @qa.
- Gate: CA 6, 7, 9, 10, 12 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: sem a RH-01c, depois da CAD-04c2) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da RH-02 (G maior que G) em 02a, 02b e 02c; nenhum cálculo legal na folha lançada; valores só por rota sensível; importação com prévia e sem guardar o arquivo; encargos da empresa digitados para a RH-02c.
- **Correções da revalidação (21/10/2026):** pontos 15 a 19 decididos (rubricas pré-cadastradas "a confirmar", CA 1; seis tipos, CA 4; uma guia por linha com órgão e vencimento padrão, CA 7; layout `v1`, CA 8; alerta de 10% como parâmetro, CA 11); seção Configurações → Parâmetros → RH nova, com o `IBusinessCalendar` criado aqui (§4.2, antes previsto na RH-01c, que agora vem depois) (CA 3); horas lançadas ou importadas, sem depender da RH-01c (CA 5); "novos na folha / conta alterada" na conferência (CA 11); CAD-04c → CAD-04c1/CAD-04c2; ordem 2 do Marco 6.2; CAs renumerados (3 a 15 viraram 4 a 16).
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência da contadora (rubricas, vencimentos e arquivo próprio, se houver).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D14, D15, tabela 3.3 pontos 15–19, ADR 0042, §4.4 padrões 3 e 8); nova ordem (Marco 6.2, ordem 2, sem depender da RH-01c); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 27/10/2026 | 1.0 | Implementada (@dev). Backend: `PayrollItem`, `PayrollRun`, `Payslip`, `PayslipLine`, `PayrollRunCharge` e `HrParameter` (migration `PayrollEntered`, só `CreateTable` e índices), `PayrollItemService` (seed de 19 rubricas pela `seed_key`, "Marcar como conferido"), `HrParametersService` (`hr.parameters`), `PayrollService` (folha, holerites, guias, importação CSV v1 e conferência), `PayrollSeeder`, rotas `/api/v1/hr/payroll-items`, `/api/v1/hr/parameters` (+ `/payees`) e `/api/v1/hr/payroll-runs` (+ `/sensitive`, `/eligible`, holerites, anterior, guias, `import/preview`, `import`, `review/sensitive`); `IBusinessCalendar` ganhou `IsBusinessDayAsync`, `NthBusinessDayOfMonthAsync`, `PreviousBusinessDayAsync` e `ExceptionsAsync`; `IAuditWriter.RecordImportAsync` (ação `Imported`). Front: RH → Folha (lista, "Nova folha"), Folha com as abas Holerites (painel com `RowsEditor`, rodapé em `aria-live`, "Copiar do mês anterior", "Importar lançamentos" com prévia), Encargos da empresa, Conferência e Histórico, RH → Rubricas, seção RH em Configurações → Parâmetros (`?secao=rh`), `?nome=` no novo terceiro ("Cadastrar órgão"), ajudas e dicas. Desvios e decisões: (1) **recomendação aceita (PO 24/10)**: a quarentena da conta (CAD-04c2) passou a ler `hr.bank_account_quarantine_days` (padrão 30, de 1 a 90, sem chip: é decisão da empresa, não da contadora); a constante `EmployeeBankRules.QuarantineDays` ficou como padrão; (2) só os vencimentos do FGTS, do INSS e do IRRF levam o chip "A confirmar com a contadora" (alerta de 10%, órgãos e quarentena não dependem dela); contribuição sindical e "Outros" têm o vencimento informado na folha, como diz o CA 3; (3) as rotas de editar e conferir parâmetros ficam com `hr.view` e o `settings.edit` é conferido no serviço (o padrão de Compras e do DRE: o teste de três camadas do RH exige `perm:hr.*` em toda rota do grupo); (4) busca de órgão pela rota própria `/hr/parameters/payees` (`hr.view`, `IParties.SearchAsync`), para quem lança a folha não precisar da permissão de terceiros; (5) "Copiar do mês anterior" não grava: devolve o holerite anterior do mesmo tipo (rota `/previous/sensitive`, com `SensitiveRead`) e o front o põe como rascunho; (6) salário vigente na Mensal = o atual, ou o de antes do primeiro reajuste com vigência depois do fim da competência; (7) "Novos na folha" = sem holerite em folha de competência anterior (qualquer tipo); "Conta alterada" = `InQuarantine` do `IEmployees.BankAccountStatusAsync`; (8) o valor da guia também é `[PersonalData]` (numa empresa pequena revela a folha); (9) a prévia lista até 100 erros (o total em `errorCount`), em ordem de linha; erro de CPF nunca repete o CPF; código de rubrica no erro passa pelo `SafeCode`; (10) a importação substitui só as linhas (as bases e o FGTS do mês ficam como estão) e grava as linhas e o resumo `Imported` (formato, linhas e funcionários, sem valores) numa gravação só; (11) a tabela `hr.parameters` usa `confirmed_at`/`confirmed_by` (o 03 falava em `review_state`; a story pede as colunas de conferência); (12) excluir folha `Draft` criada por engano ficou fora (não está nos CAs). Verificações: `*PayrollRulesTests`, `*PayrollTests`, `*PersonalDataTests`, `*EndpointAuthorizationTests` 42/42; `*EmployeesTests`, `*HelpFilesTests`, `*SupervisorPanelTests`, `*HrTests` 45/46 (o `Tres_camadas_em_todo_endpoint_do_RH` pegou o ponto 3, corrigido) e depois `*HrTests`, `*PayrollTests`, `*OpenApiDocumentTests`, `*EndpointAuthorizationTests` 25/25; vitest 397/398 na suíte (o `tv.test.tsx` estourou o tempo com a máquina carregada) e 19/19 ao repetir TV, folha e ajuda; lint, tsc e build ok; E2E (folha, configurações, RH, terceiros e ajuda, desktop e 400 px) 37/38 e o `help.spec` repetido em série 2/2. | @dev |
