# RH-02a — Folha lançada: competência, rubricas, lançamento e importação dos valores da contadora

| Campo | Valor |
|---|---|
| ID | RH-02a |
| Título | Cadastro de rubricas (proventos, descontos e informativas), folha da competência no modo "lançada" (mensal, adiantamento, férias, 13º e rescisão), lançamento por funcionário e importação em CSV no layout padrão do sistema, bases de INSS, FGTS e IRRF e encargos da empresa informados, conferência com o mês anterior; valores só com `hr.view_sensitive` |
| Tamanho | G (quebra da RH-02 em 02a lançamento, 02b holerite e envio, 02c fechamento com CP e previsão de caixa) |
| Status | Draft |
| Requisitos | R-RH-5 (parte: importar/lançar proventos e descontos vindos da contadora) · R-RH-1 · PRD §6 LGPD · R-SEC-5 · R-SEC-10 · R-PRP-1 · R-PRP-3 |
| Dependências | CAD-04b (funcionários, salário), CAD-04c (adicionais, VT, dependentes, afastamentos), RH-01c (horas do período fechado, como referência), FND-09e; Fase 0 (FND-07) |
| Bloqueia | RH-02b, RH-02c, RH-03, FLH-02a (reaproveita rubricas, folha e holerite) |
| Ordem de execução | 4 (Marco 6.2 "Folha lançada") |

## User story
Como **RH**, quero **lançar ou importar, para cada funcionário, os proventos e descontos que a contadora calculou**, para **ter a folha do mês no sistema, conferida, pronta para gerar holerites e os pagamentos**.

## Contexto e referências
- PRD R-RH-5 ("Folha lançada: importar/lançar proventos e descontos vindos da contadora, gerar holerite PDF, enviar por e-mail, gerar CP de salários"); R-RH-6 é a folha calculada (Fase 7); R-RH-8 e P-06 (layout padrão do sistema em CSV/XLSX documentado, layouts adicionais depois como plug-ins).
- `03-modelo-dados` → `hr.payroll_runs` (`period`, `type` Monthly/Vacation/13th/Termination, `mode` Entered/Calculated, `status` Draft/Closed/Paid), `hr.payslips` (`gross`, `deductions`, `net`, `fgts_base`, `inss_base`, `irrf_base`), `hr.payslip_lines` (`code`, `description`, `reference`, `type` Earning/Deduction, `amount`).
- Backlog RH-02, CA "holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido)".
- `06` Riscos: "Folha calculada completa… responsabilidade legal" → por isso a Fase 6 só **lança** o que a contadora calculou; nenhum cálculo legal aqui.
- ADR 0016 (dado sensível por rota e comando, `SensitiveRead`, allowlist, canário), ADR 0019 §2, §3, §9 (concorrência, idempotência, arredondamento), MIG-01a (proteção contra injeção de fórmula em CSV).

## Critérios de aceite
**Rubricas**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe o cadastro de rubricas com a lista padrão proposta (Pontos em aberto 1), cada uma com código (até 10 caracteres, único), nome, tipo (Provento, Desconto ou Informativa) e as marcas informativas "Base do INSS", "Base do FGTS" e "Base do IRRF" (usadas pela Fase 7), sem recriar a que o tenant excluiu (`seed_key`, `IgnoreQueryFilters()`).
2. **Dado** `hr.edit`, **quando** crio, edito, inativo ou excluo uma rubrica (excluir só sem uso, D10), **então** a operação é auditada; rubrica em uso só inativa (409 com "Inativar").

**Folha da competência**
3. **Dado** `hr.create` + `hr.view_sensitive`, **quando** clico "Nova folha", escolho a competência (mês) e o tipo (Mensal, Adiantamento, Férias, 13º 1ª parcela, 13º 2ª parcela, Rescisão; Pontos em aberto 2) e confirmo "Criar folha", **então** nasce a folha `Draft`, modo `Entered`, com um holerite por funcionário ativo na competência (admitido até o último dia e não desligado antes do primeiro; em Férias e Rescisão, eu escolho os funcionários); o comando exige `Idempotency-Key` e uma folha do mesmo tipo e competência não se repete (409 "Já existe a folha {tipo} de {mês}.").
4. **Dado** a folha nova, **quando** abro um holerite, **então** ele vem preenchido de forma propositiva: na Mensal, a linha "Salário" com o salário base vigente (CAD-04b) e a referência "30 dias", e a referência das horas do espelho fechado (RH-01c: extras, faltas, atrasos e banco de horas) mostrada ao lado, só como informação; com "Copiar do mês anterior", as rubricas e valores do holerite anterior do mesmo tipo entram como rascunho editável.
5. **Dado** um holerite `Draft`, **quando** incluo, altero ou removo linhas (rubrica, referência em texto até 20 caracteres, valor `numeric(18,2)` > 0) e informo as bases do INSS, do FGTS e do IRRF e o FGTS do mês, **então** os totais (proventos, descontos e líquido = proventos − descontos) são recalculados ao vivo e no servidor com as mesmas regras (ADR 0019 §9); líquido negativo responde 422 "O líquido não pode ser negativo."; uma linha informativa não entra no líquido.
6. **Dado** a folha, **quando** abro a seção "Encargos da empresa", **então** informo os totais da competência que a contadora calculou (FGTS a recolher, contribuições previdenciárias, IRRF retido a recolher e "outros", cada um com descrição e vencimento; Pontos em aberto 3), que a RH-02c usa para gerar os títulos a pagar; a soma do FGTS dos holerites é mostrada ao lado do FGTS informado, com o aviso âmbar quando diferem.

