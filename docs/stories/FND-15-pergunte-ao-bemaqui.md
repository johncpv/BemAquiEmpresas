# FND-15 — Pergunte ao BemAqui e provedor de IA configurável

| Campo | Valor |
|---|---|
| ID | FND-15 |
| Título | Busca nas ajudas, resposta da IA só com os trechos e com citação, fallback por trechos, tela do provedor de IA e `IAiProvider` |
| Tamanho | G (re-estimada: o backlog dizia M; três adaptadores, tela de configuração, validação de citação e registro de uso) |
| Status | Done |
| Requisitos | R-AJD-5 · R-AJD-8 · R-AJD-6 (a IA só usa ajudas permitidas ao usuário) |
| Dependências | FND-14 (catálogo e painel de ajuda), FND-11 (chave cifrada), FND-06c (permissões, `settings.edit`), FND-09c (formulário), FND-12 (alerta de integração) |
| Bloqueia | — |
| Ordem de execução | 18 |

## User story
Como **usuário**, quero **perguntar em linguagem natural como fazer algo e receber uma resposta curta baseada só na ajuda do sistema, com link para a fonte**, e como **proprietário**, quero **escolher e trocar o provedor de IA na tela**, para **tirar dúvidas na hora sem que dados da empresa saiam do sistema**.

## Contexto e referências
- PRD §5.15a, R-AJD-5 (busca das seções mais próximas, priorizando a tela atual; resposta só com elas, sempre citando a fonte; sem IA ou sem citação válida, devolve os trechos; nunca envia dado de negócio do tenant à IA), R-AJD-8 (Configurações → Inteligência artificial, `settings.edit`; provedor, modelo, chave cifrada write-only, "Testar conexão"; sem configuração, só busca; `IAiProvider` com adaptador por provedor; uso registrado por tenant).
- Padrão NSHome (`D:\GIT\NSHome`).
- FND-11 (padrão de campo write-only).
- Backlog original (CAs): a resposta sempre cita a seção usada; sem provedor, sem IA ou com citação inválida devolve os trechos; trocar de provedor vale na próxima pergunta, sem deploy; a chave nunca volta na API nem aparece em log; teste prova que nenhum dado de negócio do tenant entra no prompt.

## Critérios de aceite
**Pergunta e resposta**
1. **Dado** uma pergunta no painel de ajuda (aba "Pergunte ao BemAqui"), **quando** envio, **então** o sistema busca as seções de ajuda mais próximas entre as permitidas ao usuário (R-AJD-6), com prioridade para a tela atual.
2. **Dado** um provedor configurado e disponível, **quando** a IA responde, **então** a resposta mostra ao menos uma citação, e cada citação é link para uma seção que estava entre os trechos enviados.
3. **Dado** uma resposta da IA sem citação, ou com citação de seção que não estava entre os trechos, **quando** é validada, **então** é descartada e o usuário recebe os trechos encontrados com links ("Encontrei estes trechos na ajuda:").
4. **Dado** nenhum provedor configurado, **quando** pergunto, **então** recebo os trechos encontrados, sem chamar IA.
5. **Dado** o provedor fora do ar, com erro ou acima do tempo limite, **quando** pergunto, **então** recebo os trechos encontrados e a falha conta para o alerta de integração (FND-12).
6. **Dado** nenhuma seção relevante, **quando** pergunto, **então** a tela diz "Não encontrei isso na ajuda." e oferece abrir o índice; a IA não é chamada.
7. **[Segurança]** **Dado** o teste de prompt, **quando** inspeciono o conteúdo enviado ao provedor (adaptador falso que grava a chamada), **então** ele contém só as instruções fixas, a pergunta do usuário e os trechos de ajuda; nenhum dado vindo do banco do tenant (nome da empresa, clientes, valores, usuários) está presente.
8. **[Segurança]** **Dado** uma ajuda de módulo sem permissão para o usuário, **quando** ele pergunta sobre esse módulo, **então** os trechos daquele módulo não são enviados à IA nem devolvidos.

