# Revisão pré-Fase 3: relatório consolidado

- **Data:** 21/10/2026.
- **Objeto:** as 23 stories da Fase 3 (CAD-04c, ENG-01/02, EST-01a a EST-06, CMP-01 a CMP-04, PRD-01 a PRD-06b), escritas pelo SM e validadas pelo PO no branch `docs/fase3-stories` (commit `e1aec38`).
- **Revisores:** quatro, todos somente leitura, conferindo as stories contra o código das Fases 0 a 2:
  - AIOX architect;
  - AIOX po + pm;
  - design-squad, com `audit-design`;
  - cybersecurity, com `audit-app-security` (threat model STRIDE).
- **Como usar:** a seção 1 lista o que depende do PO. As seções 2 e 3 entram nas stories e nas ADRs 0023 a 0026 depois da decisão, sem nova consulta. Nada deste relatório foi aplicado ainda.

## 0. Vereditos

| Story | Arquitetura | Produto | Design | Segurança | Consolidado |
|---|---|---|---|---|---|
| CAD-04c Funcionários: complemento | GO c/ ajustes | Replanejar | GO | GO c/ CAs | **Quebrar em 04c1 (Fase 3) e 04c2 (Fase 6), D9** |
| ENG-01 Templates de roteiro | GO c/ ajustes | GO | GO c/ ajuste | GO | **GO com ajustes (código da etapa, StageIndicator)** |
| ENG-02 BOM da OS | GO c/ ajustes | GO c/ ajuste | GO | GO | **GO com ajustes** |
| EST-01a Saldos, movimentos, custo médio | GO c/ ajustes, **ADR 0023** | GO c/ ajuste | GO c/ ajuste | **NO-GO** | **Ajustar antes (posição agregada, trava por item, segregação das manuais)** |
| EST-01b Ajuste com aprovação | GO c/ ajustes | GO, mover | GO c/ ajuste | GO | **GO com ajustes; vai para perto da EST-05** |
| EST-02 Reservas e disponível | **NO-GO** | GO c/ ajuste | GO | GO | **Ajustar antes (reserva na posição agregada, reconciliação pela fonte)** |
| EST-03 Requisição para OS | GO c/ ajustes | GO c/ ajuste | GO | GO c/ CAs | **GO com ajustes (custo pela ADR 0025, recomposição)** |
| EST-04 Lote e série | GO | GO | GO | GO | **GO** |
| EST-05 Inventário | GO c/ ajustes | GO | GO c/ ajuste | GO c/ CA | **GO com ajustes (ajuste em Delta)** |
| EST-06 Capacidade de produzir | GO | GO, pode ir à Fase 4 | GO | GO | **GO (desejável)** |
| CMP-01 Solicitação de compra | GO c/ ajustes | GO | GO c/ ajuste | GO c/ CA | **GO com ajustes** |
| CMP-02 Cotação | GO | GO, depois da CMP-04 | GO c/ ajuste | GO c/ CA | **GO com ajustes** |
| CMP-03a Pedido de compra | **NO-GO** | GO cond. | GO c/ ajuste | GO cond. | **Ajustar antes (ADRs 0024 e 0025)** |
| CMP-03b PDF e envio do pedido | GO c/ ajustes | GO | GO | GO c/ CAs | **GO com ajustes** |
| CMP-04 Recebimento manual | **NO-GO** | GO | GO c/ ajuste | **NO-GO** | **Ajustar antes (títulos do recebimento, `purchasing.receive`, teto do aprovado)** |
| PRD-01 Geração de OS | GO c/ ajustes | GO | GO c/ ajuste | GO c/ CAs | **GO com ajustes (implantação não gera OS)** |
| PRD-02 Kanban | GO c/ ajustes | GO c/ ajuste | GO c/ ajuste | GO c/ CA | **GO com ajustes** |
| PRD-03a PWA: fila e apontamento | GO c/ ajustes | GO c/ ajuste | **Ajustar** | GO c/ CAs | **Ajustar antes (kit chão de fábrica, confirmação ao finalizar)** |
| PRD-03b PWA: PIN e sem rede | **NO-GO**, ADR 0026 | Replanejar | **Ajustar** | **NO-GO** | **Replanejar: PIN e aparelho com ADR 0026; sem rede depois do piloto (D5)** |
| PRD-04 Custo de mão de obra | GO c/ ajustes | GO c/ ajuste | GO | GO | **GO com ajustes (cria `project_costs`, recompõe o passado)** |
| PRD-05 Qualidade e NC | GO | GO c/ ajuste | **Ajustar** | GO c/ CA | **Ajustar antes (qualidade × PIN, D6)** |
| PRD-06a Conclusão e expedição | GO c/ ajustes | GO, quebra opcional | GO | GO | **Quebrar em 06a1 e 06a2 (D1)** |
| PRD-06b Romaneio | GO c/ ajustes | GO c/ ajuste | GO | GO c/ CAs | **GO com ajustes (romaneio baixa, D4)** |

