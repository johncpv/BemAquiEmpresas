# FIS-02b — Regras fiscais por operação e cálculo dos tributos da nota

| Campo | Valor |
|---|---|
| ID | FIS-02b |
| Título | Regras fiscais parametrizáveis por operação (venda de produção própria, revenda, remessa para obra, retorno, devolução), com CFOP, CST/CSOSN, alíquotas e condições (UF de destino, contribuinte ou não, NCM), vigência e aprovação; motor puro que calcula ICMS, ICMS-ST, IPI, PIS, COFINS e, quando exigido, IBS/CBS por item; simulador; conteúdo definido com a contadora |
| Tamanho | G (quebra da FIS-02) |
| Status | Draft |
| Requisitos | R-FIS-4 (regras fiscais por operação parametrizáveis, conteúdo da contadora) · R-FIS-3 (tributos por regra fiscal) · R-FIS-8 (IBS/CBS, P-03) · R-PRJ-5 (impostos no realizado) · R-PRP-1 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-02a (regime, CRT), CAD-02a (NCM, CEST e origem do item), CAD-01a (terceiro: UF, IE, contribuinte), FIS-01 (o EmissorFiscal não calcula imposto); P-01, P-02, P-03 (conteúdo) |
| Bloqueia | FIS-03a (emissão usa o cálculo), DSH-02/FIN-08 (impostos no realizado e no DRE) |
| Ordem de execução | 9 (Marco 5.4 "Nota de saída") |

## User story
Como **financeiro, junto com a contadora**, quero **cadastrar como cada operação é tributada e ver o cálculo antes de emitir**, para **a nota sair com o imposto certo sem digitar CFOP e alíquota a cada vez, e o custo do imposto entrar na margem do projeto**.

## Contexto e referências
- PRD R-FIS-4 ("Regras fiscais por operação (CFOP, CST/CSOSN, alíquotas) parametrizáveis — conteúdo definido com a contadora"), R-FIS-8.
- `03-modelo-dados` → `fiscal.tax_rules` (`operation` (Sale/Return/Remittance/Service), `cfop`, `cst_csosn`, parâmetros de ICMS/IPI/PIS/COFINS/IBS/CBS (JSON), `conditions` (UF destino, tipo de cliente, NCM)).
- EmissorFiscal (FIS-01): "o serviço transporta e assina; quem decide valores e códigos fiscais é o sistema cliente"; o payload aceita ICMS (com ST e FCP), PIS, COFINS e IPI por item, **sem IBS/CBS e sem DIFAL** (lacunas L-b e L-e). Cenários do PLANEJAMENTO §10 de lá: venda em SP, interestadual (DIFAL), venda mista, peça de reposição, **remessa para obra**, retorno de obra, devolução.
- `06-pendencias-riscos` P-01 (regime: "provável Lucro Presumido"), P-02 (sem CNAE industrial; define IPI e CFOP de produção própria), P-03 ("seguir a Nota Técnica vigente").
- `04-fluxos-estados` §3.3 ("Realizado … impostos (NF autorizadas)").

## Critérios de aceite
**Regras**
1. **Dado** `fiscal.edit`, **quando** cadastro uma regra com operação (lista proposta: Venda de produção própria, Revenda, Remessa para obra, Retorno de obra, Devolução de compra, Outras saídas), descrição, natureza da operação (texto da nota), CFOP dentro do estado e fora do estado, CST ou CSOSN conforme o CRT (FIS-02a), modalidade e alíquota de ICMS, redução de base, ICMS-ST (MVA, alíquota interna), IPI (CST, enquadramento, alíquota ou "pela TIPI do NCM" digitada), PIS e COFINS (CST e alíquotas), IBS e CBS (CST e classificação tributária, só quando a FIS-01 indicar suporte), informação complementar padrão, condições (UF de destino, contribuinte, isento ou não contribuinte, lista de NCM ou prefixo) e vigência (início e fim), e clico "Salvar regra fiscal", **então** a regra nasce `Draft` e é auditada.
2. **[Segurança]** **Dado** uma regra `Draft`, **quando** quem tem `fiscal.approve` clica "Ativar regra" depois de conferir o simulador, **então** a regra fica `Active` com quem ativou e quando; quem cadastrou ou editou a regra não a ativa (segregação, exceto o Proprietário único, como a D6), porque a regra muda o imposto de toda nota futura; editar uma regra ativa cria uma **nova versão** `Draft` (a versão ativa só sai quando a nova é ativada) e a nota emitida guarda a versão usada.
3. **Dado** duas regras ativas que se aplicam à mesma operação, UF, tipo de destinatário e NCM no mesmo período, **quando** salvo ou ativo, **então** 422 "Esta regra se sobrepõe à regra {nome}." (a mais específica por NCM só é aceita se a outra não tiver NCM).
4. **Dado** o seed, **quando** o tenant é criado ou no próximo deploy, **então** **nenhuma regra é criada** (o conteúdo é da contadora); a tela mostra o modelo vazio das operações da lista com "Preencha com a contadora" e o roteiro dos 3 cenários da FIS-03a.

