# Execução depois do contrato: projeto de serviço, etapas, horas e resultado

| Campo | Valor |
|---|---|
| Situação | **Proposta** para decisão do PO (não implementar antes da decisão; vira ADR e stories no backlog depois do aceite) |
| Data | 27/11/2026 |
| Pedido | PO, 27/11/2026: "precisamos ter algo que vem depois do orçamento e do contrato para atender a operação do que estamos vendendo, com uma forma de controlar custos e ganhos [...] Na prestação de serviço faz sentido [o projeto]? Se não, o que estaria no lugar?" |
| Caso real | Tenant do próprio BemAqui: CTR-2026-0001 (setup/implantação do Conecta + mensalidade) para a BlockWay |
| Decisões do PO já tomadas | Prioridade antes do código da F2 do Conecta · modelos de etapas configuráveis, com o sistema sugerindo pelo perfil · projeto (ou equivalente) para Fábrica e Serviços, Comércio e E-commerce seguem com venda/pedido · horas por etapa com custo pelo cargo, alimentando Custos e Orçado × realizado · encaixe com a REC-01 (mensalidade = execução recorrente do mesmo contrato) |
| Fontes lidas | PRD §4, §5.4–5.8, §5.11 · `02-arquitetura` · `03-modelo-dados` (commercial, engineering, production) · `04-fluxos-estados` §1 (Orçamento, Contrato, Projeto, OS) · ADR 0046 · stories COM-01a/b, COM-04a/b/c, COM-05, COM-06, COM-07, COM-09, ENG-01, PRD-01, PRD-04, DSH-01b/02 · backlog "Pós-go-live: melhorias do produto" (REC-01) · `docs/pdv` (OFI-01..05, VND-02) · código: `Contract.cs`, `Milestones.cs`, `ProjectService.Lifecycle.cs`, `BusinessProfileCatalog.cs`, `BusinessVocabulary.cs`, `ModuleCatalog.cs`, `src/Web/src/pages/projects/*` |

---

## 1. Diagnóstico

### 1.1 O que acontece hoje com um contrato de serviço

1. **A aprovação do orçamento sempre gera contrato + projeto** (COM-04a, PRD COML-7). O projeto foi desenhado como "o que a fábrica entrega do contrato" (comentário da entidade `Project`): estados `Planned → InProgress → Fabricated → Installing → Delivered → Closed` e o indicador de etapa fixo **P Planejado · E Em produção · F Fabricado · I Instalando · T Entregue · C Encerrado** (`PROJECT_STAGES`).
2. **Ninguém tira o projeto de serviço de "Planejado".** `Planned → InProgress` só acontece quando a primeira etapa de uma **OS de fabricação** começa (PRD-03a). Item `Service` nunca gera OS (PRD-01, D14). Não há ação "Iniciar".
3. **A tela é de fábrica.** No cabeçalho aparece "Registrar fabricação concluída" (a condição é só `active && canEdit && inProduction && !byOrders`, sem olhar o tipo dos itens); a aba se chama "Itens e OS" e lista "OS de fabricação", "Romaneios" e "OS de instalação"; o indicador mostra "Em produção" e "Fabricado".
4. **Entrega e aceite técnico saem juntos.** `RegisterDeliveryAsync` publica `ProjectDelivered` **e** `TechnicalAcceptance` com a mesma data (D15, pensado para "venda sem instalação" de equipamento). Para serviço, entregar e o cliente aceitar são momentos diferentes, e é comum haver parcela em cada um.
5. **Marco de fabricação trava a entrega.** Se o orçamento usou o exemplo padrão de marcos (30% assinatura, 40% fabricação concluída, 30% aceite técnico), `RegisterDeliveryAsync` responde 409 `fabrication_pending` "Registre a fabricação concluída antes da entrega." O usuário de serviço é obrigado a registrar uma "fabricação".
6. **O vocabulário de gatilhos é só de fábrica.** `TriggerEvent` tem 4 valores: `Signature`, `FabricationDone`, `Delivered`, `TechnicalAcceptance`. Não há "ao concluir a etapa X".
7. **Não há etapas de serviço nem horas.** O apontamento de horas (`production.labor_entries`, PRD-03a) e o custo pelo cargo (PRD-04) existem, mas pendurados na **OS de produção**, e o módulo `production` está **fora do perfil Serviços** (ADR 0046: `services` = Core + `quotes`, `projects`, `catalog`, `sales`). Para o perfil Serviços, a mão de obra orçada (`quote_item_costs` com `cost_type = Labor` e `position_id`) nunca tem realizado: o Orçado × realizado de mão de obra fica eternamente zerado.

### 1.2 O que já está certo e deve ficar

