# BNK-03a — Extrato do banco: importação pela API do Inter e por arquivo OFX

| Campo | Valor |
|---|---|
| ID | BNK-03a |
| Título | Linhas do extrato do banco (`bank_statement_lines`) importadas automaticamente pela API do Inter (job diário e "Buscar agora") e por arquivo OFX de qualquer banco, sem duplicar, com saldo do banco conferido contra o saldo do sistema por dia |
| Tamanho | M (quebra da BNK-03) |
| Status | Draft |
| Requisitos | R-FIN-8 (extrato via API do Inter e importação OFX) · R-FIN-2 · R-FIN-10 (realizado) · R-PRP-3 · R-PRP-5 · R-SEC-5 |
| Dependências | BNK-01a (`InterApiClient`, `IBankStatementProvider`), FIN-04 (extrato interno e saldo do sistema), FND-07 (anexos, varredura) |
| Bloqueia | BNK-03b (conciliação) |
| Ordem de execução | 6 (Marco 5.3 "Conciliação") |

## User story
Como **financeiro**, quero **o extrato do banco dentro do sistema todo dia, do Inter pela API e dos outros bancos por arquivo**, para **comparar com o que o sistema registrou sem baixar planilha nem digitar lançamento**.

## Contexto e referências
- PRD R-FIN-8 ("Conciliação bancária: extrato via API do Inter e importação OFX para outros bancos; sugestão automática de match"); `05-backlog` BNK-03.
- `02-arquitetura` §6.2 (API de extrato para conciliação; `IBankStatementProvider`).
- `03-modelo-dados` → `bank_statement_lines` (`bank_account_id`, `date`, `amount`, `description`, `external_id`, `source` (API/OFX), `matched_transaction_id`); `financial_transactions.reconciled` e `statement_line_id` (BNK-03).
- FIN-04 (extrato interno com saldo inicial e acumulado; 5 mil transações em < 1 s), FIN-00 (saldo na data de corte).
- ADR 0009 (anexos: tipos permitidos e magic bytes; o OFX entra na allowlist do dono, Pontos em aberto 2), revisão pré-Fase 0 §3 (parser de arquivo isolado e com tempo limite).

## Critérios de aceite
**Pela API do Inter**
1. **Dado** uma conta com integração Inter, **quando** o recorrente global `inter-statement-import` roda (proposta: 06:30 de São Paulo) ou quem tem `finance.edit` clica "Buscar agora" (no máximo 1 vez a cada 5 min por conta), **então** o sistema busca o extrato desde a última linha importada (com 3 dias de sobreposição, e nunca antes da data de corte da FIN-00) pelo `IBankStatementProvider.GetAsync` e grava cada lançamento em `bank_statement_lines` com data, valor com sinal, descrição, tipo (Pix, boleto, TED, tarifa…), contraparte (nome e documento, `[PersonalData]`), `external_id` do banco e `source = Api`.
2. **Dado** o mesmo lançamento devolvido de novo (sobreposição, nova execução, "Buscar agora" e job ao mesmo tempo), **quando** importa, **então** não duplica: índice único `(bank_account_id, source, external_id)`; o job tem teste de execução dupla e de dois tenants.
3. **Dado** o saldo do dia informado pelo banco, **quando** a importação termina, **então** grava o saldo do banco por dia (`bank_statement_balances`, proposta) e mostra "Saldo no banco R$ X · no sistema R$ Y · diferença R$ Z" na conta (FIN-04 dá o saldo do sistema); diferença ≠ 0 há mais de 2 dias gera notificação a quem tem `finance.edit` com link para a conciliação (`dedup_key = stmt-diff:{conta}:{data}`).

**Por arquivo OFX**
4. **Dado** `finance.edit` e uma conta bancária de qualquer banco, **quando** envio um arquivo OFX (até 5 MB), **então** o arquivo é validado (extensão, conteúdo OFX 1.x SGML ou 2.x XML, `DtdProcessing.Prohibit` e sem entidades externas, tempo limite de parse de 10 s, proposta), a conta do arquivo (`BANKID`/`ACCTID`) é conferida com a conta escolhida (diferente: 422 "Este extrato é da conta {final}, não da conta escolhida."), e as transações viram linhas com `source = Ofx` e `external_id = FITID`; o arquivo fica como anexo do lote, com a varredura antes da leitura.
5. **Dado** um OFX que se sobrepõe a outro já importado, **quando** importo, **então** as linhas com o mesmo `FITID` são ignoradas e o resumo mostra "{n} novas, {m} já importadas, {k} recusadas" com o motivo das recusadas; OFX sem `FITID` usa o hash de data + valor + descrição + ordem no arquivo (com aviso).
6. **Dado** uma conta com `provider = Inter`, **quando** envio OFX, **então** é permitido (contingência), e as linhas que coincidem com as da API pelo id do banco são ignoradas (Pontos em aberto 3).

**Consulta**
7. **Dado** `finance.view`, **quando** abro "Contas e extrato → {conta} → Extrato do banco", **então** vejo as linhas por data com descrição, contraparte (mascarada sem `finance.edit`, proposta), valor, origem (API/OFX), situação (Pendente info, Conciliada ok, Ignorada neutro) e o saldo do banco por dia; filtros por período e situação na URL; 5 mil linhas em menos de 1 s.
8. **Dado** uma linha importada, **quando** existe, **então** nunca é editada nem apagada; uma linha errada (OFX da conta errada já importado) é marcada "Ignorada" com motivo por quem tem `finance.edit`, e o lote inteiro pode ser desfeito enquanto nenhuma linha dele estiver conciliada ("Desfazer importação", com motivo).

