# DSH-04 — Minha pauta

| Campo | Valor |
|---|---|
| ID | DSH-04 |
| Título | Lista por usuário do que precisa dele hoje, montada pelos detectores de cada módulo (aprovações, vencimentos, atrasos, validades, faltas), ordenada pelo que mais destrava, cada linha com o porquê e o link para resolver, na tela Início |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PRP-4 (Minha pauta) · R-PRP-5 (alertas acionáveis) · R-COM-2 · PRD §5.14 (Pendências do dono) · PRD §6 (Desempenho) |
| Dependências | FIN-03a/FIN-03b ("Aguardando você"), FIN-01, FIN-02, FIN-05, COM-02, COM-07, PRD-01, PRD-02, PRD-04, PRD-05, EST-01b, EST-02, CMP-01, CMP-03a (cada detector depende da story do seu módulo; o detector de fonte ainda não entregue não é registrado), FND-09b (Início), FND-06c (permissão efetiva) |
| Bloqueia | DSH-01a (card Pendências do dono) |
| Ordem de execução | 4 (Marco 4.2 "O painel do dono, parte do dinheiro") |

## User story
Como **qualquer usuário**, quero **abrir o sistema e ver numa lista só o que depende de mim hoje, na ordem do que mais destrava o trabalho dos outros**, para **não esquecer aprovação, vencimento nem atraso, e resolver cada um com um clique**.

## Contexto e referências
- PRD R-PRP-4 ("uma lista por usuário com 'o que precisa de você hoje' (OP para aprovar, títulos vencendo, OS atrasadas, orçamentos expirando, faltas de material), em ordem do que mais destrava, com link direto para resolver"); R-PRP-5 (todo alerta leva à ação).
- Backlog Fase 4 (CA): "cada item some quando resolvido; o usuário só vê itens de módulos a que tem acesso; a pauta abre em menos de 1 s".
- ADR 0010 item 9: a tela inicial da Fase 0 é de boas-vindas com atalhos. `Ajuda/inicio.md` descreve "Próximos passos" e "Seus módulos". Esta story põe a pauta no Início.
- FIN-03a/03b (Done): a lista de OP "Aguardando você" já resolve quem pode decidir cada nível; COM-07 (Done): aditivo aguardando aprovação, autor não aprova (D6).
- Fase 3 (rascunho): CMP-01 ("a falta da BOM entra na Minha pauta quando ela existir"), PRD-04 CA 6 (cargo sem custo-hora), EST-01b (ajuste aguardando aprovação), CMP-03a (pedido aguardando aprovação), PRD-05 (etapa de qualidade e NC).
- ADR 0019 §19 e padrões da Fase 2 item 7 ("'Aguardando você' só mostra o que o usuário pode decidir").

## Critérios de aceite
**Detectores**
1. **Dado** o contrato `IAgendaSource` em `BuildingBlocks` (novo, `BemAqui.BuildingBlocks.Agenda`), **quando** um módulo o implementa, **então** declara o código do módulo e as categorias que detecta e devolve, para o usuário do request, itens `AgendaItem(category, title, why, link, dueDate?, amount?, tier, count)` com `link` relativo do app; o Host agrega em `GET /api/v1/me/agenda`; só fontes de módulos **liberados** no tenant são chamadas.
2. **Dado** as fontes desta story, **quando** a pauta é montada, **então** cada categoria só aparece para quem tem a permissão indicada e segue a regra da tabela (proposta, Pontos em aberto 2):

| Categoria | Módulo | Quem vê | Regra |
|---|---|---|---|
| OP aguardando sua decisão | finance | aprovador elegível do nível liberado (FIN-03b) | o mesmo filtro do "Aguardando você" |
| OP aprovada para pagar | finance | `finance.pay` | `Approved` |
| Títulos a pagar vencendo sem OP | finance | `finance.create` | vencem em até 3 dias, `Open`, fora de OP |
| Títulos a receber vencidos | finance | `finance.edit` | vencidos e em aberto (total e clientes) |
| Caixa projetado negativo | finance | `finance.view` | o alerta da FIN-05/FIN-07 nos próximos 30 dias |
| Orçamentos vencendo | quotes | autor com `quotes.edit` | `Sent` com validade em até 7 dias |
| Aditivo aguardando aprovação | projects | `projects.approve`, exceto o autor | `PendingApproval` |
| OS atrasadas | production | `production.approve` | prometido do projeto < hoje e OS não concluída |
| Qualidade para aprovar | production | `production.approve`, exceto quem apontou a etapa anterior | etapa de qualidade pronta |
| NC sob sua responsabilidade | production | responsável da NC | `Open` ou `InAction` |
| Faltas sem solicitação | purchasing | `purchasing.create` | falta de material sem solicitação aberta |
| Pedido de compra aguardando você | purchasing | aprovador elegível (CMP-03a) | nível liberado |
| Ajuste de estoque aguardando | inventory | `inventory.approve`, exceto o autor | pendente (EST-01b) |
| Cargo sem custo-hora | hr | `hr.edit` | cargo com apontamento marcado "Sem custo-hora" (PRD-04) |