**Configuração do provedor**
9. **Dado** um usuário com `settings.edit`, **quando** abre Configurações → Inteligência artificial, **então** escolhe o provedor (Anthropic Claude, Google Gemini, OpenAI), informa o modelo e a chave de API e salva com "Salvar configuração".
10. **[Segurança]** **Dado** a chave salva, **quando** consulto a configuração pela API ou abro a tela, **então** a chave nunca volta (só "Chave configurada em {data}" e "Substituir"), e ela não aparece em log nem na auditoria (FND-11).
11. **Dado** "Testar conexão", **quando** clico, **então** o sistema faz uma chamada mínima ao provedor com a configuração preenchida e mostra "Conexão OK" ou o erro traduzido (chave inválida, modelo inexistente, sem rede), sem salvar.
12. **Dado** a troca de provedor ou de modelo salva, **quando** a próxima pergunta é feita, **então** usa a configuração nova, sem deploy nem reinício.
13. **Dado** um usuário sem `settings.edit`, **quando** tenta salvar ou testar, **então** recebe 403 `forbidden`.

**Uso**
14. **Dado** cada chamada à IA, **quando** termina, **então** fica registrado por tenant: data UTC, usuário, provedor, modelo, tokens de entrada e saída (quando o provedor informa), duração e resultado (respondida, sem citação, falha).

## Regras de negócio e técnicas
- `IAiProvider` com `CompleteAsync(prompt, opções)` e `TestAsync()`; adaptadores `AnthropicProvider`, `GeminiProvider`, `OpenAiProvider` em `Infrastructure`, só com `HttpClient` (sem SDK que traga dependência pesada, salvo decisão em ADR).
- Prompt com instruções fixas: responder só com os trechos, em PT-BR, citar as seções pelo id fornecido, dizer que não sabe quando os trechos não respondem.
- Busca: índice em memória das seções de ajuda (catálogo da FND-14), com ranking lexical em português e reforço para a rota e a aba atuais (método definitivo: ver Pontos em aberto).
- Validação de citação no servidor, antes de devolver a resposta.
- Tempo limite configurável; sem nova tentativa automática para não atrasar o usuário.
- Chave da IA cifrada com `SecretKind.AiApiKey` no escopo do tenant (FND-11).
- Configuração lida a cada pergunta (com cache curto invalidado ao salvar).

## Permissões, eventos e tabelas
- **Tabelas (propostas, não existem no 03):** `ai_settings` (`provider`, `model`, `api_key_secret` cifrado, `updated_at`, `updated_by`) e `ai_usage_logs` (campos do CA 14). Schema sugerido: o do módulo dono de Configurações.
- **Permissões:** `settings.view` (ver a tela), `settings.edit` (salvar e testar). Perguntar não exige permissão extra (todo usuário logado pode usar a ajuda).
- **Eventos:** `AiSettingsChanged` (auditoria, sem a chave).

## Telas e estados
- **Pergunte ao BemAqui** (aba no painel de ajuda da FND-14): campo de pergunta, botão "Perguntar", resposta com citações em links, aviso fixo "As respostas vêm só da ajuda do sistema." Estados: pensando (indicador), resposta, trechos (fallback), "Não encontrei isso na ajuda.", erro inesperado com "Tentar de novo".
- **Configurações → Inteligência artificial:** provedor (ToggleGroup ou select), modelo (texto), chave (campo write-only, padrão da FND-11), "Testar conexão", "Salvar configuração". Estado vazio: "Nenhum provedor configurado. O Pergunte ao BemAqui funciona só por busca nos textos de ajuda." com a ação "Configurar provedor". Erros por campo em PT-BR.
- **Propositivo:** perguntas sugeridas a partir das "Perguntas frequentes" da tela atual; depois de "Conexão OK", o botão "Salvar configuração" ganha destaque.
- **Ajuda:** `Ajuda/configuracoes-ia.md` e seção "Pergunte ao BemAqui" em `Ajuda/ajuda.md`.

