# Produto, custo e preço: uma fonte de verdade do item ao documento

| Campo | Valor |
|---|---|
| Situação | **Proposta, aguardando o PO** (perguntas no §13). Só documentação: nenhuma story foi criada nem o backlog alterado |
| Data | 27/11/2026 |
| Pedido | PO, 27/11/2026: "Temos o item no estoque, mas não temos a precificação dele. O orçamento deveria buscar itens já cadastrados (manter a opção de digitar à mão, mas o ideal é buscar), e o item já vir com a composição de custo, a margem e o preço de venda estabelecidos. O cliente tem que conseguir configurar o preço de venda e, nessa configuração, ter o preço de compra/custo. Quero pensar no projeto todo e não remendar; não quero perder o esqueleto que temos. Esse tem que ser o melhor caminho para atender os modelos de negócio do ERP (Fábrica, Serviços, Comércio, E-commerce) para outros clientes." |
| Decisões do PO já tomadas | O item guarda uma **composição de custo padrão**, copiada para o orçamento e ajustável lá · prioridade: bloco "Pós-go-live: melhorias do produto" (depois da F2 do Conecta) |
| Fontes lidas | `CLAUDE.md` · PRD §5.4 (COML-1), §5.9 (EST-1, EST-7), RH-2, PRD-4, PRP-1 · ADR 0046 (perfis), ADR 0047 (Catálogo), ADR 0023 (motor de estoque), ADR 0052 (integrações) · stories COM-01a (CA 7 a 14), VND-02a, INT-03 · backlog: CAT-03 a CAT-07, MKT-00, REC-01, bloco de melhorias · `docs/integracoes/00-plano-integracoes.md` · código: `Inventory.Domain/Item.cs`, `ItemsContract`, `ItemCostsContract`, `Commercial.Domain/Quote.cs`, `QuotePricing.cs`, `QuoteService.Items.cs`/`.Costs.cs`, `CommercialParametersService`, `Catalog.Domain/Catalog.cs` (`PriceList`, `PriceListItem`, `ProductCategory`), `CatalogRules.MarginRules`, `ProductService`, `Sales/SalesOrderService.cs`, `Engineering.Domain/Bom.cs` e `RoutingTemplate.cs`, `Purchasing.Contracts` (`LastPurchaseAsync`), `BusinessProfileCatalog`, `src/Web/src/pages/quotes/ItemsTab.tsx` |

---

## 1. Diagnóstico do esqueleto atual

### 1.1 Onde mora cada coisa hoje

| Conceito | Onde mora | Observação |
|---|---|---|
| O que o item é (tipo, unidade, NCM, categoria, lote/série) | `inventory.items` (CAD-02a) | Tipos: `RawMaterial`, `Component`, `Finished`, `Consumable`, `Service`, `Merchandise`. Estoque está no núcleo de **todos** os perfis |
| Custo real | `inventory.items.average_cost` (`numeric(18,6)`), só o motor de estoque grava (ADR 0023) | Lido por `IItemCosts.AverageCostsAsync` |
| Último custo de compra | `Purchasing.Contracts.IPurchaseOrders.LastPurchaseAsync` | Existe, mas ninguém usa para preço |
| Custo-hora | Cargo do RH (`IHourlyCost`), com `payroll.charges_pct` | Sempre do cargo, nunca do funcionário (COM-01a CA 10) |
| Modo de preço (markup/margem) e % padrão | `tenant_settings` `pricing.mode` (Identity) e `commercial.parameters` `pricing.default_pct` | Só o orçamento usa |
| **Composição de custo** | `commercial.quote_item_costs`, **só dentro do orçamento** | Material, Mão de obra, Instalação, Frete, Impostos, Outros (`CostType`). O custo do material vem do **último orçamento** que usou o item (CA 11), não do estoque nem da compra |
| Fórmula de preço nº 1 | `Commercial.Domain.QuotePricing` | Markup ou margem sobre o custo unitário |
| Preço de venda praticado | `catalog_products.price_list_items` por **produto do Catálogo**, tabela "Balcão" (padrão, canal `Pos`), versionada, com histórico (ADR 0047 §4) | Catálogo só em Comércio, Serviços e E-commerce. A Fábrica não tem |
| Fórmula de preço nº 2 | `Catalog.Domain.MarginRules` com margem **ou** markup padrão **por categoria do Catálogo** | Sugerido a partir do custo médio, só na tela do produto |
| Preço do pedido de venda | Sempre a tabela padrão (VND-02a CA 1), relido na confirmação (409 `price_changed`) | Limite de desconto `sales.order_discount_limit_pct`; acima, `sales.approve` |
| Preço no PDV | "Balcão" pelo modelo de leitura offline (OFF-01) | Desconto com supervisor (PDV-04); relatório de venda abaixo do custo |
| Preço na API (Conecta) | `GET /products`: tabela padrão, nunca custo (INT-03 CA 4) | |
| Estrutura do produto | `engineering.boms` **por OS** (ENG-02); roteiros por "tipo de produto" em texto (ENG-01) | **Não existe estrutura padrão do item**: a BOM nasce na OS, copiada de outra OS |

### 1.2 Sobreposições