- **Orçamento → contrato imutável → recebíveis por marco** (COM-01b, COM-04a/b): serve igual para serviço.
- **O registro de gatilhos no Finance** (`finance.project_trigger_events`, ADR 0019 §1) já tolera eventos fora de ordem. Basta ganhar a chave da etapa.
- **Orçado × realizado por natureza** (`project_budget`, `project_costs` como razão de diferenças, ADR 0025), **Resultado por projeto** do Painel do proprietário (DSH-01b) e DRE por projeto (FIN-13): qualquer custo que entrar no `project_costs` com o `project_id` aparece sozinho.
- **Custo-hora do cargo** pelo contrato `IHourlyCost` do RH (sem salário exposto, LGPD) e o **snapshot no fechamento** (PRD-04).
- **Vínculo usuário ↔ funcionário** (USR-01): o consultor que lança as próprias horas já é um funcionário com cargo.
- **Padrões de UI reaproveitáveis:** o editor de etapas do roteiro (ENG-01: letra A–Z, nome, horas padrão, checklist até 30 itens, "exige aprovação"), o `StageIndicator` (display de andar), o modelo configurável da OS de instalação (COM-06, P-22: `installation_template` com etapas e checklist), o checklist linha a linha (`production_order_checklist`) e o aceite com código por e-mail do portal (POR-02).

### 1.3 A hipótese inicial, validada contra a evidência

| Parte da hipótese | Veredito | Evidência |
|---|---|---|
| "O contrato é o centro do resultado" | **Em parte.** Hoje o centro de custo é o **projeto** (`project_budget`, `project_costs`, DRE por projeto, gatilhos por `project_id`). O contrato tem **um** projeto. Mudar o centro para o contrato com N execuções seria refazer COM-04a/b, COM-07, COM-09, DSH-02 e DSH-01b. | `03-modelo-dados` commercial; ADR 0025; COM-04b CA 6–8 |
| Recomendação | O **projeto continua sendo onde o custo cai**; o **contrato consolida** projeto + assinatura (REC-01) num painel "Resultado do contrato". | — |
| "Cada item vendido gera um tipo de execução" | **Derrubada como regra por item.** Um contrato com 5 itens de serviço viraria 5 projetos para acompanhar e 5 orçados para dividir. O tipo é **por contrato** (um projeto), decidido pelos itens. A exceção é o item **recorrente** (REC-01), que não é projeto: é a assinatura, dentro do mesmo contrato. | Odoo cria projeto por produto, mas permite agrupar no mesmo projeto do pedido; Omie e Conta Azul tratam o contrato inteiro |
| "Ordem de serviço avulsa e pedido de venda como tipos de execução do contrato" | **Derrubada.** Não nascem de contrato: a OS da oficina (OFI-01..05, módulo `service_orders`) e o pedido de venda (VND-02) são **portas de entrada próprias**, sem orçamento formal nem marcos, e pagam pelo `Sales`. Ficam no mapa "o que vem depois da venda" por perfil (§2.4), não dentro do contrato. | PRD do PDV §3.3 e §4.2 |
| "Tipo sugerido pelo perfil e pelo tipo do item" | **Confirmada.** `quote_items.kind` (Equipment/Product/Service) + `ItemType` do estoque + perfis do tenant bastam para sugerir. | `Quote.cs`, `BusinessProfileCatalog` |
| "Marcos ligados a uma etapa" | **Confirmada**, e é o que dá sentido às etapas para o dinheiro. | PRJ-6, COM-04b |
| "Resultado por contrato e por execução" | **Confirmada**, com o projeto como execução e o contrato somando projeto + assinatura. | DSH-01b, REC-01 |

---

## 2. Conceito recomendado

### 2.1 Em uma frase

> **Todo contrato tem uma execução que acompanha o que foi vendido: o projeto. O projeto passa a ter um tipo, "Fabricação" ou "Serviço". O de serviço anda por etapas de um modelo que o sistema sugere, recebe as horas da equipe pelo custo do cargo e libera as parcelas quando a etapa combinada termina ou quando o cliente dá o aceite. Se o contrato tem mensalidade, ela é a assinatura do mesmo contrato (REC-01). O contrato mostra o resultado das duas.**

### 2.2 Nomes na interface (português claro, sem jargão)

| Conceito | Na tela | Observação |
|---|---|---|
| Execução do contrato | **Projeto** (o menu continua "Projetos") | Não criar nome novo: "projeto de implantação", "projeto da obra" já são como o cliente fala. "Execução" fica só neste documento |
| Tipo de execução | **Tipo: Fabricação / Serviço** (chip no cabeçalho e filtro na lista) | Só aparece para quem tem o perfil Serviços (ver §8) |
| Modelo de etapas | **Modelo de etapas** (Projetos → Modelos de etapas) | Para Fábrica continua o "Roteiro" da Engenharia, que é da OS, não do projeto |
| Etapa | **Etapa** (letra + nome, ex.: "K Kickoff") | Mesma letra no display de andar |
| Lançar horas | **Lançar horas** / **Minhas horas** | Verbo e confirmação: "Lançar horas" → "Horas lançadas" |
| Aceite do cliente | **Aceite do cliente** (no projeto de serviço) | O mesmo evento que na fábrica se chama "Aceite técnico" |
| Assinatura | **Assinatura** / **Mensalidade** | REC-01 |
| Resultado | **Resultado do contrato** e **Resultado do projeto** | Receita × custo × margem, prevista e atual |

### 2.3 Como fica a cadeia

