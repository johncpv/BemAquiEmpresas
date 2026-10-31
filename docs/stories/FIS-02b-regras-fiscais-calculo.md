# FIS-02b — Regras fiscais por operação e cálculo dos tributos da nota

| Campo | Valor |
|---|---|
| ID | FIS-02b |
| Título | Regras fiscais por operação (venda de produção própria, revenda, prestação de serviço, remessa e retorno de obra, devolução), com CFOP, CST, alíquotas, condições (UF de destino, contribuinte ou não, NCM), vigência e versões; seed com as regras do mesmo conjunto pré-configurado da FIS-02a, marcadas "A confirmar com a contadora"; ativação por outra pessoa com `fiscal.approve`; motor puro que calcula ICMS, ICMS-ST, IPI, PIS, COFINS, ISS e IBS/CBS por item; "Simular" em painel lateral; DIFAL bloqueado |
| Tamanho | G (quebra da FIS-02) |
| Status | Done (conferência da contadora, casos de referência dela e frequência do DIFAL: pré-condição externa) |
| Requisitos | R-FIS-4 (regras fiscais por operação parametrizáveis, conteúdo da contadora) · R-FIS-3 (tributos por regra fiscal) · R-FIS-7 · R-FIS-8 (IBS/CBS, P-03) · R-PRJ-5 (impostos no realizado) · R-PRP-1 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-02a (valores pré-configurados, `IFiscalSettings`, `fiscal.approve`), CAD-02a (NCM, CEST e origem do item), CAD-01a (terceiro: UF, IE, contribuinte), FIS-01 (o EmissorFiscal não calcula imposto) |
| Bloqueia | FIS-03a (a emissão usa o cálculo), FIS-06 (regra de prestação de serviço) |
| Ordem de execução | 9 (Marco 5.4 "Nota de saída") |

## User story
Como **financeiro, junto com a contadora**, quero **encontrar as regras de cada operação já montadas com os valores propostos, conferir o cálculo antes de emitir e só então ativá-las por outra pessoa**, para **a nota sair com o imposto certo sem digitar CFOP e alíquota a cada vez, e o custo do imposto entrar na margem do projeto**.

## Contexto e referências
- PRD R-FIS-4 ("Regras fiscais por operação (CFOP, CST/CSOSN, alíquotas) parametrizáveis — conteúdo definido com a contadora"), R-FIS-8.
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): tabela 3.2, pontos 40 a 44 (e 9: IBS/CBS); D4 (venda mista), D8 (imposto realizado só pela nota), D12; §4.4 padrão 6 e o achado "FIS-02b: Simular em painel lateral".
- **Decisão do PO de 21/10/2026 (novo requisito):** as regras fiscais detalhadas nascem do mesmo seed da FIS-02a, marcadas "a confirmar". **ADR 0042** ([0042-configuracoes-fiscais-e-integracoes.md](../adr/0042-configuracoes-fiscais-e-integracoes.md)). **ADR 0025, adendo de 21/10/2026** ([0025-custo-real-do-projeto.md](../adr/0025-custo-real-do-projeto.md): fontes `InvoiceTax`/`InvoiceTaxReversal`). **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md): IBS/CBS no Emissor, lacuna L-b; DIFAL não suportado).
- `03-modelo-dados` → `fiscal.tax_rules` (`operation`, `cfop`, `cst_csosn`, parâmetros de ICMS/IPI/PIS/COFINS/IBS/CBS (JSON), `conditions`).
- EmissorFiscal (FIS-01): "o serviço transporta e assina; quem decide valores e códigos fiscais é o sistema cliente"; o payload aceita ICMS (com ST e FCP), PIS, COFINS e IPI por item; IBS/CBS entram pela lacuna L-b; **sem DIFAL**.
- `06-pendencias-riscos` P-01, P-02, P-03. `04-fluxos-estados` §3.3 ("Realizado … impostos (NF autorizadas)").