1. **Duas fórmulas e dois padrões de preço.** O orçamento usa `pricing.mode` + `pricing.default_pct` (do tenant); o Catálogo usa margem ou markup **da categoria do Catálogo**. O mesmo item pode ter dois "preços sugeridos" diferentes.
2. **Duas categorias.** A do Estoque (`inventory` categories) e a do Catálogo (`catalog_products.categories`, com a margem padrão). Regra de preço por categoria só existe na segunda.
3. **Três "custos" do material.** Médio (estoque), última compra (compras) e último usado em orçamento (comercial). O orçamento usa o terceiro, que é o menos confiável (copia o erro do orçamento anterior).

### 1.3 Lacunas

1. **O orçamento não busca item.** `ItemsTab.tsx` manda sempre `itemId: null`, embora a COM-01a CA 7 e o backend (`ItemErrorAsync` com `IItems`) aceitem. É o sintoma que o PO viu.
2. **O item não tem composição padrão nem preço.** Toda composição é redigitada a cada orçamento; na Fábrica, que não tem Catálogo, o item não tem preço em lugar nenhum.
3. **Imposto e despesa variável entram como valor fixo** (linha "Impostos" em R$), não como % sobre o preço. O preço sobe e o imposto não acompanha; não há markup divisor (piso dos melhores ERPs, §3).
4. **Não há preço mínimo** nem regra de quem vende abaixo dele (só limite de desconto no pedido e supervisor no PDV, sem relação com o custo).
5. **Só existe a tabela "Balcão".** O enum `PriceChannel` tem `Pos`, `Online` e `Delivery`, mas não há tabela por canal, cliente, marketplace ou aplicativo conectado, nem tabela derivada (CAT-03 ainda não feita).
6. **Ninguém avisa quando o custo muda.** O custo médio muda a cada entrada; preço e margem ficam velhos em silêncio (a margem só aparece na tela do produto e no relatório de venda abaixo do custo).
7. **Produto acabado não tem estrutura padrão** para custear antes de existir OS.
8. **Kit** (vários itens vendidos juntos) não existe; CAT-05 (combos) e CAT-06 (ficha técnica de alimentação) estão planejadas em separado.

### 1.4 O que está certo e deve ficar

- O Estoque como dono do item e do custo real, gravado só pelo motor (ADR 0023).
- O Catálogo como dono do **preço praticado**: tabela versionada, `PriceChanged`, histórico, `catalog.edit_price` e `catalog.view_cost` (ADR 0047). O PDV offline depende disso.
- O orçamento com composição por natureza, custo-hora do cargo, "Digitado à mão" com "Voltar ao sugerido" e "Atualizar custos" (COM-01a CA 8 a 14). A composição por natureza alimenta o orçado × realizado do projeto (COM-04a, PRJ-5).
- O pedido de venda que relê o preço e pede confirmação quando mudou (VND-02a).
- Documento aprovado é imutável (contrato, venda, nota).

---

## 2. Conceito recomendado

### 2.1 Em uma frase

**Três perguntas, três donos, um caminho só:** *quanto custa* (Estoque e Compras: custo real), *quanto deveria custar ao cliente* (Precificação: ficha de custo padrão do item + política de preço → custo de referência, preço sugerido e preço mínimo) e *quanto eu cobro* (Catálogo: tabelas de preço por canal e cliente). O documento (orçamento, pedido, venda, assinatura) **copia** o que valia naquele momento e nunca é mudado depois.

```
Estoque (custo médio) ─┐
Compras (última compra)┤
RH (custo-hora do cargo)┼─► Precificação: ficha de custo padrão do item
Engenharia (estrutura) ─┘     + política (base de custo, markup/margem, impostos e despesas %, arredondamento, mínimo)
                              = custo de referência · preço sugerido · preço mínimo
                                        │ "Aplicar na tabela" (catalog.edit_price)
                                        ▼
                         Catálogo: tabelas de preço (padrão, canal, cliente, vigência, regra derivada)
                                        │ IPriceResolver (cliente > canal/loja > padrão)
          ┌──────────────┬──────────────┼──────────────┬──────────────┬──────────────┐
      Orçamento      Pedido de venda   PDV        API/Conecta    Marketplace     Assinatura
   (copia a ficha,   (tabela, mínimo)  (Balcão,   (tabela da     (tabela do      (plano = item de
    preço da tabela                    offline)   conexão)       canal + tarifa)  serviço com preço)
    ou o sugerido)
```

### 2.2 Por que assim

- **Uma fonte de verdade por pergunta.** Hoje a fórmula está em dois lugares e a composição em nenhum (só no documento). Na proposta, há **uma fórmula** (motor puro `PriceFormation`, extraído do `QuotePricing` e do `MarginRules`, com o mesmo resultado quando impostos e despesas são 0%) e **uma composição padrão** (a ficha do item).
- **Sugerido não é praticado.** A Fábrica vende sob encomenda: o preço sai da composição a cada orçamento. O Comércio vende de tabela: a composição só serve para decidir e vigiar o preço. Separar "preço sugerido" (calculado) de "preço da tabela" (decisão de alguém com `catalog.edit_price`) atende os dois sem forçar a Fábrica a ter tabela nem o Comércio a orçar cada venda.
- **Não quebra nada.** Estoque, Catálogo, PDV offline, pedido de venda e orçamento continuam com as mesmas tabelas e contratos; tudo é aditivo. Com a política vazia (impostos e despesas 0%, sem arredondamento, sem mínimo), o preço sugerido é exatamente o de hoje.
- **Funciona em todo perfil.** A ficha é do **item** (Estoque é do núcleo de todos os perfis), não do produto do Catálogo (que a Fábrica não tem).

