# 01 — PRD do PDV e dos perfis de negócio (proposta)

| | |
|---|---|
| Versão | 0.1 — 15/11/2026 |
| PO | John |
| Status | Proposta. Integra o `docs/01-PRD.md` quando aprovada |
| Base | Pedido do PO de 15/11/2026; [benchmark](00-benchmark-mercado.md); leitura do código em 15/11/2026 |

## 1. Visão

O BemAqui deixa de ser "ERP de fábrica sob encomenda" e passa a ser **gestão com visibilidade financeira para o pequeno negócio brasileiro**. O objetivo central não muda: o dono sabe quanto custa, quanto rende e como estará o caixa. No varejo isso significa que cada venda do PDV chega ao custo, à margem e ao fluxo de caixa sem digitação, com a taxa da maquininha e o prazo de recebimento já considerados.

### 1.1 Problemas que resolve
1. O lojista usa um sistema para o caixa, outro para o estoque e uma planilha para o financeiro; a margem real ninguém vê.
2. O caixa para quando a internet cai.
3. Cada segmento (lanchonete, mercado, oficina, loja virtual) compra de um fornecedor diferente; quem tem dois negócios tem dois sistemas.
4. KDS, totem, TEF e robô de pedidos são cobrados à parte, e o preço final é imprevisível.
5. Quebra de caixa, cancelamento e desconto indevidos só aparecem no fim do mês.

### 1.2 Métricas de sucesso (a validar no piloto)
- Primeira venda de teste em até **30 minutos** depois do primeiro login do proprietário.
- Item adicionado na venda em menos de **100 ms**, com ou sem rede.
- Venda de 3 itens em dinheiro em até **10 segundos**.
- Zero venda perdida ou duplicada na sincronização.
- Fechamento de caixa que já chega ao financeiro, sem relançar.
- Navegação da Konebras idêntica antes e depois (regressão zero no tenant em produção).

## 2. Como cada pedido do PO é atendido

