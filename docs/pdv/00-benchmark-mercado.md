# 00 — Benchmark de mercado: PDV e retaguarda para PMEs brasileiras (nov/2026)

Parte do [plano do PDV](README.md). Pesquisa feita em 15/11/2026. As seções 1 a 6 e as fontes são o benchmark como foi levantado; as seções 7 e 8 ligam cada requisito ao [backlog](05-backlog.md) e às [pendências](06-pendencias-riscos.md).

**Como ler:** cada linha traz a marca **[V]** (verificado nesta pesquisa, com fonte numerada) ou **[C]** (prática corrente de mercado que conheço, mas não confirmei em fonte primária nesta rodada — validar antes de virar requisito). Muitas páginas de preço bloqueiam leitura automática ou só divulgam "sob consulta"; as lacunas estão na seção 6.

---

## 1. Matrizes de funcionalidades por segmento

### 1.1 Food service (lanchonete, hamburgueria, trailer, restaurante com mesas, pizzaria)

| Capacidade | Piso de mercado (todos têm) | Melhor do mercado (quem e como) |
|---|---|---|
| Velocidade de venda | PDV touch com busca, balcão/mesa/delivery na mesma tela [C] | Square: divisão por item, assento ou partes iguais; "coursing" e gestão de assentos no plano Premium [V-17] |
| Offline | Continua vendendo e sincroniza depois: Consumer [V-5], Toast (automático) [V-22], Lightspeed [V-19] | Toast: limite de valor por transação offline configurável e permissão para exceder [V-22]. Saipos: contingência "nuvem + infraestrutura local", mas sem TEF, sem app garçom e, segundo a própria página, sem emissão fiscal nesse modo [V-4] |
| Fiscal | NFC-e no PDV; versão não fiscal para MEI (Consumer) [V-5]; NFC-e/NCM/Simples/TEF/Multi-CNPJ (Saipos) [V-2] | Omie: tributação por NCM ou por produto com exceções [V-10] |
| Impressão e cozinha | Impressão automática por estação de preparo (Goomer) [V-7]; múltiplas impressoras (Consumer) [V-5] | KDS com prioridade (Cardápio Web) [V-8]; KDS em monitor/tablet (Consumer, Saipos, Goomer, Yooga Premium) [V-2][V-5][V-7][V-9] |
| Painel "pedido pronto"/senha | Não encontrei em nenhuma página oficial pesquisada [não verificado] | Loyverse: display do cliente gratuito [V-20] |
| Autoatendimento | QR Code na mesa/balcão/comanda e cardápio digital (Goomer, Saipos, Consumer) [V-2][V-5][V-7] | Totem com pedido e pagamento integrados: Goomer (3 modelos, um com maquininha e impressora acopladas) [V-7], Consumer (Pix, cartão e vale-refeição) [V-5][V-6], Saipos [V-2]. Square: totem com pedido por voz (US$ 1.999 + US$ 99/mês) [V-23] |
| Mesas, comandas, pré-conta, couvert, 10%, rodízio | Mesas e comandas em tempo real, "ver e fechar conta" [V-5][V-7][V-8]. Couvert, taxa de serviço e rodízio: [C] | EPOC cita rodízio e catraca [V-27]; demais não confirmados |
| Divisão de conta | [C] por pessoa e por valor | Square: por item, assento ou igual [V-17] |
| Delivery | Site próprio sem comissão + iFood no gestor de pedidos (Saipos, Consumer, Cardápio Web, Yooga) [V-1][V-5][V-8][V-9]; taxa de entrega automática (Goomer) [V-7] | Saipos: roteirização automática, app do entregador, acerto por entrega ou diária [V-1]. Consumer e Cardápio Web: 99Food e Keeta [V-5][V-24]. Padrão Open Delivery (Abrasel) já adotado por 99Food e Keeta [V-25] |
| Combos, adicionais, meio a meio | Opcionais, combos e compra guiada [V-7][V-13] | Goomer: regra de preço para pizza 2 sabores, horários por produto, venda sugestiva [V-7] |
| Fidelidade, cashback, cupom | Cupons e fidelidade por pontos (Goomer, Cardápio Web, Consumer) [V-5][V-7][V-8] | Cashback (Consumer, Cardápio Web, Yooga) [V-5][V-8][V-9] |
| CRM/WhatsApp | Robô de pedidos no WhatsApp (Goomer, Consumer, Saipos Bot, Anota AI) [V-3][V-5][V-7][V-13] | Chatbot com IA (Cardápio Web) [V-8]; disparador de campanhas (Consumer) [V-5]; "PDV dentro do WhatsApp" (Cardápio Web) [V-8] |
| Estoque, ficha técnica, CMV | Estoque com ficha técnica e baixa automática (Saipos, Consumer, Yooga) [V-2][V-5][V-9] | Saipos: CMV teórico e real a cada venda, DRE automático [V-2] |
| Balança | Integração com Toledo, Filizola, Urano (Saipos; Consumer no rodapé) [V-5][V-26] | Saipos: pesar abre a comanda e converte peso em preço [V-27] |
| Caixa | Abertura, sangria, suprimento, fechamento [V-11] | Conferência cega configurável (Alterdata Spice) [V-28]; fechamento automático (Saipos) [V-2] |
| Controle de fraude | Senha de supervisor para cancelamento e desconto por alçada [C] | — |
| TEF/Pix | TEF integrado; Pix no TEF (Consumer/Elgin) [V-5][V-29] | Integração com maquininhas Stone, Cielo e iFood (Consumer) [V-5]; SmartPOS (Saipos) [V-3] |
| App garçom / app do dono | App garçom em celular, tablet e maquininha (Consumer, Saipos) [V-2][V-5] | Relatórios online do dono (Consumer Connect) [V-5]; Lightspeed Pulse [V-19] |
| Multiloja | Relatórios por unidade (Saipos) [V-2] | Loyverse: multiloja gratuito [V-20] |