```
Orçamento ──aprovar──► Contrato (imutável, aditivos)
                          ├─► Marcos de pagamento ─► Contas a receber
                          │      gatilho: data | assinatura | etapa concluída | entrega | aceite do cliente | fabricação concluída
                          ├─► Projeto (1 por contrato), tipo:
                          │      • Fabricação → itens → OS de fabricação (roteiro) → OS de instalação → aceite técnico   [como hoje]
                          │      • Serviço    → etapas do modelo → checklist + horas por etapa → entrega → aceite do cliente
                          │      custo: material, horas × custo do cargo, terceiros, despesas → project_costs → Orçado × realizado
                          └─► Assinatura (REC-01, 0..N itens recorrentes): mensalidade, reajuste, início por data ou evento do projeto
Resultado do contrato = Resultado do projeto + Resultado da assinatura (receita recorrente − custos apropriados ao contrato)
```

### 2.4 O que vem depois da venda, por perfil

| Perfil | O que se vende | O que vem depois | Onde está |
|---|---|---|---|
| Fábrica | Equipamento sob encomenda | Projeto **Fabricação** (OS, roteiro, romaneio, OS de instalação) | Hoje, sem mudança |
| Fábrica | Contrato de manutenção mensal | **Assinatura** (REC-01) do contrato | REC-01 |
| Serviços | Implantação, consultoria, obra, projeto com fim | Projeto **Serviço** (etapas, horas, entrega, aceite) | **Esta proposta** |
| Serviços | Mensalidade, suporte, SLA | **Assinatura** (REC-01) | REC-01 |
| Serviços / oficina | Conserto avulso de veículo ou equipamento | **OS de serviço** (sem contrato) | OFI-01..05 (`service_orders`) |
| Comércio, E-commerce | Mercadoria | **Venda** / **Pedido de venda** | VND-01, VND-02 |

---

## 3. Benchmark (piso de mercado)

Pesquisa de 27/11/2026. Onde a documentação pública não confirmou, está marcado.

| Recurso | Omie | Conta Azul | Bling | Odoo | Runrun.it | Artia | Teamwork |
|---|---|---|---|---|---|---|---|
| Execução criada da venda | OS do contrato | — | OS | **Projeto/tarefa na confirmação do pedido** | — | — | — |
| Etapas/estágios configuráveis e modelo de projeto | não achado | não achado | status da OS | **Sim** (estágios livres, modelo de projeto) | Sim | Sim | Sim |
| Horas (timesheet) por tarefa, cronômetro e manual | não confirmado | não confirmado | não confirmado | **Sim** | **Sim** | **Sim** | **Sim** |
| Custo-hora por pessoa, restrito por permissão | — | — | — | Sim | **Sim** (vigência por data) | **Sim** (permissão "Financeiro") | **Sim** (só admin) |
| Forma de faturar: fixo, por marco, por horas | recorrente + avulso | recorrente | — | **Fixo, marcos ou horas, no produto** | — | — | Fixo, horas, retainer |
| Orçado × realizado / rentabilidade do projeto | só previsão de faturamento | centro de custo | — | **Sim** | **Sim** (estouro) | **Sim** (previsto × realizado) | **Sim** |
| Contrato recorrente | **Sim** | **Sim** | não achado | **Sim** (Subscriptions) | — | — | retainer |
| Aceite/assinatura do cliente | — | — | — | Field Service (relatório assinado) | — | — | — |