## 1. Decisões do PO

| # | Decisão | Recomendação consolidada | Origem |
|---|---|---|---|
| D1 | **Ordem da fase: "fábrica visível" primeiro.** Pela ordem atual, o PO só vê a fábrica no sistema na 14ª story, e o estoque da Konebras veio zerado do Dolibarr (depende de contagem física). | Começar por OS, Kanban, apontamento, custo de mão de obra, qualidade e conclusão da OS que libera a parcela "fabricação concluída" (PRD-06a1, sem entrada no estoque). Estoque e compras depois. Ordem completa na seção 4. | Produto C3; Arquitetura sem objeção |
| D2 | **Custo realizado do projeto (muda a COM-05).** Hoje o Projeto 360 soma como realizado **todo título a pagar não cancelado com projeto, inclusive os previstos**: o título previsto do instalador já conta quando a OS de instalação nasce, e o CP previsto do pedido de compra contaria na aprovação e de novo na baixa do material. | Realizado = título confirmado de serviço (instalador, frete, outros) + baixa de material para a OS + horas apontadas. Título previsto e título de compra de item estocável aparecem numa coluna nova **"Comprometido"**. Projetado = realizado + max(orçado − realizado, comprometido em aberto). ADR 0025, com teste de regressão sobre os dados do tenant `konebras`. | Arquitetura C3, Produto C1 |
| D3 | **Recompor o passado.** Evento publicado antes de existir o consumidor é dado como processado e se perde (ex.: saída avulsa com projeto antes da EST-03; apontamentos antes da PRD-04). A homologação tem dado real. | Padrão comum da Fase 3: a story que entrega um consumidor recompõe o que foi publicado antes dela (marca "Calculado na implantação"). | Produto C2 |
| D4 | **Quem baixa o estoque na expedição.** O `04-fluxos` diz NF-e autorizada (Fase 5); a PRD-06b baixa no romaneio. Além disso, a etapa "Expedição" do roteiro (P-04) acontece antes do romaneio. | O romaneio confirmado baixa (chamada síncrona); na Fase 5 a NF-e referencia o romaneio e nunca baixa de novo. A etapa Expedição passa a ser "preparar e embalar": ao finalizá-la, o próximo passo é "Concluir OS" e depois "Novo romaneio". Atualizar o `04-fluxos` §2 e a P-04. | Todos |
| D5 | **Tablet do chão de fábrica (PIN, aparelho e sem rede).** | **Agora:** PIN de 6 dígitos (PBKDF2), sessão de 12 h absoluta e não renovável, saída depois de 15 min sem toque (com aviso de 60 s), aparelho registrado por 180 dias e revogado depois de 30 dias sem uso; o login completo feito no tablet (registrar aparelho, criar PIN) não deixa sessão persistente. **Depois do piloto, se o Wi-Fi falhar:** funcionamento sem rede. **Operador sem e-mail:** usar e-mail da empresa por operador (apelido no domínio `konebras.com.br`, sem mudar código); "usuário só de chão de fábrica, sem e-mail" só com ADR, se os apelidos não servirem. ADR 0026 antes da PRD-03b. | Produto A1, Segurança C1/A1/A2/M4, Arquitetura A4 |
| D6 | **Qualidade × PIN.** A PRD-03b proíbe PIN para quem tem `*.approve`, mas a fila de qualidade exige `production.approve`: o inspetor nunca veria a fila no tablet. | Ação nova `production.approve_quality` (ADR 0003), usável por PIN, que só aprova ou reprova a qualidade; a conclusão da OS e a liberação da parcela continuam com `production.approve` e login completo. Alternativa: qualidade só com e-mail e senha. | Design C2 |
| D7 | **Segregação no estoque.** Entrada e saída manual sem aprovação funcionam como ajuste disfarçado e mexem no custo médio, que vai para a margem. | Saldo inicial só na janela de implantação **e** sem movimento no item e local; depois, só por ajuste. Entrada ou saída manual acima de **R$ 1.000,00**, ou com custo que desvie mais de **20%** do custo médio, vira documento pendente aprovado por outra pessoa com `inventory.approve`. Estorno pelo autor com `inventory.edit` até R$ 1.000,00 e em até 24 h; acima disso, outra pessoa com `inventory.approve`. Resumo diário das manuais ao Proprietário. | Segurança C2/M1, Arquitetura (pontos 9 e 10) |
| D8 | **Segregação em compras.** `purchasing.create` cobre pedir e receber, e o recebimento pode subir o valor acima do aprovado. | Ação nova `purchasing.receive` (ADR 0003); o Almoxarifado recebe `purchasing.view` + `purchasing.receive`, sem `purchasing.edit`. Quem pediu ou aprovou o pedido não registra o recebimento (exceção do Proprietário único, com marca). Σ confirmado ≤ aprovado + tolerância (parâmetro, padrão 0%); frete e IPI só para baixo no recebimento; aumento volta o pedido à alçada. Cotação sem mínimo, com aviso "1 cotação" na aprovação; acima de **R$ 5.000,00**, a resposta exige o anexo da proposta do fornecedor. | Segurança A3/A4/M8, Produto M4, Design |
| D9 | **CAD-04c: escopo.** Na Fase 3 só o afastamento e a situação do funcionário têm consumidor (o apontamento recusa afastado). | Quebrar: **CAD-04c1** (P, afastamentos e `Status`) na Fase 3, antes da PRD-03a; **CAD-04c2** (dependentes, banco/Pix, vale-transporte, adicionais, documentos) na Fase 6, antes da RH-02. Revê a decisão D6 de 18/10. | Produto A5 |
| D10 | **Kanban.** O protótipo permite arrastar; o `04-fluxos` não. | Sem arraste. Ação "Concluir etapa" no menu do cartão para `production.approve`, para o quadro andar mesmo sem apontamento. Colunas pelo código da etapa (a letra do display de andar, campo novo "Letra" no roteiro). Notificação "OS atrasada" (R-COM-2) em job diário na PRD-02. | Produto M8/A8, Design, Arquitetura M7 |
| D11 | **Fila do operador.** O PO quer todas as etapas prontas visíveis; a segurança quer o servidor recusando etapa fora dos centros do cargo. | Fila com todas as etapas prontas, as do centro do operador primeiro e o filtro "Só meu centro". O servidor recusa etapa que não está pronta, de OS cancelada ou de outro tenant; não restringe por centro. Fica registrado quem apontou. | Produto A6, Design, Segurança M3 (divergência) |
| D12 | **Etapas sem BOM e conclusão.** A BOM nasce na etapa Projeto, mas o `04-fluxos` exige BOM liberada para iniciar a OS; e a qualidade não deve concluir a OS sozinha. | A primeira etapa do roteiro inicia sem BOM; as seguintes exigem BOM liberada (o bloqueio liga junto com a ENG-02). Depois da última etapa, a OS fica "Pronta para concluir" e a conclusão é explícita. Atualizar o `04-fluxos` §1. | Todos |
| D13 | **Permissões novas da Engenharia.** | `engineering.delete` (ação padrão, para excluir roteiro e descartar rascunho de BOM) e `engineering.approve` ao grupo Engenharia por seed patch (liberar BOM), com adendo à ADR 0008. | Arquitetura, Produto (ponto 23) |
| D14 | **Quais itens geram OS.** | `Equipment` gera OS automática; `Product` (componente fabricado) por "Gerar OS" manual; `Service` nunca; contrato de implantação nunca. Uma OS por item do projeto, com a quantidade do item; série por unidade na conclusão. Sem geração retroativa para os projetos da Fase 2: botão "Gerar OS" e geração em lote. | Produto, Arquitetura A6 |
| D15 | **Numerações novas.** | `MOV-AAAA-000001`, `AJ-AAAA-0001`, `REQ-AAAA-0001`, `ROM-AAAA-0001`, `INV-AAAA-0001` e série `{código}-{AAAA}-{seq}` (conferir antes a numeração que a Konebras já usa nos elevadores, campos `elev_*` do Dolibarr, só leitura). Atualizar o `04-fluxos` §4. | Produto B5/M9, Arquitetura |
| D16 | **Desenhos na OS.** | Aceitar dwg/dxf de até 25 MB como anexo da OS (adendo à ADR 0019 §7), com limite de envios e o antivírus configurado para 30 MB. O operador lê pelo PWA por um endpoint que confere a fila dele. | Produto, Arquitetura M10, Segurança M6 |
| D17 | **Correções no código que já está no ar** (achadas na revisão). | (a) Custo realizado da COM-05 conta título previsto: corrigir pela D2. (b) O indicador de etapa pinta de âmbar todas as concluídas, ao contrário do protótipo, e não cabe a 400 px com muitas etapas: corrigir na ENG-01. (c) A chave de idempotência não inclui o usuário no escopo (risco baixo, a chave é aleatória e o banco é do tenant): incluir como reforço. (d) A renovação de sessão não tem prazo absoluto: tratar na ADR 0026. Sugestão: (a) e (b) entram nas stories; (c) num PR pequeno já. | Arquitetura C3, Design C3, Segurança |