**Importação**
7. **Dado** `hr.edit` + `hr.view_sensitive` e um arquivo CSV no **layout padrão do sistema** (proposta, documentado na ajuda: `matricula_ou_cpf;codigo_rubrica;referencia;valor`, separador `;`, decimal com vírgula, UTF-8; Pontos em aberto 4), **quando** envio o arquivo em "Importar lançamentos", **então** o sistema mostra a prévia antes de gravar: funcionários encontrados, linhas por rubrica, totais e a lista de erros por linha ("Linha 12: rubrica 'HE75' não existe.", "Linha 30: CPF não pertence a funcionário ativo."); só com zero erro o botão "Importar {n} lançamentos" fica habilitado.
8. **Dado** a prévia sem erros, **quando** confirmo "Importar", **então** as linhas dos funcionários presentes no arquivo substituem as linhas desses holerites, numa transação, e os holerites ausentes do arquivo ficam como estão; o diálogo avisa antes "Substitui os lançamentos de {n} funcionários."; o arquivo não é guardado depois da importação (proposta: só o resumo na auditoria).
9. **[Segurança]** **Dado** o arquivo enviado, **quando** é lido, **então** tem limite de 2 MB e 5 mil linhas, passa pela mesma proteção de entrada da MIG-01a (célula que começa com `=`, `+`, `-` ou `@` é tratada como texto), nunca é executado e o CPF do arquivo é usado só para achar o funcionário (não vai para log nem para a mensagem de erro, que mostra só a linha).

**Conferência**
10. **Dado** a folha, **quando** abro a aba "Conferência", **então** vejo os totais por rubrica, o total de proventos, descontos e líquido, o número de holerites, e, por funcionário, a variação do líquido contra o mesmo tipo do mês anterior, com o chip alerta acima de 10% (proposta) e a lista "Sem lançamento" (holerite vazio).

**LGPD e segurança**
11. **[Segurança]** **Dado** `hr.view` sem `hr.view_sensitive`, **quando** abro RH → Folha, **então** vejo só competência, tipo, situação, número de holerites e quem fechou; nenhum valor, base, rubrica com valor ou líquido existe no JSON (rota de resumo × rota `/sensitive`, ADR 0016); a leitura do holerite e da conferência registra `SensitiveRead` e responde com `no-store`.
12. **[Segurança]** **Dado** os testes de allowlist e de canário, **quando** rodam, **então** valores de linha, bases e líquido são `[PersonalData]` e não aparecem em `audit.audit_log` (só mascarados) nem no log.
13. **Dado** uma folha `Closed` (RH-02c), **quando** tento alterar linha, base, encargo ou importar, **então** 409 "A folha de {mês} está fechada."
14. **[Segurança]** **Dado** dois usuários editando o mesmo holerite, **quando** o segundo salva, **então** 409 `concurrency_conflict` com "Recarregar" (`xmin`).

**Permissões**
15. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; criar folha com `hr.create` + `hr.view_sensitive`, editar e importar com `hr.edit` + `hr.view_sensitive`, rubricas com `hr.edit`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/payroll-runs/{id}/payslips/{payslipId}`.

## Regras de negócio e técnicas
- **Nada de cálculo legal** nesta story: todo valor é digitado ou importado da contadora. A Fase 7 reaproveita as mesmas tabelas com `mode = Calculated`.
- **Tabelas (propostas sobre o 03):** `hr.payroll_items` (rubricas: `code` único, `name`, `kind` Earning/Deduction/Information, `inss_base`, `fgts_base`, `irrf_base`, `is_active`, `seed_key`, soft delete); `hr.payroll_runs` (+ `number` opcional, `xmin`, `closed_at`, `closed_by`); `hr.payslips` (+ `fgts_amount`, `xmin`); `hr.payslip_lines` (+ `payroll_item_id`, `type` ganha `Information`); `hr.payroll_run_charges` (encargos da empresa: `kind` Fgts/Social/Irrf/Other, `description`, `amount`, `due_date`).
- **Dinheiro** `numeric(18,2)`, `AwayFromZero`; totais somam as linhas.
- **Funcionários da folha:** pela situação e pelas datas de admissão e desligamento; o afastado entra na Mensal (o que ele recebe vem da contadora).
- **Rubricas no `ModuleCatalog`:** nenhuma ação nova.
- Guid v7; `IAuditable` em rubrica, folha, holerite e linha.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view` (resumo), `hr.view_sensitive` (valores), `hr.create`, `hr.edit`.
- **Eventos:** nenhum nesta story (o fechamento é da RH-02c).
- **Tabelas:** `hr.payroll_items`, `hr.payroll_runs`, `hr.payslips`, `hr.payslip_lines`, `hr.payroll_run_charges` (propostas).

