# FLH-01 — Folha calculada: tabelas legais por vigência (INSS, IRRF, salário-família e parâmetros)

| Campo | Valor |
|---|---|
| ID | FLH-01 |
| Título | Tabelas e parâmetros legais da folha versionados por vigência, nunca fixos no código: INSS do empregado (faixas progressivas e teto), IRRF (faixas, parcela a deduzir, dedução por dependente, desconto simplificado e redução adicional da norma vigente), salário-família (limite e cota), salário mínimo, FGTS, vale-transporte e encargos da empresa (Lucro Presumido, P-01); pré-cadastradas pela legislação vigente com o chip "A confirmar com a contadora", editáveis até a conferência, imutáveis depois; histórico e leitura "pela data" para o motor |
| Tamanho | M |
| Status | Ready (pré-condição externa) |
| Requisitos | R-RH-6 ("tabelas parametrizadas por vigência") · R-SEC-5 · PRD §6 (Riscos: folha calculada com responsabilidade legal) |
| Dependências | Fase 6 concluída (ordem das fases); RH-02a (rubricas e folha), RH-02c (fechamento, onde entra o aviso do CA 12), CAD-06 (Configurações → Parâmetros, ADR 0016 M6), FND-09e |
| Bloqueia | FLH-02a, FLH-02b, FLH-03, FLH-04, FLH-05 |
| Ordem de execução | 1 (Marco 7.1 "Tabelas e cálculo mensal") |

## User story
Como **RH**, quero **ter as tabelas de INSS, IRRF e salário-família já cadastradas com a data em que passam a valer, conferir cada uma com a contadora e cadastrar as novas quando o governo muda**, para **o cálculo da folha usar sempre a tabela certa de cada competência, inclusive quando a mudança é no meio do ano**.

## Contexto e referências
- PRD R-RH-6 ("Folha calculada: INSS, IRRF, FGTS, horas extras e DSR a partir do ponto, férias, 13º, rescisão; tabelas parametrizadas por vigência"), Fase 7.
- `03-modelo-dados` → `hr.payroll_tax_tables` (`type` INSS/IRRF/SalarioFamilia, `valid_from`, `brackets` JSON), estendida aqui.
- `06` Riscos: "Folha calculada completa (INSS, IRRF, férias, 13º, rescisão, mudanças de tabela) é complexa e com responsabilidade legal" → mitigação "3 meses de conferência paralela com a contadora" (FLH-06).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (Fase 7 entra no planejamento, antes do go-live); tabela 3.4, pontos 1 a 6 e linha "Determinismo"; §4.3 FLH-01/02 (parâmetros mudados com `settings.edit` avisados no fechamento); §4.4 padrão 7.
- **ADR 0039** (`docs/adr/0039-motor-folha-calculada.md`): vigência, determinismo (`calculator_version`, ids das tabelas e retrato das entradas por holerite), esquema por tipo versionado no código, dados sintéticos no CI.
- **ADR 0042** (`docs/adr/0042-configuracoes-fiscais-e-integracoes.md`): valores pré-configurados com o chip "A confirmar com a contadora" e a ação "Marcar como conferido", com quem e quando auditado.
- ADR 0016 M6 (parâmetros no schema do módulo dono, tela em Configurações → Parâmetros com `settings.edit`); ADR 0008 (Proprietário).
- **Regra desta fase:** as regras legais brasileiras mudam todo ano. Nenhum valor legal fica no código C#: os valores vêm de um **pacote de dados versionado** (um arquivo por ano, com a fonte de cada tabela), aplicado por seed patch em cada tenant como versões "A confirmar com a contadora"; só valem como oficiais depois da conferência. O CI usa **dados sintéticos** tirados da legislação vigente (funcionários fictícios com o resultado calculado à mão e revisado); **nunca holerites reais no Git** (ADR 0039).