## Critérios de aceite
**Regras e seed**
1. **Dado** o seed `fiscal-tax-rules` (ADR 0016), **quando** o tenant é criado ou no próximo deploy, **então** nascem em `Draft`, com o chip **"A confirmar com a contadora"**, as regras montadas a partir dos valores pré-configurados da FIS-02a (`IFiscalSettings`), uma vez e só se o tenant não tiver regra:
   - **Venda de produção própria a contribuinte:** CFOP 5101 dentro de SP e 6101 fora; ICMS CST 00, 18% em SP e 12%, 7% ou 4% fora (pelo grupo da UF de destino e pela origem do item); PIS e COFINS CST 01, 0,65% e 3%; IPI CST 50 pela TIPI do NCM; IBS/CBS CST 000, `cClassTrib` 000001, CBS 0,9% e IBS 0,1%;
   - **Venda de produção própria a não contribuinte:** CFOP 5101 em SP (consumidor final) e 6107 fora, com a marca **DIFAL** (CA 8); demais como a anterior;
   - **Revenda de peça:** CFOP 5102 e 6102; tributos como a venda de produção, a confirmar;
   - **Prestação de serviço** (NFS-e, FIS-06): item 7.02 da LC 116, ISS 5% no município da obra, sem retenção, IBS/CBS como acima.
   Remessa para obra e retorno de obra **não** têm regra no seed (D4: só a venda na expedição); a tela oferece "Nova regra" para elas.
2. **Dado** `fiscal.edit`, **quando** cadastro ou edito uma regra com operação (Venda de produção própria, Revenda, Prestação de serviço, Remessa para obra, Retorno de obra, Devolução de compra, Outras saídas), descrição, natureza da operação (texto da nota), CFOP dentro e fora do estado, CST conforme o CRT (FIS-02a), modalidade e alíquota de ICMS (com alíquotas por grupo de UF e para origem importada), redução de base, ICMS-ST (MVA, alíquota interna), IPI (CST, enquadramento, alíquota ou "pela TIPI do NCM"), PIS e COFINS (CST e alíquotas), ISS (item da LC 116, alíquota, local de incidência, retenção) para serviço, IBS e CBS (CST, `cClassTrib`, alíquotas), tributos que entram no custo do projeto (CA 11), informação complementar padrão, condições (UF de destino, contribuinte, isento ou não contribuinte, lista ou prefixo de NCM) e vigência, e clico "Salvar regra fiscal", **então** a regra fica `Draft`, é auditada e o chip volta para "A confirmar com a contadora"; editar uma regra `Active` cria uma **nova versão** `Draft` (a ativa vale até a nova ser ativada) e a nota emitida guarda a versão usada.
3. **[Segurança]** **Dado** uma regra `Draft`, **quando** quem tem `fiscal.approve` clica "Ativar regra" depois de abrir o simulador, **então** a regra fica `Active` com quem ativou e quando; quem criou ou editou aquela versão não a ativa (403 `segregation_of_duties` "Quem editou a regra não a ativa."), exceto o Proprietário único (`SoleOwnerRule`, com a marca e a notificação); as regras do seed têm o sistema como autor, e qualquer pessoa com `fiscal.approve` as ativa; "Inativar regra" com `fiscal.approve` e motivo.
4. **Dado** `fiscal.approve` e uma regra (de qualquer situação) com o chip "A confirmar com a contadora", **quando** clico "Marcar como conferida", **então** a versão fica conferida com quem e quando (auditado), igual à conferência da FIS-02a; a emissão em **produção** (FIS-03a, FIS-06) usa só regra `Active` **e** conferida (`IFiscalSettings.ReadinessAsync` lista as que faltam); em homologação a regra ativa não conferida emite com o aviso.
5. **Dado** duas regras ativas que se aplicam à mesma operação, UF, tipo de destinatário e NCM no mesmo período, **quando** salvo ou ativo, **então** 422 "Esta regra se sobrepõe à regra {nome}." (a mais específica por NCM só é aceita se a outra não tiver NCM).
6. **Dado** `FiscalSettingsChanged` (FIS-02a) com mudança de regime, CRT ou alíquota padrão, **quando** chega, **então** as regras afetadas ganham o aviso "Os valores padrão mudaram em Configurações → Fiscal. Revise esta regra." e a lista mostra o filtro "A revisar"; nenhuma regra muda sozinha.

