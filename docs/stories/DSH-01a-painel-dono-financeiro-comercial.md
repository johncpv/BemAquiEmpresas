# DSH-01a — Painel do proprietário: estrutura e cards de caixa, faturamento, inadimplência, comercial e pendências

| Campo | Valor |
|---|---|
| ID | DSH-01a |
| Título | Tela única do dono com a faixa "Atenção" e os cards de saldo em contas, caixa projetado 30/60/90 dias (base × ajustado), faturamento do mês e do ano × meta, inadimplência, comercial e pendências do dono; cada número com a regra de cálculo e o drill-down até o relatório de origem; cache por tenant com invalidação por evento |
| Tamanho | G (quebra da DSH-01: a estrutura do painel, o cache e os cards que só dependem do Financeiro e do Comercial) |
| Status | Draft |
| Requisitos | PRD §5.14 (Dashboard do proprietário: Caixa, Faturamento × meta, Inadimplência, Comercial, Pendências do dono; cada card leva ao detalhe) · R-FIN-10 · R-FIN-11 · R-FIN-12 · R-FIN-13 · R-COML-9 · R-PRP-4 · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | FIN-05 (saldo atual), FIN-07 (`ICashForecast`), FIN-08 (`IRevenueSummary`), FIN-06a (aging), COM-08 (funil), DSH-04 (`IAgendaService`), FIN-03a/FIN-03b (OP), COM-07 (aditivos), FND-09e (`Chart`, componentes) |
| Bloqueia | DSH-01b (cards de margem, carteira, produção e entregas) |
| Ordem de execução | 5 (Marco 4.2 "O painel do dono, parte do dinheiro") |

## User story
Como **proprietário**, quero **abrir uma tela só e ver quanto tenho em caixa, como ele fica nas próximas semanas, quanto faturei contra a meta, quem me deve e o que espera a minha decisão**, para **entender a empresa em um minuto e ir direto ao que precisa de mim**.

## Contexto e referências
- PRD §5.14 (lista dos KPIs, "uma tela, visão simples e geral", "cada card leva ao detalhe"); §1 ("o dono saber, a qualquer momento, quanto cada projeto custa, quanto rende e como estará o caixa"); §7 Fase 4 ("Visão completa do dono").
- Backlog Fase 4 (CA da DSH-01): "carrega < 2 s; números conferem com relatórios de origem (teste com dataset fixo)".
- `ModuleCatalog`: módulo `dashboard` ("Painel do proprietário", área Visão geral, só `view`); `GroupSeeder`: o Financeiro tem `dashboard.view`; o Proprietário tem tudo. `modules.ts`: slug `painel`.
- `02-arquitetura` §3 ("Consultas pesadas de dashboard: SQL direto (Dapper) sobre views/materialized views, atualizadas por evento ou a cada 5 min") e ADR 0006 (Dapper pela conexão do tenant; um módulo `Reporting` como exceção de leitura; materialized view só quando a medição justificar).
- **Código atual:** não há projeto para o módulo `dashboard` nem `Reporting` em `src/Modules` (14 módulos no teste de arquitetura).
- Protótipo (Painel): cabeçalho "Boa tarde, John" + frase-resumo; faixa de alertas ("Ed. Monte Castelo vai atrasar 7 dias", "3 ordens de pagamento aguardam você", "Hospital São Lucas com parcela vencida"); KPIs "Saldo em contas", "Faturado em outubro / 380 mil" com medidor de meta, "Margem real média", "A receber vencido"; "Caixa projetado, próximas 12 semanas" (Os dois/Base/Ajustado e menores saldos); "Aguardando sua aprovação"; "Comercial" (em aberto, conversão, vencem em 7 dias, funil).

## Critérios de aceite
**Estrutura e acesso**
1. **Dado** `dashboard.view`, **quando** abro "Painel do proprietário" (`/app/painel`), **então** vejo o cabeçalho com a saudação pelo horário ("Bom dia/Boa tarde/Boa noite, {primeiro nome}"), a data por extenso e uma frase-resumo montada dos cards visíveis (ex.: "Caixa positivo nas próximas 12 semanas; 3 decisões esperam você."), a faixa "Atenção" e os cards.
2. **[Segurança]** **Dado** um card, **quando** o usuário não tem a permissão do módulo dono do dado (tabela do CA 3), **então** o card não aparece e o endpoint dele não é chamado; chamado direto, responde 403 `forbidden`; o painel com `dashboard.view` e sem nenhum card visível mostra "Você ainda não tem acesso aos indicadores. Fale com o administrador."
3. **Dado** os cards desta story, **quando** o painel monta, **então** cada card segue a tabela (fontes por contrato, regra visível na `<Dica>` "Como calculamos" e drill-down em "Ver origem"):