| # | Pedido | Como o plano atende | Onde |
|---|---|---|---|
| 1 | Modelos Comércio, Fábrica, Serviços e E-commerce | Quatro perfis de negócio: `retail`, `factory`, `services`, `ecommerce`, que combinam por união | Seção 3; PERF-01 |
| 2 | Supermercado, padaria, restaurante, lanchonete, hamburgueria, trailer, oficina | Segmentos: refinamento do perfil que troca dados iniciais e padrões de tela | Seções 3.1 e 5 |
| 3 | Perfil fácil de habilitar | Passo "Perfil de negócio" no provisionamento; "Adicionar perfil" em tenant existente; checklist de primeiros passos | Seção 3.4; PERF-02, PERF-04 |
| 4 | PDV moderno e prático | Dois modos por caixa (toque e teclado/leitor), consulta sempre local, atalhos | Seção 5; PDV-02 |
| 5 | Offline desde o primeiro release | PDV local-first: grava no aparelho e sincroniza | [Rascunho A](adr/rascunho-A-offline.md); OFF-01 a OFF-03 |
| 6 | Maquininha externa primeiro, integração prevista | Pagamento com tipo de integração e campos de TEF desde o primeiro dia; `ManualTerminal` agora | [Rascunho C](adr/rascunho-C-pagamentos.md); PDV-03, PAG-02 a PAG-04 |
| 7 | Impressoras com NFC-e ou cupom não fiscal | Cupom não fiscal no MVP; DANFE NFC-e na Fase 13 | [Rascunho B](adr/rascunho-B-impressao-perifericos.md); IMP-01, IMP-02, NFCE-02 |
| 8 | CPD | Local de estoque "Depósito (CPD)" e transferência para as lojas, sobre o multi-local que já existe | CPD-01 a CPD-05 |
| 9 | Painel "Na cozinha / Pedidos prontos" | Visão nova do quiosque da TV (ADR 0030) | [Rascunho D](adr/rascunho-D-tempo-real-kds-painel.md); KDS-03 |
| 10 | Mesas e comandas | Módulo Restaurante | MESA-01 a MESA-03 |
| 11 | Pagamento dividido por pessoa e por forma | "Partes" da venda, cada uma com um ou mais pagamentos | [04-fluxos-estados.md](04-fluxos-estados.md#5-split-de-pagamento); PDV-03, MESA-04 |
| 12 | Ajustes no EmissorFiscal | 40 lacunas mapeadas, com fases | [07-emissor-fiscal.md](07-emissor-fiscal.md); NFCE-00 |
| 13 | "Não propor nada menos do que o mercado pratica" | Matriz de paridade: 38 requisitos mínimos, todos com story | [Benchmark, seção 7](00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |

## 3. Perfil de negócio

### 3.1 O que é

Um preset declarado em código, num `BusinessProfileCatalog` ao lado do `ModuleCatalog` (em `BuildingBlocks/Modules`). Cada perfil declara cinco coisas:

1. **Módulos relevantes** (códigos do `ModuleCatalog`).
2. **Início e menu:** cards do Início e rótulo das áreas do dock.
3. **Vocabulário:** dicionário pequeno de termos, com teto de cerca de 30 chaves (PP-25).
4. **Pacote de dados iniciais (seed):** idempotente por `seed_key`, padrão que já existe em `units` e `locations`.
5. **Grupos sugeridos e checklist de primeiros passos.**

| Perfil | Código | Segmentos |
|---|---|---|
| Fábrica | `factory` | — |
| Comércio | `retail` | `counter` (balcão), `supermarket`, `bakery`, `restaurant`, `snack_bar` (lanchonete e hamburgueria), `food_truck` (trailer) |
| Prestação de serviços | `services` | `workshop` (oficina) |
| E-commerce | `ecommerce` | — |

**Segmento** só troca dados iniciais e padrões de tela. Nunca cria permissão nem módulo.

### 3.2 Como se apoia no que existe

| Conceito | Hoje | Proposta |
|---|---|---|
| Limite comercial | `plan_modules` → `tenant_modules` | Sem mudança |
| Relevância | Não existe | `catalog.tenant_profiles` (banco de catálogo): um ou mais perfis por tenant |
| Liberação efetiva (camada 1) | Plano + exceção manual | `liberado = (no plano E em algum perfil do tenant) OU exceção manual`. `tenant_modules` continua sendo a única tabela lida pela camada 1; o perfil só a grava, sem marcar exceção |
| Módulo fora do perfil | Aparece com cadeado | **Oculto**: estado novo `hidden` em `GET /me/permissions`. O cadeado fica para "no perfil, fora do plano". Muda uma regra do `CLAUDE.md`, por isso exige ADR (PP-03) |
| Áreas do dock | Sete fixas; uma se chama "Fábrica" | Área nova **"Loja"**; tenant sem o perfil `factory` vê "Retaguarda" no lugar de "Fábrica" |
| Início | `Inicio.tsx` + módulo `Dashboard` | Cards novos compostos pelo `Dashboard` via contratos (padrão da ADR 0028) |

**Konebras:** uma migration do catálogo grava o perfil `factory` para os tenants existentes. O perfil `factory` lista exatamente os módulos de hoje. Um teste de regressão compara a navegação da Konebras antes e depois; ela tem de ser idêntica.

### 3.3 Módulos por perfil

| Módulo | Fábrica | Comércio | Serviços | E-commerce |
|---|---|---|---|---|
| dashboard, parties, finance, fiscal, identity, settings, inventory, purchasing | sim | sim | sim | sim |
| hr, assets | sim | opcional (cadeado) | opcional | opcional |
| quotes, projects | sim | oculto | sim (orçamento) | oculto |
| engineering, production, shopfloor | sim | oculto | oculto | oculto |
| kiosk (TV) | sim | sim (painel de pedidos) | oculto | oculto |
| **catalog** (novo) | opcional | sim | sim | sim |
| **sales** + **pos** (novos) | oculto | sim | sim (balcão de peças) | sim (pedidos) |
| **restaurant** + **kds** (novos) | oculto | só nos segmentos de alimentação | oculto | oculto |
| **service_orders** (novo) | oculto | oculto | sim | oculto |
| **storefront** (novo) | oculto | opcional | opcional | sim |

Perfis combinam por união. Exemplos: padaria que vende online = `retail/bakery` + `ecommerce`; a Konebras com balcão de peças = `factory` + `retail/counter`.

### 3.4 Habilitar em minutos

O provisionamento já roda em menos de 1 minuto, com etapas retomáveis e seeds ordenados (`ProvisioningService`, `ITenantSeeder`). Falta o passo do perfil e os pacotes de dados.

1. **Super admin** (`ProvisionDialog`): dados da empresa → **perfil de negócio** (cards, multi-seleção) → **segmento** → plano → proprietário. O perfil é aplicado logo depois do `ApplyPlanAsync`; a etapa `Seed` roda os pacotes do perfil.
2. **Tenant existente:** aba "Perfis" no detalhe do tenant, com "Adicionar perfil" (idempotente). Remover perfil só oculta; nunca apaga dado.
3. **Proprietário, primeiro login:** checklist "Primeiros passos" no Início, em cinco passos: empresa e regime tributário → importar produtos por planilha → formas de pagamento e taxas → impressora → abrir o caixa e fazer uma venda de teste.
4. **Pacote do Comércio:** locais "Loja" e "Depósito (CPD)"; contas "Caixa PDV 01" e "Cofre"; formas de pagamento (Dinheiro, Pix, Débito, Crédito à vista, Crédito parcelado, Vale); adquirente "Maquininha" com taxa e prazo zerados e o chip "A confirmar"; tabela de preço "Balcão"; grupos Gerente de loja, Operador de caixa e Estoquista; regras fiscais do Simples Nacional com o chip "A confirmar com a contadora" (ADR 0042). O segmento acrescenta estações de preparo, mesas de exemplo, categorias e unidades.

Fora do plano: o cliente criar a própria conta sozinho (PP-27).

## 4. Módulos

Um projeto por módulo em `src/Modules/<M>/`, nas cinco camadas, com schema próprio, referenciando só `*.Contracts` (ADR 0006).

### 4.1 Reuso com extensão

| Módulo | O que já serve | O que precisa acrescentar |
|---|---|---|
| Platform | Catálogo, provisionamento, `tenant_modules` | `tenant_profiles`, passo de perfil, aba Perfis |
| Identity | Grupos, permissões, aparelho + PIN, quiosque | `shopfloor_devices` generalizado para "aparelhos" com tipo (Fábrica, Caixa, KDS, Garçom, Estação de impressão), loja e caixa; esquema de autenticação `Device`; PIN de supervisor como aprovação; usuário de operação sem e-mail (PP-12) |
| Parties | Clientes e fornecedores | Cadastro rápido no PDV; adquirente de cartão como terceiro; "Consumidor final" sem cadastro |
| Inventory | Itens, locais, saldos, transferência, lotes com validade, inventário cego, custo médio, reservas | `ItemType.Merchandise`; origens de movimento de venda e de devolução; **saída de venda sem bloqueio** (PP-02); contrato de transferência entre locais; perdas com motivo |
| Purchasing | Solicitação → cotação → pedido → recebimento | Sugestão de compra por giro e mínimo |
| Finance | Contas do tipo Caixa, transferências, contas a receber, conciliação, DRE | Formas de pagamento, adquirentes (taxa e D+N), contrato de escrita para a venda, origem `Sale`, recebíveis de cartão, conta "Quebra de caixa", porta de Pix cobrança |
| Fiscal | `IFiscalEmitter`, regras fiscais, NF-e | `InvoiceType.NFCe`, CSC, regras do Simples, NF-e de devolução, vínculo com a venda |
| Dashboard | Composição por contratos | Cards de vendas, caixas abertos, ticket médio, ruptura |
| Kiosk (TV) | Token de tela só leitura, `KioskHub` | Visão "pedidos", com escopo por loja |
| Engineering, Production, Commercial, HR, Assets | Sem mudança | — |

### 4.2 Módulos novos

| Módulo | Schema | Código(s) no `ModuleCatalog` | Área | Responsabilidade |
|---|---|---|---|---|
| **Catalog** | `catalog_products` (o nome evita colisão com o banco `catalog` da plataforma) | `catalog` | Loja | Tudo o que torna um item **vendável**: nome de venda, foto, códigos de barras, tabelas de preço, promoções, complementos, combos, ficha técnica, etiquetas. O Estoque segue dono do item físico (unidade, NCM, custo, saldo) |
| **Sales** | `sales` | `sales` | Loja | A venda em si: itens, partes, pagamentos, devolução e troca, vale-troca. Serve ao PDV agora e ao restaurante, à oficina e à loja virtual depois |
| **Pos** | `pos` | `pos` (tela dedicada `/pdv`, como o `shopfloor`) | Telas dedicadas | Lojas, caixas, sessão de caixa, sangria e suprimento, eventos do aparelho, sincronização, impressoras, aprovações de supervisor |
| **Restaurant** | `restaurant` | `restaurant`, `kds` (tela dedicada) | Loja | Áreas, mesas, comandas, estações de preparo, tíquetes de cozinha, senha do painel, taxa de serviço, couvert |
| **ServiceOrders** | `service_orders` | `service_orders` | Loja ou Serviços | OS de serviço com veículo ou equipamento, checklist com fotos, mão de obra e peças. Absorve a P-10 do `docs/06` |
| **Storefront** | `storefront` | `storefront` | Loja | Loja virtual em host próprio, carrinho, pedidos online, frete; depois, delivery próprio e integrações |

**Uma só implementação de pagamento e caixa.** Restaurante, OS de serviço e loja virtual pagam pelo `Sales` (contrato `IOrderPayments`). A comanda, a OS e o pedido online viram uma venda.

**Divisão Sales × Pos.** A análise de produto propunha um projeto só (`Sales`, com os códigos `sales` e `pos`); a de arquitetura, dois. O plano adota dois projetos, porque o e-commerce usa a venda sem usar caixa nem aparelho. A alternativa e a questão do Catálogo como módulo próprio estão na PP-04.

### 4.3 Contratos e eventos principais

| Contrato | Módulo dono | Para quê |
|---|---|---|
| `ICatalogLookup` | Catalog | Achar produto por código de barras, PLU ou busca, com o preço vigente |
| `IPriceLists` | Catalog | Tabelas de preço e versão vigente por loja e canal |
| `IRecipes.ExplodeAsync` | Catalog | Explodir a ficha técnica em insumos (Fase 15) |
| `ICatalogSnapshot` | Catalog | Pacote versionado para o aparelho (foto inicial + delta) |
| `ISalesRevenue` | Sales | Receita por dia para o DRE e o Dashboard |
| `ISaleLookup` | Sales | Localizar venda para devolução, por número ou CPF |
| `IOrderPayments` | Sales | Pagar comanda, OS ou pedido online |
| `IPaymentMethods` | Finance | Formas de pagamento, adquirentes, taxa e prazo |
| `IPixChargeProvider` | Finance | Pix cobrança imediata (Fase 16), ao lado do `IBankBillingProvider` |
| `IEstablishments` | A conferir (quem guarda hoje os dados da empresa) | Loja como estabelecimento: CNPJ e IE de filial, emitente, local de estoque e conta padrão (PP-15) |

| Evento | Quem publica | Quem reage |
|---|---|---|
| `SaleCompleted` | Sales | Inventory (baixa), Finance (só o fiado, na hora), Fiscal (pedido de NFC-e, na Fase 13) |
| `SaleCancelled`, `SaleReturned` | Sales | Inventory (estorno ou entrada), Finance, Fiscal |
| `CashMovementRecorded` | Pos | Finance (transferência entre o Caixa do PDV e o Cofre) |
| `CashSessionClosed` | Pos | Finance (resumos por forma, adquirente e vencimento; quebra de caixa) |
| `PriceChanged`, `ProductChanged` | Catalog | Pos (atualiza o modelo de leitura que desce para os aparelhos) |
| `InvoiceAuthorized`, `InvoiceRejected`, `InvoiceCancelled` (NFC-e) | Fiscal | Sales |
| `KitchenTicketReady`, `OrderReady` | Restaurant | Painel do cliente e app do garçom, por SignalR |

**Por que o financeiro recebe resumos.** Um supermercado gera milhares de vendas por dia; um lançamento por venda poluiria extrato, conciliação e DRE. O detalhe fica no `Sales`; o `Finance` guarda o resumo com link. O recebível de cartão é um título contra a adquirente (bruto, taxa prevista, vencimento em D+N), que a conciliação bancária existente casa com o depósito.

## 5. Escopo por segmento

**PDV núcleo (igual para todos):** venda rápida por busca, leitor ou grade de toque; quantidade; desconto por item e no total; cliente opcional e CPF; várias formas de pagamento na mesma venda; troco; venda em espera; caixa completo; cupom não fiscal ou NFC-e; reimpressão; devolução e troca; offline; atalhos de teclado.

| Segmento | Usa do núcleo | Extensão do segmento | Fase |
|---|---|---|---|
| **Balcão** (loja em geral) | Tudo | Vendedor e comissão simples; fiado com limite | 11 (MVP) e 12 |
| **Trailer de lanches** | Tudo, com **offline como caso principal** | Um aparelho só (celular ou tablet); impressora Bluetooth; painel opcional; Pix | 11 e 15 |
| **E-commerce** | Catálogo, estoque, financeiro | Vitrine, carrinho, checkout Pix e cartão, frete, NF-e, expedição; marketplaces e plataformas | 14 |
| **Lanchonete, hamburgueria** | Tudo, no modo toque com grade e fotos | Complementos e combos; ficha técnica baixando insumos; pedido com senha; KDS; painel "Na cozinha / Pedidos prontos"; para viagem; delivery | 15 |
| **Restaurante** | Pagamento e caixa | Mapa de mesas; comandas; garçom no celular; envio por estação; taxa de serviço; couvert; pré-conta; **split por pessoa**; transferir e juntar mesas | 15 |
| **Supermercado** | Tudo, no modo teclado e leitor | Etiqueta e carga de balança; balança no caixa; promoções; consulta de preço; validade e perdas; sugestão de compra; TEF | 12 e 16 |
| **Padaria** | Tudo | Venda por peso; fornada (receita que produz para o estoque); encomendas; comanda por cartão; etiquetas | 15 e 16 |
| **Oficina** | Pagamento, caixa, balcão de peças | OS com veículo, checklist com fotos, orçamento aprovado pelo cliente, mão de obra + peças, NFS-e + NF-e | 17 |

Dois modos de tela, escolhidos por caixa:
- **Toque:** grade de categorias e produtos, botões de 56 px ou mais (padrão do `/fab`).
- **Teclado/leitor:** foco permanente no campo de código, teclas `F` para funções, `*` para quantidade, sem mouse.

## 6. Regras

### 6.1 Regras de negócio
1. **A venda concluída nunca é recusada.** Nem por falta de saldo, nem por preço desatualizado, nem por conflito de sincronização. Divergência vira pendência para conferência humana.
2. **O preço é o do aparelho.** O preço e o desconto ficam gravados na linha; o servidor não reprecifica venda sincronizada, só sinaliza a versão vencida da tabela.
3. **Venda e documento fiscal são coisas separadas.** A venda fecha no aparelho; o documento fiscal é um pedido ao servidor, com estado próprio. O aparelho não calcula imposto nem guarda certificado.
4. **Sem sessão de caixa aberta não se vende.** Uma sessão aberta por caixa.
5. **Conferência cega.** No fechamento, o operador não vê o valor esperado.
6. **Troco só no dinheiro.**
7. **Pagamento capturado é fato imutável.** Se uma parte do split falha, as partes pagas ficam; nada é desfeito sozinho.
8. **O supervisor nunca aprova a própria operação.**
9. **Número da venda `{loja}-{caixa}-{seq}`**, gerado no aparelho, fora do serviço central de numeração. Um caixa tem um aparelho ativo por vez.
10. **Item só desce para o caixa de loja fiscal se estiver apto** (NCM, CFOP, CSOSN ou CST e CEST conferidos no cadastro). Assim uma venda offline não falha na emissão depois.
11. **Remover perfil nunca apaga dado.** Só oculta.
12. **O sistema sugere, não decide** (regra do `CLAUDE.md`): efeito financeiro, fiscal ou de estoque sempre com confirmação.

### 6.2 O que funciona sem rede

| Funciona sem rede | Não funciona sem rede |
|---|---|
| Vender (catálogo e preço da última sincronização) | NFC-e (ver PP-01) |
| Dinheiro, cartão em maquininha externa, Pix conferido à mão | Pix dinâmico e TEF |
| Cupom não fiscal na impressora local | Fiado, vale-troca, devolução de venda de outro caixa |
| Sangria, suprimento e fechamento com conferência cega | Consulta de saldo de outra loja, transferência |
| Aprovação de supervisor por PIN (marcada como offline) | Cadastro e troca de preço, relatórios |
| Abrir turno (a partir da primeira versão pública) | Mesas e comandas compartilhadas, KDS e painel em tempo real (PP-23) |
| | Primeiro login e registro do aparelho |

Custos aceitos: o preço pode estar desatualizado até a próxima sincronização; o estoque só é conhecido depois; o aparelho guarda o verificador do PIN dos operadores da loja; aparelho perdido ou roubado com vendas não enviadas perde essas vendas.

### 6.3 Permissões novas

Sempre `modulo.acao`, específicas em snake_case (ADR 0003), todas por seed do `ModuleCatalog`.

| Módulo | Ações padrão | Ações específicas |
|---|---|---|
| `pos` (tela dedicada) | `view`, `create` (vender) | `discount` (acima do limite do operador), `change_price`, `cancel_item`, `cancel_sale`, `return` (devolução e troca), `cash_out` (sangria), `cash_in` (suprimento), `open_drawer` (gaveta sem venda), `skip_fiscal` (vender sem NFC-e em loja fiscal), `manage_devices` (registrar aparelho) |
| `sales` (retaguarda) | `view`, `create`, `edit`, `approve`, `delete` | `view_cost` (custo e margem). `approve` = conferir caixa com quebra, reabrir caixa, aprovar ação restrita de outro operador |
| `catalog` | `view`, `create`, `edit`, `delete` | `edit_price`, `view_cost` |
| `restaurant` | `view`, `create` (lançar), `edit` | `cancel_item` (já enviado à cozinha), `transfer`, `waive_service` (retirar a taxa de serviço) |
| `kds` (tela dedicada) | `view`, `create` (marcar pronto) | — |
| `service_orders` | `view`, `create`, `edit`, `approve`, `delete` | `view_cost` |
| `storefront` | `view`, `create`, `edit`, `approve`, `delete` | — |
| `inventory` (existente) | — | `transfer` e `write_off`, só se a segregação pedir (PP-28) |

Grupos sugeridos pelo seed, editáveis: Operador de caixa (`pos.view`, `pos.create`), Fiscal de caixa (+ as restritas do `pos`), Gerente de loja (+ `sales.approve`, `catalog.edit_price`), Estoquista, Garçom, Cozinha.

### 6.4 Aprovação de supervisor

Ação restrita sem a permissão abre "Chamar supervisor". O supervisor digita o PIN no mesmo aparelho (PIN por usuário e aparelho da ADR 0026). Grava-se `supervisor_approvals`: ação, operador, supervisor, motivo, valores antes e depois, venda, online ou offline.

- Cinco erros bloqueiam, como hoje.
- A ADR 0008 exige TOTP recente para estornar, e a ADR 0026 recusa PIN para quem tem `*.approve`. No balcão isso é impraticável: a proposta é PIN de supervisor + motivo + aviso ao Proprietário, como exceção nomeada (PP-11).

### 6.5 Controles antifraude

| Risco | Controle |
|---|---|
| Cancelamento para embolsar dinheiro | Supervisor diferente do operador; motivo obrigatório; relatório por operador; alerta quando passa de X% das vendas do turno |
| Desconto indevido | Limite % por grupo; acima, PIN; sempre com motivo; relatório "vendas abaixo do custo" |
| Troca de preço | `pos.change_price` separada; preço só da tabela; histórico em `PriceChanged` |
| Sangria desviada | Comprovante em duas vias; a sangria é transferência com destino (cofre), e o cofre é conferido |
| Quebra de caixa | Conferência cega; tolerância; aprovação acima; histórico por operador |
| Gaveta aberta sem venda | `pos.open_drawer` + registro; contagem por turno no relatório |
| Venda não registrada | Painel do cliente e cupom sempre impresso ou oferecido; CMV teórico × real |
| Devolução fictícia | Só com a venda original localizada; prazo; vale-troca em vez de dinheiro por padrão; supervisor |
| Reimpressão usada como comprovante | Marca "2ª VIA" e registro |
| Item cancelado depois de produzido | `restaurant.cancel_item`; relatório de itens cancelados após o envio |
| Fraude offline | Aprovações offline marcadas; relógio do aparelho conferido na sincronização; cadeia de hash dos eventos; vendas offline listadas no fechamento |
| Abuso de `pos.skip_fiscal` | Relatório de vendas não fiscais por operador; parâmetro que desliga o modo por loja |

A trilha é o registro de eventos do aparelho (imutável) mais o `audit.audit_log` nos eventos de exceção (cancelar, descontar, devolver). Não se audita cada item bipado, por causa do volume.

## 7. Requisitos não funcionais

| Tema | Requisito |
|---|---|
| Desempenho | "Instantâneo" é propriedade do aparelho: leitura do código, busca, gravação e impressão não tocam a rede |
| Plataformas do caixa | Windows com Chrome ou Edge; Android com Chrome; PWA instalado. iOS fora do caixa (PP-09) |
| Durabilidade | RPO de 24 h não serve ao varejo: arquivamento contínuo de WAL com restauração a um ponto no tempo (INFRA-01) |
| Hospedagem | API, PostgreSQL e EmissorFiscal num datacenter em São Paulo, em VPS dedicada, antes da versão pública do PDV (PP-07) |
| LGPD | CPF do consumidor só para o documento fiscal e para localizar devolução; cifrado no aparelho, marcado `[PersonalData]`, fora de log e outbox; entra na tabela de retenção (ADR 0018 e 0037) |
| Ajuda | Toda tela nova com `Ajuda/<chave>.md` (regra existente, com teste de cobertura) |
| Tema | A tela de venda segue os tokens do produto; o painel do cliente é sempre escuro |

## 8. Fora do escopo deste plano
- O cliente criar a própria conta (PP-27).
- Servidor local de loja para manter mesas e KDS sem internet (só com demanda comprovada, PP-23).
- iOS como caixa.
- Multi-região de hospedagem.
- Equipamento fiscal específico de UF (a confirmar se ainda existe exigência, QC-07).