**Cálculo**
7. **Dado** o motor `TaxCalculator` (Domain, puro), **quando** recebe a operação, o emitente (UF, CRT), o destinatário (UF, IE, contribuinte, consumidor final), o item (NCM, origem, CEST, quantidade, valor unitário, desconto, frete e outras despesas rateados) e a regra ativa na data de emissão, **então** devolve por item CFOP, CST, bases, alíquotas e valores de ICMS, ICMS-ST, FCP, IPI, PIS, COFINS, ISS (serviço) e **IBS e CBS** (sempre que a regra tiver, assumidos obrigatórios, ponto 9), com arredondamento só no valor do item (2 casas, `AwayFromZero`) e totais da nota = soma dos itens; a tabela de casos de referência passa exatamente (casos sintéticos tirados da legislação e dos valores pré-configurados no CI, marcados como tais; os casos da contadora entram na mesma tabela quando chegarem).
8. **Dado** uma venda interestadual a não contribuinte (regra com a marca DIFAL), **quando** calculo, **então** o motor calcula e mostra o DIFAL, mas a emissão é **bloqueada** com "A nota com DIFAL ainda não é suportada pelo Emissor Fiscal. Fale com a contadora para emitir esta venda." (ponto 43); a Konebras informa a frequência dessas vendas (pré-condição externa, não bloqueia).
9. **Dado** nenhuma regra ativa para a combinação, **quando** calculo, **então** o motor recusa com "Não há regra fiscal para {operação} para {UF}/{tipo de cliente} e NCM {ncm}." e a nota não pode ser emitida (nunca usa padrão silencioso).
10. **Dado** `fiscal.view`, **quando** clico "Simular" na lista ou na regra, **então** abre o **painel lateral** de simulação (padrão de painel lateral da FND-09d) com operação, cliente, itens e quantidades (abre com o último item vendido), e vejo a nota simulada com a regra aplicada a cada item, as bases, os impostos, os totais e o que entra no custo do projeto, sem gravar nada e sem chamar o EmissorFiscal; o painel fecha sem perder a regra aberta; política de rate limit nomeada `fiscal-simulate` (30 por minuto por usuário).

**Imposto como custo (visibilidade financeira)**
11. **Dado** a regra, **quando** cadastro, **então** os tributos que entram como **custo do projeto** (`CostType.Tax`) vêm do padrão da FIS-02a (ICMS, PIS, COFINS, ISS e IBS/CBS; IPI fora; ponto 41) e podem ser ajustados na regra; a nota autorizada (FIS-03a, FIS-06) lança o realizado de impostos na razão do projeto (fonte `InvoiceTax`, ADR 0025 adendo) por essa marca; até a nota, o projetado usa o imposto do orçado (D8).

**Permissões e auditoria**
12. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar e editar sem `fiscal.edit`, ativar, inativar e marcar como conferida sem `fiscal.approve`: 403 e os botões somem; simular com `fiscal.view`; `RequireModule` e `perm:`; IDOR em `/tax-rules/{id}/versions/{versionId}`.
13. **Dado** qualquer alteração, ativação ou conferência, **quando** abro o histórico, **então** vejo antes e depois de cada campo, a versão, quem editou, quem ativou e quem conferiu.