## Critérios de aceite
**Tabelas pré-cadastradas e cadastro por vigência**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `hr-payroll-tables-{ano}` (ADR 0016) roda, **então** cada tabela do pacote do ano (INSS, IRRF, Salário-família, Salário mínimo, FGTS, Vale-transporte e Encargos da empresa) é criada uma vez, com "vale a partir de" e a fonte (norma e data de publicação), na situação `Unconfirmed` com o chip **"A confirmar com a contadora"**; rodar de novo não duplica, e uma versão que o tenant já conferiu ou editou nunca é sobrescrita.
2. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** abro RH → Tabelas da folha e crio uma versão de INSS com "vale a partir de" (primeiro dia do mês, `date`), as faixas (limite superior e alíquota `numeric(7,4)`; a última com o teto do salário de contribuição) e a fonte (obrigatória), e clico "Salvar tabela", **então** ela nasce `Unconfirmed` com o chip "A confirmar com a contadora".
3. **Dado** a versão de IRRF, **quando** cadastro ou edito, **então** informo as faixas (limite, alíquota, parcela a deduzir), a dedução por dependente, o desconto simplificado mensal e a **redução adicional** no formato da norma vigente (faixas de rendimento com redução por valor fixo e por percentual sobre a diferença), conferido na norma no início da story; se a norma pedir uma forma que o esquema não cobre, a forma nova entra como nova versão do esquema no código, com nota na ADR 0039; valores em `numeric(18,2)` e `numeric(7,4)`.
4. **Dado** as demais tabelas, **quando** cadastro, **então** existem os tipos Salário-família (remuneração máxima e cota por filho), Salário mínimo (valor), FGTS (alíquota mensal, a da rescisão e a do aprendiz), Vale-transporte (percentual máximo de desconto) e Encargos da empresa, pré-cadastrado para **Lucro Presumido** (P-01): contribuição patronal 20%, RAT pelo CNAE preponderante (sugerido 3%) × FAP (sugerido 1,0000) e terceiros pelo FPAS (sugerido 5,8%), todos "A confirmar com a contadora"; cada tipo com vigência, fonte e a **regra de arredondamento** (CA 9).

**Conferência**
5. **Dado** uma versão `Unconfirmed`, **quando** um usuário com `hr.approve` que **não** criou nem editou a versão clica "Marcar como conferido" e informa "Conferido com {contadora} em {data}", **então** a versão fica `Active`, o chip sai e ficam gravados quem, quando e a nota; quem criou ou editou recebe 403 "Quem cadastrou a tabela não confere. Peça a outra pessoa com permissão de aprovar." (as versões do seed contam como criadas pelo sistema).
6. **Dado** o tenant com um único membro com `hr.approve` (Proprietário único), **quando** ele tenta conferir uma versão que ele mesmo editou, **então** só consegue depois de anexar o dataset de referência da contadora daquela vigência (casos base → imposto) e o simulador do CA 10 bater em todos os casos; a conferência fica registrada como "Conferência pelo Proprietário único, com dataset da contadora".
7. **Dado** uma versão `Unconfirmed`, **quando** edito faixa ou valor, **então** grava com auditoria (antes/depois), a versão continua `Unconfirmed` e as folhas em `Draft` calculadas com ela ganham a marca "Tabela alterada depois do cálculo" com "Recalcular" (CA 13); uma versão `Active` não muda (409 "Tabela conferida não muda. Crie uma nova versão com a vigência certa ou retifique.").

**Leitura pelo cálculo**
8. **Dado** o motor de cálculo (FLH-02a), **quando** pede a tabela de um tipo para uma competência, **então** recebe a versão `Unconfirmed` ou `Active`, não substituída, de maior "vale a partir de" ≤ primeiro dia da competência, com o id e a situação; sem versão, o cálculo da competência é recusado com 422 "Falta a tabela {tipo} para {mês}." e o link para cadastrar; o fechamento **oficial** da folha calculada (depois da virada da FLH-06) exige todas as versões usadas `Active` (FLH-02a CA 16).
9. **Dado** a validação da tabela, **quando** salvo, **então** o servidor confere: faixas crescentes, sem lacuna nem sobreposição, alíquotas entre 0 e 100%, teto ≥ última faixa, regra de arredondamento escolhida (padrão: 2 casas, `AwayFromZero`, no resultado de cada imposto; opções `ToEven` e truncar) e a continuidade do IRRF na fronteira das faixas (aviso âmbar quando não é contínua, sem bloquear).
10. **Dado** o simulador "Base R$ {valor} → INSS {valor}, IRRF {valor}" da tela, **quando** informo uma base, dependentes e a competência, **então** vejo o resultado com a memória (faixa, alíquota, parcela a deduzir, redução, arredondamento) pelas versões que o motor usaria; o dataset sintético do CI (ADR 0039) cobre o teste centavo a centavo de cada tipo, inclusive o arredondamento.
11. **Dado** o dataset de teste com duas versões de INSS (uma de janeiro, uma de maio do mesmo ano), **quando** consulto as competências de abril e de junho, **então** recebo cada uma a sua; teste de propriedade: para qualquer data, no máximo uma versão vigente por tipo.

