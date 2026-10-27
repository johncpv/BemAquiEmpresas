# BNK-03a — Extrato do banco: importação pela API do Inter e por arquivo OFX

| Campo | Valor |
|---|---|
| ID | BNK-03a |
| Título | Linhas do extrato do banco (`bank_statement_lines`) importadas automaticamente pela API do Inter (job diário às 06:30 e "Buscar agora") e por arquivo OFX de outro banco, sem duplicar, com saldo do banco conferido contra o saldo do sistema por dia; OFX bloqueado em conta com API, liberável por conta com `finance.approve` como contingência, com as linhas marcadas "não verificadas pelo banco" |
| Tamanho | M (quebra da BNK-03) |
| Status | Done |
| Requisitos | R-FIN-8 (extrato via API do Inter e importação OFX) · R-FIN-2 · R-FIN-10 (realizado) · R-PRP-3 · R-PRP-5 · R-SEC-5 |
| Dependências | BNK-01a (`InterApiClient`, `IBankStatementProvider`, `bank_production_allowed`), FIN-04 (extrato interno e saldo do sistema), FND-07 (anexos, varredura) |
| Bloqueia | BNK-03b (conciliação) |
| Ordem de execução | 5 (Marco 5.3 "Conciliação") |

## User story
Como **financeiro**, quero **o extrato do banco dentro do sistema todo dia, do Inter pela API e dos outros bancos por arquivo**, para **comparar com o que o sistema registrou sem baixar planilha nem digitar lançamento**.

## Contexto e referências
- PRD R-FIN-8 ("Conciliação bancária: extrato via API do Inter e importação OFX para outros bancos; sugestão automática de match"); `05-backlog` BNK-03.
- **ADR 0032** (Banco Inter: integração por conta; OFX bloqueado em conta com API), ADR 0009 adendo de 21/10/2026 (`ofx` na allowlist só do dono `BankStatementBatch`).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D13; tabela 3.2, pontos 28 a 31; §4.3 (BNK-03a/03b e transversal).
- `02-arquitetura` §6.2 (API de extrato; `IBankStatementProvider`).
- `03-modelo-dados` → `bank_statement_lines` (`bank_account_id`, `date`, `amount`, `description`, `external_id`, `source` (API/OFX), `matched_transaction_id`); `financial_transactions.reconciled` e `statement_line_id` (BNK-03).
- FIN-04 (extrato interno; 5 mil transações em < 1 s), FIN-00 (saldo na data de corte).
- ADR 0009 (anexos: tipos permitidos e magic bytes), revisão pré-Fase 0 §3 (parser de arquivo isolado e com tempo limite).

## Critérios de aceite
**Pela API do Inter**
1. **Dado** uma conta com integração Inter, **quando** o recorrente global `inter-statement-import` roda (**06:30** de São Paulo, um job por `tenantId` com integração ativa e módulo `finance` liberado) ou quem tem `finance.edit` clica "Buscar agora" (no máximo 1 vez a cada 5 min por conta), **então** o sistema busca o extrato desde a última linha importada (com 3 dias de sobreposição, e nunca antes da data de corte da FIN-00) pelo `IBankStatementProvider.GetAsync` e grava cada lançamento em `bank_statement_lines` com data, valor com sinal, descrição, tipo (Pix, boleto, TED, tarifa…), contraparte (nome e documento, `[PersonalData]`), `external_id` do banco e `source = Api`; conta em produção sem `bank_production_allowed` responde 409 `bank_production_not_allowed`.
2. **Dado** o mesmo lançamento devolvido de novo (sobreposição, nova execução, "Buscar agora" e job ao mesmo tempo), **quando** importa, **então** não duplica: índice único `(bank_account_id, source, external_id)`; o job tem teste de execução dupla e de dois tenants.
3. **Dado** o saldo do dia informado pelo banco, **quando** a importação termina, **então** grava o saldo do banco por dia (`bank_statement_balances`) e mostra "Saldo no banco R$ X · no sistema R$ Y · diferença R$ Z" na conta (FIN-04 dá o saldo do sistema); diferença ≠ 0 há mais de 2 dias gera notificação a quem tem `finance.edit` com link para a conciliação (`dedup_key = stmt-diff:{conta}:{data}`).