### 1.1 Pontos em aberto das stories

A numeração é a do `docs/stories/README.md` (seção da Fase 3). "Proposta" quer dizer aprovar a proposta que está na story. **B** marca o que bloqueia a story.

| # | Story | Ponto | Recomendação consolidada |
|---|---|---|---|
| 1 | CAD-04c | Vale-transporte (B) | Vai para a CAD-04c2 (D9); lá, só "usa / não usa" e o valor na folha |
| 2 | CAD-04c | Tipos de afastamento (B) | Proposta: doença, acidente de trabalho, maternidade, paternidade, militar, outros |
| 3 | ENG-01 | Tipo de produto e vínculo (B) | Texto livre com sugestões, mais `routing_template_items(template_id, item_id único)` ligando o roteiro ao produto |
| 4 | ENG-01 | Absorver o modelo da instalação | Não absorver; compartilhar só os componentes visuais |
| 5 | ENG-01 | Excluir sem `delete` | `engineering.delete` (D13) |
| 6 | ENG-01 | `routing_template_id` no orçamento | Proposta: coluna anulável junto com a PRD-01, sem tela |
| 7 | ENG-01 | Seed `engineering-permissions` | Aprovar |
| 8 | EST-01a | Numeração MOV | D15 |
| 9 | EST-01a | Saldo inicial depois do corte | D7 |
| 10 | EST-01a | Quem estorna | D7 |
| 11 | EST-01a | Custo médio visível | Com `inventory.view`; nunca no PWA |
| 12 | EST-01a | Saída avulsa com projeto | Permitida, com a regra de valor da D7 e a recomposição da D3 |
| 13 | EST-01b | Numeração AJ | D15 |
| 14 | EST-01b | Alçada por valor | Sem faixa: todo ajuste com `inventory.approve`; o tipo fica previsto no motor de alçadas |
| 15 | EST-01b | Saldo abaixo do reservado | Recusar, com a ação "Ver reservas do item" |
| 16 | EST-01b | Autoaprovação do Proprietário | Regra D6 de 20/10 (Proprietário único), num helper comum, com notificação |
| 17 | EST-01b | Seed `inventory-adjust-warehouse` | Aprovar |
| 18 | PRD-01 | Itens que geram OS (B) | D14 |
| 19 | PRD-01 | OS por item ou por unidade (B) | D14 |
| 20 | PRD-01 | OS dos projetos da Fase 2 | D14; bloquear "Gerar OS" em projeto já fabricado |
| 21 | PRD-01 | Cancelar OS iniciada | `production.approve` |
| 22 | PRD-01 | dwg/dxf de 25 MB | D16 |
| 23 | ENG-02 | `engineering.approve` à Engenharia (B) | D13 |
| 24 | ENG-02 | Quantidade por unidade ou por OS (B) | Por unidade do produto, mostrando também a necessidade da OS |
| 25 | ENG-02 | Estado `Superseded` | Aprovar, com índice único parcial da revisão liberada |
| 26 | ENG-02 | BOM sem item de estoque do produto | Permitir com o chip "Sem item de estoque"; a conclusão da OS exige o produto. BOM vazia liberável para OS só de mão de obra |
| 27 | EST-02 | Local da reserva | Local padrão primeiro, depois os demais |
| 28 | EST-02 | Ordem das faltas | Primeiro a OS que originou a compra, depois a BOM liberada há mais tempo |
| 29 | EST-02 | Completar a reserva | Automático, na mesma regra do 28 |
| 30 | CMP-01 | Gatilho do mínimo | Disponível + pedidos em aberto < mínimo; comprar até o mínimo |
| 31 | CMP-01 | Prazo padrão de compra | 15 dias, em `purchasing.parameters` |
| 32 | CMP-01 | Aprovação da SC | Sem aprovação |
| 33 | CMP-01 | Serviço na SC | Fora do MVP |
| 34 | CMP-01 | Seed de Compras | Aprovar, com o Almoxarifado só em `purchasing.view` + `purchasing.receive` (D8) |
| 35 | CMP-02 | Cotação por e-mail | Fora do MVP |
| 36 | CMP-02 | Mínimo de cotações | D8 |
| 37 | CMP-02 | IPI na cotação | IPI % por fornecedor |
| 38 | CMP-03a | Alçada do pedido (B) | `IApprovalRouting` no Finance, parametrizado por tipo de documento (ADR 0024) |
| 39 | CMP-03a | Conta e natureza do título | Padrão 2.1 Materiais / natureza Material (frete 2.4), a confirmar com a contadora (P-17) |
| 40 | CMP-03a | Um título por parcela (B) | `installment` no título e índice `(origin, origin_id, installment)` (ADR 0024) |
| 41 | CMP-03a | Contar duas vezes (B) | D2 |
| 42 | CMP-03a | Fracionamento | Valor de referência por fornecedor em 30 dias |
| 43 | CMP-03a | Reprovação | Volta a rascunho, histórico imutável, nova resolução de alçada no reenvio |
| 44 | CMP-03a | "Proprietário aprova tudo" | Aprovar, com regra padrão própria do tipo `PurchaseOrder` |
| 45 | CMP-03b | Limite diário | 50 por dia somado ao do orçamento, mais limite por usuário, contado por `shared.email_logs` de forma atômica |
| 46 | CMP-03b | Endereço de entrega | Endereço da empresa |
| 47 | CMP-04 | IPI recuperável | Não recuperável até a P-01 |
| 48 | CMP-04 | Tolerância de quantidade | Parâmetro, padrão 0% (D8) |
| 49 | CMP-04 | Preço diferente do pedido | Preço do pedido; muda só com `purchasing.approve` e motivo, nunca por quem recebe; acima da tolerância volta à alçada |
| 50 | CMP-04 | CP no recebimento parcial | Cada recebimento gera os próprios títulos e replaneja o saldo previsto do pedido (ADR 0024) |
| 51 | CMP-04 | Quem estorna o recebimento | `purchasing.approve` |
| 52 | CMP-04 | Quem registra o recebimento | `purchasing.receive` (D8) |
| 53 | CMP-04 | Frete de outro fornecedor | Fora: título manual com projeto e natureza Frete |
| 54 | EST-03 | Numeração REQ | D15 |
| 55 | EST-03 | Consumo acima da BOM | Até o disponível, com motivo; acima de R$ 1.000,00, aprovação de outra pessoa |
| 56 | EST-03 | Sugestão de lote | Validade mais próxima, depois o mais antigo, com a tag "Sugerido" |
| 57 | EST-03 | Pedido do operador | O operador pede; o almoxarife ou o supervisor confirma e baixa |
| 58 | EST-03 | Custo da devolução | Custo da baixa original |
| 59 | EST-03 | Contar duas vezes (B) | D2 |
| 60 | PRD-02 | Arrastar muda etapa | D10 |
| 61 | PRD-02 | Colunas com roteiros diferentes | D10 (código da etapa) e filtro por roteiro |
| 62 | PRD-02 | "Em risco" | Prometido em até 7 dias e a OS fora da última etapa, numa função só, com `<Dica>` |
| 63 | PRD-03a | Fila do operador (B) | D11 |
| 64 | PRD-03a | Etapa Projeto sem BOM (B) | D12 |
| 65 | PRD-03a | Vários operadores na mesma etapa | Permitido; "Sair da etapa (só eu)" e "Concluir etapa para todos" |
| 66 | PRD-03a | QR code da OS | Fora; "Buscar OS pelo número" na fila |
| 67 | PRD-03a | Funcionário do usuário | `EmployeeOfUserAsync` no `Identity.Contracts` |
| 68 | PRD-03b | PIN, sessão e aparelho (B) | D5 |
| 69 | PRD-03b | Nomes no aparelho | Primeiro nome e inicial, só com aparelho registrado válido |
| 70 | PRD-03b | Operador sem e-mail (B) | D5 |
| 71 | PRD-03b | Quem revoga aparelho | `settings.edit` ou `production.approve`, e o próprio usuário apaga o próprio PIN; notificação ao Proprietário |
| 72 | PRD-03b | Relógio e fila sem rede | Sai com o adiamento do sem rede (D5); quando voltar: até 2 min no futuro, não antes do início da sessão de origem, validade de 72 h |
| 73 | PRD-04 | Momento do custo-hora | No fechamento do apontamento |
| 74 | PRD-04 | Recalcular sem custo-hora | Permitido com `production.approve`, publicando a diferença |
| 75 | PRD-04 | Pausa paga | Nenhuma pausa entra no custo; tempo parado vira indicador na Fase 4 |
| 76 | PRD-05 | Segregação na qualidade | Quem fez qualquer etapa da tentativa não aprova; sem outro aprovador elegível, permite com a marca "Autoinspeção" e notifica o Proprietário |
| 77 | PRD-05 | Responsável padrão da NC | Quem reprovou (corrigir o CA 4) |
| 78 | PRD-05 | Custo da NC | Informativo |
| 79 | PRD-05 | `Quality → Completed` (B) | D12 |
| 80 | PRD-06a | Formato da série | D15 |
| 81 | PRD-06a | Fabricação manual com OS | Bloqueado quando o item tem OS |
| 82 | PRD-06a | Conclusão parcial | Fora |
| 83 | PRD-06a | Estorno com parcela liberada | Não estorna o título; recusado se o título já estiver em OP ou recebido; notifica o Financeiro |
| 84 | PRD-06b | Romaneio ou NF-e baixa (B) | D4 |
| 85 | PRD-06b | Numeração ROM | D15 |
| 86 | PRD-06b | Quem monta o romaneio | `production.create` monta; com produto do estoque geral, outra pessoa com `inventory.approve` confirma |
| 87 | EST-04 | Lotes por série | Herda os lotes da OS inteira |
| 88 | EST-04 | Nome do cliente | Cliente PJ pelo nome; cliente PF com iniciais para quem não tem `parties.view` |
| 89 | EST-05 | Numeração INV | D15 |
| 90 | EST-05 | Contagem cega | Sim |
| 91 | EST-05 | Movimentos durante a contagem | Permitidos, com o ajuste gerado em modo Delta; ajuste e estorno manual no local bloqueados enquanto houver contagem |
| 92 | EST-06 | Material a caminho | Só o disponível de hoje, com a linha "Com os pedidos em aberto: {n}" |
| 93 | PRD-06b | Etapa Expedição antes do romaneio (novo) | D4 |
| 94 | PRD-05 | Qualidade no tablet (novo) | D6 |
| 95 | PRD-03a/03b | Tema do `/fab` (novo) | Por aparelho registrado, padrão automático |

