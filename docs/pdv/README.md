# Plano do PDV e dos perfis de negócio (proposta)

| | |
|---|---|
| Versão | 0.1 — 15/11/2026 |
| PO | John |
| Status | **Plano aprovado; construção iniciada em 16/11/2026 na `main`, com os módulos novos desligados para a Konebras (PP-41 revista); ADRs em rascunho até os spikes e as stories que as escrevem** |
| Origem | Pedido do PO de 15/11/2026 |
| O que é | Só planejamento. Nenhum código de produto foi alterado |

## 1. Visão geral

O BemAqui Empresas nasceu como gestão para pequena indústria sob encomenda. Este plano propõe abrir o produto para **comércio, alimentação, serviços e e-commerce**, com um PDV moderno, sem mudar o objetivo central do `CLAUDE.md`: cada venda alimenta custo, margem e caixa com dado real.

O plano se apoia em três ideias:

1. **Perfil de negócio.** Um preset em código (módulos, menu, vocabulário, dados iniciais e primeiros passos) aplicado sobre a liberação de módulos que já existe (`tenant_modules`). Habilitar uma padaria ou uma oficina vira escolher o perfil no provisionamento, sem deploy.
2. **PDV local-first.** A tela de venda grava no aparelho e sincroniza depois, com ou sem rede, pelo mesmo caminho. O offline não é adaptado depois; nasce com o primeiro commit.
3. **Reuso do que já existe.** Estoque multi-local, contas a receber, conciliação bancária, aparelho com PIN, quiosque da TV e o adaptador do EmissorFiscal já estão em produção. O PDV acrescenta módulos novos (Catálogo, Vendas, PDV, Restaurante, OS de serviço, Loja virtual) ao lado deles, pelas mesmas regras do monolito modular.

O caminho crítico não é código: é a **NFC-e no EmissorFiscal**, o **agente de assinatura na loja** (decidido pelo PO para a venda sem internet), o **hardware real** (impressoras, balanças, maquininhas) e as respostas da contadora por UF.

## 2. O que o PO pediu

Lista de requisitos do pedido de 15/11/2026, como foi passada ao planejamento. Os trechos entre aspas são as palavras do PO.

1. Modelos de negócio: **Comércio, Fábrica, Serviços e E-commerce**.
2. Segmentos: **supermercado, padaria, restaurante, lanchonete, hamburgueria, trailer e oficina**.
3. Perfil de negócio **fácil de habilitar**.
4. PDV **moderno e prático**.
5. **Offline desde o primeiro release.**
6. **Maquininhas de cartão externas primeiro**, com a integração já prevista.
7. Impressoras com **NFC-e ou cupom não fiscal**.
8. **CPD**: o Centro de Processamento de Dados no sentido do varejo, isto é, a **retaguarda**, onde se faz o controle geral (lançar as notas de entrada, precificar, cadastrar, acompanhar os caixas). Não é centro de distribuição (esclarecido pelo PO em 15/11/2026).
9. Painel **"Na cozinha / Pedidos prontos"**.
10. **Mesas e comandas.**
11. **Pagamento dividido por pessoa e por forma de pagamento.**
12. **Ajustes no EmissorFiscal** para o que o PDV precisar.
13. **"Não propor nada menos do que o mercado pratica."**

Onde cada pedido é atendido: [01-PRD.md](01-PRD.md), seção 2.

## 3. Índice