**Por arquivo OFX**
4. **[Segurança]** **Dado** `finance.edit` e uma conta bancária **sem** integração por API, **quando** envio um arquivo OFX (até 5 MB e até 20 mil transações), **então** o arquivo passa pela varredura de antivírus antes da leitura, é validado (extensão e magic bytes do dono `BankStatementBatch`, conteúdo OFX 1.x SGML ou 2.x XML, `DtdProcessing.Prohibit` e sem entidades externas, tempo limite de parse de 10 s), a conta do arquivo (`BANKID`/`ACCTID`) é conferida com a conta escolhida (diferente: 422 "Este extrato é da conta {final}, não da conta escolhida.") e as transações viram linhas com `source = Ofx` e `external_id = FITID`; o arquivo fica como anexo do lote.
5. **[Segurança]** **Dado** o mesmo arquivo já importado (mesmo SHA-256) ou um OFX que se sobrepõe a outro, **quando** importo, **então** o arquivo repetido é recusado inteiro ("Este arquivo já foi importado em dd/mm por {usuário}."), as linhas com o mesmo `FITID` na conta são ignoradas e o resumo mostra "{n} novas, {m} já importadas, {k} recusadas" com o motivo; `FITID` repetido dentro do mesmo arquivo recusa o arquivo; OFX sem `FITID` usa o hash de data + valor + descrição + ordem no arquivo (com aviso).
6. **[Segurança]** **Dado** uma conta com `provider = Inter`, **quando** envio OFX, **então** é recusado com 409 `ofx_blocked_api_account` "Esta conta recebe o extrato direto do Inter. O arquivo só entra como contingência." enquanto a contingência não estiver liberada; quem tem `finance.approve` pode "Liberar OFX nesta conta" (por conta, com motivo e prazo de até 30 dias, notificado aos membros do Proprietário e auditado); com a contingência liberada, as linhas do OFX entram com a marca **"não verificada pelo banco"** e as que coincidem com as da API pelo id do banco são ignoradas.
7. **[Segurança]** **Dado** qualquer importação de OFX, **quando** termina, **então** os membros do Proprietário recebem o aviso "{usuário} importou o extrato da conta {nome} ({n} linhas, período dd/mm a dd/mm)." com link para o lote.

**Consulta**
8. **Dado** `finance.view`, **quando** abro "Contas e extrato → {conta} → Extrato do banco", **então** vejo as linhas por data com descrição, contraparte, valor, origem (API/OFX, com o chip "Não verificada pelo banco" nas de contingência), situação (Pendente info, Conciliada ok, Ignorada neutro) e o saldo do banco por dia; a **contraparte aparece mascarada para quem só tem `finance.view`** (nome com a inicial e documento com os 3+2 dígitos) e inteira com `finance.edit`; filtros por período e situação na URL; 5 mil linhas em menos de 1 s.
9. **Dado** uma linha importada, **quando** existe, **então** nunca é editada nem apagada; uma linha errada é marcada "Ignorada" com motivo por quem tem `finance.edit` (linha de **débito** só com `finance.approve`, §4.3), e o lote inteiro pode ser desfeito com motivo enquanto nenhuma linha dele estiver conciliada ("Desfazer importação", no `ConfirmDialog` com o impacto em lista).

**Permissões e auditoria**
10. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; importar, buscar agora, ignorar crédito e desfazer sem `finance.edit`: 403; ignorar débito e liberar OFX em conta com API sem `finance.approve`: 403; `RequireModule` e `perm:`; IDOR em `/bank-accounts/{id}/statement-lines/{lineId}`.
11. **[Segurança]** **Dado** as linhas do extrato, **quando** saem pela API, **então** levam `Cache-Control: no-store`; o log da importação traz só conta, período, contagens e duração, nunca descrição, contraparte ou valores linha a linha.
12. **Dado** qualquer importação, liberação de contingência, ignorar ou desfazer, **quando** abro o histórico, **então** vejo lote, quem, quando, origem, hash do arquivo, contagens e o motivo.