## 2. Correções sem decisão do PO

### 2.1 Arquitetura (entram nas ADRs 0023 a 0026 e nas stories)
- **Motor de estoque (ADR 0023, EST-01a):**
  - posição agregada `inventory.stock_positions (item, local, físico, reservado)` com os `CHECK`s, onde a reserva trava; o detalhe por lote guarda só o físico; os dois mudam na mesma transação; `UNIQUE NULLS NOT DISTINCT` com `INSERT … ON CONFLICT DO NOTHING` e `FOR UPDATE` na primeira entrada;
  - trava da linha do item antes dos saldos, sempre na ordem item → local → lote, para o custo médio não perder atualização e não haver deadlock;
  - `IStockMovements.PostAsync` exige transação aberta;
  - movimento imutável (trigger recusa qualquer UPDATE), kardex pela ordem de lançamento, `balance_after` e `avg_cost_after` no movimento; estorno de entrada já consumida protege o custo médio; chave de idempotência com perna e tipo;
  - saldos e custo médio fora do diff de auditoria (o movimento é a trilha);
  - princípio: **efeito de estoque de um documento é chamada síncrona; projeção (custo do projeto, notificação, painel) é evento**.
- **Compras ↔ Financeiro (ADR 0024, CMP-03a e CMP-04):**
  - o Purchasing chama o `IPayables` na própria transação (como a COM-06); o evento fica só para aviso;
  - cada recebimento gera os próprios títulos em aberto (origem `GoodsReceipt`), e os previstos do pedido são replanejados para o saldo a receber; o estorno cancela os do recebimento (409 se estiverem em OP);
  - `IPayables`: coluna `installment`, índice único `(origin, origin_id, installment)` criado antes de remover o atual, conta e natureza como parâmetros, e os métodos `CreatePlannedInstallmentsAsync`, `ReplacePlannedAsync`, `CancelAllFromOriginAsync`;
  - `IApprovalRouting` em `Finance.Contracts`: `ResolveAsync(documentType, referenceAmount, accountId, requesterId)` e `EligibleAsync`; mapa tipo de documento → permissão do aprovador no Finance; o valor de referência contra fracionamento vem do chamador; o Purchasing guarda os próprios níveis e aprovações; helper comum da regra do Proprietário único.
