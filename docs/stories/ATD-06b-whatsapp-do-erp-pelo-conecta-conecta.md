# ATD-06b — WhatsApp do ERP pelo Conecta: lado do Conecta

| Campo | Valor |
|---|---|
| ID | ATD-06b |
| Título | O Conecta recebe `whatsapp_message.requested`, lê a mensagem no BemAqui Empresas, **valida** link, variáveis e destino, consulta a própria lista de supressão (categoria "avisos do pedido"), manda pelo número oficial escolhido com a mensagem pronta aprovada, devolve a situação (inclusive entregue e lido) e repassa os SAIR; o administrador do Conecta também liga e desliga; nada de criar contato ou conversa sem interação |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ATD-06a](ATD-06a-whatsapp-do-erp-pelo-conecta-erp.md) · [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §2 · WPP-01 (D17) · decisões do PO de 27/11/2026 (4) |
| Dependências | ATD-01, ATD-05a (supressão e detector de SAIR), ATD-06a; canal oficial da Meta (Embedded Signup) e mensagens prontas aprovadas na conta do Conecta |
| Bloqueia | — |
| Repositórios | **Conecta** (`bemaqui-atende-api`, `bemaqui-atende-web`) |
| Ordem | Depois da ATD-06a e da ATD-05a |

## User story
Como **administrador do Conecta**, quero **que os avisos do BemAqui Empresas saiam pelo nosso número oficial, com as nossas mensagens prontas, e que eu possa desligar a qualquer momento**, para **a empresa ter um WhatsApp só e o controle do que sai por ele**.

## Contexto
- O Conecta já tem a API oficial da Meta (`MetaCloudApiService`, `WhatsAppSignupController`), mensagens prontas com situação na Meta (`MessageTemplate.MetaStatus`), envio por `SendTemplateWithNamedParamsAsync` e a situação das mensagens pelo webhook da Meta (`Message.DeliveryStatus`).

## Critérios de aceite
1. **Dado** Configurações → BemAqui Empresas → **WhatsApp do BemAqui Empresas** (`admin`), **quando** abro, **então** ligo e desligo o envio **também no Conecta** (os dois lados precisam estar ligados), escolho o canal oficial (só `MetaCloudApi`) e a mensagem pronta aprovada de cada modelo do sistema ("Aviso de pagamento", "Aviso do projeto", "Código de verificação") com a prévia; sem mensagem pronta, "Crie e aprove a mensagem no WhatsApp" com o atalho preenchido com o texto da WPP-01.
2. **Dado** o aviso `whatsapp_message.requested` validado (ATD-01), **quando** processado, **então** o Conecta lê a mensagem (`GET /whatsapp-messages/{id}`), confere `not_after` e manda **uma vez por `id`** (`erp_whatsapp_relays`, `id` único); a varredura de 5 min (`GET …?status=pending`) cobre aviso perdido.
3. **[Segurança]** **Dado** a mensagem lida, **quando** o Conecta valida, **então** recusa (responde `failed` com o motivo) se: `link` não for `https` no host do portal do tenant cadastrado na conexão (ou no host da conexão), tiver credencial, porta ou fragmento estranho; alguma variável passar de 60 caracteres, tiver quebra de linha ou caractere de controle; `first_name` contiver URL, `@` ou dígitos em sequência; o modelo não for um dos três do sistema; o carimbo de opt-in faltar (exceto no "Código de verificação").
4. **[Segurança]** **Dado** o destino, **quando** vai mandar, **então** consulta a **lista de supressão** do Conecta pela porta única da ATD-05a (CA 13), na categoria **(a) avisos do pedido** (toda mensagem do ERP pelo Conecta é (a), exceto o "Código de verificação", pedido pelo próprio cliente) — com (a) desligada, ou `IsBlocked`, `failed` "Pediu para sair" e o SAIR é reenviado ao ERP — e o teto próprio do Conecta (padrão 500/dia por conta e 3/dia por telefone, alerta ao `admin`).
5. **Dado** tudo válido, **quando** manda, **então** usa `SendTemplateWithNamedParamsAsync` só com as variáveis recebidas, devolve `sent` com o `wamid`; canal oficial inativo ou mensagem pronta não aprovada → `failed` com o motivo.
6. **Dado** a situação da Meta (`delivered`, `read`, `failed`) de um `wamid` do BemAqui Empresas, **quando** chega pelo webhook que o Conecta já recebe, **então** repassa ao ERP uma vez por situação.
7. **[Segurança]** **Dado** a mensagem enviada, **quando** já existe conversa com aquele telefone no canal escolhido, **então** grava a mensagem nela com o rótulo "Aviso do BemAqui Empresas"; **sem** conversa ou contato, **não cria** contato nem conversa (fica só em `erp_whatsapp_relays`, com o telefone em HMAC); se o cliente responder, a conversa nasce pelo caminho normal e o atendente vê "Este cliente recebeu um aviso do BemAqui Empresas em dd/mm".
8. **Dado** SAIR, PARAR, CANCELAR ou STOP (detector da ATD-05a), **quando** chega, **então** o Conecta aplica a regra de categoria da ATD-05a CA 11 (a mensagem do ERP conta como aviso do pedido (a) na escolha da categoria) e chama `POST /integration/whatsapp-opt-outs` com a `category` (com fila e nova tentativa), repassando também o VOLTAR; responde ao cliente como na ATD-05a CA 11 e 12. Saída só de marketing não derruba o opt-in do ERP.
9. **Dado** a lista **Mensagens do BemAqui Empresas** (`admin` e `supervisor`), **quando** abro, **então** vejo modelo, telefone mascarado, situação e motivo, paginada, sem o conteúdo das variáveis.

## O que muda (Conecta)
- **API:** `ErpWhatsAppRelay`, `ErpWhatsAppSettings` (liga/desliga, canal, mapeamento); reação ao aviso, validação, supressão, teto, envio, repasse da situação, varredura; migration aditiva.
- **Web:** tela "WhatsApp do BemAqui Empresas", lista de mensagens, rótulo na conversa.

## Telas e estados
- Carregando, erro, desligado ("O BemAqui Empresas não manda WhatsApp pelo Conecta"), falta canal ou mensagem pronta (com atalho). Verbos: "Ligar envio" → "Envio ligado"; "Desligar envio" → "Envio desligado".

## Plano de testes
- **Integração (Conecta, ERP falso, canal falso):** uma mensagem por `id` com aviso repetido e varredura; cada recusa do CA 3 (link de outro host, com credencial, variável longa, `first_name` com URL); supressão; tetos; repasse de situação; sem conversa não cria contato; SAIR com fila; desligado no Conecta não manda; dois tenants.

## Tarefas
- [ ] Conecta API e web; manual do administrador; fluxo do repositório do Conecta.

## Dev Notes
- `Infrastructure/Channels/MetaCloudApiService.cs`, `API/Controllers/WhatsAppSignupController.cs`, `MessageTemplatesController.cs`, `WebhookController.cs` (situação da Meta), `Domain/Entities/MessageTemplate.cs`, `Message.cs`.

## Fora do escopo
- O lado do ERP (ATD-06a); avisos de pedido do próprio Conecta (ATD-05a).

## Pontos em aberto
Nada pendente.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Separada da ATD-06 nas revisões de 27/11/2026 (arquitetura, segurança, UX; decisões do PO): validação de link e variáveis, supressão própria, tetos, liga/desliga no Conecta, sem criar contato ou conversa sem interação, lista paginada, SAIR global. | Claude (planejamento) |
| 28/11/2026 | 0.2 | Decisão do PO de 28/11/2026: SAIR por categoria; VOLTAR geral; unificar com o opt-out existente do Conecta. O mantenedor (Thiago) revisa. Mensagens do ERP pelo Conecta são categoria (a); supressão pela porta única; opt-out ao ERP com a categoria; repasse do VOLTAR. | Claude (planejamento) |