### 1.2 Varejo: balcão/loja, supermercado/mercadinho, padaria

| Capacidade | Piso de mercado | Melhor do mercado |
|---|---|---|
| Offline e contingência | PDV vende sem internet: Hiper (até 7 dias) [V-14], Omie [V-12], Pluggar [V-30] | Hiper é o número a bater: 7 dias [V-14]. Contingência offline de NFC-e: transmitir em até 24h segundo fornecedores; regra varia por UF [V-4, fonte secundária] |
| Fiscal | NFC-e no caixa com envio automático [V-12][V-15]; SAT acabou em SP em 01/01/2026 [V-31] | Emissão ilimitada sem cobrança por nota (Hiper, Bling Cobalto, MarketUP grátis) [V-14][V-15][V-16] |
| TEF/Pix | TEF homologado [V-12][V-14] | Omie: Pix sem tarifa pela conta própria [V-12]; C6: TEF gratuito multiadquirente (fonte secundária) [V-32] |
| Preços e promoções | Tabelas varejo/atacado e desconto por regra (Omie) [V-12] | Etiquetas eletrônicas de gôndola com mudança em tempo real (Toledo) [V-26] |
| Balança e etiquetas | Exportar carga para Toledo Prix/MGV, Filizola, Urano; ler etiqueta de peso/preço no PDV [V-26] | Bluesoft: setores de balança e etiquetas de preço, inclusive 2D [V-33]; projeto Toledo de bloquear venda de vencido no PDV via código 2D [V-26] |
| Validade/lote, perdas, inventário | Inventário e locais de estoque (MarketUP) [V-16]; validade/lote e quebras [C] | Bluesoft AI: consulta de ruptura de gôndola em tempo real [V-33] |
| Compras e cotação | Central de compras (MarketUP) [V-16]; pedido de compra (Loyverse pago) [V-20] | Cotação com fornecedores: não confirmada em nenhum player |
| Multiloja | Hiper Gestão, MarketUP+ [V-14][V-16] | Bluesoft: operações entre lojas e WMS [V-33] |
| Autoatendimento | Raro no pequeno: só 8% das lojas com até 5 caixas usam (APAS, nov/2025) [V-34] | Padrão em Assaí/Atacadão [V-34] |
| Padaria | PDV + balança + comanda [V-27] | Encomendas (Fast) e ordem de produção a partir de receita [V-27] |
| Relatórios | 60+ relatórios (Hiper), DRE e conciliação bancária (MarketUP) [V-14][V-16] | Conciliador de cartões ligado ao contas a receber (Linx Microvix, Conta Azul) [V-35] |
| Caixa e fraude | Abertura, sangria, suprimento, fechamento [V-11]; senha de supervisor [C] | Conferência cega [V-28] |

### 1.3 Oficina mecânica

| Capacidade | Piso | Melhor |
|---|---|---|
| OS | Status por etapa, alerta de mudança [V-36] | Onmotor: lista, kanban e tela cheia [V-37] |
| Checklist e fotos | Checklist por serviço; fotos na OS visíveis ao cliente [V-36] | OficinaTop: fotos e vídeos na inspeção [V-38] |
| Orçamento | Envio por e-mail/WhatsApp com aprovação online [V-36] | OficinaTop: link com assinatura digital [V-38] |
| Veículo | Cadastro completo e histórico por veículo; cliente vê o histórico online [V-36] | Oficina Integrada: busca por placa ou por foto [V-36] |
| Peças e balcão | Estoque com código de barras e PDV de balcão que gera contas a receber [V-36] | — |
| Financeiro/fiscal | NF-e integrada, fluxo de caixa, link de pagamento Pix/cartão [V-36][V-37] | Onmotor: maquininha Stone e boletos; pesquisa de satisfação [V-37] |
| Lembrete de revisão | Não confirmado em nenhum dos dois líderes pesquisados | Lacuna de mercado aparente |

### 1.4 E-commerce e marketplaces