3. **Dado** uma fonte cuja story ainda não foi entregue, **quando** a pauta é montada, **então** a categoria simplesmente não existe (o detector só é registrado na story dele, ou nesta se a fonte já existir); um teste lista as fontes registradas e as categorias.

**Ordem e conteúdo**
4. **Dado** itens de várias categorias, **quando** a pauta é ordenada, **então** vem primeiro o nível 1 "Destrava outra pessoa ou dinheiro" (aprovações e pagamentos), depois o nível 2 "Já passou do prazo" (vencidos, atrasadas), depois o nível 3 "Vence em breve"; dentro do nível, pela data mais próxima e, no empate, pelo maior valor (proposta, Pontos em aberto 1).
5. **Dado** uma categoria, **quando** aparece, **então** mostra o título com a contagem ("3 ordens de pagamento aguardam você"), o porquê numa linha ("R$ 38.420,00 no total; a primeira vence segunda-feira, 20/10."), até 5 itens com o link direto para a ação (o documento aberto na ação que resolve, ex.: o diálogo de aprovação) e "Ver todos" para a lista filtrada.
6. **Dado** que resolvi um item (aprovei, paguei, prorroguei), **quando** volto ao Início, **então** o item já não aparece: a pauta é calculada na leitura, sem tabela própria; ela também é recarregada ao voltar o foco para a aba e a cada 60 s.

**Tela**
7. **Dado** o Início (`/app`), **quando** abro, **então** a "Minha pauta" é o primeiro bloco, acima de "Próximos passos" e dos atalhos (proposta, Pontos em aberto 3); a contagem total aparece também no título da aba do navegador ("(4) Início").
8. **Dado** nada pendente, **quando** abro, **então** vejo "Nada pendente para você hoje." com os atalhos dos módulos.
9. **Dado** uma fonte que falhou ou passou de 300 ms, **quando** a pauta responde, **então** as outras categorias aparecem e a que falhou mostra "Não foi possível carregar {categoria}." com "Tentar de novo" (resposta parcial, a falha vai para o log com o tenant).

**Contrato para o painel do dono**
10. **Dado** o `IAgendaService.ForUserAsync(categories)` (BuildingBlocks), **quando** o painel do dono (DSH-01a) pede "OP aguardando sua decisão" e "Aditivo aguardando aprovação", **então** recebe os mesmos itens da pauta (mesma regra).

**Desempenho, permissões e segurança**
11. **Dado** 50 mil títulos, 5 mil OS, 50 mil orçamentos e todas as fontes registradas, **quando** o Proprietário abre o Início, **então** a pauta chega em menos de 1 s (cada fonte com uma consulta indexada com limite, as fontes chamadas em sequência no mesmo escopo, sem N+1; medição no teste de integração).
12. **[Segurança]** **Dado** usuários de cada grupo do seed, **quando** abrem a pauta, **então** cada um só vê as categorias da tabela do CA 2 para as suas permissões efetivas (∩ módulos liberados); nenhum item é de documento que ele não pode abrir; a resposta não é cacheada entre usuários (cache opcional por `{tenantId}:{userId}:{permVersion}` de no máximo 30 s) e leva `Cache-Control: no-store`.
13. **Dado** dois tenants, **quando** os usuários abrem a pauta, **então** cada um só vê itens do próprio tenant (teste).
14. **Dado** o endpoint `/api/v1/me/agenda`, **quando** chamado sem login, **então** 401; é rota do usuário (sem `RequireModule`), e cada fonte confere a própria camada 1 a 3 (teste de arquitetura: toda fonte declara módulo e permissão).

