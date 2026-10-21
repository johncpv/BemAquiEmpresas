# FLH-01 — Folha calculada: tabelas legais por vigência (INSS, IRRF, salário-família e parâmetros)

| Campo | Valor |
|---|---|
| ID | FLH-01 |
| Título | Tabelas e parâmetros legais da folha versionados por vigência, nunca fixos no código: INSS do empregado (faixas progressivas e teto), IRRF (faixas, parcela a deduzir, dedução por dependente, desconto simplificado e redução adicional quando a norma prevê), salário-família (limite e cota), salário mínimo, alíquotas de FGTS e parâmetros de encargo; cadastro pelo RH com conferência da contadora, histórico imutável e leitura "pela data" para o motor de cálculo |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-RH-6 ("tabelas parametrizadas por vigência") · R-SEC-5 · PRD §6 (Riscos: folha calculada com responsabilidade legal) |
| Dependências | Fase 6 concluída (RH-02a: rubricas e folha); FND-09e |
| Bloqueia | FLH-02a, FLH-02b, FLH-03, FLH-04, FLH-05 |
| Ordem de execução | 1 (Marco 7.1 "Tabelas e cálculo mensal") |

## User story
Como **RH**, quero **cadastrar as tabelas de INSS, IRRF e salário-família com a data em que passam a valer**, para **o cálculo da folha usar sempre a tabela certa de cada competência, inclusive quando o governo muda os valores no meio do ano**.

## Contexto e referências
- PRD R-RH-6 ("Folha calculada: INSS, IRRF, FGTS, horas extras e DSR a partir do ponto, férias, 13º, rescisão; tabelas parametrizadas por vigência"), P, Fase 7.
- `03-modelo-dados` → `hr.payroll_tax_tables` (`type` INSS/IRRF/SalarioFamilia, `valid_from`, `brackets` JSON).
- `06` Riscos: "Folha calculada completa (INSS, IRRF, férias, 13º, rescisão, mudanças de tabela) é complexa e com responsabilidade legal" → mitigação "3 meses de conferência paralela com a contadora" (FLH-06).
- **Regra desta fase:** as regras legais brasileiras mudam todo ano; **nenhum valor legal fica no código** nem no seed como verdade: o seed, se houver, é marcado "a conferir" e só vale depois da conferência da contadora (Pontos em aberto 1). Exemplo de mudança estrutural: a tabela do IRRF de 2026 passou a ter uma redução adicional do imposto para rendimentos até um limite; o modelo precisa aceitar componentes assim sem mudar código (Pontos em aberto 2).

## Critérios de aceite
**Cadastro por vigência**
1. **Dado** `hr.edit` + `hr.view_sensitive` (proposta), **quando** abro RH → Tabelas da folha e crio uma versão de INSS com "vale a partir de" (primeiro dia do mês, `date`), as faixas (limite superior de cada faixa e alíquota `numeric(7,4)`; a última com o teto do salário de contribuição) e a fonte (texto: norma e data de publicação, obrigatório), e clico "Salvar tabela", **então** ela fica `Draft` até ser conferida (CA 4).
2. **Dado** a versão de IRRF, **quando** cadastro, **então** informo as faixas (limite, alíquota, parcela a deduzir), a dedução por dependente, o desconto simplificado mensal (valor) e, quando a norma prevê, a **redução adicional** como faixas próprias (limite de rendimento, fórmula de redução por parâmetros: valor fixo, percentual sobre a diferença; Pontos em aberto 2); tudo em `numeric(18,2)` e `numeric(7,4)`.
3. **Dado** as demais tabelas, **quando** cadastro, **então** existem os tipos: Salário-família (remuneração máxima e valor da cota por filho), Salário mínimo (valor), FGTS (alíquota mensal e a da rescisão, com a variação do aprendiz), Vale-transporte (percentual máximo de desconto) e Encargos da empresa (parâmetros que dependem do regime, ver ponto em aberto 3), cada um com vigência e fonte.
4. **Dado** uma versão `Draft`, **quando** um segundo usuário com `hr.approve` (proposta: segregação, quem cadastra não confere) clica "Conferir e ativar" e informa "Conferido com {contadora} em {data}", **então** a versão fica `Active` e passa a valer para as competências a partir de "vale a partir de"; quem cadastrou não confere a própria versão (403), salvo a exceção do Proprietário único (Pontos em aberto 4).
5. **Dado** uma versão `Active`, **quando** alguém tenta alterar faixa ou valor, **então** não pode (409 "Tabela ativa não muda. Crie uma nova versão com a vigência certa."); uma versão `Active` só é substituída por outra com vigência posterior, ou "Retificada" por uma nova versão com a **mesma** vigência, que exige motivo e marca as folhas já calculadas com ela (CA 9).

**Leitura pelo cálculo**
6. **Dado** o motor de cálculo (FLH-02a), **quando** pede a tabela de um tipo para uma competência, **então** recebe a versão `Active` de maior "vale a partir de" ≤ primeiro dia da competência (e a retificação mais recente dessa vigência); sem versão, o cálculo da competência é recusado com 422 "Falta a tabela {tipo} para {mês}." e o link para cadastrar.
7. **Dado** o dataset de teste com duas versões de INSS (uma de janeiro, uma de maio do mesmo ano), **quando** consulto as competências de abril e de junho, **então** recebo cada uma a sua; teste de propriedade: para qualquer data, no máximo uma versão vigente por tipo.
8. **Dado** a validação da tabela, **quando** salvo, **então** o servidor confere: faixas crescentes, sem lacuna nem sobreposição, alíquotas entre 0 e 100%, teto ≥ última faixa, parcela a deduzir coerente com a continuidade do IRRF (aviso âmbar quando a fórmula não é contínua na fronteira, sem bloquear).