### 2.3 Onde mora: módulo novo `pricing` ("Precificação")

Alternativas avaliadas:

| Opção | Por que não (ou sim) |
|---|---|
| Preço e ficha no item do Estoque | Mistura o motor de estoque (custo real, crítico) com política comercial; o Estoque passaria a depender de RH, Produção e Parties para a mão de obra e a instalação |
| Tudo no Catálogo | O Catálogo não está na Fábrica e é orientado ao varejo (GTIN, PLU, grade, PDV offline). Pôr o Catálogo na Fábrica para ter ficha mostraria coisas de loja à Konebras |
| Ficha no Commercial | O Commercial (orçamento) não está no Comércio nem no E-commerce |
| **Módulo `pricing` (recomendado)** | Dono da política, da ficha e do custo de referência; consumido pelo Commercial, Catálogo, Vendas e PDV por `Pricing.Contracts`; permissões próprias; tela própria ("Precificação") e aba "Custo e preço" na tela do item e do produto. Segue a ADR 0006 (cada módulo com suas tabelas, contrato entre módulos) |

- **Perfis:** `services`, `retail`, `ecommerce`. **`factory` só com decisão do PO** (pergunta 1), pela regra da ADR 0046.
- **Área:** Comercial (no Comércio e no E-commerce, que não têm área Comercial visível, aparece na área "Loja").
- Vira ADR (proposta: **ADR 0054 — Precificação: ficha de custo padrão, política e preço sugerido**) na primeira story.

---

## 3. Benchmark (piso de mercado)

Pesquisa de 27/11/2026 nas ajudas oficiais; onde não houve fonte oficial, está marcado.