**Mudanças depois do cálculo e auditoria**
12. **[Segurança]** **Dado** uma tabela criada, editada, conferida ou retificada, ou um parâmetro da folha (`hr.parameters`) alterado com `settings.edit`, depois do último fechamento de folha, **quando** alguém abre o diálogo de fechamento (RH-02c), **então** o diálogo lista, como aviso, cada mudança (o quê, antes → depois, quem, quando) e pede ciência antes de fechar; a lista vem da auditoria e não pode ser omitida.
13. **Dado** uma retificação de versão `Active` (nova versão com a **mesma** vigência e motivo obrigatório), **quando** criada, **então** nasce `Unconfirmed` e passa por nova conferência (CA 5); ao ser conferida, a anterior fica `Superseded`; as folhas em `Draft` calculadas com a anterior ganham "Tabela retificada depois do cálculo" com "Recalcular"; as **folhas fechadas nunca são recalculadas**: ganham a marca e "Ver diferença" (FLH-02a CA 14).
14. **Dado** qualquer versão, **quando** abro o histórico, **então** vejo quem cadastrou, quem editou, quem conferiu, a fonte, as datas e o conteúdo completo (tabela legal não é dado pessoal); a lista mostra a vigente hoje e as futuras com o chip "A partir de {data}".

**Permissões**
15. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela padrão; ver as tabelas com `hr.view`; cadastrar, editar e retificar com `hr.edit` + `hr.view_sensitive`; conferir com `hr.approve`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Tabela:** `hr.payroll_tax_tables` (03) + `kind` (Inss, Irrf, FamilyAllowance, MinimumWage, Fgts, TransportVoucher, EmployerCharges), `valid_from` (`date`), `status` (`Unconfirmed`/`Active`/`Superseded`), `rectifies_id`, `content` (jsonb validado pelo esquema do tipo, com `schema_version`), `rounding`, `source`, `seed_package` (ano do pacote, nulo quando cadastrada à mão), `created_by`, `edited_by`, `checked_by`, `checked_note`, `checked_at`, `checked_with_dataset_id`; `xmin`.
- **Esquema por tipo** versionado no código (a forma muda pouco); os **valores** só no banco, por tenant, vindos do pacote de dados do seed ou do cadastro (ADR 0039). A plataforma sugere (pacote do ano), o tenant recebe pelo seed patch e a contadora confere (ponto 5).
- **Leitura** pelo `IPayrollTaxTables.ForCompetenceAsync(kind, competence)` dentro do HR, devolvendo id, situação e conteúdo; o motor grava os ids usados em cada holerite (determinismo, ADR 0039).
- **Arredondamento** parametrizado por tipo, testado centavo a centavo (ponto 6).
- **Segregação:** quem cria ou edita não confere; exceção do Proprietário único só com dataset da contadora (CA 6).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`; `hr.edit` + `hr.view_sensitive` (cadastrar, editar, retificar); `hr.approve` (conferir). Nenhuma ação nova.
- **Eventos:** `PayrollTaxTableActivated` (`{ kind, validFrom, tableId }`), dentro do HR.
- **Tabelas:** `hr.payroll_tax_tables` (extensão); pacote de dados `hr-payroll-tables-{ano}` aplicado por seed patch (`identity.seed_patches`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (retificação com o impacto em lista: folhas em rascunho e fechadas afetadas).
- **RH → Tabelas da folha** (`/app/rh/tabelas-folha`): uma seção por tipo, com a vigente, as futuras e as antigas; chip "A confirmar com a contadora" em cada versão `Unconfirmed` e "Marcar como conferido" (ADR 0042); editor de faixas com o `RowsEditor`; simulador (CA 10).
- **Verbos:** "Salvar tabela" / "Tabela salva"; "Marcar como conferido" / "Tabela conferida"; "Retificar" / "Retificação criada".
- **Estados:** carregando; vazio (só num tenant sem o pacote: "Cadastre as tabelas vigentes antes de calcular a folha." + "Cadastrar tabela"); erro (o que houve e como resolver); faixa no topo "{n} tabelas a confirmar com a contadora" com o filtro.
- **Propositivo:** as tabelas do ano já vêm cadastradas; a nova versão abre preenchida com a vigente (só muda o que mudou); o simulador vem com a base do salário médio da empresa quando o usuário tem `hr.view_sensitive`.
- **400 px:** faixas em cartões; simulador em tela cheia.
- **Ajuda:** `Ajuda/rh-tabelas-folha.md` (prevista); `<Dica>` em "Vale a partir de", "A confirmar com a contadora", "Retificar", "Redução adicional" e "Arredondamento".

## Plano de testes
- **Unidade:** validação das faixas e do arredondamento (CA 9); escolha da versão por competência e propriedade (CA 8, 11); simulador com o dataset sintético (CA 10).
- **Integração (Testcontainers):** seed patch uma vez só e sem sobrescrever (CA 1); cadastro (CA 2–4); conferência com segregação e a exceção do Proprietário único (CA 5, 6); edição antes e imutabilidade depois (CA 7); falta de tabela e exigência de `Active` no fechamento oficial (CA 8); aviso de mudanças no fechamento (CA 12); retificação sem recalcular folha fechada (CA 13); histórico (CA 14); três camadas (CA 15).
- **E2E (Playwright):** conferir uma tabela do seed com outro usuário, criar a versão de maio e simular.

## Fora do escopo
- Cálculo da folha: FLH-02a em diante.
- Atualização automática das tabelas por fonte oficial: fora do MVP (o pacote do ano entra por deploy).
- eSocial, DCTFWeb e FGTS Digital: fora do produto (PRD §3.3).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 1–6 e linha "Determinismo"):
1. Valores iniciais: pré-cadastrados pela legislação vigente num pacote de dados por ano, com o chip "A confirmar com a contadora" até a conferência; o CI usa dados sintéticos, nunca holerites reais no Git (CA 1, 10; ADR 0039).
2. Redução adicional do IRRF no formato da norma vigente; esquema por tipo versionado no código (CA 3).
3. Encargos pré-cadastrados para Lucro Presumido (P-01), a confirmar (CA 4).
4. Quem cadastra não confere; o Proprietário único só confere a própria versão com o dataset da contadora (CA 5, 6).
5. A plataforma sugere a tabela (pacote do seed), o tenant recebe e a contadora confere (CA 1, 5).
6. Arredondamento por regra parametrizada por tipo, testado centavo a centavo (CA 9, 10).
7. Determinismo: ids das tabelas usados gravados no holerite; folha fechada nunca recalculada; retificação nasce `Unconfirmed` e passa por nova conferência (CA 8, 13).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); §4.3 FLH-01/02 (mudanças de tabela e de parâmetro listadas no fechamento, CA 12); novo requisito de Configurações (chip e "Marcar como conferido", ADR 0042).

**Pré-condição externa:** conferência dos valores pré-cadastrados pela contadora (o valor proposto já está cadastrado como "A confirmar com a contadora"; não bloqueia o `Ready` nem a simulação; o fechamento oficial só existe depois da virada da FLH-06).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (dataset sintético), integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva); seed patch `hr-payroll-tables-{ano}` com o pacote de dados e a fonte de cada tabela; nenhum valor legal no código C#.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados; ajuda prevista escrita; teste de cobertura passando.
- [ ] Auditoria ligada (antes/depois, conferência com quem e quando).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Esquemas por tipo e validação; `IPayrollTaxTables`; pacote de dados do ano e seed patch.
- [ ] Endpoints `/api/v1/hr/payroll-tax-tables` (cadastro, edição, conferência, retificação, simulação).
- [ ] Lista de mudanças desde o último fechamento para o diálogo da RH-02c (CA 12).
- [ ] Tela, simulador e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (esquema por tipo, determinismo), @qa, revisão da contadora (externa, nos valores).
- Gate: CA 1, 5, 7, 8, 12 e 13 obrigatórios; nenhum número legal em código C# (revisão).

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: RH-02a, RH-02c e CAD-06 explícitas) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** nenhum valor legal no código nem no seed sem conferência (CA 1, 4); tabela ativa imutável, com retificação explícita (CA 5, 9); esquema aberto para mudança estrutural do IRRF (ponto 2).
- **Correções da revalidação (21/10/2026):** tabela 3.4 pontos 1 a 6 decididos (pacote do ano pré-cadastrado "A confirmar com a contadora", CA 1; redução do IRRF pela norma vigente, CA 3; encargos do Lucro Presumido, CA 4; segregação e exceção do Proprietário único com dataset, CA 5, 6; arredondamento parametrizado, CA 9); situação `Unconfirmed` usável na simulação e editável até a conferência (CA 7, 8); aviso de mudanças de tabela e de parâmetro no fechamento (§4.3, CA 12); determinismo e retificação sem recalcular folha fechada (ADR 0039, CA 13); dados sintéticos no CI (CA 10); ADRs 0039 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência dos valores pela contadora (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, tabela 3.4 pontos 1–6 e "Determinismo", §4.3 FLH-01/02, §4.4 padrão 7, ADR 0042); nova ordem (Marco 7.1, ordem 1, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
