# ATD-05b — Funil por evento do BemAqui Empresas e gatilho "Evento do BemAqui Empresas" nas automações

| Campo | Valor |
|---|---|
| ID | ATD-05b |
| Título | O negócio (`Deal`) ligado ao pedido anda sozinho no funil conforme os avisos (criado, pago, entregue, cancelado), sem nunca voltar de etapa por aviso atrasado, com valor e ganho/perda; e o editor de automações ganha o gatilho `erp_event`, cujos envios respeitam o canal da conversa, o "cliente falou nas últimas 24 h", o consentimento e a supressão da ATD-05a |
| Tamanho | P |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) · [INT-02](INT-02-webhooks-de-saida.md) · [Plano](../integracoes/00-plano-integracoes.md) §3.3 ("Atualizar a etapa do negócio") |
| Dependências | ATD-01, ATD-03a (`ErpOrderLink.DealId`, reconciliação), ATD-05a (consentimento e supressão); **VND-02b** para `picked|invoiced|shipped|delivered` |
| Bloqueia | — |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`). ERP: nenhuma mudança |
| Ordem | Depois da ATD-05a |

## User story
Como **gestor comercial**, quero **ver no funil do Conecta em que pé cada venda está, sem ninguém arrastar cartão**, para **saber o que está pago, o que falta cobrar e o que se perdeu**.

## Contexto
- O funil do Conecta: `Deal` (contato, etapa, valor, `open|won|lost`), `PipelineStage`, `MoveDealCommand` (dispara `deal_stage_changed` e as regras de tarefa). Automações: `AutomationFlow` com `TriggerType` e nós (`move_deal`, `send_message`, `send_message_to_contact`…).

## Critérios de aceite
1. **Dado** **Funil por evento** (Configurações → BemAqui Empresas, `admin`), **quando** ligo e escolho a etapa de cada evento (ex.: `sales_order.created` → "Pedido", `.paid` → "Pago", `.delivered` → "Entregue (com a VND-02b)", `.cancelled` → "Perdido"), **então** o negócio do pedido (`ErpOrderLink.DealId`) vai à etapa pelo `MoveDealCommand`, recebe o total do BemAqui Empresas como valor, fica `won` no evento marcado "ganho" (padrão `.paid`) e `lost` no cancelado.
2. **Dado** um aviso atrasado ou repetido (ou a reconciliação da ATD-03a), **quando** iria mover o negócio, **então** o Conecta relê o pedido e só avança: nunca volta de etapa pela ordem do funil; idempotente por **(pedido, situação)**.
3. **Dado** um pedido feito no BemAqui Empresas para um cliente identificado, **quando** chega `sales_order.created`, **então** a opção "Criar negócio para pedidos feitos fora do Conecta" (padrão desligada) cria o negócio na etapa configurada.
4. **Dado** o editor de automações (`AutomationEditorView.vue`), **quando** o `admin` cria uma automação, **então** há o gatilho **"Evento do BemAqui Empresas"** (`erp_event`) com o filtro do tipo de aviso (rótulos humanos: "Pagamento confirmado", "Pedido enviado"…); o contexto leva contato, conversa, negócio, tipo e número do pedido (sem dado pessoal).
5. **[Segurança]** **Dado** um nó de envio numa automação `erp_event`, **quando** executa, **então** manda pelo canal da conversa do pedido (nunca pelo canal mais recente da conta), só canal oficial, respeitando "o cliente falou nas últimas 24 h" (texto) ou a mensagem pronta, o consentimento e a supressão da ATD-05a, e a chave (pedido, situação, telefone, automação); o nó `send_message_to_contact` de hoje não é oferecido para esse gatilho.
6. **Dado** um movimento feito por evento, **quando** o atendente abre o negócio, **então** o histórico diz "Movido para Pago: pagamento confirmado no BemAqui Empresas em dd/mm hh:mm".

## O que muda (Conecta)
- **API:** `ErpPipelineSettings`; reação aos avisos e à reconciliação; gatilho `erp_event` no `AutomationEngine` (`ShouldRun`) e no `AutomationContext` (`ErpEventType`, `ErpOrderNumber`); nó de envio com canal da conversa e as regras da ATD-05a.
- **Web:** "Funil por evento" nas configurações; gatilho e rótulos no editor; histórico do negócio.

## Telas e estados
- Funil por evento: carregando, erro, etapas do funil da conta, "Sem funil: crie as etapas primeiro" com o atalho. Verbos: "Salvar funil" → "Funil salvo".

## Plano de testes
- **Integração (Conecta):** avanço por evento com valor, ganho e perda; aviso atrasado não volta etapa; repetição sem duplicar; pedido de fora com a opção ligada e desligada; automação `erp_event` mandando pelo canal da conversa e parando com supressão e sem consentimento; dois tenants.

## Tarefas
- [ ] Conecta API e web; manual do administrador; fluxo do repositório do Conecta.

## Dev Notes
- `Infrastructure/Automations/AutomationEngine.cs` (`ShouldRun`, `ExecuteMoveDeal`), `Application/Common/Interfaces/IAutomationEngine.cs`, `Application/Features/Pipeline/Commands/MoveDealCommand.cs`, `bemaqui-atende-web/src/views/AutomationEditorView.vue`, `PipelineView.vue`.

## Fora do escopo
- Avisos ao cliente (ATD-05a); funil para orçamento e projeto.

## Pontos em aberto
Nada pendente.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Separada da ATD-05 nas revisões de 27/11/2026 (arquitetura, segurança, UX; decisões do PO): funil e gatilho sem voltar etapa, idempotentes por (pedido, situação), envios com as regras da ATD-05a. | Claude (planejamento) |