| ERP | Custo de referência | Formação de preço | Tabelas de preço | Composição | Fonte |
|---|---|---|---|---|---|
| **Omie** | CMC (custo médio com frete, impostos e despesas de aquisição) | Sugestão por markup no cadastro (blog) | Base no CMC, no preço de venda ou em outra tabela; arredondamento; vigência; filtro por cliente e produto; **bloqueia o vendedor de mudar o preço**; tabela sugerida pelo histórico de 90 dias | Estrutura com aba "Custo de produção" (MP × CMC + MOD + GGF) | [CMC](https://ajuda.omie.com.br/pt-BR/articles/6597224-como-e-feito-o-calculo-do-cmc), [tabelas](https://ajuda.omie.com.br/pt-BR/articles/1399985-tabelas-de-preco), [estrutura](https://ajuda.omie.com.br/pt-BR/articles/1426253-criando-a-estrutura-dos-seus-produtos) |
| **Bling** | Custo do fornecedor padrão (margem por pedido) | Só no blog | Listas com % ou valor, acréscimo ou desconto, vigência, preço por item | Produto com composição | [margem](https://ajuda.bling.com.br/hc/pt-br/articles/35658275718551-Dashboard-Margem-por-Pedido), [listas](https://blog.bling.com.br/listas-de-precos-customizadas-no-bling/) |
| **Conta Azul** | Custo médio | Não tem; indica planilha externa | Preço único (conhecimento prévio) | Kit simples no plano Pro | [blog](https://contaazul.com/blog/preco-de-custo-e-preco-de-venda/) |
| **Tiny/Olist** | Custo e custo médio (conhecimento prévio) | Não confirmado | Listas por % (acréscimo ou desconto) com exceção por produto | Simples, kit, variação, fabricado, matéria-prima | [API listas](https://api-docs.erp.olist.com/api-reference/lista-de-preços/criar-lista-de-preços) |
| **Odoo** | Por categoria: padrão, médio ou FIFO; "Compute Price from BoM" | Regra de fórmula: base no preço, no custo ou em outra lista; desconto; arredondamento; taxa extra; **margem mínima e máxima** | Por cliente, país, site, quantidade mínima e vigência | BoM fabricar ou kit, com roteiro | [custos da OP](https://www.odoo.com/documentation/18.0/applications/inventory_and_mrp/manufacturing/basic_setup/mo_costs.html), [pricelists](https://www.odoo.com/documentation/18.0/applications/sales/sales/products_prices/prices/pricing.html) |
| **TOTVS** | Protheus: custo pela estrutura (formação de preço); Winthor: CMV | Margem real = (preço − CMV) ÷ preço com frete e comissão; **preço mínimo por produto** | DA0/DA1 com vigência; preço por região | Estrutura (SG1) | [DA0](https://tdn.totvs.com/x/aKeVI), [margem real](https://tdn.totvs.com.br/download/attachments/181142552/Como%20funciona%20o%20Simulador%20de%20Margem%20Real.pdf?api=v2), [preço mínimo](https://tdn.totvs.com.br/download/attachments/185750724/Como%20alterar%20o%20Pr%20Minimo.pdf?api=v2) |
| **Senior** (no lugar da Sankhya, sem fonte oficial) | Custo padrão; ficha técnica na indústria | **Markup com impostos, despesas variáveis, comissão e margem** | — | Ficha técnica | [formação de preço](https://documentacao.senior.com.br/gestaoempresarialerp/5.10.3/manual-processos/custos/gestao-de-preco-para-industria/formacao-de-preco.htm) |

**Piso de mercado** (todos ou quase todos): tipo de item (produto, serviço, kit, fabricado); custo médio na entrada e custo da última compra; preço padrão no cadastro com sugestão por markup; várias tabelas por cliente, canal ou loja com % ou preço fixo; vigência; arredondamento; kit e estrutura; orçamento, pedido e PDV puxam a tabela do cliente; margem por pedido.

**Diferenciais dos melhores** (o que esta proposta também entrega): markup divisor com impostos e despesas "por dentro"; custo do fabricado pela estrutura; tabela derivada por fórmula com margem mínima; preço mínimo com alçada; bloqueio do vendedor quando há tabela.

**Item digitado à mão no orçamento:** nenhuma fonte oficial confirma; no Odoo a linha exige produto. Manter "Digitar à mão" (pedido do PO) é acima do piso, desde que o padrão seja buscar.

Nenhum item da proposta fica abaixo do piso; a ficha padrão por item com mão de obra por cargo e cópia editável para o orçamento é acima dele.

---

## 4. Formação de preço

### 4.1 Custo de referência (por unidade do item)

A **base de custo** vem da política do tenant, com exceção por categoria do Estoque e por item:

| Base | De onde vem | Uso típico |
|---|---|---|
| **Custo médio** (padrão) | `IItemCosts` (motor de estoque) | Revenda, matéria-prima e componente com estoque |
| **Última compra** | `IPurchaseOrders.LastPurchaseAsync` | Revenda com custo em alta, quando o médio atrasa |
| **Manual (reposição)** | Digitado na ficha, com data | Item sem compra ainda, ou custo negociado |
| **Pela composição** | Soma das linhas da ficha (§5) | Serviço, produto acabado, kit, e todo item com custo além do material |

Na prática a ficha é sempre a soma das linhas; para revenda ela nasce com uma linha só, "o próprio item", na base escolhida. Item sem custo nenhum mostra "Sem custo de referência" e não tem preço sugerido nem mínimo (o documento pede o preço à mão).

### 4.2 Fórmula (markup divisor)

- **Despesas variáveis sobre o preço** (política, com exceção por categoria e por item): impostos sobre a venda %, comissão %, despesas variáveis % (cartão, frete grátis), e a **tarifa do canal** % (marketplace, ver §6).
- **Modo margem:** `preço = custo ÷ (1 − (despesas% + margem%))`.
- **Modo markup:** `preço = custo × (1 + markup%) ÷ (1 − despesas%)`.
- Com despesas 0% as duas fórmulas são as de hoje (`QuotePricing.SuggestedPrice`, COM-01a CA 12): o teste de regressão do motor garante.
- **Margem exibida** = margem de contribuição: `(preço − custo − preço × despesas%) ÷ preço`. O rótulo de hoje ("Margem 40,00% · Markup 66,67%") continua.
- Soma de despesas% + margem% ≥ 100% responde 422 ("Não existe preço com essa soma de percentuais").
- `PriceFormation` é código puro no `SharedKernel` (como o `CostType`), usado por Precificação, Commercial e Catálogo: uma fórmula só.

### 4.3 Arredondamento

Política: nenhum (centavo, padrão), para cima em R$ 0,10, R$ 0,50, R$ 1,00, ou terminação ",90"/",99". Vale para o sugerido e para tabela derivada; nunca para preço digitado.

### 4.4 Preço mínimo e quem vende abaixo

- **Mínimo** = o preço com a **margem mínima** da política (exceção por categoria e item), ou um valor fixo na ficha. Sem margem mínima definida, não há mínimo (padrão: vazio; nada muda para quem não configurar).
- Abaixo do mínimo (preço da linha líquido de desconto):
  - **Orçamento:** chip "Abaixo do mínimo" na linha; enviar ou aprovar exige `quotes.approve` (sem ação nova).
  - **Pedido de venda:** confirmar exige `sales.approve`, como o desconto acima do limite (VND-02a CA 7). Aplicativo (Conecta) nunca confirma abaixo do mínimo: vira rascunho "para aprovar" (como o CA 5).
  - **PDV:** pede supervisor (mesmo fluxo do PDV-04); o feed offline leva só o mínimo, **nunca o custo**.
- O mínimo é gravado na linha do documento (snapshot) para a auditoria mostrar "vendido R$ X abaixo do mínimo de R$ Y, aprovado por Z".

---

## 5. Composição padrão (ficha de custo do item)

### 5.1 Estrutura

A ficha usa as **mesmas naturezas do orçamento** (`CostType`: Material, Mão de obra, Instalação, Frete, Impostos, Outros) e o **mesmo editor** (`RowsEditor` por seção), para uma unidade do item. Novidade: as **despesas % sobre o preço** (§4.2) ficam num bloco à parte, porque não são custo, e sim parte da fórmula.

| Tipo do item | Ficha nasce com | Fonte das linhas |
|---|---|---|
| Serviço (`Service`) | Mão de obra: horas por cargo (e centro de trabalho, se houver); Outros (licença, deslocamento, infraestrutura) | Custo-hora do cargo, sempre atual (`IHourlyCost`) |
| Produto acabado (`Finished`) | Material da **estrutura padrão** (§5.2) + mão de obra por cargo + instalação + frete | Custo de referência de cada componente |
| Revenda (`Merchandise`) | Material: "o próprio item" na base de custo (médio ou última compra) + frete de compra, se quiser | §4.1 |
| Matéria-prima, componente, consumo | "O próprio item" (quando vendido avulso, ex.: peça de reposição) | §4.1 |
| Kit (§5.3) | Um material por componente, com a quantidade | Custo de referência de cada componente |

- A ficha guarda **quantidades e escolhas** (item, cargo, horas, instalador); os **custos unitários** vêm vivos das fontes, exceto o que o usuário fixar à mão (marcado "Custo fixado em dd/mm"). Assim, mudar o custo-hora ou o médio muda o custo de referência sem redigitar.
- Ficha versionada: cada gravação sobe a versão e guarda quem e quando (`IAuditable`); o documento que copiou guarda a versão.
- Componente de material pode ter ficha própria (ex.: subconjunto): o custo de referência dele entra na soma (até 5 níveis, sem ciclo; o ciclo é recusado com 422).

### 5.2 Produto acabado: estrutura padrão da Engenharia

Hoje a BOM existe só por OS. Proposta **ENG-03 "Estrutura padrão do produto"**: BOM-modelo por item acabado (revisão, liberada), dona a Engenharia, com o roteiro padrão do item. Ela:

- alimenta o **Material** da ficha por contrato (`IStandardBoms`), sem cópia (a ficha mostra "Da estrutura padrão, rev. 3");
- vira o ponto de partida da BOM da OS (o ENG-02 já copia de outra OS; passa a copiar também da estrutura padrão).

Mão de obra do acabado: horas por cargo digitadas na ficha (o roteiro de hoje tem etapas, não horas). Sugerir horas a partir do realizado das OS é evolução (não entra agora).

### 5.3 Kit

Item vendido como um conjunto (ex.: "Kit portão": motor + controle + instalação). Proposta: produto do Catálogo marcado **Kit**, com componentes (itens e quantidades); a venda baixa **os componentes** (consumidor de estoque da venda, como o `ConsumeOnSale` da CAT-06); o disponível do kit é o menor entre os componentes; a ficha nasce dos componentes. Unifica com a CAT-05 (combos) e a CAT-06 (ficha técnica de alimentação), que passam a usar a mesma ficha em vez de criar outra.

### 5.4 Cópia para o orçamento

Ao escolher um item com ficha, o orçamento **copia** as linhas para `quote_item_costs` (snapshot com custo unitário de agora), a margem/markup e as despesas % da ficha, com a origem "Ficha do item, versão N". Tudo continua editável no orçamento (decisão do PO). Mudar a ficha depois não muda o orçamento (ver §8).

---

## 6. Tabelas de preço e a relação item ↔ Catálogo

### 6.1 Relação

- **Item** (Estoque): o que é e quanto custa. **Ficha** (Precificação): quanto deveria custar ao cliente. **Produto** (Catálogo): como é vendido em tabela (nome de venda, foto, GTIN, grade) e **quanto se cobra**. Um produto por item continua (ADR 0047 §2), então "preço do produto" e "preço do item" são o mesmo número, buscado pelo item.
- Item sem produto (ex.: Fábrica, ou serviço sob orçamento) não tem preço de tabela: o documento usa o **sugerido** da ficha.
- A tela do produto mostra a ficha (custo de referência, sugerido, mínimo, margem atual) e o botão **"Aplicar sugerido"** na tabela escolhida (`catalog.edit_price`; grava pelo `PriceService`, com versão e `PriceChanged`).
- A margem/markup por **categoria do Catálogo** (`ProductCategory.DefaultMarginPercent`) migra para a regra por categoria da Precificação (cópia na migration, campos mantidos só para leitura e removidos só com ADR). Fim da fórmula nº 2.

### 6.2 Tabelas (revisão da CAT-03, dividida em CAT-03a e CAT-03b)

| Tipo | Exemplo | Regra |
|---|---|---|
| **Padrão** | "Balcão" (já existe) | Preço por produto; é a base de todas |
| **Por canal** | PDV, Loja virtual, Delivery, **Venda direta** (orçamento e pedido), **Marketplace** (uma por conta), **Aplicativo conectado** (Conecta, uma por conexão) | Preço próprio ou **derivada** |
| **Por cliente** | "Construtora X" | Ligada ao cliente (tabela `customer_price_lists` no Catálogo, chave `party_id`, para o Parties não depender do Catálogo) |
| **Promoção** | "Black Friday" | Vigência `valid_from`/`valid_to` (colunas já existem) |

- **Derivada:** "base + x%" (ou − x%) sobre outra tabela, ou "custo de referência com margem y%" (usa a Precificação), com arredondamento; item a item pode ter exceção com preço fixo. Derivada sobre outra tabela recalcula sozinha (é regra, não decisão nova); derivada sobre custo **não** muda sozinha (§8).
- **Resolução** (`IPriceResolver` no `Catalog.Contracts`): promoção vigente da tabela aplicável > **tabela do cliente** > tabela do **canal/loja** > **padrão**; quantidade mínima (atacado) na CAT-03b. Devolve preço, tabela, versão e a origem ("Tabela Construtora X"), que o documento grava.
- Atacado por faixa, promoção por regra e remarcação em lote para o PDV ficam na **CAT-03b** (fase do PDV, sem mudança de prioridade).

---

## 7. Como cada documento consome

| Documento | Busca | Custo e composição | Preço | Abaixo do mínimo |
|---|---|---|---|---|
| **Orçamento** | Busca única por código, nome e GTIN (`IItems` + produto do Catálogo quando houver), com tipo, unidade, preço e (com `quotes.view_cost`) custo de referência. **"Digitar à mão"** continua (item sem vínculo) | Copia a ficha (§5.4); item sem ficha: revenda nasce com "o próprio item" no custo de referência, serviço nasce vazio | Tabela do cliente/canal "Venda direta" se o item tem produto com preço; senão o **sugerido** da ficha. Chip de origem: "Da tabela", "Sugerido", "Digitado à mão" (este já existe) | `quotes.approve` para enviar/aprovar |
| **Pedido de venda** (VND-02a) | Como hoje (produto do Catálogo) | Grava custo de referência na linha (para margem do pedido, com `sales.view_cost`) | `IPriceResolver` (cliente > canal > padrão) em vez de sempre a padrão; relido na confirmação como hoje | `sales.approve` |
| **PDV** | Como hoje | Nada no caixa | "Balcão"/tabela da loja pelo feed offline | Supervisor (PDV-04) |
| **API / Conecta** (INT-03) | `GET /products` | Nunca custo nem margem | Tabela da **conexão** (canal Aplicativo), senão a padrão | Rascunho "para aprovar" |
| **Marketplaces** (MKT-00) | Anúncio ↔ SKU | Tarifa do canal entra como despesa % na formação (§4.2), para o sugerido por canal | Tabela do canal Marketplace (derivada da padrão + tarifa, ou fixa) enviada ao anúncio | Recusa publicar abaixo do mínimo |
| **Assinatura** (REC-01) | Plano = item de **Serviço** marcado recorrente, com produto no Catálogo | Ficha mensal (horas de suporte por cargo, infraestrutura em "Outros") para mostrar margem da mensalidade | Mensalidade da tabela (cliente > padrão) ou sugerida; congela no contrato; reajuste segue a REC-01 | `quotes.approve` |

Na aprovação do orçamento, as despesas % viram valores por natureza (impostos → `Tax`, comissão e outras → `Other`) para o orçado do projeto fechar com o preço (COM-04a CA 8, maior resto).

---

## 8. Quando o custo muda

- **Gatilho:** o motor de estoque passa a publicar `ItemAverageCostChanged` (outbox, uma vez por gravação, só com o id do item) e a Precificação também ouve `HourlyCostChanged` (custo-hora do cargo), a liberação da estrutura padrão e a gravação de ficha.
- **Recálculo:** job Hangfire por tenant (com `tenantId`, idempotente, agrupado por alguns minutos) recalcula o custo de referência, o sugerido e o mínimo das fichas afetadas (inclusive as que usam o item como componente) e grava o resultado (`pricing.item_prices`).
- **Aviso, nunca mudança silenciosa:**
  - Lista **"Preços a revisar"** na Precificação (e no sino para quem tem `catalog.edit_price`): item cuja margem na tabela caiu abaixo da meta, ficou abaixo do mínimo, ou cujo sugerido mudou mais que o limite da política (padrão 5%). Ação: "Aplicar sugerido" (um ou em lote, com prévia do impacto na margem) ou "Manter preço".
  - **Orçamento em rascunho:** faixa "O custo de N itens mudou desde que foram incluídos" com **"Atualizar custos"** (amplia o diálogo da COM-01a CA 13, que hoje só olha a mão de obra; preço digitado não muda).
  - **Orçamento enviado, contrato, pedido confirmado, venda, assinatura:** nunca mudam. O orçado × realizado mostra a diferença.
  - Tabela derivada sobre outra tabela recalcula sozinha; nenhuma tabela muda por causa do custo sem alguém aplicar.

---

## 9. Permissões

| Ação | Código | Observação |
|---|---|---|
| Ver Precificação, ficha, custo de referência e margem | `pricing.view` | O módulo é de custo: ver = ver custo |
| Editar ficha, política e regras por categoria | `pricing.edit` | |
| Ver custo e margem no orçamento | `quotes.view_cost` (existe) | O vendedor sem ela vê só preço e origem |
| Ver custo e margem no produto e no pedido | `catalog.view_cost`, `sales.view_cost` (existem) | |
| Mudar preço de tabela, "Aplicar sugerido", criar tabela | `catalog.edit_price` (existe) | |
| Vender abaixo do mínimo | `quotes.approve`, `sales.approve`, supervisor do PDV (existem) | Sem ação nova (ADR 0003: só com necessidade real) |
| Vincular tabela ao cliente | `catalog.edit_price` | Não `parties.edit`: é decisão de preço |

Custo e margem ficam **ausentes** (não zerados) na resposta sem a permissão, como na ADR 0047 §4. A API de integração nunca expõe custo.

---

## 10. O que muda para a Konebras (Fábrica)

- **Nada visível sem decisão.** `pricing` não entra em `factory` (ADR 0046); o Catálogo continua oculto; tabelas não aparecem.
- **Exceção proposta: a PRC-00** (busca de item no orçamento) vale para todos, porque cumpre a COM-01a CA 7 já aprovada. Para a Konebras ela só liga o item, traz descrição, unidade e tipo; **não** muda a composição (o custo do material continua vindo do último orçamento, CA 11). Fica como pergunta 1 para o PO confirmar.
- Fórmula: o motor único dá o mesmo preço de hoje com despesas 0% (teste de regressão com orçamentos reais da Konebras).
- Se o PO liberar a Precificação para a Fábrica: estrutura padrão (ENG-03), ficha do elevador e cópia no orçamento passam a valer, com o teste de regressão do perfil `factory`.
- Migração **só aditiva**: tabelas novas no schema `pricing`; colunas novas anuláveis em `quote_items`, `quote_item_costs`, `order_lines`, `price_lists`; nenhuma coluna removida; `pricing.mode` e `pricing.default_pct` continuam sendo lidos (a política nasce com esses valores).

---

## 11. Impacto no modelo de dados (alto nível)

| Schema | Novo / alterado | Conteúdo |
|---|---|---|
| `pricing` (novo) | `policies` | 1 por tenant: base de custo, modo (lido de `pricing.mode`), % padrão (de `pricing.default_pct`), impostos %, comissão %, despesas %, arredondamento, margem mínima, limite do aviso |
| | `category_rules` | Por categoria do **Estoque**: exceções da política (inclui a margem migrada da categoria do Catálogo) |
| | `cost_sheets` | Por item (único): base de custo própria, margem/markup, despesas %, mínimo fixo, versão, `xmin` |
| | `cost_sheet_lines` | Natureza, item, cargo, centro, instalador, quantidade/horas, custo fixado (opcional), origem (manual, estrutura padrão) |
| | `item_prices` | Resultado calculado: custo de referência, sugerido, mínimo, margem, data e versão da ficha (lido pelos contratos; nunca digitado) |
| | `price_reviews` | "Preços a revisar": item, motivo, situação (aberta, aplicada, mantida), quem e quando |
| `engineering` | `standard_boms`, `standard_bom_lines` (ENG-03) | Estrutura padrão por item acabado, com revisão |
| `catalog_products` | `price_lists` + `kind`, `base_list_id`, `rule` (jsonb validado), `rounding`, `connection_id`/`marketplace_account_id` | Tabelas por canal, derivadas, promoção |
| | `customer_price_lists` (novo) | `party_id` → tabela |
| | `products` + `is_kit`; `kit_components` (novo) | Kit |
| `commercial` | `quote_items` + `cost_sheet_version`, `price_source` (`suggested`, `list`, `manual`), `price_list_id`/`version`, `variable_pct` (jsonb), `min_price` | Snapshot da origem |
| | `quote_item_costs` + `origin` | "Ficha", "Estrutura padrão" ou "Manual" |
| `sales` | `order_lines` + `price_source`, `price_list_id`, `reference_cost`, `min_price` | Snapshot |
| `inventory` | evento `ItemAverageCostChanged` no outbox | Sem coluna nova |

Contratos novos: `Pricing.Contracts.IItemPricing` (custo de referência, sugerido, mínimo, ficha para copiar), `Catalog.Contracts.IPriceResolver`, `Engineering.Contracts.IStandardBoms`; `SharedKernel.PriceFormation` (motor puro).

---

## 12. Fatiamento em stories (ordem e dependências)

Tamanhos: P (≤ 2 dias), M (3 a 5), G (> 5). Cada story passa pela revisão (arquitetura, UX, cyber) antes de codar, como o resto do bloco.

| Ordem | Story | Tam. | Depende de | O que entrega |
|---|---|---|---|---|
| 1 | **PRC-00 Orçamento busca item cadastrado** (correção da COM-01a CA 7) | P | — | Busca única no "Adicionar item" (código, nome, GTIN; tipo, unidade e, com `quotes.view_cost`, custo médio); escolhido o item: vínculo, descrição, unidade e tipo (Serviço→Serviço, Acabado→Equipamento, demais→Produto); **"Digitar à mão"** mantido; cadastro rápido de item fica para a UX-02. Vale para todos os perfis (pergunta 1) |
| 2 | **PRC-01 Precificação: módulo, política e motor único** | M | PRC-00 | Módulo `pricing` (5 camadas, `ModuleCatalog`, perfis `services`/`retail`/`ecommerce`, plano), ADR 0054, `PriceFormation` no `SharedKernel` (orçamento e Catálogo passam a usá-lo, regressão com despesas 0%), tela "Política de preço" (base de custo, modo, %, impostos, comissão, despesas, arredondamento, margem mínima), regra por categoria do Estoque e migração da margem da categoria do Catálogo |
| 3 | **PRC-02 Ficha de custo padrão do item** | M | PRC-01 | Aba "Custo e preço" no item (e no produto): linhas por natureza (editor do orçamento), base de custo, custo de referência, sugerido, mínimo, margem; versão e histórico; `item_prices` |
| 4 | **PRC-03 Orçamento traz ficha e preço** | M | PRC-02 | Cópia da ficha no item do orçamento, origem do preço ("Sugerido", "Digitado à mão"), despesas % na fórmula do orçamento e conversão por natureza na aprovação; item sem ficha (revenda) nasce com "o próprio item" no custo de referência; "Atualizar custos" passa a olhar material e ficha |
| 5 | **CAT-08 Catálogo usa a formação** | P | PRC-02 | Produto mostra a ficha; "Aplicar sugerido" na tabela com prévia de margem; fim da fórmula por categoria do Catálogo |
| 6 | **CAT-03a Tabelas por canal e cliente, derivadas e vigência; resolvedor** | M | CAT-08 | Tipos de tabela, tabela no cliente, regra derivada, `IPriceResolver`; orçamento ("Venda direta"), pedido de venda e API (tabela da conexão) passam a resolver por ele; origem "Da tabela X" |
| 7 | **PRC-04 Preço mínimo e alçada** | M | PRC-03, CAT-03a | Mínimo gravado nas linhas; chip e bloqueio no orçamento (`quotes.approve`) e no pedido (`sales.approve`); rascunho "para aprovar" do aplicativo; mínimo no feed do PDV e supervisor |
| 8 | **PRC-05 Custo mudou: recálculo e "Preços a revisar"** | M | PRC-02 (e CAT-03a para aplicar em tabela) | Evento `ItemAverageCostChanged`, job por tenant, lista e sino, aplicar em lote, faixa no orçamento em rascunho |
| 9 | **ENG-03 Estrutura padrão do produto acabado** | M | PRC-02 | BOM-modelo por item, liberada com revisão; alimenta a ficha e vira ponto de partida da BOM da OS. Útil de verdade só na Fábrica (pergunta 1) |
| 10 | **CAT-09 Kit** | M | PRC-02, CAT-03a | Produto kit com componentes, baixa dos componentes, disponível derivado, ficha pelos componentes; CAT-05/CAT-06 passam a reusar |
| — | **CAT-03b** atacado, promoção por regra e remarcação em lote | M | CAT-03a | Continua na fase do PDV (era parte da CAT-03) |
| — | **MKT-00** (critério novo) | — | CAT-03a, PRC-01 | Tabela por conta de marketplace e tarifa como despesa % |
| — | **REC-01** (critério novo) | — | PRC-00; preço do plano melhora com CAT-03a | Plano = item de serviço recorrente; mensalidade da tabela ou sugerida |

Ordem resumida: **PRC-00 → PRC-01 → PRC-02 → PRC-03 → CAT-08 → CAT-03a → PRC-04 → PRC-05 → ENG-03 → CAT-09**. O valor que o PO pediu ("o item já vem com composição, margem e preço") chega na PRC-03 (quatro stories, 1 P + 3 M).

---

## 13. Perguntas para o PO

Só as que mudam o desenho; cada uma com a recomendação.

1. **Fábrica (Konebras).** A PRC-00 (busca de item no orçamento) vale também para a Konebras, já que cumpre a COM-01a CA 7 aprovada? E a Precificação (ficha, sugerido, estrutura padrão ENG-03) entra no perfil `factory`?
   **Recomendação:** PRC-00 sim, para todos (só vínculo, descrição, unidade e tipo; a composição da Konebras não muda). Precificação na Fábrica **depois** de validada no BemAqui (após a PRC-03), por decisão explícita com teste de regressão do `factory`; a ENG-03 só entra junto.
2. **Qual preço o orçamento usa quando o item tem preço na tabela e também ficha?**
   **Recomendação:** a tabela (cliente > "Venda direta" > padrão), porque é o preço decidido; o sugerido aparece ao lado para quem vê custo, com "Usar sugerido". Sem tabela, o sugerido.
3. **Base de custo padrão do tenant.**
   **Recomendação:** custo médio (é o que o estoque mantém e o que vira CMV), com "última compra" e "manual" como opção por categoria ou item. Nunca "o maior dos dois" automático: esconde a origem do número.
4. **Impostos no preço: % estimado ou calculado pela tributação do Fiscal?**
   **Recomendação:** % estimado na política (ex.: alíquota efetiva do Simples), com exceção por categoria e item, "por dentro" (markup divisor). O imposto real continua no Fiscal e no orçado × realizado. Calcular pela regra fiscal por item é muito maior e pode vir depois.
5. **Preço mínimo bloqueia ou só avisa?**
   **Recomendação:** bloqueia o envio/confirmação sem a ação `approve` do módulo (orçamento e pedido), supervisor no PDV, e o aplicativo nunca confirma abaixo do mínimo. Sem margem mínima configurada não há mínimo (nada muda para quem não usar).
6. **Tabela por cliente individual ou por grupo de clientes?**
   **Recomendação:** por cliente agora (simples, cobre o caso "construtora com tabela própria"); grupo de clientes só quando houver caso real, sem mudar o resolvedor.
7. **Quando o custo muda, o preço da tabela muda sozinho?**
   **Recomendação:** não. Lista "Preços a revisar" com aplicar em lote (`catalog.edit_price`). Só a tabela derivada de outra tabela acompanha sozinha, porque é regra que alguém já decidiu.
8. **Onde a PRC entra no bloco de melhorias?**
   **Recomendação:** PRC-00 primeiro do bloco (P, é correção); PRC-01 a PRC-03 logo depois da REC-01 e antes da COM-10. O restante (CAT-08 em diante) depois do bloco, antes do MKT-00, que precisa da CAT-03a.