**Cálculo**
5. **Dado** o motor `TaxCalculator` (Domain, puro), **quando** recebe a operação, o emitente (UF, CRT), o destinatário (UF, IE, contribuinte, consumidor final), o item (NCM, origem, CEST, quantidade, valor unitário, desconto, frete e outras despesas rateados) e a regra ativa na data de emissão, **então** devolve por item CFOP, CST/CSOSN, bases, alíquotas e valores de ICMS, ICMS-ST, FCP, IPI, PIS, COFINS e, se a regra tiver, IBS e CBS, com arredondamento só no valor do item (2 casas, `AwayFromZero`) e totais da nota = soma dos itens; os casos de referência da contadora (dataset fixo com os resultados esperados, Pontos em aberto 1) passam exatamente.
6. **Dado** nenhuma regra ativa para a combinação, **quando** calculo, **então** o motor recusa com "Não há regra fiscal para {operação} para {UF}/{tipo de cliente} e NCM {ncm}." e a nota não pode ser emitida (nunca usa padrão silencioso).
7. **Dado** uma venda interestadual a não contribuinte, **quando** calculo, **então** o motor calcula o DIFAL quando a regra o define, mas a emissão é bloqueada com "A nota com DIFAL ainda não é suportada pelo Emissor Fiscal." enquanto a lacuna L-e existir (proposta).
8. **Dado** `fiscal.view`, **quando** abro "Simular" e escolho operação, cliente, itens e quantidades, **então** vejo a nota simulada com a regra aplicada a cada item, as bases, os impostos e os totais, sem gravar nada e sem chamar o EmissorFiscal.

**Imposto como custo (visibilidade financeira)**
9. **Dado** a regra, **quando** cadastro, **então** marco quais tributos entram como **custo do projeto** na natureza "Impostos" (`CostType.Tax`) e quais são recuperáveis ou retidos (proposta: ICMS, PIS, COFINS, IPI destacado e IBS/CBS da saída como custo; a decisão é da contadora pela P-01, Pontos em aberto 2); a nota autorizada (FIS-03a) usa essa marca para lançar o realizado de impostos.

**Permissões e auditoria**
10. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; criar e editar sem `fiscal.edit`, ativar e inativar sem `fiscal.approve`: 403; simular com `fiscal.view`; `RequireModule` e `perm:`; IDOR em `/tax-rules/{id}/versions/{versionId}`.
11. **Dado** qualquer alteração ou ativação, **quando** abro o histórico, **então** vejo antes e depois de cada campo, a versão, quem editou e quem ativou.