## Plano de testes
- **Unidade:** ranking da busca com prioridade da tela atual (CA 1); validação de citação (CA 2, 3); montagem do prompt (CA 7); filtro por permissão (CA 8).
- **Integração (Testcontainers + servidor HTTP falso por provedor):** os três adaptadores com respostas gravadas; provedor fora do ar e tempo limite (CA 5); troca de provedor sem reinício (CA 12); chave write-only e ausente do log (CA 10); 403 sem `settings.edit` (CA 13); registro de uso (CA 14).
- **E2E (Playwright):** perguntar sem provedor (trechos) e com provedor falso (resposta citada); configurar e testar conexão.

## Fora do escopo
- Busca por embeddings ou banco vetorial (só se a busca lexical não bastar; decisão em ADR).
- Respostas sobre dados do tenant ("quanto tenho a receber?"): proibido pelo R-AJD-5.
- Limite de gasto ou cota por tenant.
- Histórico de conversas.

## Pontos em aberto
- **Pergunta do usuário com dado de negócio:** o R-AJD-5 proíbe enviar dado de negócio do tenant à IA, mas a própria pergunta digitada pode conter dados (nome de cliente, valor). Decidir se há aviso, filtro ou se a pergunta digitada é aceita como está.
- **Método de busca:** lexical em memória (sugestão) ou o mesmo do NSHome; conferir o NSHome antes de implementar.
- **Módulo e schema de Configurações (`settings`):** mesmo ponto das FND-05 e FND-06.
- **Modelos sugeridos por provedor:** o PRD pede o campo "modelo"; definir se é texto livre ou lista mantida pelo sistema.
- **Tempo limite** da chamada à IA (sugestão: 15 s) e **cota de uso** por tenant ou usuário.
- **Conteúdo do prompt de sistema** (instruções fixas): redigir e revisar com o PO.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração e E2E passando.
- [ ] Migrations criadas (`ai_settings`, `ai_usage_logs`).
- [ ] Permissões registradas no seed (`settings.view`, `settings.edit`).
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das telas escrita.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada (alterações da configuração, sem a chave).
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Índice de busca das seções de ajuda com filtro por permissão.
- [ ] `IAiProvider` e adaptadores Claude, Gemini e OpenAI.
- [ ] Montagem do prompt e validação de citação.
- [ ] Endpoint de pergunta (`POST /api/v1/ajuda/perguntar`).
- [ ] Endpoints de configuração (ler, salvar, testar) e registro de uso.
- [ ] Aba "Pergunte ao BemAqui" no painel e tela Configurações → Inteligência artificial.
- [ ] Teste de prompt sem dado de negócio.

## Quality gates
- Agentes previstos: @dev, cybersecurity (chave e vazamento de dados), @qa.
- Gate: teste de prompt sem dado de negócio (CA 7) obrigatório.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Tratamento da pergunta com dado de negócio; método de busca (conferir o NSHome); tempo limite.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada a partir do backlog e do PRD §5.15a | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 1.0 | Implementada; pontos em aberto decididos na ADR 0014 | @dev |

## Dev Agent Record
- Pontos em aberto decididos na ADR 0014: busca do NSHome, aviso com confirmação para pergunta com dado da empresa, configuração no Identity, lista de modelos mantida pelo sistema com "Outro", tempo limite de 20 s e o texto do prompt em `HelpPrompt.System`.
- Backend:
  - lógica em `HelpSearch`, `HelpPrompt` (prompt e validação de citação), `BusinessDataGuard`, `AiSettingsService` e `HelpAnswerService`;
  - adaptadores `AnthropicProvider`, `GeminiProvider` e `OpenAiProvider`;
  - endpoints `POST /api/v1/ajuda/perguntar`, `GET|PUT|DELETE /api/v1/settings/ia` e `POST /api/v1/settings/ia/testar`;
  - migration `AiSettingsAndUsage` e rate limit da pergunta.
- Front: aba "Pergunte ao BemAqui" no painel de ajuda, com perguntas sugeridas das FAQ da tela, e a tela Configurações → Inteligência artificial, com abas de Configurações.
- Testes de unidade (busca, citação, prompt e aviso) e de integração com o servidor falso dos três provedores (CA 1 a 14), incluindo o teste de prompt sem dado do tenant (CA 7).

## QA Results
_A preencher pelo @qa._