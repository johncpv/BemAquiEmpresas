# FIN-06a — Inadimplência por cliente (aging) e aviso diário de título vencido

| Campo | Valor |
|---|---|
| ID | FIN-06a |
| Título | Títulos a receber vencidos por cliente, com aging (0–30, 31–60, 61–90, 90+), e aviso diário consolidado de título que venceu ontem |
| Tamanho | P (quebra da FIN-06, revisão pré-Fase 2) |
| Status | Ready |
| Requisitos | R-FIN-12 (parte: vencidos por cliente, aging) · R-COM-2 (notificação de título vencido) · R-PRP-5 |
| Dependências | FIN-01 (títulos e baixas), FIN-00 (títulos de implantação, para o dado real), FND-09e (`dedup_key`, `DataTable` com totais) |
| Bloqueia | FIN-06b (visão por projeto e pontualidade), FIN-09 (régua), DSH-01 (card de inadimplência) |
| Ordem de execução | 5 (Marco 2.1a "Ver o caixa") |

## User story
Como **financeiro**, quero **ver quem está devendo e há quanto tempo, e ser avisado todo dia do que venceu**, para **cobrar primeiro quem pesa mais**.

## Contexto e referências
- PRD R-FIN-12; §1.1 problema 2 ("nem visão de quem atrasa pagamento"); protótipo: painel "A receber vencido" (cliente, título, dias, valor).
- Revisão pré-Fase 2: quebra da FIN-06 em 06a (aging por cliente e aviso, no Marco 2.1a, que não depende do Comercial) e 06b (por projeto, pontualidade e aba do terceiro, Marco 2.4); §2.1-M8 (jobs e `dedup_key`); §2.2 baixos (SQL parametrizado e allowlist de ordenação); §2.3 (vazio com "Ver a receber").
- ADR 0017: sem histórico do Dolibarr; os títulos de implantação (FIN-00) entram no aging.

## Critérios de aceite
**Aging por cliente**
1. **Dado** o dataset fixo (títulos vencidos há 5, 35, 70 e 120 dias para 3 clientes, um deles de implantação), **quando** abro "Inadimplência" hoje, **então** vejo o total vencido, a quantidade de títulos e de clientes e as faixas 0–30, 31–60, 61–90 e 90+ (dias de atraso = hoje − vencimento) com o saldo em aberto de cada uma, batendo com o esperado.
2. **Dado** a visão "Por cliente", **quando** abro, **então** cada cliente mostra o total vencido por faixa e o maior atraso, ordenado do maior saldo vencido para o menor; clicar abre a lista A receber filtrada pelo cliente e por "Vencidos".
3. **Dado** um título `PartiallyPaid` vencido, **quando** entra no aging, **então** conta pelo saldo em aberto; títulos `Planned` (sem vencimento) e `Cancelled` não entram; títulos `Opening` entram como os demais.

**Aviso diário**
4. **Dado** títulos a receber que venceram ontem e continuam em aberto, **quando** o job diário roda às 08:00 de São Paulo, **então** cada usuário com `finance.edit` recebe uma notificação consolidada "{n} títulos a receber venceram ontem ({total})." com link para a lista filtrada; sem títulos, nada é enviado; rodar de novo no mesmo dia, ou em paralelo, não repete o aviso (`dedup_key = overdue:{data}`), e um tenant não avisa os usuários de outro (teste de dois tenants).

**Permissões e segurança**
5. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`; desempenho: aging com 50 mil títulos em menos de 1 s.
6. **[Segurança]** **Dado** os filtros e a ordenação, **quando** a consulta roda, **então** o SQL só usa parâmetros e a ordenação aceita só as colunas da allowlist (422 fora dela).

## Regras de negócio e técnicas
- Leitura no próprio Finance (Dapper pela conexão do `TenantDbContext`), sem tabela nova; "hoje" pelo `BusinessTime.Today`; dinheiro `numeric(18,2)`.
- **Faixas** por dias de atraso: 1–30 (rótulo "0–30", como no PRD), 31–60, 61–90, 91+.
- **Job:** recorrente global diário no `RecurringJobsRegistrar` (fuso de São Paulo) que enfileira `OverdueReceivablesNoticeJob` por tenant, com argumentos só de ids; deduplicação pelo `dedup_key` da FND-09e.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (aging), `finance.edit` (destinatários do aviso).
- **Eventos:** nenhum. **Tabelas:** nenhuma nova.

## Telas e estados
- **Financeiro → Inadimplência** (`/app/financeiro/inadimplencia`; grupo "Caixa e pagamentos"): cards por faixa (cor semântica com texto), aba na URL "Por cliente" (a aba "Por projeto" entra na FIN-06b); `DataTable` com rodapé de totais; cada linha leva aos títulos.
- **Estados:** carregando; vazio ("Nenhum título vencido. Bom sinal." + "Ver a receber"); erro.
- **Propositivo:** o aviso leva à lista dos vencidos de ontem; cada cliente oferece "Ver títulos" e, a partir da BNK-01, "Enviar boleto".
- **400 px:** colunas sempre visíveis: cliente, total vencido e maior atraso; as faixas vão para a linha expansível.
- **Ajuda:** `Ajuda/financeiro-inadimplencia.md` (aba `clientes`, `padrao: true`); `<Dica>` em "Aging" e "Dias de atraso".

## Plano de testes
- **Unidade:** faixas e dias de atraso (CA 1, 3).
- **Integração (Testcontainers):** dataset fixo de aging por cliente (CA 1–3); job com e sem vencidos, sem repetir, em paralelo e com dois tenants (CA 4); três camadas e desempenho (CA 5); allowlist de ordenação (CA 6).
- **E2E (Playwright):** abrir inadimplência e ir aos títulos do cliente.

## Fora do escopo
- Visão por projeto, pontualidade e aba "Comercial e financeiro" do terceiro: FIN-06b.
- Cenário ajustado do fluxo de caixa: FIN-07. Régua de cobrança: FIN-09. Boleto: BNK-01.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 14): aviso diário consolidado para quem tem `finance.edit`. P-18 decidida (ADR 0021).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo) e E2E passando.
- [ ] Sem migration.
- [ ] Permissões só do `ModuleCatalog`; job registrado.
- [ ] Tela com estados.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alerta acionável aplicado.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AgingQuery` (Dapper) e `OverdueReceivablesNoticeJob`.
- [ ] Endpoint `/api/v1/finance/aging`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1 e 4 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026.

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

- **Correções feitas na validação (20/10/2026):** quebra da FIN-06 (CA 1, 2, 4, 5 e 10 da original vieram para cá); títulos de implantação no aging; aviso com `dedup_key` e teste de dois tenants; vazio com "Ver a receber".
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (como FIN-06) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções (como FIN-06) | Pax (PO) |
| 20/10/2026 | 0.3 | Quebra da FIN-06 em 06a (esta) e 06b; ajustes da revisão pré-Fase 2 | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