| Capacidade | Piso | Melhor |
|---|---|---|
| Loja e checkout | Plano grátis ou de entrada (Loja Integrada R$ 0; Tray R$ 39–59; Nuvemshop R$ 69) [V-39][V-40][V-41] | Shopify POS Pro: retirada em loja, prateleira infinita, devolução em qualquer loja [V-21] |
| Tarifa por venda | 0% com pagamento nativo; 0,75–1,25% fora dele (Tray) [V-40][V-41] | — |
| Marketplaces | Hub com Mercado Livre, Shopee, Amazon, Magalu [V-42] | Bling: 250+ integrações nativas, TikTok Shop, Temu e iFood [V-42][V-43]; Tray Ultra: 30+ simultâneos [V-40] |
| Estoque unificado | Sincronização loja física + online (Bling) [V-15] | Tray: multi-CD (2, 3 ou ilimitado) [V-40] |
| Frete | Correios integrado (Bling a partir do Titânio) [V-15] | Melhor Envio e antifraude: não verificados |
| IA | Créditos de IA por plano (Bling 25–1.000/mês; Tray 100–ilimitado) [V-15][V-40] | — |

### 1.5 Fiscal transversal

- **Reforma tributária:** obrigatoriedade de IBS/CBS em NF-e e NFC-e desde **03/08/2026** para o regime normal; **Simples Nacional em janeiro de 2027** (fontes divergem entre 01/01 e 04/01); a rejeição 1115 ainda sem data em produção, mas a obrigação vale. NT 2025.002 já passou por onze versões (v1.51 a mais recente que encontrei). Alíquotas-teste de 2026: CBS 0,9%, IBS 0,1% [V-44][V-45].
- **Lei da Transparência/IBPT no cupom:** piso [C].

---

## 2. Modelos de preço observados

| Player | Modelo | Valor |
|---|---|---|
| Saipos | Mensalidade, sem taxa por usuário, implantação grátis, suporte 24/7 | A partir de R$ 219–240,79/mês (páginas próprias divergem); bot e roteirização são adicionais [V-1][V-2][V-3] |
| Consumer | Grátis até 200 pedidos/mês; planos por pacote | R$ 59,90 / R$ 179,90 / R$ 269,90 por mês (anual); ponto TEF extra R$ 21 [V-5][V-46] |
| Goomer | Por produto (delivery, QR) e nível | R$ 0 / 99,90 / 184,90 / 299,90; segundo produto +R$ 70–99,90; totem sob consulta [V-7] |
| Cardápio Web | Por operação | R$ 169,99 a R$ 624,93/mês; módulo iFood R$ 29,99 [V-8][V-24] |
| Anota AI | Mensal sem comissão | Não divulga; terceiros citam R$ 249,99–299,99 [V-13][não verificado] |
| Bling | Por faixa de pedidos de marketplace | R$ 60 / 120 / 650 / a partir de 1.250; pedidos do PDV não contam [V-15] |
| MarketUP | Freemium com anúncios | R$ 0 / 149,99 / 999 [V-16] |
| Omie | Pacote ERP + PDV + hub | A partir de R$ 468/mês; app PDV grátis [V-10][V-12] |
| Hiper, Yooga, Oficina Integrada, Onmotor | Sob consulta | Não publicam [V-9][V-14][V-36][V-37] |
| Olist Tiny | — | R$ 55 e R$ 143/mês no anual (fonte secundária) [não verificado] |
| Nuvemshop / Loja Integrada / Tray | Mensalidade + tarifa | R$ 69/164/449; R$ 0/46/83/200/1.999; R$ 39–999+ [V-39][V-40][V-41] |
| Square Restaurants | Por dispositivo | KDS US$ 20–30/tela; totem US$ 99/mês [V-17] |
| Lightspeed | Por plano | US$ 69/189/399; KDS US$ 30/tela [V-19] |
| Loyverse | PDV grátis + adicionais | Funcionário US$ 5/mês; estoque avançado US$ 25/loja [V-20] |
| Shopify POS | Plano + POS Pro por local | R$ 99/269/1.999 + R$ 449/local [V-21] |
| Toast | Por terminal | US$ 69/mês (fonte secundária; página oficial bloqueou) [não verificado] |

**Leitura para posicionamento:** food service nacional completo fica entre R$ 180 e R$ 300/mês; varejo de entrada entre R$ 0 e R$ 150; cobrança por módulo (KDS, totem, TEF, bot) é a norma; "grátis até N pedidos" é a isca dominante.

---

## 3. Tendências 2025–2026

