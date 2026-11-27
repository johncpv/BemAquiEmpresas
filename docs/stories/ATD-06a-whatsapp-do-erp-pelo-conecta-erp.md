# ATD-06a — WhatsApp do ERP pelo Conecta: lado do BemAqui Empresas (transporte da WPP-01, fecha o P-29)

| Campo | Valor |
|---|---|
| ID | ATD-06a |
| Título | O ERP ganha o `IWhatsAppSender` com o adaptador **"via Conecta"**: a mensagem nasce no ERP (fila, idempotência, opt-in e janela da WPP-01), sai o aviso assinado `whatsapp_message.requested` sem dado pessoal, o Conecta lê a mensagem numa rota com o escopo `conecta.send_whatsapp` (paginada e auditada), devolve a situação e informa os "SAIR"; tetos diários por conexão e por destinatário; tela Configurações → Integrações → WhatsApp "Pelo Conecta" no lugar do parceiro BSP |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §2 e Decisões do PO item 4 · [ADR 0053](../adr/0053-aplicativos-conectados.md) · [INT-02](INT-02-webhooks-de-saida.md) · [INT-04](INT-04-ajustes-avisos-e-conexao.md) (CA 7, 8, 12) · [WPP-01](WPP-01-notificacoes-whatsapp.md) · ADR 0041 · P-29 · decisões do PO de 27/11/2026 (4) |
| Dependências | INT-04 (escopo `conecta.send_whatsapp` e aviso `whatsapp_message.requested`); para mandar a **clientes**, a WPP-01 (opt-in no portal, POR-01a; régua, FIN-09; etapas, POR-01b) |
| Bloqueia | ATD-06b; WPP-01 (passa a usar este transporte) |
| Repositórios | **ERP** |
| Ordem | Em paralelo com o ERP da ATD-03b, depois da INT-04 |

## User story
Como **Proprietário**, quero **que os avisos de cobrança e de etapa do projeto saiam pelo WhatsApp oficial que a empresa já usa no Conecta**, para **não contratar outro parceiro (BSP) e ver a mensagem na caixa de entrada do atendimento**.

## Contexto
- A WPP-01 (Ready, não implementada) previa BSP contratado pelo tenant (P-29). Não existe `IWhatsAppSender` no código. ADR 0052 §2: o WPP-01 sai pelo Conecta, sem BSP.
- O aviso da INT-02 não leva dado pessoal e o ERP não tem credencial para chamar o Conecta: o ERP avisa "há mensagem" e o Conecta lê o destino numa rota protegida por escopo (a revisão de 27/11/2026 recusou "rota da própria conexão" porque ela expõe telefone).

## Critérios de aceite
1. **Dado** a story, **quando** entra, **então** nasce `IWhatsAppSender` (BuildingBlocks, como o `IEmailSender`) com `EnqueueAsync(destino, modeloDoSistema, variáveis, dedupKey, relacionado)` e `shared.whatsapp_logs` (`to` `[PersonalData]`, modelo, `related_type`, `related_id`, `dedup_key` único, `status`, `provider_message_id`, tentativas, erro, `scheduled_for`, `not_after`, `opt_in_at`, `opt_in_text_version`); o adaptador é `ConectaWhatsAppSender`; o de BSP deixa de existir (adendo à ADR 0041).
2. **[Segurança]** **Dado** uma mensagem pronta para sair (dentro da janela da WPP-01), **quando** o ERP a libera, **então** grava na mesma transação o aviso `whatsapp_message.requested` com `data = {id, template, not_after}`, **sem telefone, nome ou variáveis**, para a inscrição da conexão ativa do Conecta **que tem `conecta.send_whatsapp` efetivo** e o envio ligado (CA 6); sem isso, `Failed` "Conecta desconectado ou sem permissão para WhatsApp" e alerta `whatsapp:{tenant}` (FND-12).
3. **[Segurança]** **Dado** `GET /api/v1/integration/whatsapp-messages/{id}` (escopo **`conecta.send_whatsapp`**, módulo `conecta`, `app-api`, `no-store`), **quando** o Conecta lê, **então** recebe `{id, to (E.164), template, language, variables{first_name, company_name, link}, not_after, opt_in{at, text_version}}` só se a mensagem é do Conecta desta empresa, está `Pending` e o opt-in ainda vale **no momento da leitura**; senão 404 ou 410 `message_not_sendable`; `link` é sempre do host do portal do tenant e **nunca leva credencial** (sem token na URL); cada leitura vai à auditoria com o termo em hash.
4. **Dado** `GET /api/v1/integration/whatsapp-messages?status=pending&page=&pageSize=` (mesmo escopo), **quando** o Conecta varre, **então** recebe **só ids e `not_after`**, paginado (até 100), auditado.
5. **Dado** `POST /api/v1/integration/whatsapp-messages/{id}/status` (mesmo escopo, `Idempotency-Key`) com `sent` (`provider_message_id`), `delivered`, `read` ou `failed` (motivo), **quando** chega, **então** o `whatsapp_logs` só anda para a frente; `failed` entra nas tentativas da WPP-01 (3 com espera crescente, depois `Failed` e alerta).
6. **[Segurança]** **Dado** Configurações → Integrações → **WhatsApp**, **quando** o Proprietário abre, **então** vê "WhatsApp oficial pelo Conecta" com a situação e o que falta ("Conecte o Conecta", "Autorize de novo para o Conecta mandar WhatsApp" quando falta o escopo, "Ative o WhatsApp oficial no Conecta"), o custo ("As mensagens são cobradas pela Meta na conta do WhatsApp que a {empresa} usa no Conecta.") e liga **"Enviar avisos pelo WhatsApp do Conecta"** com `settings.edit` e **2FA de novo**; números de teste e "Testar envio" da WPP-01 passam por aqui.
7. **[Segurança]** **Dado** os tetos `Integrations:WhatsAppDailyCap` por conexão (padrão 500/dia) e por destinatário (padrão 3/dia), **quando** são atingidos, **então** a mensagem fica `Held` "Limite do dia" para o dia seguinte e os Proprietários recebem alerta (envio anormal pode ser abuso).
8. **Dado** uma mensagem `Pending` sem `sent` em 15 min, **quando** o varredor roda, **então** o aviso é reenviado uma vez; passado `not_after`, `Failed` "O Conecta não confirmou o envio".
9. **[Segurança]** **Dado** `POST /api/v1/integration/whatsapp-opt-outs` com `{to (E.164), keyword, occurred_at}` e `Idempotency-Key`, **quando** o Conecta informa um SAIR (decisão do PO 4), **então** o ERP derruba o opt-in de **todos** os contatos com aquele telefone (`whatsapp_opt_out_at`, "Pediu para sair pelo WhatsApp"), cancela as mensagens `Pending` para ele e não guarda o texto; esta rota exige só token válido e o módulo `conecta` (rota da própria conexão: só recebe dado, não expõe nada), para o SAIR valer mesmo sem o escopo do WhatsApp; registrado na auditoria.
10. **Dado** o código de identidade por telefone (INT-04 CA 12), **quando** o canal é "phone", **então** sai por este transporte com o modelo do sistema "Código de verificação" (categoria autenticação), fora do opt-in de avisos (é pedido pela própria pessoa) e com o teto do CA 7.