## Regras de negócio e técnicas
- **`fiscal.tax_rules`** (03 + colunas): `operation`, `name`, `nature_of_operation`, `version`, `status` (`Draft`/`Active`/`Inactive`), `valid_from`, `valid_to`, `cfop_internal`, `cfop_interstate`, `crt_scope`, parâmetros por tributo (jsonb tipado: `icms` com alíquotas por grupo de UF e importado, `icms_st`, `fcp`, `ipi`, `pis`, `cofins`, `iss`, `ibs`, `cbs`, `difal`), `cost_taxes`, `complementary_info`, `conditions` (jsonb), `is_seed`, `needs_review`, `created_by`, `activated_by`, `activated_at`, `confirmed_by`, `confirmed_at`, `xmin`.
- **Percentuais** `numeric(7,4)`; bases e valores `numeric(18,2)`; arredondamento só no item.
- **Motor puro**, sem banco, com testes de tabela; a resolução da regra (mais específica por NCM, depois sem NCM) fica no Application (`TaxRuleResolver`).
- **Seed:** vem dos valores pré-configurados da FIS-02a, nunca de valor inventado fora deles; "a confirmar" até a conferência.
- **Grupos de UF** do ICMS interestadual a partir de SP: 12% para MG, RJ, PR, SC e RS; 7% para as demais UFs e o ES; 4% para item de origem importada (origem 1, 2, 3 ou 8).

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.view` (ver e simular), `fiscal.edit` (cadastrar e editar), `fiscal.approve` (ativar, inativar e marcar como conferida; reservada ao Proprietário, FIS-02a).
- **Eventos:** consome `FiscalSettingsChanged` (FIS-02a); nenhum publicado.
- **Tabelas:** `fiscal.tax_rules` (03 com as colunas acima); seed `fiscal-tax-rules`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** achado pontual "Simular em painel lateral"; botões de `EmptyState`/`ErrorState` com 44 px a 400 px.
- **Fiscal → Regras fiscais** (`/app/fiscal/regras`, entrada no `modules.ts`, grupo "Configuração fiscal"): lista por operação com versão, vigência, situação (Rascunho neutro, Ativa ok, Inativa neutro), chip de conferência e quem ativou; filtros "A confirmar" e "A revisar"; detalhe em seções por tributo (`/app/fiscal/regras/$id`); **Simular** em painel lateral (`?simular=1`, CA 10).
- **Verbos:** "Salvar regra fiscal" / "Regra fiscal salva"; "Ativar regra" / "Regra ativada"; "Inativar regra" / "Regra inativada"; "Marcar como conferida" / "Regra conferida"; "Simular".
- **Estados:** carregando; regras do seed em rascunho ("As regras abaixo foram montadas com os valores propostos. Simule, confira com a contadora e ative." + "Simular" e "Baixar para a contadora" da FIS-02a); vazio só quando o tenant apagou o seed por inativação ("Nenhuma regra ativa. Ative as regras antes de emitir a primeira nota." + "Nova regra fiscal"); erro no campo com foco no primeiro; modo leitura sem `fiscal.edit`; DIFAL com o aviso do CA 8.
- **Propositivo:** regras já montadas; CFOP fora do estado sugerido a partir do de dentro (5xxx → 6xxx) com "Confirme"; CST filtrado pelo CRT; o simulador abre com o último item vendido; depois de ativar, o `NextSteps` oferece "Simular uma nota" e "Ver romaneios a faturar" (FIS-03a).
- **400 px:** seções empilhadas; o painel de simulação ocupa a tela cheia com os itens em cartões.
- **Ajuda:** `Ajuda/fiscal-regras.md`, `Ajuda/fiscal-regra.md` (com a seção "Simular") (previstas); `<Dica>` em "CFOP", "CST", "MVA", "DIFAL", "Entra no custo do projeto", "IBS/CBS" e "A confirmar com a contadora".

## Plano de testes
- **Unidade:** `TaxCalculator` com a tabela de casos sintéticos (ICMS interno e interestadual por grupo de UF e importado, ST, IPI pela TIPI, PIS, COFINS, ISS, IBS/CBS, arredondamento e totais) (CA 7); DIFAL calculado e bloqueado (CA 8); sem regra (CA 9); resolução por especificidade e sobreposição (CA 5).
- **Integração (Testcontainers):** seed uma vez só e a partir dos valores da FIS-02a (CA 1); versão `Draft` → `Active` com segregação, autor sistema e Proprietário único (CA 2, 3); conferência e `ReadinessAsync` em produção e homologação (CA 4); aviso de revisão pelo `FiscalSettingsChanged` (CA 6); simulador sem gravar e rate limit (CA 10); marca de custo (CA 11); três camadas e IDOR (CA 12); auditoria (CA 13).
- **E2E (Playwright):** abrir as regras do seed, simular no painel lateral, ativar com outro usuário e marcar como conferida; tela a 400 px.

## Fora do escopo
- Emissão: FIS-03a e FIS-06. Crédito de imposto na entrada: FIS-04b (P-01). Importar tabela TIPI ou de alíquotas: fora do MVP. DIFAL na emissão: depende do Emissor, fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 40–44):
1. Regras e casos de teste: o seed traz as regras do conjunto proposto, "a confirmar"; os casos da contadora entram na tabela de referência quando chegarem, sem bloquear (CA 1, 7).
2. Tributos no custo do projeto: ICMS, PIS, COFINS, ISS e IBS/CBS; IPI fora, a confirmar (CA 11).
3. IBS/CBS: ver o ponto 9; assumidos obrigatórios, calculados sempre (CA 7).
4. DIFAL bloqueado; a Konebras informa a frequência (CA 8).
5. Segregação na ativação como proposta: quem editou não ativa, exceto o Proprietário único (CA 3).

Decisões da revisão aplicadas: novo requisito do PO (regras do mesmo seed, "a confirmar" e "Marcar como conferida", ADR 0042; CA 1, 4); D4 (sem regra de remessa para obra no seed; prestação de serviço para a NFS-e da instalação); D8 e ADR 0025 adendo (imposto realizado só pela nota, fonte `InvoiceTax`; CA 11); achado de design "Simular em painel lateral" (CA 10); §4.3 (rate limit nomeado na simulação, CA 10).

**Pré-condição externa:** conteúdo da contadora (regras, casos de referência, P-01, P-02, P-03) e a frequência do DIFAL (Konebras); não bloqueia o `Ready`, porque as regras vêm pré-configuradas e a conferência é feita na tela; a produção exige as regras conferidas (CA 4).

## Definition of Done
- [x] Critérios de aceite atendidos (CA 11 na parte desta story: a marca de custo por regra e o custo no cálculo; o lançamento na razão do projeto pela fonte `InvoiceTax` acontece na nota autorizada, FIS-03a e FIS-06).
- [x] Testes de unidade (casos sintéticos) e integração (Testcontainers) passando. E2E (Playwright): pendente, ver Change Log.
- [x] Migration criada (não destrutiva: só cria `fiscal.tax_rules`); seed `fiscal-tax-rules` idempotente. A conferência sobre cópia do banco do `konebras` fica para o deploy.
- [x] Permissões só do `ModuleCatalog` (nenhuma ação nova: `fiscal.view`, `fiscal.edit` e `fiscal.approve` da FIS-02a).
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajudas escritas; teste de cobertura passando.
- [x] Padrões e próximo passo avaliados; ativação com confirmação.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `TaxRule` com versões e conferência (Domain); `TaxCalculator` puro; `TaxRuleResolver`.
- [x] Seed `fiscal-tax-rules` a partir do `IFiscalSettings`; consumidor do `FiscalSettingsChanged`.
- [x] Endpoints `/api/v1/fiscal/tax-rules` (+ `/activate`, `/deactivate`, `/confirm`, `/simulate`).
- [x] Telas, painel de simulação e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa, contadora (conferência das regras na tela, antes da produção).
- Gate: CA 1, 3, 4, 7, 8, 9 e 11 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: não bloqueia mais DSH-02 e FIN-08, que vêm na Fase 4 com o imposto do orçado) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK (seed só com os valores pré-configurados, "a confirmar") |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** seed sem nenhuma regra, para não inventar tributação; sem regra, sem nota; ativação por outra pessoa e versão por edição; DIFAL e IBS/CBS condicionados às lacunas do EmissorFiscal.
- **Correções da revalidação (21/10/2026):** pontos 40 a 44 decididos; o seed passa a trazer as regras do conjunto pré-configurado da FIS-02a, em rascunho e "A confirmar com a contadora", com "Marcar como conferida" exigida só em produção (novo requisito do PO; CA 1, 4); IBS/CBS calculados sempre (ponto 9; CA 7); DIFAL calculado e bloqueado na emissão (CA 8); prestação de serviço para a NFS-e (CA 1); "Simular" em painel lateral (CA 10); imposto como custo pela fonte `InvoiceTax` (ADR 0025 adendo; CA 11); aviso de revisão quando os padrões mudam (CA 6); casos sintéticos no CI e os da contadora quando chegarem (gate deixa de exigir os casos da contadora antes do `Done`); bloqueios ajustados à nova ordem; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conteúdo da contadora (pré-configurado) e frequência do DIFAL.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D4, D8, D12, novo requisito de Configurações, tabela 3.2 pontos 9 e 40–44, §4.3, §4.4); regras do seed "a confirmar"; Simular em painel lateral; ordem 9 (Marco 5.4); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 31/10/2026 | 1.0 | **Implementação.** `TaxRule` com versões, conferência e segregação na ativação (`SoleOwnerRule`); `TaxCalculator` puro com a tabela de casos sintéticos; `TaxRuleResolver` (mais específica por NCM, só ativa do regime atual, só conferida em produção); seed `fiscal-tax-rules` a partir de Configurações → Fiscal; `TaxRuleReviewConsumer` do `FiscalSettingsChanged` (aviso "A revisar") e a lista das regras afetadas no salvar da FIS-02a; `ReadinessAsync` lista as regras ativas não conferidas (409 `fiscal_tax_rules_not_confirmed` em produção, aviso em homologação); endpoints `/api/v1/fiscal/tax-rules` (+ `/versions/{versionId}`, `/activate`, `/deactivate`, `/confirm` e `/simulate` com a política `fiscal-simulate`, 30 por minuto por usuário); telas Fiscal → Regras fiscais, regra e nova regra, com o painel lateral "Simular"; ajudas `fiscal-regras`, `fiscal-regra` e `fiscal-regra-nova`. **Fica para a conferência da contadora** (valores e fórmulas propostos, como na FIS-02a): base do PIS/COFINS sem excluir o ICMS (Tema 69 do STF); base do IBS/CBS igual à base da operação, sem exclusões; DIFAL em base única, com a alíquota interna do destino pré-configurada igual à interna de SP (18%) até a contadora informar por UF; FCP só na operação interna; CSOSN do Simples sem ICMS próprio destacado; a revenda vale para todos os destinatários e leva a marca DIFAL (a interestadual a não contribuinte calcula e bloqueia; o CFOP dela seria 6108); a prestação de serviço também calcula PIS e COFINS (Lucro Presumido); os casos de referência dela entram na tabela do `TaxCalculatorTests`. **Recomendações aceitas (PO 24/10), pela opção mais conservadora:** IBS e CBS num jsonb só (`ibs_cbs`), porque dividem o CST e o `cClassTrib`; a coluna `version` do 03 virou `rule_version` (o `xmin` já é a versão de concorrência); a ativa única por regra é garantida no serviço (a troca de versão atualiza as duas linhas no mesmo SaveChanges); a regra do regime anterior não é aplicada pelo resolvedor (`crt_scope`) e ganha o aviso, sem mudar de situação (concilia o CA 9 da FIS-02a com o CA 6 daqui); o IPI "pela TIPI do NCM" pede a alíquota do item (a TIPI não é importada no MVP) e recusa sem ela; "Ativar regra" fica liberado na tela depois de abrir o simulador (o servidor confere a segregação; a simulação não grava nada, então não há como conferi-la no servidor). **Pendências:** "abre com o último item vendido" usa a última simulação do navegador até a FIS-03a guardar os itens vendidos; o simulador recebe a UF e o tipo do cliente digitados (o contrato de terceiros ainda não expõe a IE ao Fiscal); o caso do Proprietário único é coberto pela unidade do `SoleOwnerRule` (o tenant de teste tem vários Proprietários); E2E (Playwright) fica para a rodada de E2E da fase. | Dex (dev) |