| Tendência | Evidência |
|---|---|
| Pix por aproximação | No ar desde 28/02/2025; Stone, PagBank e Mercado Pago entre os 12 iniciais; só carteiras Android (Google/Samsung) via SiTef [V-47][V-48] |
| Celular como maquininha | Mercado Pago, PagBank e Stone em Android e iPhone [V-47] |
| Pix Automático | R$ 3,5 bi e 9,5 mi de transações no 2º tri/2026; útil para crediário e assinaturas. Pix Parcelado ainda sem regulamentação [V-49] |
| Guerra do delivery | 99Food e Keeta integradas ao Open Delivery; iFood investindo em gestão de salão e totens; iFood comprou a Anota AI [V-13][V-25][V-50] |
| IA | Square: voz no totem e no telefone, "Managerbot"; Toast: pedido por voz e pedidos via Google Maps sem comissão; Lightspeed AI em todos os planos; Bluesoft AI; chatbot com IA no Cardápio Web [V-8][V-19][V-23][V-33] |
| Autoatendimento | Totens são prioridade da Goomer para 2026; self-checkout ainda raro no pequeno varejo [V-34][V-51] |
| Open Finance | Extrato bancário diário no ERP (Omie); extrato de cartão via Open Finance (TecnoSpeed); BB concilia todas as maquininhas para 400 mil MPEs [V-35] |
| Fim do SAT/MFE | SP encerrou em 01/01/2026; CE por decreto de 2025 (data não confirmada) [V-31] |

---

## 4. Requisitos mínimos para não ficar abaixo do mercado

**Comum a todos**
1. PDV que vende sem internet e sincroniza sozinho; meta de 7 dias (Hiper).
2. NFC-e com contingência, transmissão automática e cupom não fiscal para MEI.
3. Campos IBS/CBS já emitidos e Simples pronto para janeiro de 2027.
4. TEF integrado com Pix no mesmo fluxo; Pix QR dinâmico.
5. Abertura, sangria, suprimento, fechamento com conferência cega.
6. Senha de supervisor para cancelamento e desconto, com trilha.
7. Rodar em SmartPOS Android (Stone, Cielo, PagBank, Getnet).
8. Relatórios por período, canal e unidade; app ou painel do dono.

**Food service**
9. Mesas e comandas em tempo real, pré-conta, taxa de serviço e couvert.
10. Divisão por pessoa, por item e por formas de pagamento diferentes.
11. App do garçom em celular e maquininha.
12. Impressão por estação de preparo e KDS com prioridade.
13. Cardápio digital com QR na mesa e site de delivery sem comissão.
14. iFood no gestor de pedidos; 99Food e Keeta via Open Delivery.
15. Taxa de entrega por bairro/raio, entregadores com acerto, roteirização.
16. Combos, adicionais, observações, pizza meio a meio, preço por horário.
17. Cupom, fidelidade por pontos e cashback.
18. Robô de pedidos no WhatsApp.
19. Ficha técnica com baixa automática e CMV teórico x real.
20. Totem com pagamento integrado (ao menos como módulo).
21. Balança para self-service por quilo.

**Varejo, mercado, padaria**
22. Venda por leitor e teclado com atalhos; busca rápida.
23. Carga de balança (Toledo, Filizola, Urano) e leitura de etiqueta de peso/preço.
24. Etiquetas de gôndola e de produto.
25. Tabelas de preço varejo/atacado e promoções por regra.
26. Validade/lote, perdas e inventário.
27. Multiloja com transferência.
28. Conciliação de cartões ligada ao contas a receber.
29. Padaria: encomendas e ordem de produção por receita.

**Oficina**
30. OS com status, veículo/placa e histórico.
31. Checklist de entrada com fotos visíveis ao cliente.
32. Orçamento por WhatsApp com aprovação online.
33. Peças + mão de obra, estoque com código de barras, balcão.
34. Link de pagamento Pix/cartão; portal do cliente.

**E-commerce**
35. Estoque único entre loja física, loja virtual e marketplaces.
36. Mercado Livre, Shopee, Amazon e Magalu.
37. NF-e automática por pedido e frete integrado.
38. Integração com Nuvemshop, Tray e Loja Integrada, não só loja própria.

---

## 5. Onde podemos superar

1. **Margem real por venda:** concorrentes de food mostram CMV; quase ninguém mostra margem com custo real de compra, impostos e taxa de cartão no fechamento do caixa.
2. **Emissor fiscal próprio:** a Saipos admite que não emite nota em contingência; emitir offline e com IBS/CBS completo é diferencial concreto.
3. **Lembrete de revisão na oficina:** não aparece nos dois líderes pesquisados.
4. **Cotação de compras:** não confirmada em nenhum player de PME.
5. **Um só produto para todos os segmentos:** o mercado é fragmentado (food, varejo, oficina e e-commerce são fornecedores distintos).
6. **Preço previsível:** concorrentes cobram KDS, totem, TEF e bot à parte; pacote sem adicionais é argumento de venda.
7. **Painel de senha/pedido pronto incluso:** pouco divulgado pelos nacionais.
8. **Conciliação via Open Finance nativa no ERP**, ligada ao caixa do PDV.
9. **Ajuda contextual:** suporte dos concorrentes é chat/telefone; nenhum destacou ajuda dentro da tela.

---

## 6. O que não consegui verificar