| Card | Permissão | Número | Fonte (contrato) | Drill-down |
|---|---|---|---|---|
| Saldo em contas | `finance.view` | saldo consolidado e por conta | `BankBalanceAsync` (FIN-05) | Contas e extrato |
| Caixa projetado | `finance.view` | linhas base × ajustado de 12 semanas, saldo final em 30/60/90 dias, menor saldo de cada cenário e a diferença | `ICashForecast` (FIN-07) | Fluxo de caixa no cenário escolhido |
| Faturamento × meta | `finance.view` | receita bruta do mês e acumulada do ano, meta do mês e do ano, % atingido | `IRevenueSummary` (FIN-08) + metas (CA 7) | DRE, linha Receita bruta |
| A receber vencido | `finance.view` | total vencido, aging 0–30/31–60/61–90/90+, 5 maiores clientes, maior atraso | aging da FIN-06a | Inadimplência |
| Comercial | `quotes.view` | em aberto (valor), conversão dos últimos 90 dias, vencendo em 7 dias, funil | consulta do funil (COM-08) | Funil |
| Pendências do dono | (cada item, a sua) | OP aguardando sua decisão e aditivos aguardando aprovação | `IAgendaService` (DSH-04) | o documento na ação |

**Números que conferem**
4. **Dado** o dataset fixo de teste (o mesmo das stories de origem), **quando** abro o painel, **então** cada número de cada card é igual ao do relatório de origem no mesmo dia e período (teste que chama o card e o relatório e compara, centavo a centavo); o painel nunca recalcula por conta própria: chama o contrato que o relatório usa.
5. **Dado** "Ver origem" de qualquer número, **quando** clico, **então** abre o relatório de origem com os mesmos filtros (período, cenário, faixa de aging), e o número está lá.
6. **Dado** a `<Dica>` "Como calculamos" de um card, **quando** abro, **então** ela explica a regra em uma ou duas frases (ex.: "Ajustado: cada recebível deslocado pelo atraso médio do cliente nos últimos 12 meses; clientes com inadimplência acima de 20% entram com o valor reduzido.").

**Meta de faturamento**
7. **Dado** `settings.edit`, **quando** abro Configurações → Parâmetros → Financeiro → "Metas de faturamento" e informo o valor de cada mês de um ano (`numeric(18,2)`, ≥ 0; "Repetir para o ano" preenche os 12 meses), **então** grava em `finance.revenue_targets` (proposta) com auditoria, pelo endpoint do Finance (ADR 0016 M6); o card mostra o medidor "{x}% da meta" e a frase "Faltam {n} dias úteis" (dias úteis pelo calendário padrão do tenant, CAD-03, lido por um contrato do Production, proposta); sem meta, o card mostra só o faturado e o link "Definir meta" para quem tem `settings.edit` (proposta, Pontos em aberto 2).

**Faixa "Atenção"**
8. **Dado** os cards visíveis, **quando** o painel monta, **então** a faixa "Atenção" mostra até 3 alertas, na ordem: caixa ajustado negativo nos próximos 90 dias (crítico), OP aguardando sua decisão (alerta, com total e o primeiro vencimento), maior título vencido (alerta, com dias e o atraso médio do cliente); cada alerta é um link para a ação (R-PRP-5); sem alerta, a faixa não aparece. (Os alertas de projeto atrasado e em risco entram na DSH-01b.)