- **Custo real do projeto (ADR 0025, antes da PRD-04):** `commercial.project_costs` nasce na PRD-04 como razão de diferenças (`source_id` + `seq`; eventos levam valor anterior e novo); fontes e exclusões da D2; custo da OS na conclusão por fontes síncronas (horas da Produção e `IStockMovements.CostByOrderAsync`), sem depender da permissão de quem conclui.
- **Chão de fábrica (ADR 0026, antes da PRD-03b):** aparelho com segredo rotativo; refresh ligado ao aparelho, com tipo de sessão e prazo absoluto; `amr=pin` (adendo à ADR 0008); rota anônima `POST /auth/pin` (adendo à ADR 0009); lista permitida de endpoints com teste de arquitetura; aparelho revogado conferido a cada request.
- **Reservas e faltas:** consumidores reconciliam a partir da fonte (`IBoms.ReleasedForOrderAsync`, `IProductionOrders.StatusAsync`); OS cancelada ou concluída tem alvo 0; revisão antiga é ignorada; `StockShortageChanged` vira só aviso.
- **OS de fabricação e de instalação:** mesma entidade `ProductionOrder`, com `CHECK` por tipo (`installer_id` e `contract_value` anuláveis) e contratos separados; `ProjectItemCreated` ganha `kind`, `isOnboarding` e `routingTemplateId` (compatível com as mensagens antigas); `InstallationOrdersContract.StatusAsync` filtra por tipo; handler próprio de `ContractCancelled` para fabricação; a versão anterior (volta automática do deploy) não pode carregar linhas de fabricação.
- **Conclusão e romaneio:** entrada do produto acabado, séries e liberação das reservas síncronas na conclusão; `ProjectFabricated` por evento; baixa do romaneio síncrona, com 409 `insufficient_stock`.
- **Outros:** inventário gera o ajuste em modo Delta; avisos de tempo real depois do commit, também nos consumidores do outbox (no BuildingBlocks); `RequireModule` e permissão no `MapHub`; contratos que faltam (`PromisedDate` no projeto, `EmployeeOfUserAsync`, tipo e roteiro em `IProjects.ItemsAsync`); job diário para afastamento com data futura; prefixos de numeração únicos.