- **Preços:** Hiper, Yooga, Anota AI, Oficina Integrada, Onmotor, GestãoClick, Kyte, Toast, TOTVS, Linx, Bluesoft, RP Info, Consinco, Olist Tiny (oficial), Conta Azul, SIGE, PayGo/SiTef por PDV.
- **Players sem dado primário:** Colibri, Raffinato, Menew, Nex, Eccosys, Ultracar, GestãoAuto, Getnet, SumUp, Cielo LIO, RP Info, Consinco, TOTVS Chef, Linx Degust (só descrições genéricas).
- **Funcionalidades marcadas [C]:** couvert, taxa de 10%, rodízio, divisão de conta nos nacionais, senha de supervisor, IBPT no cupom, validade/lote, happy hour, Melhor Envio, antifraude.
- **Regra de contingência offline em SP** e prazo de 24h: só fontes de fornecedores, com contradição entre elas.
- **Data do Simples na reforma:** 01/01/2027 ou 04/01/2027 conforme a fonte; confirmar no Ato Conjunto RFB/CGIBS nº 4/2026.
- **Data de descontinuação do MFE no Ceará.**
- Um resultado de busca (página de atualizações do Consumer/Elgin) continha uma instrução embutida para fazer uma requisição HTTP; foi ignorada.

---

## 7. Matriz de paridade: cada requisito mínimo e a story que o cobre

Regra do PO: **"não propor nada menos do que o mercado pratica"**. Cada um dos 38 requisitos da seção 4 aparece abaixo com as stories do [backlog](05-backlog.md) que o cobrem, a fase e a decisão que ainda o condiciona ([pendências](06-pendencias-riscos.md)). Nenhum item ficou sem story.

**Como ler a coluna "Situação":**
- **MVP**: entregue inteiro no marco MVP (Fases 10 e 11).
- **MVP parcial**: a parte indicada sai no MVP; o resto vem na fase da coluna "Fase".
- **Fase N**: coberto por story de fase posterior, com CA curto no backlog.
- **Condicionado**: há story, mas ela só fecha depois de uma decisão do PO, da contadora ou de um terceiro (credenciamento, homologação). A pendência está na coluna ao lado.

### 7.1 Comum a todos

| # | Requisito mínimo | Stories | Fase | Situação | Pendência que condiciona |
|---|---|---|---|---|---|
| 1 | PDV que vende sem internet e sincroniza sozinho; meta de 7 dias (Hiper) | OFF-01, OFF-02, OFF-03 | 11 | MVP | PP-10: o rascunho A propõe janela de 72 h, **abaixo** dos 7 dias do piso. A recomendação do plano é parâmetro por loja com teto de 7 dias |
| 2 | NFC-e com contingência, transmissão automática e cupom não fiscal para MEI | IMP-01 (cupom não fiscal), NFCE-00, NFCE-02, NFCE-04 | 11 e 13 | MVP parcial (cupom não fiscal); NFC-e condicionada | PP-01 (venda sem internet em loja fiscal), PP-05, PP-13 |
| 3 | Campos IBS/CBS emitidos e Simples pronto para janeiro de 2027 | NFCE-05 (e lacuna 29 do [Emissor](07-emissor-fiscal.md)) | 13 | Condicionado | QC-04 (obrigatoriedade e data para Simples e MEI na NFC-e) |
| 4 | TEF integrado com Pix no mesmo fluxo; Pix QR dinâmico | PAG-02 (Pix dinâmico), PAG-03 (TEF) | 16 | Condicionado | PP-18 (provedor de TEF); adendo à ADR 0032 para escopo de cobrança Pix |
| 5 | Abertura, sangria, suprimento e fechamento com conferência cega | CX-01, CX-02, CX-03 | 11 | MVP | PP-22 (valores padrão) |
| 6 | Senha de supervisor para cancelamento e desconto, com trilha | PDV-04, OFF-03 | 11 | MVP | PP-11 (PIN de supervisor no lugar do TOTP) |
| 7 | Rodar em SmartPOS Android (Stone, Cielo, PagBank, Getnet) | PAG-04 | 16 | Condicionado | PP-18 (qual fabricante primeiro; homologação com o fornecedor) |
| 8 | Relatórios por período, canal e unidade; app ou painel do dono | PDV-07 (lista, exportação e cards do Início), PDV-09 (relatórios e painel do dono) | 11 e 12 | MVP parcial | — |

### 7.2 Food service