| Documento | O que traz |
|---|---|
| [00-benchmark-mercado.md](00-benchmark-mercado.md) | Piso e melhor do mercado por segmento, com fontes; os 38 requisitos mínimos; a **matriz de paridade** (requisito → story) |
| [01-PRD.md](01-PRD.md) | Escopo, perfis de negócio, módulos reusados e novos, escopo por segmento, regras e permissões |
| [02-arquitetura.md](02-arquitetura.md) | Como o PDV cabe no monolito modular; síntese dos seis rascunhos de ADR; os três spikes |
| [03-modelo-dados.md](03-modelo-dados.md) | Entidades novas por módulo e as ligações com Estoque, Financeiro e Fiscal |
| [04-fluxos-estados.md](04-fluxos-estados.md) | Máquinas de estado: venda, caixa, comanda e mesa, cozinha, split, devolução, documento fiscal, sincronização |
| [05-backlog.md](05-backlog.md) | Fases 10 a 17, épicos, as 93 stories, o marco MVP, a regra do go-live, esforço em dois cenários e paralelismo |
| [06-pendencias-riscos.md](06-pendencias-riscos.md) | Perguntas ao PO em primeiro lugar, decisões (PP-01 a PP-42), perguntas para a contadora (QC-01 a QC-17) e riscos |
| [07-emissor-fiscal.md](07-emissor-fiscal.md) | As 41 lacunas do EmissorFiscal para a NFC-e, o contrato da API, fases e homologação |
| [adr/](adr/README.md) | Seis rascunhos de ADR (A a F), ainda não numerados |
| [prototipo/](prototipo/README.md) | Dois protótipos navegáveis em HTML: o do plano (por tipo de comércio, com offline, cozinha, retaguarda e caixa) e as quatro telas por modo de venda trazidas pelo PO |
| [stories/](stories/README.md) | Stories detalhadas das Fases 10 e 11 e as quatro primeiras do e-commerce (32 stories) |

## 4. Como ler