## Regras de negócio e técnicas
- **`fiscal.tax_rules`** (03 + proposta): `operation`, `name`, `nature_of_operation`, `version`, `status` (`Draft`/`Active`/`Inactive`), `valid_from`, `valid_to`, `cfop_internal`, `cfop_interstate`, `crt_scope`, parâmetros por tributo (jsonb tipado: `icms`, `icms_st`, `fcp`, `ipi`, `pis`, `cofins`, `ibs`, `cbs`, `difal`), `cost_taxes` (lista dos que entram como custo), `complementary_info`, `conditions` (jsonb: UFs, tipos de destinatário, NCMs), `created_by`, `activated_by`, `activated_at`, `xmin`.
- **Percentuais** `numeric(7,4)`; bases e valores `numeric(18,2)`; arredondamento só no item.
- **Motor puro**, sem banco, com testes de tabela; a resolução da regra (mais específica por NCM, depois sem NCM) fica no Application.
- **Nenhuma regra fiscal inventada:** o seed é vazio; os exemplos dos testes são fictícios e marcados como tal, até a contadora entregar os casos.

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.view` (ver e simular), `fiscal.edit` (cadastrar e editar), `fiscal.approve` (ativar e inativar; proposta da FIS-02a).
- **Eventos:** nenhum.
- **Tabelas:** `fiscal.tax_rules` (03 com as colunas acima).

## Telas e estados
- **Fiscal → Regras fiscais** (`/app/fiscal/regras`, entrada nova no `modules.ts`, grupo "Configuração fiscal"): lista por operação com versão, vigência, situação (Rascunho neutro, Ativa ok, Inativa neutro) e quem ativou; detalhe em seções por tributo; **Simular** (`/app/fiscal/regras/simular`).
- **Verbos:** "Salvar regra fiscal" / "Regra fiscal salva"; "Ativar regra" / "Regra ativada"; "Inativar regra" / "Regra inativada"; "Simular" / —.
- **Estados:** carregando; vazio ("Nenhuma regra fiscal. Preencha as regras com a contadora antes de emitir a primeira nota." + "Nova regra fiscal" + "Baixar roteiro para a contadora" (PDF com os campos)); erro no campo; modo leitura sem `fiscal.edit`.
- **Propositivo:** CFOP fora do estado sugerido a partir do de dentro (5xxx → 6xxx) com "Confirme"; CST filtrado pelo CRT; o simulador abre com o último item vendido.
- **400 px:** seções empilhadas; simulador com itens em cartões.
- **Ajuda:** `Ajuda/fiscal-regras.md`, `Ajuda/fiscal-regra.md`, `Ajuda/fiscal-regras-simular.md`; `<Dica>` em "CFOP", "CST/CSOSN", "MVA", "DIFAL", "Entra no custo do projeto" e "IBS/CBS".

## Plano de testes
- **Unidade:** `TaxCalculator` com a tabela de casos (fictícios até a contadora; depois os dela) para ICMS, ST, IPI, PIS, COFINS, IBS/CBS, arredondamento e totais (CA 5); sem regra (CA 6); DIFAL bloqueado (CA 7); resolução por especificidade e sobreposição (CA 3).
- **Integração (Testcontainers):** versão `Draft` → `Active` com segregação e Proprietário único (CA 2); seed vazio (CA 4); simulador sem gravar (CA 8); marca de custo (CA 9); três camadas e IDOR (CA 10); auditoria (CA 11).
- **E2E (Playwright):** cadastrar uma regra, ativar com outro usuário e simular; tela a 400 px.

## Fora do escopo
- Emissão: FIS-03a. Tributação da NFS-e (ISS, retenções): FIS-06. Crédito de imposto na entrada: FIS-04b (P-01).
- Importar tabela TIPI ou de alíquotas: fora do MVP.

## Pontos em aberto
1. **Conteúdo das regras e casos de referência** (contadora, P-01, P-02): CFOP de produção própria × revenda depende do CNAE de fabricação (P-02); os 3 cenários da FIS-03a.
2. **Quais tributos entram no custo do projeto** (contadora + PO, P-01): proposta do CA 9.
3. **IBS/CBS** (contadora, P-03): obrigatórios já na NF-e da Konebras? Depende da NT vigente e da lacuna L-b do EmissorFiscal.
4. **DIFAL** (PO): bloquear até o EmissorFiscal suportar (proposta) ou estender o EmissorFiscal antes do go-live.
5. **Segregação na ativação** (PO): proposta do CA 2.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (com os casos da contadora), integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas escritas; teste de cobertura passando.
- [ ] Padrões e próximo passo avaliados; ativação com confirmação.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TaxRule` com versões (Domain); `TaxCalculator` puro; `TaxRuleResolver`.
- [ ] Endpoints `/api/v1/fiscal/tax-rules` (+ `/activate`, `/deactivate`, `/simulate`).
- [ ] Telas, simulador, roteiro em PDF para a contadora (`IPdfRenderer`) e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa, contadora (validação dos casos de referência).
- Gate: CA 2, 5, 6 e 9 obrigatórios; casos de referência da contadora conferidos antes do `Done`.

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

- **Correções feitas na validação:** seed sem nenhuma regra (CA 4), para não inventar tributação; sem regra, sem nota (CA 6); ativação por outra pessoa e versão por edição (CA 2); DIFAL e IBS/CBS condicionados às lacunas do EmissorFiscal.
- **Pré-condições para `Ready`:** P-01 decidida e os casos da contadora (ponto 1); pontos 2 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