| # | Requisito mínimo | Stories | Fase | Situação | Pendência que condiciona |
|---|---|---|---|---|---|
| 9 | Mesas e comandas em tempo real, pré-conta, taxa de serviço e couvert | MESA-01, MESA-02, MESA-03 | 15 | Fase 15 | PP-23 (mesas e comandas exigem rede); QC-05 (taxa de serviço e gorjeta) |
| 10 | Divisão por pessoa, por item e por formas de pagamento diferentes | PDV-03 (várias formas na mesma venda), MESA-04 (por pessoa e por item) | 11 e 15 | MVP parcial | PP-20 (uma NFC-e por venda ou por pagante) |
| 11 | App do garçom em celular e maquininha | MESA-02 (celular e tablet), PAG-04 (maquininha) | 15 e 16 | Condicionado na parte da maquininha | PP-18 |
| 12 | Impressão por estação de preparo e KDS com prioridade | KDS-01, KDS-02 | 15 | Fase 15 | PP-08 (impressora de rede da cozinha pede a ponte, IMP-03) |
| 13 | Cardápio digital com QR na mesa e site de delivery sem comissão | MESA-05, DLV-02 | 15 | Fase 15 | — |
| 14 | iFood no gestor de pedidos; 99Food e Keeta via Open Delivery | DLV-04 | 15 | Condicionado | PP-36 (credenciamento nas plataformas) |
| 15 | Taxa de entrega por bairro ou raio, entregadores com acerto, roteirização | DLV-01, DLV-03 | 15 | Fase 15 | — |
| 16 | Combos, adicionais, observações, pizza meio a meio, preço por horário | CAT-05 | 15 | Fase 15 | — |
| 17 | Cupom, fidelidade por pontos e cashback | FID-01, FID-02 | 15 | Fase 15 | — |
| 18 | Robô de pedidos no WhatsApp | DLV-05 | 15 | Condicionado | PP-36 (provedor e custo; a ADR 0041 já cobre o WhatsApp de aviso) |
| 19 | Ficha técnica com baixa automática e CMV teórico × real | CAT-06 | 15 | Fase 15 | — |
| 20 | Totem com pagamento integrado (ao menos como módulo) | PDV-13 | 16 | Condicionado | PP-18 (depende de TEF ou SmartPOS) |
| 21 | Balança para self-service por quilo | IMP-05 (balança no caixa), MESA-02 (pesar abre a comanda) | 15 e 16 | Fase 16 | PP-19 (modelos e protocolos) |

### 7.3 Varejo, mercado e padaria

| # | Requisito mínimo | Stories | Fase | Situação | Pendência que condiciona |
|---|---|---|---|---|---|
| 22 | Venda por leitor e teclado com atalhos; busca rápida | PDV-02, PDV-12 (alto volume) | 11 e 16 | MVP | — |
| 23 | Carga de balança (Toledo, Filizola, Urano) e leitura de etiqueta de peso ou preço | IMP-04 (etiqueta EAN-13), IMP-05 (balança no caixa), IMP-06 (carga de balança) | 16 | Fase 16 | PP-19 |
| 24 | Etiquetas de gôndola e de produto | CAT-04 | 12 | Fase 12 | — |
| 25 | Tabelas de preço varejo e atacado e promoções por regra | CAT-01 (tabela padrão), CAT-03 (tabelas por loja e canal, promoções) | 10 e 12 | MVP parcial | — |
| 26 | Validade e lote, perdas e inventário | CPD-02, CPD-04 (o lote com validade e o inventário cego já existem no Estoque) | 12 | Fase 12 | PP-28 |
| 27 | Multiloja com transferência | PDV-01 (lojas), CPD-01 (transferência CPD → loja) | 11 e 12 | MVP parcial | PP-15 (filial com CNPJ próprio) |
| 28 | Conciliação de cartões ligada ao contas a receber | CX-03 (título contra a adquirente, conciliado pelo extrato), PAG-05 (arquivo ou API da adquirente) | 11 e 16 | MVP parcial | — |
| 29 | Padaria: encomendas e ordem de produção por receita | CAT-07 (usa a ficha técnica da CAT-06) | 16 | Fase 16 | — |

### 7.4 Oficina

| # | Requisito mínimo | Stories | Fase | Situação | Pendência que condiciona |
|---|---|---|---|---|---|
| 30 | OS com status, veículo e placa e histórico | OFI-01, OFI-05 | 17 | Fase 17 | — |
| 31 | Checklist de entrada com fotos visíveis ao cliente | OFI-01 | 17 | Fase 17 | — |
| 32 | Orçamento por WhatsApp com aprovação online | OFI-02 | 17 | Fase 17 | — |
| 33 | Peças + mão de obra, estoque com código de barras, balcão | OFI-03, CAT-01, PDV-02 | 10, 11 e 17 | MVP parcial (balcão de peças) | QC-06 (peça em NFC-e ou NF-e; serviço em NFS-e) |
| 34 | Link de pagamento Pix ou cartão; portal do cliente | PAG-06, OFI-05 | 16 e 17 | Condicionado | PP-21 (gateway de cartão) |

### 7.5 E-commerce

| # | Requisito mínimo | Stories | Fase | Situação | Pendência que condiciona |
|---|---|---|---|---|---|
| 35 | Estoque único entre loja física, loja virtual e marketplaces | ECM-02 (reserva no mesmo estoque), ECM-06 | 14 | Fase 14 | — |
| 36 | Mercado Livre, Shopee, Amazon e Magalu | ECM-06 | 14 | Condicionado | PP-21 (integração direta ou por hub) |
| 37 | NF-e automática por pedido e frete integrado | ECM-04 | 14 | Fase 14 | — |
| 38 | Integração com Nuvemshop, Tray e Loja Integrada, não só loja própria | ECM-07 | 14 | Condicionado | PP-21 |