**Permissões e auditoria**
9. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; importar, buscar agora, ignorar e desfazer sem `finance.edit`: 403; `RequireModule` e `perm:`; IDOR em `/bank-accounts/{id}/statement-lines/{lineId}`.
10. **[Segurança]** **Dado** as linhas do extrato, **quando** saem pela API, **então** levam `Cache-Control: no-store`; o log da importação traz só conta, período, contagens e duração, nunca descrição, contraparte ou valores linha a linha.
11. **Dado** qualquer importação, ignorar ou desfazer, **quando** abro o histórico, **então** vejo lote, quem, quando, origem e contagens.

## Regras de negócio e técnicas
- **`bank_statement_lines`** (03 + proposta): `bank_account_id`, `batch_id`, `date`, `amount` (com sinal, `numeric(18,2)`), `description`, `kind`, `counterparty_name` e `counterparty_document` (`[PersonalData]`), `external_id`, `source` (`Api`/`Ofx`), `status` (`Pending`/`Reconciled`/`Ignored`), `ignore_reason`, `matched_transaction_id` (BNK-03b), `imported_at`, `xmin`; único `(bank_account_id, source, external_id)`.
- **`bank_statement_batches`** (proposta): conta, origem, período, arquivo (anexo), contagens, `undone_at`, `undone_by`, `undo_reason`.
- **`bank_statement_balances`** (proposta): conta, data, saldo do banco, origem.
- **Data de corte:** nada antes da `finance.cutover_date` (FIN-00) entra.
- **Parser OFX** próprio e restrito (sem dependência de licença comercial), com limites de tamanho, de linhas (proposta: 20 mil) e de tempo.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver), `finance.edit` (importar, buscar, ignorar, desfazer). Nenhuma ação nova.
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `finance.bank_statement_lines` (03 com as colunas acima), `finance.bank_statement_batches`, `finance.bank_statement_balances` (propostas); dono de anexo `BankStatementBatch` (ofx, até 5 MB; leitura `finance.view`, escrita `finance.edit`).
- **Jobs:** recorrente global `inter-statement-import`.

## Telas e estados
- **Contas e extrato → conta → aba "Extrato do banco"** (`/app/financeiro/contas/$id?aba=extrato-banco`), com "Buscar agora" (Inter) e "Importar OFX".
- **Verbos:** "Importar extrato" / "Extrato importado"; "Buscar agora" / "Extrato atualizado"; "Ignorar linha" / "Linha ignorada"; "Desfazer importação" / "Importação desfeita".
- **Estados:** carregando; vazio ("Nenhuma linha do banco ainda. Conecte o Inter ou importe um arquivo OFX." + as duas ações); erro com o motivo do arquivo; importação em andamento.
- **Propositivo:** a conta e o período vêm preenchidos; depois de importar, o `NextSteps` oferece "Conciliar {n} linhas pendentes" (BNK-03b).
- **400 px:** data, valor e situação sempre visíveis; descrição e contraparte na linha expansível.
- **Ajuda:** a ajuda da conta ganha a aba `extrato-banco`; `<Dica>` em "OFX" e "Diferença de saldo".

## Plano de testes
- **Unidade:** parser OFX 1.x e 2.x, sem `FITID`, conta diferente, entidade externa recusada e tempo limite (CA 4, 5).
- **Integração (Testcontainers + fake do Inter):** importação pela API com sobreposição e sem duplicar, execução dupla e dois tenants (CA 1, 2); saldo e notificação de diferença (CA 3); OFX válido, repetido e da conta errada (CA 4, 5); Inter + OFX (CA 6); imutabilidade, ignorar e desfazer (CA 8); três camadas e IDOR (CA 9); log sem dado de linha (CA 10); desempenho com 5 mil linhas (CA 7).
- **E2E (Playwright):** importar um OFX de exemplo e ver as linhas pendentes; tela a 400 px.

## Fora do escopo
- Casar linhas com transações e lançar a partir do extrato: BNK-03b. CNAB de retorno: fora do MVP.

## Pontos em aberto
1. **Horário e frequência da importação** (PO): proposta 06:30 diário + "Buscar agora".
2. **OFX na allowlist de anexos** (arquiteto): a ADR 0009 não tem `ofx`; acrescentar no dono `BankStatementBatch`.
3. **OFX numa conta Inter** (PO): proposta: permitir como contingência, deduplicando pelo id do banco quando existir.
4. **Contraparte mascarada para `finance.view`** (PO, LGPD): proposta: sim.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake) e E2E passando; importação do sandbox registrada.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis (diferença de saldo com link).
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IBankStatementProvider` e `InterStatementProvider`; `OfxParser` restrito.
- [ ] `BankStatementLine`, `BankStatementBatch`, `BankStatementBalance`; recorrente `inter-statement-import`.
- [ ] Endpoints `/api/v1/finance/bank-accounts/{id}/statement-lines` (+ `/import-ofx`, `/fetch`, `/{lineId}/ignore`, `/batches/{id}/undo`); aba e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (parser de arquivo, dados de contraparte), @qa.
- Gate: CA 2, 4, 5 e 8 obrigatórios.

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

- **Correções feitas na validação:** nada antes da data de corte (CA 1); linha imutável, com "Ignorar" e "Desfazer importação" no lugar de excluir (CA 8); parser com `DtdProcessing.Prohibit` e tempo limite (CA 4).
- **Pré-condições para `Ready`:** ponto em aberto 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