**Retificação e auditoria**
9. **Dado** uma retificação de versão já usada, **quando** ativada, **então** as folhas calculadas (FLH-02a) com a versão anterior ganham a marca "Tabela retificada depois do cálculo" e o `NextSteps` da folha oferece "Recalcular" (se ainda em rascunho) ou "Ver diferença" (se fechada); nada é recalculado sozinho.
10. **Dado** qualquer versão, **quando** abro o histórico, **então** vejo quem cadastrou, quem conferiu, a fonte, a data e o conteúdo completo (tabela legal não é dado pessoal); a lista mostra a versão vigente hoje e as futuras com o chip "A partir de {data}".

**Permissões**
11. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; ver as tabelas com `hr.view`, cadastrar com `hr.edit` + `hr.view_sensitive` (proposta), conferir com `hr.approve`; sem a ação, 403 `forbidden`. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Tabela:** `hr.payroll_tax_tables` (03) + `kind` (Inss, Irrf, FamilyAllowance, MinimumWage, Fgts, TransportVoucher, EmployerCharges), `valid_from` (`date`), `status` (`Draft`/`Active`/`Superseded`), `rectifies_id`, `content` (jsonb validado por esquema por tipo, com versão do esquema), `source`, `created_by`, `checked_by`, `checked_note`, `checked_at`.
- **Esquema por tipo** versionado no código (a forma da tabela muda pouco); os **valores** só no banco, por tenant. Proposta: o catálogo de plataforma pode publicar uma "tabela sugerida" que cada tenant importa e confere (Pontos em aberto 5).
- **Leitura** pelo `IPayrollTaxTables.ForCompetenceAsync(kind, competence)` dentro do HR.
- **Arredondamento:** cada imposto arredonda no resultado (2 casas, `AwayFromZero`), conforme a regra decidida com a contadora (Pontos em aberto 6).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit` + `hr.view_sensitive` (proposta), `hr.approve` (conferir).
- **Eventos:** `PayrollTaxTableActivated` (`{ kind, validFrom }`), dentro do HR.
- **Tabelas:** `hr.payroll_tax_tables` (extensão).

## Telas e estados
- **RH → Tabelas da folha** (`/app/rh/tabelas-folha`): uma seção por tipo, com a vigente, as futuras e as antigas; editor de faixas com o `RowsEditor`; "Conferir e ativar"; simulador "Base R$ {valor} → INSS {valor}, IRRF {valor}" para conferir com a contadora (propositivo).
- **Estados:** carregando; vazio ("Cadastre as tabelas vigentes antes de calcular a folha. Confira cada valor com a contadora."); erro; `Draft` com o chip "A conferir".
- **400 px:** faixas em cartões.
- **Ajuda:** `Ajuda/rh-tabelas-folha.md`; `<Dica>` em "Vale a partir de", "Retificar" e "Redução adicional".

## Plano de testes
- **Unidade:** validação das faixas (CA 8); escolha da versão por competência e propriedade (CA 6, 7); simulador com casos fornecidos pela contadora (Pontos em aberto 1).
- **Integração (Testcontainers):** cadastro, conferência com segregação e imutabilidade (CA 1–5); falta de tabela (CA 6); retificação marca as folhas (CA 9); histórico (CA 10); três camadas (CA 11).
- **E2E (Playwright):** cadastrar INSS e IRRF de uma vigência, conferir com outro usuário e simular.

## Fora do escopo
- Cálculo da folha: FLH-02a em diante.
- Atualização automática das tabelas por fonte oficial: fora do MVP.

## Pontos em aberto
1. **Valores iniciais** (pré-condição, contadora): as tabelas vigentes na competência da virada, conferidas e assinadas pela contadora, com casos de teste (base → imposto) para o simulador.
2. **Formato da redução adicional do IRRF** (contadora + arquiteto): conferir a norma vigente e se o esquema proposto (faixas de redução com valor fixo e percentual) cobre o cálculo; se não, novo esquema com ADR.
3. **Encargos da empresa** (contadora, depende da P-01, regime tributário em aberto): contribuição patronal, RAT/FAP e terceiros mudam com o regime; no Simples parte deles não existe.
4. Segregação na conferência (proposta) e a exceção do Proprietário único.
5. Tabela sugerida pela plataforma para todos os tenants (super admin) ou cada tenant cadastra a sua.
6. Regras de arredondamento de cada imposto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (com os casos da contadora), integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva); nenhum valor legal no código.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados; ajuda; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Esquemas por tipo e validação; `IPayrollTaxTables`; endpoints `/api/v1/hr/payroll-tax-tables`; tela, simulador e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora (externa).
- Gate: CA 5, 6, 7 e 9 obrigatórios; nenhum número legal em código (revisão).

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

- **Correções feitas na validação:** nenhum valor legal no código nem no seed sem conferência (CA 1, 4); tabela ativa imutável, com retificação explícita (CA 5, 9); esquema aberto para mudança estrutural do IRRF (ponto 2).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; Fase 6 concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