## Telas e estados
- **RH → Folha** (`/app/rh/folha`): lista de folhas (competência, tipo, situação, holerites; com `hr.view_sensitive`, o líquido total).
- **Folha** (`/app/rh/folha/$id`), abas na URL: Holerites, Encargos da empresa, Conferência, Histórico de alterações.
- **Holerite** em painel lateral (desktop) ou tela cheia (400 px), com o `RowsEditor` de linhas e o rodapé fixo de totais anunciado em `aria-live`.
- **RH → Rubricas** (`/app/rh/rubricas`): cadastro simples em painel lateral.
- **Verbos:** "Criar folha" / "Folha criada"; "Salvar holerite" / "Holerite salvo"; "Importar lançamentos" / "{n} lançamentos importados"; "Copiar do mês anterior".
- **Estados:** carregando; vazio ("Nenhuma folha ainda. Crie a folha do mês para lançar os valores da contadora." + "Nova folha"); prévia com erros; erro; "Dados protegidos" sem `hr.view_sensitive`; folha fechada em modo leitura.
- **Propositivo:** holerite preenchido com o salário e com o mês anterior; horas do espelho ao lado; aviso de variação.
- **400 px e teclado:** E2E do holerite com o editor de linhas.
- **Ajuda:** `Ajuda/rh-folha.md`, `Ajuda/rh-folha-detalhe.md` (uma por aba: `holerites`, `encargos`, `conferencia`, `historico`), `Ajuda/rh-rubricas.md`, com o layout do CSV descrito; `<Dica>` em "Base do INSS", "Informativa" e "Folha lançada".

## Plano de testes
- **Unidade:** totais e líquido (CA 5); leitura do CSV com casos de erro e de injeção (CA 7, 9); funcionários da competência (CA 3).
- **Integração (Testcontainers):** seed idempotente (CA 1); rubrica em uso (CA 2); criação com `Idempotency-Key` e duplicidade (CA 3); preenchimento e cópia (CA 4); encargos (CA 6); prévia e substituição (CA 7, 8); limites do arquivo e CPF fora do log (CA 9); conferência (CA 10); JSON sem valor com `hr.view` (CA 11); canário (CA 12); folha fechada (CA 13); concorrência (CA 14); três camadas e IDOR (CA 15).
- **Arquitetura:** DTOs de resumo e lista da folha sem propriedade de valor.
- **E2E (Playwright):** criar a folha, importar o CSV de exemplo, conferir os totais; usuário só com `hr.view` não recebe valores na rede.

## Fora do escopo
- Holerite em PDF e envio: RH-02b. Fechamento, títulos a pagar e previsão de caixa: RH-02c.
- Qualquer cálculo de INSS, IRRF, FGTS, férias, 13º ou rescisão: Fase 7.
- Leitura de PDF do holerite da contadora: fora do MVP.

## Pontos em aberto
1. **Lista padrão de rubricas** (contadora): proposta Salário, Horas extras 50%, Horas extras 100%, DSR, Adicional de insalubridade, Adicional de periculosidade, Adicional noturno, Férias, 1/3 de férias, 13º salário, Salário-família, Faltas, Atrasos, INSS, IRRF, Vale-transporte, Adiantamento, Pensão alimentícia, FGTS (informativa). Os códigos seguem os da contadora, se ela tiver.
2. **Tipos de folha lançada** (contadora): os seis da proposta, ou só Mensal e 13º no MVP (férias e rescisão lançadas dentro da Mensal).
3. **Encargos da empresa** (contadora): quais guias entram (FGTS, contribuições previdenciárias, IRRF, contribuição sindical, outros), com que vencimento padrão e se um título por guia.
4. **Layout de importação** (PO + contadora): o layout padrão proposto (P-06) ou o arquivo que o sistema da contadora exporta (vira um plug-in de importação).
5. Limite do alerta de variação (10%, proposta).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); seed das rubricas idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive "Dados protegidos".
- [ ] Ajuda da lista, de cada aba e do layout do CSV; teste de cobertura passando.
- [ ] Padrões propositivos aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PayrollItem`, `PayrollRun`, `Payslip`, `PayslipLine`, `PayrollRunCharge` (Domain).
- [ ] Leitor do CSV com prévia (sem gravar) e aplicação; proteção de entrada.
- [ ] Endpoints `/api/v1/hr/payroll-items`, `/api/v1/hr/payroll-runs` (+ `/sensitive`, holerites, importação, conferência); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (corte de valores, CSV), @qa.
- Gate: CA 5, 8, 9, 11 e 12 obrigatórios.

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

- **Correções feitas na validação:** quebra da RH-02 (G maior que G) em 02a, 02b e 02c; nenhum cálculo legal na folha lançada; valores só por rota sensível (CA 11); importação com prévia e sem guardar o arquivo (CA 7, 8); encargos da empresa digitados para a RH-02c (CA 6).
- **Pré-condições para `Ready`:** pontos em aberto 1, 3 e 4; CAD-04c concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