- **Tem 10 minutos:** esta página (com a seção 7, da revisão), a seção 7.6 do [benchmark](00-benchmark-mercado.md#76-resultado) e as duas primeiras seções de [06-pendencias-riscos.md](06-pendencias-riscos.md#1-perguntas-ao-po-em-primeiro-lugar).
- **Vai decidir o escopo:** [01-PRD.md](01-PRD.md) e [05-backlog.md](05-backlog.md).
- **Vai falar com a contadora:** [06-pendencias-riscos.md](06-pendencias-riscos.md), seção 4, e [07-emissor-fiscal.md](07-emissor-fiscal.md), seção 2.6.
- **Vai construir:** [02](02-arquitetura.md), [03](03-modelo-dados.md), [04](04-fluxos-estados.md), os [rascunhos de ADR](adr/README.md) e as [stories](stories/README.md).

### Marcas de confiança

O plano junta quatro análises feitas em paralelo. Cada uma marcou o que conferiu e o que supôs, e as marcas foram mantidas:

| Marca | Onde aparece | Significado |
|---|---|---|
| **[V]** / **[C]** | Benchmark | [V] verificado em fonte numerada; [C] prática corrente de mercado, não confirmada em fonte primária |
| **[C]** / **[F]** | Emissor fiscal | [C] conferido no código ou na documentação do repositório; [F] conhecimento fiscal geral, a confirmar com a contadora ou a SEFAZ |
| "Conferido" / "a validar" | Arquitetura e rascunhos de ADR | Lido no código ou nas ADRs; ou conhecimento geral que pede spike |

Atenção: `[C]` quer dizer coisas diferentes no benchmark e no documento do Emissor. **Nenhuma regra fiscal deste plano foi confirmada com a contadora.** Tudo o que é prazo, obrigatoriedade ou regra por UF está como pergunta (QC-nn), não como requisito.

### Identificadores

| Prefixo | O que é |
|---|---|
| `PERF`, `CAT`, `PAG`, `VND`, `PDV`, `CX`, `IMP`, `OFF`, `CPD`, `NFCE`, `ECM`, `KDS`, `MESA`, `DLV`, `FID`, `OFI`, `INFRA` | Stories, por prefixo ([05-backlog.md](05-backlog.md)) |
| `SPK` | Spikes (prova técnica com prazo fechado, sem entrega de produto) |
| `PP-nn` | Pendência do plano: decisão do PO. Vira `P-31` em diante no `docs/06-pendencias-riscos.md` quando o plano for aprovado |
| `QC-nn` | Pergunta para a contadora |
| `RP-nn` | Risco do plano |
| Rascunho A a F | Rascunhos de ADR; viram ADR 0046 em diante na aprovação |

### Marcos

Três termos aparecem em todo o plano e querem dizer coisas diferentes:

| Termo | O que é | Onde |
|---|---|---|
| **Piloto interno** | O PDV vende com rede e não trava se ela cair; abrir turno e entrar ainda exigem rede. Só uso interno e demonstração. É a etapa "F1" do rascunho A | Fase 11 até a OFF-02 |
| **MVP** | Balcão com cupom não fiscal e offline completo (abrir turno e PIN sem rede: a etapa "F2" do rascunho A), em **piloto com um lojista parceiro**. Roda na VPS atual (PP-07), com o backup mais frequente da INFRA-02 | Fim da Fase 11 |
| **Versão pública** | O que se anuncia e vende ao mercado: o MVP mais NFC-e online | Fim da Fase 13 |

## 5. Onde as análises discordam

Dois pontos tiveram recomendações opostas. O plano apresenta os dois lados; a decisão é do PO.

| Tema | Posição 1 | Posição 2 | Situação |
|---|---|---|---|
| Venda sem internet em loja que emite NFC-e | Arquitetura: cupom não fiscal agora e NFC-e na volta da rede, com 4G de reserva | Emissor: agente de loja com certificado na loja, que é a contingência prevista na norma | [PP-01](06-pendencias-riscos.md#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e): **decidida pelo PO em 15/11/2026: assinatura na loja**, com um servidor por loja e as proteções. Ficam as perguntas à contadora por UF e a consulta à certificadora |
| Venda com saldo de estoque insuficiente | Produto: manter a regra de saldo nunca negativo e abrir pendência | Arquitetura: permitir saldo negativo só em local de loja | [PP-02](06-pendencias-riscos.md#pp-02--venda-com-saldo-insuficiente): em aberto |

Há também um ponto em que a arquitetura ficou **abaixo do piso de mercado**: a janela máxima sem sincronizar proposta é de 72 h, e o piso é de 7 dias (PP-10).

## 6. O que acontece quando o plano for aprovado

Este diretório é uma proposta isolada. Nada fora de `docs/pdv/` foi alterado. Na aprovação:

1. As decisões do PO entram em `docs/06-pendencias-riscos.md` como P-31 em diante, com data e autor.
2. Os rascunhos A a F viram ADRs numeradas (0046 em diante) em `docs/adr/`, junto com os adendos às ADRs 0003, 0008, 0019, 0023, 0026, 0027, 0030 e 0032 ([lista](adr/README.md#adendos-a-adrs-existentes)).
3. O conteúdo de `01` a `05` é integrado aos documentos principais (`docs/01-PRD.md` a `docs/05-backlog.md`), e as stories passam para `docs/stories/`.
4. O `CLAUDE.md` é atualizado em dois pontos: a frase "para pequenas indústrias sob encomenda" e a regra "módulo não liberado aparece no menu com cadeado" (PP-03).

Até lá vale a regra do repositório: **item pendente não é implementado sem decisão registrada.**

## 7. Revisão independente de 15/11/2026: achados e o que foi aplicado

Uma revisão independente leu os 43 arquivos da primeira versão e deu o veredito "pronto para revisão do PO depois das correções". No mesmo dia o PO respondeu a cinco perguntas. Esta seção registra as duas coisas.

### Decisões do PO (15/11/2026)

| Pergunta | Decisão | Onde entrou |
|---|---|---|
| O primeiro cliente é e-commerce ou balcão? | **Balcão.** A trilha do balcão vem antes | PP-16; [backlog](05-backlog.md#dependências-e-paralelismo) |
| Quem faz o EmissorFiscal, e pode começar antes do go-live? | **O próprio PO, em outra frente, e pode começar antes** (exceção à regra do go-live, só para o Emissor) | PP-13, PP-41; [regra do go-live](05-backlog.md#regra-do-go-live) |
| Venda sem internet em loja fiscal | **Contingência offline de verdade, com assinatura na loja.** Certificado no computador da loja: um servidor por loja, chave não exportável, certificado separado do cofre, série própria com alerta, inventário. "Cupom agora, NFC-e depois" só como modo degradado, desligado por padrão | PP-01; rascunho E; 07, seção 2.6; NFCE-04 |
| Cupom fiscal × não fiscal | **As duas opções existem; a escolha é configurável por tenant**, para qualquer empresa. O Proprietário liga com aceite registrado; o comprovante diz "Não é documento fiscal"; há relatório de vendas sem documento fiscal | PP-05, PP-35; IMP-01 |
| "CPD" é depósito ou retaguarda? | **Retaguarda** (Centro de Processamento de Dados no sentido do varejo) | PP-40; Fase 12 reescrita; CPD-01 |
| Limite de desconto | **Por loja**, com o grupo dando só a permissão | PP-38; PDV-04 |
| PIN sem rede | **Operador com o mesmo PIN; supervisor com código de aprovação offline separado** | PP-39; OFF-03 |
| Cupom fiscal ou não fiscal: por empresa ou por loja | **Padrão da empresa, com exceção por loja** | PP-05; `stores.fiscal_mode` |
| Hospedagem | **Ficar na VPS atual por enquanto**, com consequências aceitas, mitigações e gatilhos para reabrir | PP-07; INFRA-01 condicionada; INFRA-02 |

Continuam abertas, fora do PO: as perguntas à contadora por UF (QC-01 e QC-12 seguram a assinatura na loja) e a consulta à certificadora. Com o PO: as pendências da [seção 2 do 06](06-pendencias-riscos.md#2-decisões-que-destravam-as-fases-10-e-11), a começar pela PP-02.

### Achados e correções

| Achado | Resumo | Onde foi corrigido |
|---|---|---|
| **B1** | E-commerce: dependência declarada falsa; sem story detalhada | Núcleo de vendas sem caixa na Fase 10 ([VND-01](stories/VND-01-nucleo-vendas-sem-caixa.md)); dependências corrigidas no [backlog](05-backlog.md#dependências-e-paralelismo) (VND-01, dívida E14, lacuna 25); stories ECM-01 a ECM-04 escritas; ECM-07 reduzida descrita; ordem decidida pelo PO (PP-16) |
| **B2** | Contingência "internet ok, SEFAZ fora" sem lacuna nem story | Lacuna 41 no [07](07-emissor-fiscal.md); story NFCE-08, dona do estado `AuthorizedInContingency`; NFCE-00 inclui a lacuna; QC-01; SPK-02 |
| **B3** | "A1 dedicado de alcance controlado" induzia a erro | [PP-01](06-pendencias-riscos.md#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e) e [07, 2.6](07-emissor-fiscal.md#26-quem-assina-offline-opções-e-recomendação) reescritos: o certificado vale para todo o CNPJ; cinco proteções obrigatórias; risco RP-26. O PO decidiu pela assinatura na loja já com esse texto |
| **B4** | Prazo e capacidade pouco honestos | [Esforço](05-backlog.md#esforço) com uma tabela e dois cenários; dono do Emissor (PP-13); regra do go-live sem contradição (PP-41); PDV-01 e OFF-03 passam a G |
| Major 1 | Pix dinâmico só na Fase 16 e só pelo Inter | PAG-02 na Fase 12, com porta para qualquer PSP; rascunho C |
| Major 2 | Pix conferido pelo operador sem controle | PDV-03, CX-02 e CX-03: conferência contra o extrato no fechamento e relatório por operador |
| Major 3 | Matriz "0 itens sem story" nominal | Épicos marcados no backlog (NFCE-00, NFCE-04, ECM-06, ECM-07, DLV-04, PAG-04) e nota na [seção 7.6](00-benchmark-mercado.md#76-resultado) do benchmark |
| Major 4 | Inversões de ordem | "Pesar abre a comanda" foi para a IMP-05; KDS-01 lista a IMP-03; DLV-02 explicita a ECM-01; PERF-04 passou para o fim da Fase 10 |
| Major 5 | Faltas que o lojista nota | Categoria e variação no [modelo](03-modelo-dados.md) e na CAT-01; NF-e 55 no balcão (PDV-14); pré-venda (PDV-15); recolhimento do dinheiro no fechamento (CX-02 e CX-03) |
| Major 6 | Piloto não fiscal só legal para dispensado | Superado pela decisão do PO: PP-05 e PP-35 reescritas; aceite do Proprietário e relatório na IMP-01; risco RP-17 mantido, com a responsabilidade do fornecedor a confirmar com o jurídico |
| Major 7 | "Sete decisões" incompleto | [06, seção 2](06-pendencias-riscos.md#2-decisões-que-destravam-as-fases-10-e-11) com a lista completa |
| Major 8 | Sentido de "CPD" | Decidido pelo PO: retaguarda. Fase 12 reescrita; CPD-01 (nota de entrada e precificação) com critérios testáveis; PP-40 |
| Major 9 | Fronteira `Sales` × `Pos` contraditória | [02, seção 4.1](02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê): dono de cada tabela e endpoint e o contrato `ISaleIngestion`; 03, rascunhos A e F, PDV-01, CX-02 e OFF-02 alinhados |
| Major 10 | Troca e revogação de aparelho podiam perder vendas | Época do aparelho na chave e no número da venda; aparelho revogado mantém só a subida (PDV-01, OFF-02, 03, 04) |
| Major 11 | Limite de desconto por grupo × por loja | Virou pendência: PP-38 |
| Major 12 | Rotas anônimas novas sem adendo | Adendo à ADR 0009 em [adr/README](adr/README.md#adendos-a-adrs-existentes); citado nas stories que abrem rota |
| Major 13 | Conciliação N para 1 | CX-03: um depósito baixa vários títulos; conferir a BNK-03b virou ponto em aberto da story |
| Major 14 | PIN offline | OFF-03 reescrita (chave de cifra não extraível, limites, piso de horário do servidor); PP-39 (PIN offline igual ao online ou não) |
| Major 15 | Registro de aparelho | PDV-01: TOTP recente e aviso ao Proprietário |
| Major 16 | Dados de cartão | Regra "nunca guardar número de cartão, CVV ou trilha" no PRD, no rascunho C, na VND-01, na PDV-03 e na ECM-03; risco RP-27 |
| Major 17 | LGPD | CPF fora do evento imutável (OFF-02, 03); busca por CPF por POST e índice cego (PDV-06); PP-26 reescrita (finalidades, operador × controlador, piloto) |
| Major 18 | Regras fiscais escritas como fato | QC-17 (data de emissão × data da venda); "item apto" marcado como a confirmar; nota sobre as datas de IBS/CBS no benchmark |
| Major 19 | Controles de caixa | Tolerância por forma; sangria sem revelar o esperado; reabertura por pessoa diferente, com aviso ao Proprietário (CX-01, CX-02, 04) |
| Minor | Linha de status dos rascunhos B a F | Corrigida |
| Minor | Rascunho A sem o tipo Garçom; `pos.catalog_items` | Corrigidos |
| Minor | Devolução sem rede divergente | PRD 6.2 alinhado: toda devolução exige rede |
| Minor | OFF-03 CA 5 × CA 1 | CA 5 reescrito |
| Minor | "Versão pública", "MVP" e F1/F2 sem definição | Seção [Marcos](#marcos) |
| Minor | PPs fora de "Dependências" | IMP-02, OFF-01, CX-01 e PDV-02 corrigidas |
| Minor | NFCE-05 separada da NFCE-02 | Regra de data na NFCE-05 |
| Minor | Remover item bipado por engano | PDV-02 CA 10: o último item lançado sai sem supervisor |
| Minor | Sem restrição de acesso por loja | Virou pendência: PP-42 |