Fontes: Omie — [contrato de serviço](https://ajuda.omie.com.br/pt-BR/articles/498939-o-que-e-um-contrato-de-servico), [cadastrando contrato](https://ajuda.omie.com.br/pt-BR/articles/498998-cadastrando-um-novo-contrato-de-servico), [faturamento adicional](https://ajuda.omie.com.br/pt-BR/articles/3719954-faturamento-adicional-de-um-contrato), [relatórios de contratos](https://ajuda.omie.com.br/pt-BR/articles/15862194-conhecendo-os-relatorios-de-contratos-de-servico) · Conta Azul — [contratos recorrentes](https://contaazul.com/funcionalidades/contratos-recorrentes/), [centro de custo](https://contaazul.com/funcionalidades/gestao-de-compras-e-fornecedores/) · Bling — [ordem de serviço](https://blog.bling.com.br/ordem-de-servico/) · Odoo — [pedido → tarefa](https://www.odoo.com/documentation/13.0/applications/services/project/advanced/so_to_task.html), [estágios](https://www.odoo.com/documentation/18.0/applications/services/project/tasks/task_stages_statuses.html), [faturar por marcos](https://www.odoo.com/documentation/17.0/applications/sales/sales/invoicing/milestone.html), [faturar por horas](https://www.odoo.com/documentation/16.0/applications/sales/sales/invoicing/time_materials.html), [rentabilidade](https://www.odoo.com/documentation/13.0/applications/services/project/advanced/profitability.html), [Subscriptions](https://www.odoo.com/documentation/latest/applications/sales/subscriptions.html), [Field Service](https://www.odoo.com/documentation/master/applications/services/planning/field_service.html) · Runrun.it — [orçado × realizado](https://blog.runrun.it/orcado-x-realizado/), [página do projeto](https://blog.runrun.it/nova-pagina-de-um-projeto-do-runrun-it/) · Artia — [apontamentos](https://ajuda.artia.com/pt/articles/24278-o-que-sao-apontamentos), [custo de mão de obra](https://ajuda.artia.com/pt/articles/24742-calculo-de-custos-de-mao-de-obra) · Teamwork — [rentabilidade](https://support.teamwork.com/projects/reports/profitability-report), [valores por projeto](https://support.teamwork.com/projects/finance/project-rates), [valores por usuário](https://support.teamwork.com/projects/finance/user-rates).

**Piso (não propor menos):**
1. Execução criada da venda aprovada, com etapas configuráveis e modelo reaproveitável → EXE-01..03.
2. Horas por etapa e pessoa, manual e com cronômetro → EXE-04.
3. Custo-hora por pessoa visível só com permissão → já temos (cargo + `projects.view_cost`).
4. Faturar por preço fixo e por marco → já temos; **marco por etapa** → EXE-05. **Faturar por horas** → EXE-09 (ver pergunta 4).
5. Orçado × realizado em horas e em valor, com alerta de estouro, e rentabilidade → EXE-04 e EXE-07.
6. Contrato recorrente com previsão de receita → REC-01.

**Onde superamos:** aceite do cliente com termo e registro de quem aceitou (EXE-06; no mercado só o Field Service do Odoo e módulos de terceiros), marco de pagamento que só libera com a etapa feita (o Odoo tem marco, mas marcado à mão) e resultado do contrato somando projeto e mensalidade.

---

## 4. Ciclo de vida de cada tipo

### 4.1 Projeto de Fabricação (sem mudança)

Como no `04-fluxos-estados` §1 "Projeto": `Planned → InProgress → Fabricated → Installing → Delivered → Closed`, indicador **P E F I T C**, OS de fabricação, romaneio, OS de instalação e aceite técnico pela OS.

### 4.2 Projeto de Serviço (novo)

```
Planned ──Iniciar execução / primeira etapa iniciada──► InProgress
InProgress ──etapa de Entrega concluída / "Registrar entrega"──► Delivered (aguardando aceite, se houver)
Delivered ──"Registrar aceite do cliente" / etapa de Aceite concluída──► Delivered + accepted_on (publica TechnicalAcceptance)
Delivered ──"Registrar recusa" (motivo) ──► InProgress (volta à etapa escolhida; o histórico guarda a recusa)
Delivered + aceito (ou sem etapa de aceite) + parcelas quitadas ──► Closed
Closed ──baixa estornada──► Delivered
Planned | InProgress ──cancelar contrato──► Cancelled
```

- **Reaproveita os estados que existem** (`Planned`, `InProgress`, `Delivered`, `Closed`, `Cancelled`); `Fabricated` e `Installing` não se aplicam ao tipo Serviço. O aceite vira o campo `accepted_on` (e quem aceitou), não um estado novo: evita mexer em todos os consumidores do enum.
- **Indicador de etapa:** com modelo, as letras e nomes das etapas do modelo (ex.: **K** Kickoff · **L** Levantamento · **C** Configuração · **T** Treinamento · **G** Go-live · **A** Aceite). Sem modelo (EXE-01): **P** Planejado · **X** Em execução · **E** Entregue · **A** Aceito · **C** Encerrado.
- **Eventos publicados:** `ProjectStarted` (novo, informativo), `ProjectStageCompleted` (novo, `{ projectId, contractId, stageCode, occurredOn }`), `ProjectDelivered` e `TechnicalAcceptance` (existentes, agora em momentos separados). No tipo Serviço, a entrega **também publica `ProjectFabricated`** para que contrato antigo com marco "Fabricação concluída" não fique preso (compatibilidade; o orçamento novo de serviço não oferece mais esse gatilho).
- **Contrato `Completed`** (COM-04b CA 14): para Serviço, "projeto entregue" passa a ser "entregue **e** aceito" quando o projeto tem etapa de aceite ou o contrato tem marco de aceite; sem isso, como hoje.

### 4.3 Assinatura (REC-01, encaixe)

`Agendada (início por data ou evento) → Ativa (gera título por competência) → Suspensa ↔ Ativa → Cancelada | Encerrada`. O **início por evento** usa o mesmo vocabulário dos marcos: "na entrega do projeto", "no aceite do cliente" ou **"ao concluir a etapa Go-live"** (EXE-05). Assim a mensalidade da BlockWay começa quando o setup é aceito, sem ninguém lembrar.

### 4.4 OS de serviço avulsa e pedido de venda

Fora desta proposta (OFI-*, VND-02). Recomendação única: quando a OFI-01 for desenhada, ela usa o **mesmo cadastro de Modelos de etapas** (tipo "OS de serviço") e o mesmo componente de lançamento de horas, para não haver dois jeitos de fazer a mesma coisa.

---

## 5. Como o orçamento e o contrato decidem o tipo

1. **Sugestão no orçamento** (campo "Como será executado", visível só para tenant com perfil Serviços):
   - algum item `Equipment`, ou `Product` com roteiro, e o tenant tem perfil Fábrica → **Fabricação**;
   - todos os itens não recorrentes são `Service` → **Serviço**;
   - item recorrente (REC-01) → vai para a **Assinatura**, não decide o tipo;
   - só itens recorrentes → contrato **sem projeto** (só assinatura). Hoje a aprovação sempre cria projeto; com a REC-01 isso passa a ser opcional (pergunta 2).
   - misto (fabricação + serviço) → **Fabricação**: a parte de serviço entra como item do projeto (como a instalação hoje).
2. **Modelo de etapas sugerido** (tipo Serviço), nesta ordem: o modelo ligado ao item de serviço principal (como o roteiro ligado ao produto, `routing_template_items`) → o modelo padrão do segmento do perfil → o modelo padrão do tenant. O usuário troca enquanto o orçamento é rascunho e, depois, enquanto o projeto está `Planned` sem etapa iniciada.
3. **Horas previstas preenchem o custo** (propositivo): "Preencher mão de obra pelas etapas" cria as linhas `Labor` do item com horas do modelo × custo-hora do cargo sugerido de cada etapa. O usuário ajusta; o sistema nunca grava sem o clique.
4. **Na aprovação:** o contrato congela o tipo e o modelo no snapshot; o projeto nasce com `execution_kind` e com as etapas **copiadas** do modelo (mudar o modelo depois não muda projeto existente, como roteiro → OS).

---

## 6. Modelos de etapas propostos (seeds por perfil e segmento)

Cada etapa: letra, nome, horas previstas, cargo sugerido, checklist (até 30 itens), **papel** (Execução, Entrega ou Aceite do cliente) e "exige aprovação". Seeds marcados "Sugerido — ajuste ao seu jeito", com `seed_key` para não voltarem se o tenant excluir (padrão ENG-01).

| Modelo | Perfil / uso | Etapas (letra, nome, papel, horas) | Marcos sugeridos |
|---|---|---|---|
| **Implantação de software** | Serviços (padrão do BemAqui/Conecta) | K Kickoff (4 h) · L Levantamento e configuração (16 h) · I Integrações e dados (16 h) · T Treinamento (6 h) · G Go-live — **Entrega** (4 h) · A Aceite do cliente — **Aceite** (1 h) | 50% assinatura · 50% ao concluir "A Aceite"; mensalidade começa no aceite |
| **Consultoria / projeto com entregáveis** | Serviços | D Diagnóstico (8 h) · P Plano de ação — **Entrega** parcial (8 h) · E Execução acompanhada (24 h) · R Relatório final — **Entrega** (6 h) · A Aceite — **Aceite** | 30% assinatura · 30% ao concluir "P" · 40% no aceite |
| **Obra / instalação** | Serviços (e opcional para Fábrica, pergunta 5) | V Vistoria (2 h) · M Mobilização (4 h) · X Execução (40 h, checklist de segurança) · T Testes (4 h) · E Entrega técnica — **Entrega** (2 h) · A Termo de aceite — **Aceite** | 40% assinatura · 40% ao concluir "X" · 20% no aceite |
| **Manutenção programada** (preventiva com escopo) | Serviços | P Programação (1 h) · V Visita e checklist (3 h) · R Relatório — **Entrega** (1 h) | 100% na entrega (ou pela assinatura, se recorrente) |

Fábrica: **nenhum seed novo**. O roteiro da Engenharia continua sendo o modelo de etapas da OS.

---

## 7. Marcos ligados a etapas, aceite, horas e resultado

### 7.1 Marcos ligados a etapas
- `TriggerEvent` ganha **`StageCompleted`**; `quote_milestones`, `contract_milestones` e `contract_amendment_milestones` ganham `trigger_stage_code` (`char(1)`, obrigatório só com `StageCompleted`). O orçamento só oferece letras do modelo escolhido; trocar o modelo com marco apontando para letra que sumiu dá 422 no marco.
- O Finance grava o gatilho em `project_trigger_events` com a chave `(project_id, event, stage_code)` e libera o título `Planned → Open` com vencimento = data da conclusão + prazo do marco. Mesma regra de eventos fora de ordem da ADR 0019 §1.
- **Rótulos por tipo** no orçamento, contrato, PDF (`ContractPdfModel`) e "Aguarda: …" do título: Serviço mostra "Ao concluir a etapa {letra} {nome}", "Na entrega", "No aceite do cliente"; **não** oferece "Fabricação concluída". Fábrica continua igual.
- "Preencher marcos pelo modelo": sugere os marcos da tabela do §6 (o usuário ajusta, soma 100%).
- Etapa concluída que libera parcela mostra no diálogo "Libera a parcela {marco} de {valor}, vencendo em {data}." (mesmo padrão da COM-04b CA 9). Estornar a conclusão de etapa com parcela já liberada: só com `projects.approve`, e o título já aberto não volta (decisão manual do financeiro, como o estorno da OS).

### 7.2 Aceite do cliente
- **v1 (EXE-01):** "Registrar aceite do cliente" com data, nome de quem aceitou e anexo opcional (termo assinado), `projects.approve`.
- **v2 (EXE-06):** **termo de aceite em PDF** gerado pelo sistema (etapas, checklist, entregáveis, observações), enviado por e-mail; "Registrar recusa" com motivo volta o projeto à etapa escolhida (padrão da reprovação de qualidade, PRD-05). O aceite **por link com código por e-mail** reaproveita o mecanismo da POR-02 (nome digitado, IP, user-agent, hash do PDF) quando o Portal (Fase 8) estiver no plano do tenant.

### 7.3 Horas e custo
- **Lançamento** `commercial.project_time_entries`: projeto, etapa, funcionário (HR, pelo vínculo da USR-01), data, minutos, descrição, origem (Manual/Cronômetro), quem lançou, "lançado por {usuário}" quando for para outro, correção com motivo. Retroativo até N dias (parâmetro, padrão 7, como a PRD-03a).
- **Custo** = minutos × `IHourlyCost` do cargo do funcionário na data, **snapshot** no lançamento (ADR 0025); mudança do custo do cargo recompõe só o que não está fechado (mesma regra da PRD-04: `HourlyCostChanged`). Grava em `project_costs` com fonte nova `TimeEntry` / `TimeEntryCorrection`, natureza `Labor`. Sem custo do cargo: lança as horas com o aviso "Sem custo-hora" e a pendência aparece em "Minha pauta" de quem tem `hr.edit`.
- **Telas:** aba **Horas** do projeto (previsto × lançado por etapa e por pessoa, barra de estouro em âmbar acima de 90% e vermelho acima de 100%); **Minhas horas** (semana do usuário, lançamento rápido com projeto e etapa sugeridos pelos últimos usados, cronômetro que sobrevive a recarregar a página). O valor em reais só aparece com `projects.view_cost`; sem ela, só horas.
- **Fábrica:** nada muda; as horas da OS continuam pela PRD-03a.

### 7.4 Onde o resultado aparece
| Lugar | O que mostra | Story |
|---|---|---|
| Projeto → Resumo e Custos | Orçado × realizado por natureza (Mão de obra passa a ter realizado no tipo Serviço), margem prevista × atual | EXE-04 (o resto já existe) |
| Projeto → Horas | Horas previstas × lançadas por etapa e pessoa, estouro | EXE-04 |
| **Contrato → Resultado** (novo) | Receita contratada (marcos + mensalidades emitidas), faturada, recebida, custo (projeto + custos lançados no contrato), margem prevista × atual, MRR do contrato | EXE-07 (parte da assinatura depois da REC-01) |
| Painel do proprietário → Resultado por projeto (DSH-01b) | Já recebe; ganha filtro por tipo (Fabricação/Serviço) | EXE-07 |
| Finanças → DRE por projeto (FIN-13) | Já recebe (lançamentos com `project_id`); assinatura em conta própria (REC-01) | — |

---

## 8. O que muda para a Konebras (Fábrica)

**Nada visível.** Garantias:
- `projects.execution_kind` entra com padrão `Fabrication` (migration só `ADD COLUMN` com default; nenhuma coluna apagada). Backfill: tenant **com** perfil `factory` → todos `Fabrication`; tenant **sem** `factory` → `Service` quando todos os itens do projeto são `Service` e não há OS de fabricação nem de instalação (é o caso da BlockWay).
- Tudo o que é de serviço fica atrás do **recurso por perfil** `projects.service_execution` = `[services]` no `BusinessProfileCatalog.Features` (adendo da ADR 0046: fora do perfil, some da tela e a API responde 403 `module_not_released`). A regra do catálogo ("nunca liste `factory` sem decisão") vale: a Konebras só verá se o PO decidir (pergunta 5).
- Enum `TriggerEvent` ganha `StageCompleted` (aditivo; ninguém da fábrica usa). Eventos novos são aditivos; consumidores existentes não mudam.
- **Teste de regressão** sobre cópia do banco `konebras` (padrão da PRD-04): tela do projeto, liberação de parcelas e Resultado por projeto iguais antes e depois.
- Vocabulário: **nenhuma chave nova** no `BusinessVocabulary` (teto de 30). O rótulo muda pelo **tipo do projeto**, não pelo perfil, porque um tenant com Fábrica + Serviços pode ter os dois tipos.

---

## 9. Impacto em alto nível

### 9.1 Modelo de dados (tudo aditivo, sem migration destrutiva)
| Tabela | Mudança |
|---|---|
| `commercial.projects` | + `execution_kind` (Fabrication/Service, padrão Fabrication), `stage_template_id`, `started_on`, `accepted_on`, `accepted_by_name` (`[PersonalData]`), `acceptance_rejected_at` |
| `commercial.stage_templates` (nova) | name, kind (Service; reservado OS de serviço), segment (opcional), is_default, is_active, seed_key, soft delete, xmin |
| `commercial.stage_template_steps` (nova) | template_id, sequence, code (A–Z), name, role (Execution/Delivery/Acceptance), standard_hours `numeric(18,4)`, position_id (cargo sugerido), requires_approval, checklist jsonb |
| `commercial.stage_template_items` (nova) | template_id, item_id (único): modelo sugerido para o item de serviço |
| `commercial.project_stages` (nova) | project_id, sequence, code, name, role, standard_hours, position_id, status (Waiting/Ready/InProgress/Done), responsible_user_id, planned_start/end, started_at, completed_at, completed_by, requires_approval, approved_by |
| `commercial.project_stage_checklist` (nova) | stage_id, sequence, text, result (Done/NotApplicable), marked_at, marked_by |
| `commercial.project_time_entries` (nova) | ver §7.3 |
| `commercial.project_costs` | + fontes `TimeEntry`, `TimeEntryCorrection` |
| `commercial.quotes` | + `execution_kind`, `stage_template_id` |
| `commercial.quote_milestones`, `contract_milestones`, `contract_amendment_milestones` | + `trigger_stage_code` |
| `finance.receivables` | + `trigger_stage_code` |
| `finance.project_trigger_events` | + `stage_code`; único passa a `(project_id, event, coalesce(stage_code,''))` (troca de índice, não destrutiva) |
| `SharedKernel.TriggerEvent` | + `StageCompleted` |

Nenhum módulo novo e nenhuma tabela de outro módulo acessada: etapas, horas e modelos ficam no **Commercial** (dono do projeto); custo-hora pelo `IHourlyCost` (HR.Contracts); funcionário pelo `IEmployees`; Finance só pelos eventos.

### 9.2 Permissões (três camadas e `ModuleCatalog`)
- **Camada 1:** módulo `projects`, já liberado para Fábrica e Serviços. **Camada 1b:** recurso `projects.service_execution` (perfil Serviços).
- **Camada 2:** sem mudança (grupo habilita `projects`).
- **Camada 3:**
  - `projects.edit`: iniciar e concluir etapa, marcar checklist, escolher modelo no projeto `Planned`; cadastrar **Modelos de etapas** (precedente: modelo da OS de instalação com `production.edit`).
  - `projects.approve`: registrar entrega, aceite e recusa do cliente; concluir etapa que "exige aprovação"; estornar conclusão de etapa que liberou parcela.
  - **Nova ação `projects.log_time` "Lançar horas"** (ADR 0003, necessidade real: o consultor lança as próprias horas sem poder editar o projeto). Lançar para outra pessoa ou corrigir: `projects.edit`. **Seed patch** `projects-log-time`: concede aos grupos que já têm `projects.edit`.
  - `projects.view_cost`: valores em reais das horas e do resultado (como hoje).
- `ConnectedAppScopes` (`projects.view` "Ver projetos e a etapa em que estão") passa a expor a etapa do modelo: útil para o Conecta mostrar "sua implantação está na etapa Treinamento" (F2).
- Sem teste de padrão novo: o `EndpointAuthorizationTests` varre os endpoints.

---

## 10. Fatiamento em stories

Prefixo **EXE** (execução). Cada uma segue o fluxo do projeto: revisão (arquitetura, UX, cyber) antes de codar, ajuda da tela no mesmo commit, estados de vazio/carregando/erro.

| Ordem | Story | Tam. | Depende de | Entrega |
|---|---|---|---|---|
| 1 | **EXE-01 Projeto de serviço: tipo, tela própria, entrega e aceite separados** | M | — | **Destrava a BlockWay** (ver §10.1) |
| 2 | **EXE-02 Modelos de etapas** | M | EXE-01 | Cadastro em Projetos → Modelos de etapas (editor no padrão do roteiro ENG-01: letra, nome, papel, horas, cargo, checklist, aprovação); seeds do §6 por segmento; modelo sugerido no item de serviço; ajuda |
| 3 | **EXE-03 Etapas no projeto de serviço** | M | EXE-02 | Projeto copia o modelo; iniciar/concluir etapa, checklist, responsável e datas previstas; indicador com as letras do modelo; status do projeto derivado das etapas (Entrega → `Delivered`, Aceite → `accepted_on`); `ProjectStageCompleted`; "próximo passo" no cabeçalho ("Concluir Treinamento") |
| 4 | **EXE-04 Horas por etapa e custo pelo cargo** | M | EXE-03 | `project_time_entries`, `projects.log_time` + seed patch, custo por `IHourlyCost` com snapshot e recomposição, `project_costs` (`TimeEntry`), aba Horas, Minhas horas com cronômetro, estouro previsto × lançado; teste de integração (Testcontainers) do custo no projeto |
| 5 | **EXE-05 Marcos ligados a etapas e orçamento que sugere a execução** | M | EXE-03 (EXE-04 para "Preencher mão de obra") | `StageCompleted` + `trigger_stage_code` no orçamento, contrato, aditivo e Finance; "Como será executado" e modelo no orçamento; "Preencher marcos pelo modelo"; "Preencher mão de obra pelas etapas"; rótulos por tipo no PDF; teste de integração da liberação fora de ordem |
| 6 | **REC-01 Assinatura** (já no backlog) | G | EXE-05 recomendado | Início da mensalidade por evento do projeto, incluindo "ao concluir a etapa X"; contrato só de assinatura sem projeto (pergunta 2) |
| 7 | **EXE-06 Termo de aceite e recusa do cliente** | M | EXE-03 | PDF do termo, envio por e-mail, recusa com motivo e volta de etapa; aceite por link (POR-02) quando o Portal estiver no plano |
| 8 | **EXE-07 Resultado do contrato** | M | EXE-04 (parte da assinatura: REC-01) | Aba Resultado no contrato (projeto + assinatura), filtro por tipo no Resultado por projeto |
| 9 | **EXE-08 Execução de serviço na fábrica** (opcional) | P | EXE-03, decisão do PO (pergunta 5) | Liberar o recurso para `factory` (ex.: modernização, conserto sob contrato) |
| 10 | **EXE-09 Faturar por horas** (opcional) | M | EXE-04 | Marco "por horas aprovadas no período" (tempo e material), aprovação das horas faturáveis (pergunta 4) |

Ordem recomendada: **EXE-01 → EXE-02 → EXE-03 → EXE-04 → EXE-05** antes do código da F2 do Conecta; depois, no bloco pós-go-live, **REC-01 → EXE-06 → EXE-07** e, por decisão, EXE-08/09. EXE-02 e o desenho da REC-01 podem andar em paralelo (sem arquivo em comum).

### 10.1 EXE-01, a correção mínima (destrava a BlockWay)

Critérios de aceite em resumo:
1. `projects.execution_kind` com backfill (§8). Projeto novo: `Service` quando o tenant tem o recurso `projects.service_execution` e todos os itens não recorrentes do orçamento são `Service`; senão `Fabrication`. O CTR-2026-0001 da BlockWay vira `Service` sem ação manual.
2. Projeto **Serviço** mostra: chip "Serviço", indicador **P X E A C** (Planejado · Em execução · Entregue · Aceito · Encerrado), aba "Itens" sem OS/romaneio/cronograma, **sem** "Registrar fabricação concluída" (a API responde 409 `not_a_fabrication_project` "Projeto de serviço não tem fabricação.").
3. **"Iniciar execução"** (`projects.edit`, data ≤ hoje): `Planned → InProgress`, publica `ProjectStarted`.
4. **"Registrar entrega"** (`projects.approve`): `→ Delivered`, publica `ProjectDelivered` e, só no tipo Serviço, `ProjectFabricated` (compatibilidade com marco antigo de fabricação). **Não** publica mais o aceite junto; o diálogo traz "O cliente já deu o aceite nesta data" marcado quando o contrato não tem marco de aceite separado.
5. **"Registrar aceite do cliente"** (`projects.approve`, data ≥ entrega e ≤ hoje, nome de quem aceitou, anexo opcional): grava `accepted_on`, publica `TechnicalAcceptance`; diálogo mostra a parcela que libera.
6. Conclusão do contrato no Serviço exige entregue + aceito (quando há marco de aceite) + parcelas quitadas.
7. Orçamento de serviço: o gatilho "Fabricação concluída" não é oferecido; "Aceite técnico" aparece como "Aceite do cliente" no orçamento, contrato, PDF e chip "Aguarda: …".
8. Projeto **Fabricação** e a tela da Konebras idênticos (teste de regressão).
9. Ajuda `projetos-projeto.md` com seção "Projeto de serviço" e `<Dica>` em "Registrar aceite do cliente".

Fora da EXE-01: etapas, modelos, horas e marcos por etapa (EXE-02..05). Até lá, a BlockWay usa **Iniciar → Registrar entrega → Registrar aceite**, e a mão de obra pode ser lançada como custo manual no projeto (contas a pagar com `project_id`) se for urgente.

---

## 11. Riscos

- **Dois "jeitos de etapa"** (roteiro da OS e modelo do projeto): mitigado por papéis claros (roteiro = como fabricar uma unidade; modelo = como executar o contrato) e pelo mesmo componente de editor. Unificar a OS de instalação com o modelo de etapas fica como evolução, com ADR.
- **Lançamento de horas exige funcionário e cargo**: para o perfil Serviços o RH é "opcional" (cadeado). Ver pergunta 3.
- **Marcos com letra de etapa** quebram se o modelo do projeto trocar depois do contrato: a troca do modelo só vale com `Planned` e sem marco apontando para letra que sumiria (409 com a lista dos marcos).
- **Aceite ≠ entrega** muda a conclusão do contrato de serviço: projeto entregue sem aceite fica em "Aguardando aceite" na Minha pauta do responsável, para não esquecer.

---

## 12. Perguntas para o PO

Só as que mudam o desenho, cada uma com a recomendação.

1. **Quantas stories antes da F2 do Conecta?** O pedido foi "esta correção vem antes". **Recomendo EXE-01 a EXE-05 antes** (sem etapas e horas a pergunta "quanto custou a implantação da BlockWay?" continua sem resposta) e EXE-06/07 junto com a REC-01, no bloco pós-go-live. Alternativa enxuta: só EXE-01 antes, e o resto depois da F2.
2. **Contrato só de mensalidade cria projeto?** Hoje toda aprovação cria projeto. **Recomendo não criar** quando o orçamento tem só itens recorrentes (a execução é a assinatura); muda a COM-04a e o critério de conclusão do contrato, então fica na REC-01.
3. **Horas exigem o RH liberado?** O custo pelo cargo vem do RH, que no perfil Serviços é opcional (cadeado). **Recomendo**: lançar horas exige funcionário com cargo; se o RH não estiver no plano do tenant, o RH fica liberado em modo "só cadastro de funcionário e cargo" para quem tem o recurso `projects.service_execution` (sem ponto, folha nem dados sensíveis). Alternativa: custo-hora no próprio usuário, mas aí haveria duas fontes de custo de mão de obra no produto.
4. **Faturar por horas (tempo e material) entra?** Odoo e Teamwork têm; Omie, Conta Azul e Bling não. **Recomendo deixar como EXE-09 depois** e começar por preço fixo + marcos por etapa, que cobrem a BlockWay e a maioria das pequenas empresas de serviço; reabrir quando houver um cliente que cobra por hora.
5. **A Konebras (fábrica) pode usar projeto de serviço?** (ex.: modernização ou conserto sob contrato, sem OS de fabricação). **Recomendo não liberar agora** (nada visível muda para ela, como pedido) e decidir na EXE-08 com o caso real em mãos.
6. **Aceite do cliente por link já na primeira versão?** **Recomendo não**: v1 com registro interno + termo anexado (EXE-01) e termo em PDF por e-mail (EXE-06); o aceite por link reaproveita a POR-02 quando o Portal entrar no plano, sem abrir rota anônima nova agora (ADR 0009).
