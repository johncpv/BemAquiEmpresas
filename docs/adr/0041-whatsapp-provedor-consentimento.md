# ADR 0041: WhatsApp: provedor e consentimento

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D17 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`) e os pontos 17 a 21 da tabela 3.5.
- Contexto: a WPP-01 manda avisos de cobrança (régua da FIN-09) e de etapa do projeto por WhatsApp. A story deixava em aberto o provedor (API oficial, intermediário não oficial, automação de WhatsApp Web), quem registra o consentimento (a proposta deixava o usuário interno marcar "aceita" por consentimento verbal), o que vai no texto (a proposta tinha valor e link do boleto) e o horário. Um número de telefone reciclado pela operadora faria a cobrança de um cliente chegar a um desconhecido.
- Complementa as ADRs 0007 (segredo `WhatsAppCredentials`, adendo de 21/10/2026), 0031 (recepção de webhooks), 0037 (LGPD), 0040 (portal) e 0042 (Configurações → Integrações → WhatsApp).

## Decisão

### 1. Provedor
- **API oficial do WhatsApp Business (Meta), por um parceiro (BSP)**, escolhido pelo tenant; **custo do tenant** (conta e cobrança do BSP no nome dele). Sem automação de WhatsApp Web nem intermediário não oficial.
- Atrás do **`IWhatsAppSender`** (BuildingBlocks, como o `IEmailSender`), um adaptador por BSP só com `HttpClient`, URL base fixa por BSP (contra SSRF), log do `HttpClient` desligado.
- Credenciais por tenant cifradas (`SecretKind.WhatsAppCredentials`, só de escrita), em **Configurações → Integrações → WhatsApp** (ADR 0042), com `settings.edit` e "Testar conexão".
- Webhook de situação pelo provedor `whatsapp` da ADR 0031 (assinatura `X-Hub-Signature-256` validada, deduplicação pelo id da mensagem), que declara o **GET de verificação** da Meta (`hub.mode=subscribe`, `hub.verify_token` guardado como hash, resposta com o `hub.challenge`; ADR 0031 §1). Conteúdo de mensagem recebida do cliente **não é guardado**; só o descadastro.

### 2. Consentimento
- **Opt-in pelo próprio contato**, no portal (POR-01a, "Preferências de aviso") ou por resposta ao convite do portal; o usuário interno **não marca** o consentimento por ele. Registro de origem, data, IP ou id da mensagem e versão do texto aceito.
- **Trocar o número do contato derruba o opt-in** (o número novo precisa de opt-in novo); o mesmo vale para contato excluído ou terceiro inativado.
- Opt-out a qualquer momento ("SAIR" pelo WhatsApp ou no portal), válido na hora e registrado.
- Telefone `[PersonalData]`, mascarado para quem não tem `parties.edit`, com expurgo do log de envio pela ADR 0037.

### 3. Conteúdo
- A mensagem leva **só o aviso e o link do portal** ("{Empresa}: há um aviso sobre o seu projeto {nome curto}. Veja no portal: {link}"). **Sem valor, CPF, CNPJ completo nem link de boleto**; o detalhe só aparece depois do login no portal.
- Texto só de **modelo aprovado no BSP**, com variáveis de lista fixa; nada de texto livre do usuário ou do cliente. Textos aprovados pela Konebras, com prévia da mensagem na tela.

### 4. Envio
- Só em **dias úteis, das 8 h às 18 h** (`America/Sao_Paulo`, calendário do tenant); fora disso, a mensagem espera a próxima janela.
- Fila própria `whatsapp`, com poucos workers; 3 tentativas com backoff, depois `Failed` com o motivo, lista de falhas reenviáveis e alerta de integração (FND-12). Argumentos do job só com ids e `tenantId`.
- Idempotente por título ou projeto, etapa da régua e dia (`dedup_key`).
- **A FIN-09 (régua por e-mail) vem antes**; o WhatsApp é só mais um canal da régua e dos avisos de etapa.

## Consequências
- **WPP-01** implementa o adaptador, a tela de Configurações → Integrações → WhatsApp, os modelos, o consentimento pelo portal, a janela de envio e o webhook; depende da FIN-09 e da POR-01a.
- A WPP-01 bloqueia a GOL-01a (última story da Fase 8).
- `docs/03-modelo-dados.md` registra `shared.whatsapp_settings`, `whatsapp_templates`, `whatsapp_logs` e os campos de opt-in de `party_contacts` (+ `whatsapp_opt_in_phone`, para derrubar o opt-in na troca do número).

## Alternativas consideradas
- **Automação de WhatsApp Web ou API não oficial:** rejeitada; viola os termos da Meta, o número pode ser banido e as credenciais ficam num navegador.
- **Opt-in marcado pelo usuário interno** (consentimento verbal): rejeitada; não prova o consentimento do titular nem que o número é dele.
- **Valor e link do boleto na mensagem:** rejeitada; a mensagem pode chegar a outra pessoa (número reciclado, celular compartilhado).
