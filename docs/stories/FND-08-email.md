# FND-08 — E-mail transacional por fila

| Campo | Valor |
|---|---|
| ID | FND-08 |
| Título | `IEmailSender` (SMTP/SES), templates, fila Hangfire e log de envio |
| Tamanho | M (re-estimada: o backlog dizia P; os CAs de sandbox e de links ampliaram o escopo) |
| Status | Draft |
| Requisitos | R-COM-1 |
| Dependências | FND-07 (Hangfire, `TenantJob`), FND-11 (senha SMTP cifrada), FND-02 (host primário do tenant) |
| Bloqueia | FND-04 (convite e recuperação de senha), FND-03a (etapa `SendInvite`), FND-12 (alerta por e-mail) |
| Ordem de execução | 6 |

## User story
Como **usuário do sistema**, quero **receber os e-mails do sistema (convite, recuperação de senha e, depois, documentos e alertas) de forma confiável e com o visual da empresa**, para **concluir as ações sem depender de outro canal**.

## Contexto e referências
- `CLAUDE.md` → Integrações externas: "SMTP/AWS SES atrás de `IEmailSender`, envio por fila (Hangfire)".
- `docs/02-arquitetura.md` §3 (`IEmailSender` em BuildingBlocks), §6.3 (registros de envio e falha).
- `docs/03-modelo-dados.md` → `shared.email_logs` (`to`, `subject`, `template`, `status`, `error`, `related_type`, `related_id`).
- Revisão §2.3, CAs de segurança da FND-08: templates do tenant só em Fluid sandboxed; nunca buscar URL fornecida pelo tenant.
- Revisão §2.3, FND-04: links de e-mail montados com o host primário do catálogo.

## Critérios de aceite
1. **Dado** um caso de uso que envia e-mail, **quando** chama `IEmailSender.Enqueue(template, destinatário, modelo, relacionado)`, **então** o envio entra na fila `email` do Hangfire com o `tenantId`, e `email_logs` registra `Queued`.
2. **Dado** um envio na fila, **quando** o SMTP aceita, **então** `email_logs` passa a `Sent` com horário UTC.
3. **Dado** um SMTP que falha, **quando** o job roda, **então** tenta de novo 3 vezes com intervalo crescente e, depois da 3ª falha, marca `Failed` com a mensagem de erro no log de envio (visível na consulta de `email_logs`) e gera evento para o alerta da FND-12.
4. **[Segurança]** **Dado** um template editável pelo tenant, **quando** é renderizado, **então** usa Fluid em modo sandbox: sem acesso a objetos .NET fora do modelo explícito, sem include de arquivo, com limite de tempo e de recursão.
5. **[Segurança]** **Dado** um template ou um modelo com URL externa (ex.: `<img src="https://...">` do tenant), **quando** o e-mail é montado, **então** o servidor nunca busca essa URL; o logo do tenant é anexado inline a partir do armazenamento interno.
6. **[Segurança]** **Dado** um e-mail com link (convite, redefinição), **quando** o link é montado, **então** usa o host primário do tenant em `catalog.tenant_domains` (`is_primary`), nunca o cabeçalho `Host` da requisição.
7. **Dado** um valor do modelo com HTML (ex.: nome com `<script>`), **quando** o template renderiza, **então** o valor sai escapado.
8. **Dado** os templates padrão do sistema `invite` e `password-reset`, **quando** são renderizados, **então** saem em PT-BR, com versão HTML e texto puro, nome e logo do tenant, e o link de ação em destaque.
9. **Dado** um e-mail na fila, **quando** o tenant está suspenso, **então** o job não envia (regra da FND-07) e o log registra `Skipped`.

## Regras de negócio e técnicas
- `IEmailSender` com dois adaptadores: SMTP (MailKit) e SES. Configuração escolhida por ambiente.
- Credenciais SMTP cifradas com `SecretKind.SmtpPassword` (FND-11), nunca logadas.
- Fila Hangfire `email`, job `SendEmailJob : TenantJob<...>`.
- Templates padrão versionados com o código (arquivos `.liquid` embutidos); templates customizados pelo tenant ficam para depois, mas o motor já é o sandbox.
- Assunto e corpo em PT-BR; datas exibidas em America/Sao_Paulo.

## Permissões, eventos e tabelas
- **Tabelas:** `shared.email_logs` (+ `attempts`, `sent_at`, `queued_at`).
- **Permissões:** nenhuma nova nesta story (a tela de configuração SMTP do tenant, se houver, entra junto com as Configurações; ver Pontos em aberto).
- **Eventos:** `EmailFailed` (interno, para alerta).

## Telas e estados
Sem tela nesta story. Visual do e-mail: identidade do protótipo (aço frio, acento cobalto), largura de 600px, botão de ação com verbo claro ("Aceitar convite", "Redefinir senha").

## Plano de testes
- **Unidade:** renderização dos templates (escape, PT-BR, texto puro); bloqueio de recursos no sandbox; montagem de link com o host primário.
- **Integração (Testcontainers):** servidor SMTP de teste (smtp4dev ou MailHog em container) para envio com sucesso (CA 2); SMTP derrubado para os 3 retries e `Failed` (CA 3); tenant suspenso (CA 9); verificação de que nenhuma requisição HTTP externa é feita (CA 5, com `HttpMessageHandler` que falha se chamado).

## Fora do escopo
- Editor de templates pelo tenant.
- Envio de documentos (PDF de orçamento, holerite) e régua de cobrança (fases seguintes).
- Rastreamento de abertura e clique.

## Pontos em aberto
- **SMTP da plataforma ou por tenant:** a arquitetura cita "senha SMTP" como segredo por tenant, mas não define se a Konebras usa um remetente próprio ou o da plataforma. Definir o remetente padrão e se haverá tela de configuração de SMTP no tenant.
- **Motor dos templates padrão:** o backlog cita "Razor/Fluid". Esta story usa Fluid para todos, para ter um motor só; confirmar.
- **Onde o log de envio fica visível:** o CA original diz "falha visível no log". Não há tela definida na Fase 0; propõe-se consulta pelo super admin e um painel de e-mails em Configurações numa story futura.
- **Provedor em homologação e produção:** SES ou SMTP de qual provedor; domínio do remetente e SPF/DKIM.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando.
- [ ] Migration criada (campos novos de `email_logs`).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela: não se aplica.
- [ ] Ajuda: não se aplica.
- [ ] Padrões e próximo passo propositivos: todo e-mail tem uma única ação principal com verbo claro.
- [ ] Auditoria: `email_logs` registra todo envio.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IEmailSender`, adaptadores SMTP e SES.
- [ ] Renderizador Fluid em sandbox, com escape e limites.
- [ ] Templates `invite` e `password-reset` (HTML e texto).
- [ ] `SendEmailJob` com retry 3× e log.
- [ ] Montagem de links pelo host primário (`ITenantLinks`).
- [ ] smtp4dev no `docker-compose.yml` local.

## Quality gates
- Agentes previstos: @dev, cybersecurity (sandbox), @qa.
- Gate: teste do sandbox e de "nenhuma URL externa" obrigatórios.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | N/A |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Remetente: SMTP da plataforma ou por tenant; provedor em homologação.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada com os CAs de segurança da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._