## Regras de negócio e técnicas
- **Nada gravado:** a pauta é uma leitura; nenhuma tabela nova, nenhuma notificação nova (o sino continua com as notificações existentes).
- **Cada módulo é dono do seu detector** (implementação no Application do módulo, registrada no DI), sem ler tabela de outro módulo; o Host só agrega e ordena.
- **Link** sempre relativo (`/app/...`), validado como o de `shared.notifications` (FND-07).
- **"Hoje"** pelo `BusinessTime.Today`; dinheiro `numeric(18,2)`.

## Permissões, eventos e tabelas
- **Permissões:** as da tabela do CA 2 (nenhuma nova).
- **Eventos:** nenhum.
- **Contratos:** `IAgendaSource` e `IAgendaService` (BuildingBlocks, **propostos**).
- **Tabelas:** nenhuma.

## Telas e estados
- **Início** (`/app`): bloco "Minha pauta" com as categorias em cartões (nível 1 com a faixa lateral âmbar, nível 2 vermelha), cada item com o verbo da ação ("Aprovar", "Pagar", "Prorrogar validade", "Gerar solicitação").
- **Estados:** carregando (skeleton de 3 cartões); vazio (CA 8); erro geral com "Tentar de novo"; erro parcial por categoria (CA 9).
- **Propositivo:** cada item abre o documento já na ação que resolve; "Ver todos" abre a lista filtrada.
- **400 px:** cartões empilhados, cada um com a contagem e os 3 primeiros itens.
- **Ajuda:** atualizar `Ajuda/inicio.md` (seção "Minha pauta": o que entra, ordem, por que um item some); `<Dica>` em "Destrava outra pessoa ou dinheiro".

## Plano de testes
- **Unidade:** ordenação por nível, data e valor (CA 4); montagem do porquê (CA 5).
- **Integração (Testcontainers):** cada fonte com dataset próprio e o usuário de cada grupo do seed (CA 2, 12); item some depois de resolvido (CA 6); fonte com falha e com atraso (CA 9); `IAgendaService` igual à pauta (CA 10); desempenho (CA 11); dois tenants (CA 13); sem login (CA 14).
- **Arquitetura:** toda implementação de `IAgendaSource` declara módulo e permissão.
- **E2E (Playwright):** Proprietário vê OP aguardando, aprova pelo link e a linha some; Operador sem itens vê o vazio; tela a 400 px.

## Fora do escopo
- Configurar a pauta (esconder categoria, adiar item): quando o PO pedir.
- Pauta por e-mail diário: o aviso diário de vencidos já existe (FIN-06a).
- Fontes de RH (ponto, folha) e Patrimônio (manutenção): entram nas stories da Fase 6.

## Pontos em aberto
1. **Ordem "do que mais destrava"** (PO): proposta do CA 4 (aprovações e pagamentos, depois atrasos, depois o que vence; data e valor no empate).
2. **Categorias e limites do MVP** (PO): proposta da tabela do CA 2 (3 dias para títulos a pagar, 7 dias para orçamentos; vencidos para quem tem `finance.edit`).
3. **Início** (PO): a pauta passa a ser o primeiro bloco do Início, acima dos "Próximos passos" da ADR 0010 item 9. Alternativa: um item próprio "Minha pauta" no dock (área Visão geral).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, usuários do seed, dois tenants), arquitetura e E2E passando.
- [ ] Nenhuma migration.
- [ ] Permissões só do `ModuleCatalog`; cada fonte com as 3 camadas.
- [ ] Tela com estados (inclusive o parcial) e 400 px.
- [ ] Ajuda do Início atualizada; teste de cobertura passando.
- [ ] Cada item leva à ação que resolve.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IAgendaSource`, `IAgendaService` e o agregador com tempo limite por fonte.
- [ ] Fontes dos módulos já entregues (Finance, Commercial, Production, Inventory, Purchasing, HR).
- [ ] Endpoint `/api/v1/me/agenda`; bloco no Início e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (contrato em BuildingBlocks), @qa, design (Início).
- Gate: CA 2, 6, 11 e 12 obrigatórios.

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

- **Correções feitas na validação:** fonte ainda não entregue não é registrada (CA 3), para a story não depender de toda a Fase 3 para começar; a pauta é leitura, sem tabela nem notificação nova; a dependência indevida com a DSH-03 saiu (validação cruzada).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