### 2.2 Segurança (viram CAs)
- **CAD-04c:** download de documento do funcionário grava leitura sensível (`AttachmentOwnerRules.AuditReads` no BuildingBlocks) e a lista de anexos só com `hr.view_sensitive`; aviso quando a chave Pix ou o titular não bate com o CPF; marca "conta alterada há menos de 30 dias"; dependentes, afastamentos e documentos na exportação e anonimização (ADR 0018).
- **EST-01a, EST-01b, EST-03, EST-05:** D7; nenhum DTO do PWA traz custo; o aprovador do ajuste decide sobre o que foi enviado, sem editar; o pedido do operador nunca baixa estoque; recontador diferente do primeiro contador, conferido no servidor.
- **CMP-01:** job e consumidores conferem o módulo `purchasing` liberado no tenant.
- **CMP-02:** registrar quem lançou cada resposta; anexo obrigatório acima do limite (D8).
- **CMP-03a:** teste de que `finance.approve` sem `purchasing.approve` recebe 403; fracionamento sobre os pedidos; editar pedido enviado volta à aprovação e exige reenvio.
- **CMP-03b:** confirmação de destinatário fora dos contatos do fornecedor (como a COM-03); mensagem escapada, sem links clicáveis; CR/LF recusado nos endereços.
- **CMP-04:** D8.
- **PRD-01 e PRD-03a:** anexos pelo endpoint do chão de fábrica que confere a fila do operador; limite de envio; "Apontar por funcionário" sem sobreposição e com no máximo 7 dias de retroativo; quem corrige ou aprova não é o dono do apontamento ou da etapa.
- **PRD-02:** grupo do hub para o chão de fábrica com `shopfloor.view`; conexão fechada quando a permissão ou o security stamp mudam; token fora dos logs do nginx e do Serilog.
- **PRD-03b:** D5; perfil sensível reconferido a cada login por PIN (ganhar `*.approve` ou `finance.pay` apaga o PIN, exceto a `production.approve_quality` da D6); trocar o vínculo usuário↔funcionário apaga o PIN e revoga as sessões; limite de erros por aparelho; notificação ao Proprietário em cada registro; teto de aparelhos; o service worker nunca guarda `/api/*`.
- **PRD-05, PRD-06a, PRD-06b, EST-04:** pontos 76, 83, 86 e 88.