**Atualização e desempenho**
9. **Dado** os cards, **quando** o painel carrega, **então** cada card tem o seu endpoint (`/api/v1/dashboard/cards/{card}`) e o seu skeleton; um card com erro mostra "Não foi possível carregar {card}." com "Tentar de novo" e os outros continuam.
10. **Dado** o cache, **quando** um card é calculado, **então** o resultado fica em memória por `{tenantId}:{card}:{parâmetros}` por até 60 s (o cache guarda só o agregado do tenant; a permissão é conferida a cada request, antes do cache), e é invalidado quando chegam os eventos que mudam o número (`ReceivableSettled`, `PaymentOrderSubmitted`, `PaymentOrderApproved`, `PaymentOrderPaid`, `ContractSigned`, `ContractAmended`, `QuoteApproved`) ou quando o usuário clica "Atualizar"; o card mostra "Atualizado às {hh:mm}" (proposta, Pontos em aberto 4).
11. **Dado** 50 mil títulos, 20 mil transações, 50 mil orçamentos e 2 mil clientes, **quando** abro o painel sem cache, **então** todos os cards desta story chegam em menos de 2 s (cada card em menos de 1,5 s), e com cache em menos de 300 ms (medição no teste de integração).
12. **[Segurança]** **Dado** dois tenants com o mesmo card em cache, **quando** usuários dos dois abrem o painel, **então** cada um vê só os números do próprio tenant (teste); nenhum card desta story leva custo, orçado ou margem (isso é da DSH-01b, com `projects.view_cost`).
13. **Dado** o módulo `dashboard` não liberado, **quando** abro, **então** a tela `ModuleNotReleased` ("Funcionalidade não liberada nessa versão do sistema.") e a API responde 403 `module_not_released`; sem grupo com `dashboard.view`, o item some do dock e a API responde 403 `forbidden`; o card de um módulo não liberado não aparece. `RequireModule("dashboard")` e `perm:dashboard.view` em todos os endpoints, mais a permissão do card.

## Regras de negócio e técnicas
- **Composição, não recálculo:** os endpoints do painel ficam no Host (proposta, Pontos em aberto 1), chamam só os `*.Contracts` dos módulos donos e devolvem DTOs de card; nenhum SQL do painel lê tabela de módulo.
- **Mesma fonte do relatório:** cada contrato usado aqui é o mesmo serviço que o relatório usa (FIN-05, FIN-07, FIN-08, FIN-06a, COM-08), garantido por teste.
- **Cache** só de agregados por tenant, nunca da resposta por usuário; invalidação por evento consumido no Host (consumidor idempotente da inbox) e TTL de reserva de 60 s.
- **Dinheiro** `numeric(18,2)`, exibido com `tabular-nums`; "hoje" e dias úteis pelo `BusinessTime` e pelo calendário padrão.

## Permissões, eventos e tabelas
- **Permissões:** `dashboard.view` (tela); `finance.view` e `quotes.view` (cards); `settings.edit` (metas).
- **Eventos:** consome (só para invalidar o cache) `ReceivableSettled`, `PaymentOrderSubmitted`, `PaymentOrderApproved`, `PaymentOrderPaid`, `ContractSigned`, `ContractAmended`, `QuoteApproved`.
- **Contratos:** `ICashForecast` (FIN-07), `IRevenueSummary` (FIN-08), aging (novo `IReceivablesAging.SummaryAsync` em `Finance.Contracts`, proposta), funil (novo `IQuoteFunnel.SummaryAsync` em `Commercial.Contracts`, proposta), `IAgendaService` (DSH-04).
- **Tabelas:** `finance.revenue_targets` (**proposta**: `year`, `month`, `amount` `numeric(18,2)`, único em `(year, month)`, auditada).

## Telas e estados
- **Visão geral → Painel do proprietário** (`/app/painel`): cabeçalho; faixa Atenção; linha de KPIs (Saldo em contas, Faturado no mês com medidor, A receber vencido); grade principal com "Caixa projetado" (gráfico de linhas do `Chart`, seletor de cenário) e "Aguardando sua decisão"; grade com "Comercial" (barras do funil, como na COM-08) e o espaço dos cards da DSH-01b.
- **Componentes:** `Kpi` (rótulo, valor, linha de contexto, medidor opcional) e `DashboardCard` (título, subtítulo, "Como calculamos", "Ver origem", "Atualizado às", estados), nos componentes de UI, com tokens e sem cor literal.
- **Estados:** carregando (skeleton por card); vazio por card ("Sem títulos ainda. Comece pela implantação financeira." + link); erro por card (CA 9); sem nenhum card visível (CA 2).
- **Propositivo:** alertas e números levam à ação; "Definir meta" quando falta a meta.
- **400 px:** KPIs em 1 coluna, cards empilhados na ordem Atenção → Caixa → Pendências → Faturamento → Vencido → Comercial; o gráfico ocupa a largura com a tabela equivalente.
- **Ajuda:** `Ajuda/painel.md` (rota `/app/painel`) com cada card e a regra; `Ajuda/configuracoes-parametros.md` (metas); `<Dica>` em cada "Como calculamos".

