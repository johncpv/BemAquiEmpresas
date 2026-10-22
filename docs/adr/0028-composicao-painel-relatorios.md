# ADR 0028: Composição do painel e dos relatórios

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D9 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`) e os pontos 12, 17 a 22 da tabela 3.1, com os achados de segurança da seção 4.3 (DSH-01a/01b).
- Contexto: as stories do painel do dono (DSH-01a/01b), da pauta (DSH-04) e do resultado gerencial (FIN-08) não diziam onde mora a composição dos números de vários módulos. A ADR 0006 previa um módulo `Reporting` que lê os schemas dos outros, como **única exceção à fronteira**; nenhuma story o criou, e as Fases 4 a 8 mostram que ele não é necessário: todo número do painel já tem dono e contrato (`ICashForecast`, `IRevenueSummary`, aging, funil, pauta). Sem regra de cache, o painel do dono faria dezenas de consultas a cada abertura, e um card poderia devolver dado de um módulo que o usuário não vê.
- **Revoga a parte "Consultas de dashboard → módulo `Reporting`" da ADR 0006.** Complementa as ADRs 0006 (fronteiras), 0008 (cache de permissões versionado) e 0019 (§13 consultas em lote).

## Decisão

### 1. Módulo `Dashboard`, só de composição
- O módulo **`dashboard`** (já no `ModuleCatalog`, ação `view`, área Visão geral) ganha projetos **só de `Application` e `Endpoints`** em `src/Modules/Dashboard/`, referenciando apenas `*.Contracts` dos outros módulos. **Sem `Domain`, sem `Infrastructure`, sem schema e sem tabela** (exceção documentada à regra das 5 camadas; o teste de arquitetura da ADR 0006 trata o `Dashboard` como módulo de composição e reprova qualquer `DbSet`, Dapper ou referência a `TenantDbContext` nele).
- Cada número vem do contrato do módulo dono, com método de resumo próprio (`SummaryAsync`), nunca de consulta aos schemas dos outros. Contratos novos ficam no módulo dono: `IReceivablesAging.SummaryAsync` (Finance), `IQuoteFunnel.SummaryAsync` (Commercial), `IAgendaService` (pauta, DSH-04) e os do Production para o painel do supervisor (DSH-03).
- **Metas de faturamento** em `finance.revenue_targets` (`year`, `month`, `amount numeric(18,2)`, único em `(year, month)`, auditada), donas do Finance, editadas com `settings.edit`.
- **"Resultado gerencial por projeto"** sai do DRE (FIN-08) e vai para o `Dashboard`, entregue na **DSH-01b**, com a nota "Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento." O DRE continua no Finance.

### 2. Permissão por card
- A tela exige `dashboard.view`; **cada card** exige também o `RequireModule` e a permissão do módulo dono (ex.: caixa com `finance.view`, funil com `quotes.view`, margem com `projects.view_cost`). Card sem permissão não é montado no servidor nem aparece (não vira card vazio).
- **DTO por forma de permissão:** o contrato devolve um DTO sem custo e margem a quem não tem `*.view_cost` (ADR 0019 §20) e sem nome de funcionário a quem não tem `hr.view_sensitive`; o teste de arquitetura confere que o DTO sem custo não tem propriedade de custo.

### 3. Cache
- Resultado de cada card em cache de **60 s**, com chave **`{tenant}:{usuário}:{versão de permissão}:{card}:{parâmetros}`** (a versão é a `perm_version` da ADR 0008, para a perda de permissão valer na hora).
- **Contador de geração por tenant** (`dashboard:{tenant}:gen`), incrementado pelos consumidores de eventos que mudam os números (recebimento, OP enviada, aprovada e paga, contrato assinado e aditado, orçamento aprovado); o contador entra na chave, então um evento invalida todo o painel do tenant sem varrer o cache.
- Botão **"Atualizar"** força a leitura (ignora o cache para o usuário), com rate limit por política nomeada (1 por 10 s por usuário). Cada card mostra "Atualizado às {hh:mm}".
- Cache em memória do processo (uma réplica hoje); se a API ganhar réplicas, o cache vai para o PostgreSQL ou Redis com a mesma chave, sem mudar o contrato.

### 4. Página inicial
- **Início, com a Minha pauta, é a página inicial de todos** (`/app`), sem item próprio no dock.
- O Proprietário com `dashboard.view` cai em **`/app/painel`**; o card "Aguardando sua decisão" do painel é a pauta filtrada pelas decisões dele.
- O "Faturamento" do painel antes de existirem notas reais usa os títulos emitidos, com o rótulo **"provisório"** (D4); com notas reais (go-live), passa a ser pela data da nota.

## Consequências
- **DSH-01a** cria o módulo `Dashboard` (Application e Endpoints), o cache e `revenue_targets`; **DSH-01b** acrescenta os cards de margem, carteira, produção e entregas e o "Resultado gerencial por projeto"; **DSH-03** acrescenta o painel do supervisor pelo mesmo padrão; **DSH-04** entrega o `IAgendaService` e o Início.
- **FIN-08** deixa o resultado por projeto para o `Dashboard`.
- A ADR 0006 recebe a nota de revogação do `Reporting`. Nenhuma view ou consulta lê schema de outro módulo; materialized view só no módulo dono, quando a medição justificar.
- `docs/02-arquitetura.md` (módulos e consultas de painel) e `docs/03-modelo-dados.md` (`finance.revenue_targets`) atualizados.

## Alternativas consideradas
- **Módulo `Reporting` lendo os schemas dos outros** (ADR 0006): rejeitada; cria acoplamento ao modelo físico de todos os módulos, fura a fronteira sem necessidade e dificulta aplicar a permissão do dono em cada número.
- **Composição no front, chamando os endpoints de cada módulo:** rejeitada; multiplica as chamadas no celular e espalha a regra de permissão por card no cliente.