### 2.3 Design (padrões da Fase 3, entram no README)
1. Tela dedicada (`/fab`, `/fab/entrar`, contagem pelo celular): alvos de 56 × 56 px com 8 px entre eles (16 px entre ações opostas), texto de 18 px, nenhum gesto fino, região de mensagens própria, confirmação em folha de tela cheia, botão "Ajuda" no cabeçalho (abre sozinha só na `/fab/entrar`). Kit "chão de fábrica" criado na PRD-03a.
2. Faixa lateral sempre com texto ("Atrasada {n} d", "Em risco, previsto {data}", "+3 h acima do padrão"); nada só por cor.
3. Indicador de etapa: atual em âmbar, concluídas apagadas, formato compacto "M · 3/12" acima de 8 etapas, letra vinda do roteiro, `key` pelo índice (corrigido na ENG-01).
4. Efeito em estoque, financeiro ou custo sempre com diálogo de impacto (antes → depois) e o mesmo verbo, inclusive no PWA ("Finalizar etapa · {h} h apontadas", com "Pausar" separado).
5. "Aguardando você" como filtro padrão de toda lista com aprovação.
6. Valores sugeridos com a tag "Sugerido" (lote, fornecedor, preço, roteiro, local, vencedor).
7. Processamento assíncrono: "Fazendo X…", aviso de demora depois de 30 s e notificação ao terminar.
8. Lista com saldo mostra físico, reservado e disponível; a 400 px fica só o disponível, o resto na linha expansível.
9. Uma navegação só por módulo (topbar), com abas agrupadas em "Operação" e "Cadastros" (Estoque e Produção).
10. OS de fabricação e de instalação compartilham `OrderHeader` e `FactsBar`; ordem das abas da OS: Etapas, BOM, Material, Apontamentos, Qualidade, Custos, Séries, Documentos, Histórico.
11. Recomposição do passado (D3) com a marca "Calculado na implantação".
- **Achados pontuais:** motivos de pausa com "Fim do turno" e "Outro" de texto opcional; tela "Apontamentos abertos há mais de {x} h" para o supervisor; faixa fixa "Sem conexão" quando o sem rede voltar; "Recebimentos" abre em "A receber"; "Lançar entrada" como ação principal de Saldos; um modo por ajuste; dock "Operador" leva ao `/fab`; Kanban sem interativo aninhado e sem `draggable`; comparativo de cotação com rádio por linha; "Enviar todos para aprovação" depois de gerar pedidos.