## Plano de testes
- **Unidade:** frase-resumo e ordem da faixa Atenção (CA 1, 8); dias úteis restantes (CA 7).
- **Integração (Testcontainers, obrigatória):** cada card igual ao relatório de origem com o dataset fixo (CA 4); visibilidade por permissão com os usuários do seed (CA 2, 3); metas com auditoria e `settings.edit` (CA 7); cache e invalidação por evento (CA 10); desempenho com e sem cache (CA 11); dois tenants (CA 12); três camadas (CA 13).
- **E2E (Playwright):** Proprietário abre o painel, troca o cenário do caixa, vai à origem de um número e volta; usuário do Financeiro vê os cards dele; tela a 400 px.

## Fora do escopo
- Margem, carteira, produção e entregas: DSH-01b.
- Painel configurável (mover e esconder cards), comparação com ano anterior, envio do painel por e-mail.
- Faturamento pela NF-e autorizada: Fase 5 (troca a fonte do `IRevenueSummary`, Pontos em aberto 3).

## Pontos em aberto
1. **Onde ficam as consultas do painel** (arquiteto): proposta: endpoints no Host que compõem os `*.Contracts` dos donos, sem projeto novo. Alternativa: o módulo `Reporting` da ADR 0006 (leitura cruzada de schemas), que muda a contagem de módulos do teste de arquitetura e a regra de posse de tabela da ADR 0016.
2. **Meta de faturamento** (PO): proposta: meta mensal por ano em `finance.revenue_targets`, editada em Configurações com `settings.edit`. Alternativas: meta anual única dividida por 12; edição com `finance.edit`.
3. **O que é "faturamento" antes da Fase 5** (PO/contadora): proposta: a receita bruta do DRE (FIN-08 CA 3, títulos a receber pela data de emissão). Alternativa: o valor dos contratos assinados no mês.
4. **Cache e frescor** (PO): proposta: até 60 s com invalidação por evento e "Atualizar". Alternativa: sem cache até a medição pedir.
5. **Primeira tela do dono** (PO): proposta: o Início continua sendo a primeira tela (com a Minha pauta, DSH-04) e o painel fica no dock. Alternativa: quem tem `dashboard.view` cai direto no painel depois do login.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo comparado com as origens, dois tenants), desempenho e E2E passando.
- [ ] Migration de `finance.revenue_targets` (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; cada card com a permissão do dono.
- [ ] Tela com estados por card e 400 px.
- [ ] Ajuda do painel e dos parâmetros; teste de cobertura passando.
- [ ] Faixa Atenção e drill-down acionáveis.
- [ ] Auditoria ligada nas metas.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Endpoints `/api/v1/dashboard/cards/*` no Host, cache e consumidores de invalidação.
- [ ] `IReceivablesAging.SummaryAsync` e `IQuoteFunnel.SummaryAsync` (contratos de resumo).
- [ ] `finance.revenue_targets`, endpoint e tela das metas.
- [ ] `Kpi`, `DashboardCard`; tela `/app/painel` e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (composição no Host × Reporting), @qa, design (painel e 400 px), cybersecurity (cache por tenant e cards por permissão).
- Gate: CA 2, 4, 11 e 12 obrigatórios.

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

- **Correções feitas na validação:** quebra da DSH-01 em 01a (estrutura, cache e cards do Financeiro e do Comercial) e 01b; o painel não recalcula: cada card chama o contrato do relatório de origem, com teste de igualdade (CA 4); onde ficam as consultas (Host × módulo `Reporting` da ADR 0006) ficou para o arquiteto (ponto 1); meta e definição de faturamento como pontos em aberto.
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da DSH-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