## Regras de negócio e técnicas
- **`bank_statement_lines`:** `bank_account_id`, `batch_id`, `date`, `amount` (com sinal, `numeric(18,2)`), `description`, `kind`, `counterparty_name` e `counterparty_document` (`[PersonalData]`), `external_id`, `source` (`Api`/`Ofx`), `bank_verified` (falso nas de OFX de contingência), `status` (`Pending`/`Reconciled`/`Ignored`), `ignore_reason`, `matched_transaction_id` (BNK-03b), `imported_at`, `xmin`; único `(bank_account_id, source, external_id)`.
- **`bank_statement_batches`:** conta, origem, período, arquivo (anexo), `file_sha256` (único por conta), contagens, `imported_by`, `undone_at`, `undone_by`, `undo_reason`.
- **`bank_statement_balances`:** conta, data, saldo do banco, origem.
- **`bank_ofx_contingencies`:** conta, liberado por, motivo, `valid_until` (até 30 dias).
- **Data de corte:** nada antes da `finance.cutover_date` (FIN-00) entra.
- **Parser OFX** próprio e restrito (sem dependência de licença comercial), com limites de tamanho, de linhas e de tempo; o `ofx` entra na allowlist de anexos só do dono `BankStatementBatch` (adendo à ADR 0009).

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver, contraparte mascarada), `finance.edit` (importar, buscar, ignorar crédito, desfazer, contraparte inteira), `finance.approve` (ignorar débito, liberar OFX em conta com API). Nenhuma ação nova.
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `finance.bank_statement_lines` (com as colunas acima), `finance.bank_statement_batches`, `finance.bank_statement_balances`, `finance.bank_ofx_contingencies` (novas); dono de anexo `BankStatementBatch` (ofx, até 5 MB; leitura `finance.view`, escrita `finance.edit`).
- **Jobs:** recorrente global `inter-statement-import`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (busca assíncrona no Inter, `EnvironmentBanner` em sandbox), 7 (impacto em lista no "Desfazer importação").
- **Contas e extrato → conta → aba "Extrato do banco"** (`/app/financeiro/contas/$id?aba=extrato-banco`), com "Buscar agora" (Inter), "Importar OFX" (contas sem API) e "Liberar OFX nesta conta" (contas com API, só `finance.approve`).
- **Verbos:** "Importar extrato" / "Extrato importado"; "Buscar agora" / "Extrato atualizado"; "Liberar OFX nesta conta" / "OFX liberado até dd/mm"; "Ignorar linha" / "Linha ignorada"; "Desfazer importação" / "Importação desfeita".
- **Estados:** carregando; vazio ("Nenhuma linha do banco ainda. Conecte o Inter ou importe um arquivo OFX." + as duas ações, conforme a conta); erro com o motivo do arquivo; importação em andamento; contingência liberada com faixa âmbar "OFX liberado nesta conta até dd/mm. As linhas não são verificadas pelo banco."
- **Propositivo:** a conta e o período vêm preenchidos; depois de importar, o `NextSteps` oferece "Conciliar {n} linhas pendentes" (BNK-03b).
- **400 px:** data, valor e situação sempre visíveis; descrição e contraparte na linha expansível; botões de 44 px.
- **Ajuda:** `Ajuda/financeiro-contas-extrato-banco.md` (prevista, aba `extrato-banco` da conta); `<Dica>` em "OFX", "Não verificada pelo banco" e "Diferença de saldo".

## Plano de testes
- **Unidade:** parser OFX 1.x e 2.x, sem `FITID`, `FITID` repetido, conta diferente, entidade externa recusada, limites e tempo (CA 4, 5); máscara da contraparte (CA 8).
- **Integração (Testcontainers + fake do Inter):** importação pela API com sobreposição e sem duplicar, execução dupla e dois tenants, produção sem a flag (CA 1, 2); saldo e notificação de diferença (CA 3); OFX válido, com antivírus, repetido pelo hash e da conta errada (CA 4, 5); OFX bloqueado em conta Inter, contingência com `finance.approve` e linhas "não verificadas" (CA 6); aviso ao Proprietário (CA 7); máscara por permissão e desempenho com 5 mil linhas (CA 8); imutabilidade, ignorar débito com `finance.approve` e desfazer com motivo (CA 9); três camadas e IDOR (CA 10); log sem dado de linha (CA 11); auditoria (CA 12).
- **E2E (Playwright):** importar um OFX de exemplo numa conta sem API e ver as linhas pendentes; tentar OFX numa conta Inter e ver a recusa; tela a 400 px.

## Fora do escopo
- Casar linhas com transações e lançar a partir do extrato: BNK-03b. CNAB de retorno: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 28–31):
1. Importação às 06:30, mais "Buscar agora" (CA 1).
2. `ofx` na allowlist só do dono `BankStatementBatch` (adendo à ADR 0009; CA 4).
3. OFX bloqueado em conta com API; liberável por conta com `finance.approve` como contingência; linhas "não verificadas pelo banco" (D13; CA 6).
4. Contraparte mascarada para quem só tem `finance.view` (CA 8).