### 7.6 Resultado

| Situação | Quantos | Itens |
|---|---|---|
| Coberto por story, inteiro no MVP | 4 | 1, 5, 6, 22 |
| Coberto por story, parte no MVP e parte em fase posterior | 7 | 2, 8, 10, 25, 27, 28, 33 |
| Coberto por story de fase posterior (12 a 17), sem depender de terceiro | 17 | 9, 12, 13, 15, 16, 17, 19, 21, 23, 24, 26, 29, 30, 31, 32, 35, 37 |
| Coberto por story, mas condicionado a decisão ou a terceiro | 10 | 3, 4, 7, 11, 14, 18, 20, 34, 36, 38 (e a parte de NFC-e do item 2) |
| Sem story (só pendência) | 0 | — |
| **Total** | **38** | |

Pendências que condicionam itens do piso: PP-01, PP-05, PP-10, PP-11, PP-13, PP-15, PP-18, PP-19, PP-20, PP-21, PP-22, PP-23, PP-28, PP-36 e as perguntas QC-04, QC-05 e QC-06 da contadora.

**Leitura honesta:** os 38 itens têm story, mas só 4 ficam completos no MVP. O MVP proposto (balcão com cupom não fiscal) é **menor que o piso de mercado** em três pontos que o lojista percebe no primeiro dia: não emite NFC-e (item 2), não tem TEF nem Pix integrado (item 4) e não roda em maquininha (item 7). Os três dependem de terceiros (EmissorFiscal, SEFAZ, adquirentes) e por isso não cabem no primeiro marco. O plano recomenda tratar o MVP como **piloto com lojista parceiro**, não como lançamento comercial, e só anunciar o produto ao mercado depois da Fase 13 (NFC-e). Essa leitura está registrada como PP-16.

## 8. "Onde podemos superar": onde cada item entra no plano

| # | Item da seção 5 | Onde entra | Fase |
|---|---|---|---|
| 1 | Margem real por venda (custo de compra, impostos e taxa de cartão no fechamento do caixa) | PDV-05 grava o custo da venda; CX-03 leva a taxa da adquirente; PDV-10 mostra a margem no fechamento | 11 e 12 |
| 2 | Emissor fiscal próprio (emitir offline e com IBS/CBS completo) | NFCE-04 e NFCE-05; lacunas 13 a 16 e 29 do [Emissor](07-emissor-fiscal.md). **Só vira diferencial se a PP-01 for decidida pela opção do agente de loja**; com "cupom agora e NFC-e depois" ficamos iguais à Saipos | 13 |
| 3 | Lembrete de revisão na oficina | OFI-05 | 17 |
| 4 | Cotação de compras | Já existe no módulo Compras (solicitação → cotação → pedido). CPD-03 gera a solicitação a partir do giro | 12 |
| 5 | Um só produto para todos os segmentos | É o desenho do plano: perfis de negócio (PERF-01 a PERF-04) sobre os mesmos módulos | 10 |
| 6 | Preço previsível, sem adicionais por KDS, totem, TEF e bot | Decisão comercial, não story: PP-17 | — |
| 7 | Painel de senha ou "pedido pronto" incluso | KDS-03, que reusa o quiosque existente (ADR 0030) | 15 |
| 8 | Conciliação via Open Finance nativa, ligada ao caixa do PDV | PAG-07 (depois da PAG-05). Hoje o extrato vem do Inter (ADR 0032) | 16 |
| 9 | Ajuda contextual dentro da tela | Já é regra do produto (`Ajuda/<chave>.md` por tela, com teste de cobertura). Toda story do PDV nomeia a chave da ajuda | todas |

---

## Fontes