## Regras de negócio e técnicas
- **Quem decide:** o ERP decide se e quando manda (opt-in, janela de dias úteis 8 h às 18 h, `dedup_key`, conteúdo mínimo da WPP-01 D17); o Conecta só transporta.
- **Rotas novas (`integration-v1`, aditivas):** as dos CA 3, 4, 5 e 9; código novo `message_not_sendable`.
- **Ambientes:** WPP-01 CA 13 (só números de teste no `konebras` antes da virada e no `demo`).

## O que muda (ERP)
`IWhatsAppSender`, `ConectaWhatsAppSender`, `shared.whatsapp_logs` (migration aditiva), tradutor do aviso, rotas, tetos, varredor, tela com 2FA, ajuda `Ajuda/configuracoes-integracoes-whatsapp.md`; adendo à ADR 0041 (transporte pelo Conecta, sem BSP); WPP-01 atualizada (CA 1 a 4 e 14 trocados por este transporte; `SecretKind.WhatsAppCredentials` e o provedor `whatsapp` da FND-16 deixam de ser necessários); P-29 fechado em `docs/06-pendencias-riscos.md`; OpenAPI `integration-v1`.

## Plano de testes
- **Unidade:** aviso sem dado pessoal; transições só para a frente; link sem credencial.
- **Integração (Testcontainers, recebedor falso):** enfileirar → aviso → leitura → `sent` → `delivered`; leitura sem o escopo (403), de outra empresa (404), depois do opt-out (410); listagem paginada só com ids; tetos com alerta; varredor e `not_after`; 2FA ao ligar; SAIR sem o escopo do WhatsApp cancela pendentes; código de identidade por telefone; execução dupla; dois tenants; canário de telefone no log.

## Tarefas
- [ ] Adendo à ADR 0041; contrato, adaptador, tabela, aviso, rotas, tetos, varredor, tela, ajuda, OpenAPI.
- [ ] Atualizar a WPP-01 e fechar o P-29.

## Dev Notes
- `src/BuildingBlocks/BemAqui.BuildingBlocks/Webhooks/` (catálogo e publicador), `IEmailSender` (molde), `Jobs/TenantJob.cs`, `Observability/IntegrationHealth.cs`, `ConnectedAppScopes.cs`; a WPP-01 para as regras de negócio.

## Fora do escopo
- O envio no Conecta (ATD-06b); régua, etapas, opt-in no portal e histórico (WPP-01); mensagens com valor ou boleto.

## Pontos em aberto
Decididos (PO, 27/11/2026): escopo próprio (`conecta.send_whatsapp`, INT-04) em vez de rota da conexão; 2FA para ligar; opt-in da WPP-01 continua no portal. Nada pendente além do nome do escopo (INT-04).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Escrita como ATD-06 (ERP e Conecta juntos). | Claude (planejamento) |
| 27/11/2026 | 0.2 | Revisões de 27/11/2026: arquitetura, segurança, UX; decisões do PO. ATD-06 dividida em 06a (ERP) e 06b (Conecta); leitura com o escopo `conecta.send_whatsapp`; listagem paginada só com ids; carimbo do opt-in; link sem credencial; tetos por conexão e destinatário; SAIR global com cancelamento dos pendentes; código de identidade por telefone. | Claude (planejamento) |