Decisões da revisão aplicadas: D13 (OFX em conta com API, CA 6); §4.3 (parser sem DTD, limite, antivírus, hash do arquivo e `FITID` únicos, aviso ao Proprietário a cada importação, ignorar débito com `finance.approve`, desfazer com motivo, job por `tenantId` com módulo conferido, CA 1, 4–7, 9); D1 (produção só depois da GOL-01b, CA 1); §4.4 (padrões 6 e 7).

**Pré-condição externa:** nenhuma (o fake do Inter cobre o desenvolvimento; a importação real do sandbox é registrada no runbook).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers + fake) e E2E passando; o roteiro da importação do sandbox está no runbook (pré-condição externa: a credencial de sandbox).
- [x] Migration criada (não destrutiva, só tabelas novas), aplicada nos testes de integração; a aplicação sobre a cópia do `konebras` acontece no deploy da homologação.
- [x] Permissões só do `ModuleCatalog`; dono de anexo registrado e adendo à ADR 0009 aplicado.
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajuda escrita; teste de cobertura passando.
- [x] Alertas acionáveis (diferença de saldo com link; aviso de importação ao Proprietário).
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `IBankStatementProvider` e `InterStatementProvider`; `OfxParser` restrito.
- [x] `BankStatementLine`, `BankStatementBatch`, `BankStatementBalance`, `BankOfxContingency`; recorrente `inter-statement-import`.
- [x] Endpoints `/api/v1/finance/bank-accounts/{id}/statement-lines` (+ `/import-ofx`, `/fetch`, `/ofx-contingency`, `/{lineId}/ignore`, `/batches/{id}/undo`); aba e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (parser de arquivo, antivírus, dados de contraparte, contingência), @qa.
- Gate: CA 2, 4, 5, 6 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: `finance.approve` na contingência e no débito ignorado) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 5) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADR 0032, adendo 0009) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** nada antes da data de corte; linha imutável, com "Ignorar" e "Desfazer importação" no lugar de excluir; parser com `DtdProcessing.Prohibit` e tempo limite.
- **Correções da revalidação (21/10/2026):** pontos 28 a 31 decididos (CA 1, 4, 6, 8); OFX bloqueado em conta com API, contingência por conta com `finance.approve`, prazo e linhas "não verificadas pelo banco" (CA 6); antivírus, hash do arquivo e `FITID` únicos (CA 4, 5); aviso ao Proprietário a cada importação (CA 7); ignorar débito com `finance.approve` e desfazer com motivo no `ConfirmDialog` (CA 9); job por tenant com módulo conferido e produção bloqueada até a GOL-01b (CA 1); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D13, tabela 3.2 pontos 28–31, §4.3, §4.4); nova ordem (Marco 5.3, ordem 5); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 27/10/2026 | 1.0 | Implementada (sobre a BNK-01a): `IBankStatementProvider` + `InterStatementProvider` (extrato completo paginado e saldo por dia), `OfxParser` próprio (1.x SGML e 2.x XML com `DtdProcessing.Prohibit`, limites de tamanho, lançamentos e 10 s), `BankStatementImportService` (API, OFX, contingência, ignorar, desfazer, consulta com máscara 3+2), recorrente `inter-statement-import` (06:30), tabelas `bank_statement_lines`, `bank_statement_batches`, `bank_statement_balances` e `bank_ofx_contingencies` (migration `BankStatementImportBnk03a`, só aditiva), dono de anexo `BankStatementBatch` só com OFX (adendo à ADR 0009 aplicado), aba "Extrato do banco" com ajuda, runbook do sandbox. Desvios: (1) a aba fica em `/app/financeiro/contas/$id/extrato?aba=extrato-banco`, porque a conta não tem página própria (o extrato da FIN-04 já é a página da conta); (2) as linhas criadas não geram uma linha de auditoria cada (o lote registra a importação; `IAuditUpdatesOnly` audita só ignorar), e desfazer marca as linhas `Undone` (estado novo, fora do índice único) em vez de apagá-las; (3) o link "Conciliar {n} linhas pendentes" filtra as pendentes até a BNK-03b entregar a conciliação; (4) a diferença de saldo avisa pelo dia mais recente com mais de 2 dias. Recomendação aceita (PO 24/10): o FITID do OFX é comparado com o id da API só no OFX de contingência; conferir no sandbox (runbook, item 6). Sandbox real: pré-condição externa, roteiro em `docs/operacao/banco-inter.md` | Dex (dev) |