## 3. Pré-requisitos compartilhados
- **ADR 0025 (custo real do projeto)** antes da PRD-04 (Marco 3.1), porque a PRD-04 cria a `project_costs` e muda a COM-05.
- **ADR 0026 (chão de fábrica)** antes da PRD-03b.
- **ADR 0023 (motor de estoque)** antes da EST-01a (Marco 3.2).
- **ADR 0024 (compras ↔ financeiro)** antes da CMP-03a (Marco 3.3).
- **Adendos:** fluxos e eventos (`04-fluxos` §1, §2 e §4: D4, D12, D15, estorno `Fabricated → InProgress`); ADR 0003 (`engineering.delete`, `purchasing.receive`, `production.approve_quality`); ADR 0008 (seed com `engineering.approve`, `amr`); ADR 0009 (rota do PIN); ADR 0019 §7 (donos de anexo `Rfq`, `PurchaseOrder`, `NonConformity`, `Shipment`; dwg/dxf); protótipo (nota do arraste no Kanban); P-04 (etapa Expedição).
- **Componentes:** kit chão de fábrica (PRD-03a), indicador de etapa (ENG-01), `OrderHeader`/`FactsBar` (PRD-01).
- **Migrations:** nada destrutivo; cuidado com a versão anterior em `production_orders` (colunas anuláveis com `CHECK`), na troca do índice de `payables` e no trigger e `CHECK`s de saldo.

## 4. Ordem recomendada (D1)
- **3.1 Fábrica visível:** ENG-01 → PRD-01 → PRD-02 → CAD-04c1 → PRD-03a → *(ADR 0025)* PRD-04 → PRD-05 → PRD-06a1 (concluir a OS, projeto "Fabricado", parcela liberada, sem estoque) → *(ADR 0026)* PRD-03b (PIN e aparelho, sem o sem rede).
  - **O PO valida:** OS no Kanban, horas no Projeto 360 e a parcela "fabricação concluída" liberada sozinha.
- **3.2 Estoque e BOM:** *(ADR 0023)* EST-01a → ENG-02 (liga o bloqueio por BOM) → EST-02 (reconcilia as BOMs já liberadas) → EST-03 (realizado de material, com recomposição).
- **3.3 Comprar o que falta:** *(ADR 0024)* CMP-01 → CMP-03a → CMP-04 → CMP-02 → CMP-03b.
- **3.4 Fechar e rastrear:** PRD-06a2 (produto acabado com série e custo; as OS concluídas antes ficam sem entrada, documentado) → PRD-06b → EST-04 → EST-01b → EST-05 → EST-06 (desejável; pode ir para a Fase 4).
- **Adiados:** o sem rede da PRD-03b (depois do piloto) e a CAD-04c2 (Fase 6, antes da RH-02).
- **Total:** 25 stories na Fase 3 depois das quebras (CAD-04c1, PRD-06a1 e 06a2), mais a CAD-04c2 na Fase 6.