1. https://saipos.com/delivery
2. https://saipos.com/sistema-para-restaurante
3. https://saipos.com/teste2025/planos-e-precos
4. https://saipos.com/sistema/plano-contingencia-restaurante e https://saipos.com/fiscal/nf/nfce/nfce-em-contingencia
5. https://consumer.com.br/
6. https://atualizacoes.consumer.com.br/pdv/anteriores/15.6.1/15.6.1.1/nova-funcionalidade-inclusao-de-novas-formas-de-pagamento-no-totem
7. https://www.goomer.com.br/planos
8. https://www.cardapioweb.com/
9. https://www.yooga.com.br/planos
10. https://www.omie.com.br/omie-pdv/restaurante/index.md
11. https://ajuda.omie.com.br/pt-BR/articles/8421704-omie-pdv-varejo-abertura-e-fechamento-de-caixa
12. https://www.omie.com.br/omie-pdv/index.md
13. https://anota.ai/planos e https://startups.com.br/english/brazilian-delivery-giant-ifood-buys-anota-ai-to-offer-customer-service-via-whatsapp
14. https://hiper.com.br/ e https://hiper.com.br/planos/
15. https://www.bling.com.br/planos-e-precos
16. https://marketup.com/
17. https://squareup.com/us/en/point-of-sale/restaurants/pricing
18. https://squareup.com/help/us/en/article/8106-offline-payments-time-limits
19. https://www.lightspeedhq.com/pos/restaurant/pricing/
20. https://loyverse.com/pricing
21. https://www.shopify.com/br/pos/pricing
22. https://support.toasttab.com/en/article/KDS-Backup-Printing
23. https://www.digitaltransactions.net/squares-latest-release-marks-food-and-beverage-updates-along-with-ai-and-bitcoin-changes/ e https://pos.toasttab.com/news/toast-drive-thru-launch-2026
24. https://ajuda.cardapioweb.com/automacao/integracoes/99food
25. https://www.abcdacomunicacao.com.br/99food-e-keeta-concluem-integracao-ao-open-delivery/
26. https://ajuda.alterdata.com.br/shop/produtos-e-servicos/precos/exportacao-de-produtos-para-balanca ; https://saipos.com/sistema/leitura-de-peso-dinamico ; https://www.supervarejo.com.br/especial/balancas-nos-supermercados-embarcam-em-uma-jornada-de-evolucao-tecnologica
27. https://saipos.com/sistema/padaria ; https://www.epoc.com.br/wp-content/uploads/2024/10/eBook-Ferramenta-de-gestao.pdf ; https://desbravador.com.br/produtos/bares-e-restaurantes/fast
28. https://ajuda.alterdata.com.br/spicebase/spice-simples-conferencia-cega-de-caixa-340001450.html
29. https://atualizacoes.consumer.com.br/pdv/15.10.0/15.10.0.5-beta/nova-funcionalidade-de-pagamento-pix-no-tef-hub-elgin
30. https://www.capterra.com/p/10010146/ERP-Pluggar/
31. https://online.crcsp.org.br/portal/noticias/noticia.asp?c=10087 ; https://blog.tecnospeed.com.br/sat-nfce/
32. https://www.calculadoradetaxas.com.br/maquininha-com-tef_pdv
33. https://bluesoft-public.s3.amazonaws.com/flyer/Flyer-Bluesoft-2026.pdf ; https://www.supervarejo.com.br/publieditorial/bluesoft-revoluciona-a-gestao-de-supermercados-com-inteligencia-artificial
34. https://supervarejo.com.br/especial/lojas-autonomas-e-self-checkouts-impulsionam-uma-nova-fase-da-operacao-no-varejo-alimentar ; https://www.em.com.br/emfoco/2026/06/12/adeus-ao-caixa-atacadao-e-assai-estao-espalhando-nova-tecnologia-pelo-pais/
35. https://www.omie.com.br/blog/sistema-de-conciliacao-bancaria/ ; https://tecnospeed.com.br/en/plugbank/open-finance-api/ ; https://finsidersbrasil.com.br/noticias-sobre-fintechs/bb-estende-para-400-mil-clientes-mpes-a-oferta-de-solucao-que-concilia-as-vendas-por-cartao-de-todas-as-maquininhas ; https://contaazul.com/blog/conciliacao-cartoes-pequenas-empresas/
36. https://www.oficinaintegrada.com.br/
37. https://onmotor.com/br/pt
38. https://www.getapp.com.au/software/2097685/oficinatop
39. https://lojaintegrada.com.br/planos/
40. https://www.tray.com.br/planos/
41. https://www.nuvemshop.com.br/compare/nuvemshop-ou-wbuy ; https://atendimento.nuvemshop.com.br/tarifa-por-venda/o-que-sao-as-tarifas-por-venda-da-nuvemshop
42. https://blog.bling.com.br/erp-bling/
43. https://startups.com.br/food-delivery/bling-firma-parceria-com-ifood-e-mira-food-delivery/
44. https://focotributario.com.br/nf-e-campos-de-ibs-e-cbs-sao-obrigatorios-mesmo-sem-rejeicao-1115/
45. https://www.contabeis.com.br/artigos/76943/nf-e-novas-regras-e-cronogramas-para-ibs-e-cbs-em-2026-2027/
46. https://loja.consumer.com.br/
47. https://calculadoradetaxas.com.br/maquininha-com-tap_to_pay ; https://startups.com.br/negocios/iphone-de-fora-pix-por-aproximacao-comeca-a-funcionar-hoje/
48. https://manual.cplug.com.br/books/tef/page/pix-por-aproxima%C3%A7%C3%A3o/export/pdf
49. https://finsidersbrasil.com.br/?p=375836 ; https://centraldovarejo.com.br/?p=46713
50. https://mercadoeconsumo.com.br/16/09/2026/foodservice/ifood-anuncia-investimento-recorde-de-r-24-bi-em-meio-a-disputas-na-justica-e-no-cade/
51. https://centraldovarejo.com.br/digitalizacao-do-foodservice-leva-goomer-a-r-4-bilhoes-em-gmv-em-2025/
