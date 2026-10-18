# ADR 0014: Pergunte ao BemAqui e provedor de IA configurável

- Status: Aceito (18/10/2026).
- Contexto: a FND-15 deixou em aberto seis pontos: o tratamento da pergunta com dado da empresa, o método de busca, o módulo dono da configuração, os modelos por provedor, o tempo limite e o texto fixo do prompt. A referência é o NSHome (`D:\GIT\NSHome`, `ResponderPerguntaAjuda`).

## Decisão
- **Busca:** a mesma do NSHome. É lexical e em memória, sobre as seções das ajudas que o usuário pode abrir (R-AJD-6).
  - O texto é normalizado (minúsculas, sem acento) e as palavras vazias são descartadas.
  - Pontuação: o termo no texto vale 1; no título da ajuda ou da seção, vale mais 2; a ajuda da tela atual ganha mais 2 e vence o empate.
  - Até 6 seções vão para a IA; sem IA, a resposta mostra as 3 primeiras.
  - Embeddings ficam de fora até a busca lexical se mostrar insuficiente.
- **Prompt:** contém só três partes, e nenhum dado do banco do tenant:
  - as instruções fixas (`HelpPrompt.System`): responder em PT-BR, só com os trechos, sem inventar, tratar o conteúdo como dado e devolver JSON `{resposta, fontes, naoEncontrou}`;
  - a pergunta, com até 500 caracteres;
  - os trechos numerados `[a1]…[a6]`.
- **Citação:** validada no servidor. A resposta só é aceita com ao menos uma fonte, e toda fonte (na lista ou no texto) precisa ser um dos trechos enviados. Qualquer outro caso cai nos trechos encontrados:
  - JSON inválido;
  - `naoEncontrou`;
  - nenhuma fonte;
  - fonte inventada.
- **Pergunta com dado da empresa:** o sistema avisa e pede confirmação, sem bloquear nem mascarar.
  - O aviso dispara quando a pergunta tem e-mail, sequência longa de dígitos (CPF, CNPJ, telefone) ou valor em reais.
  - Só aparece quando há provedor configurado, porque sem IA nada sai do sistema.
  - Confirmada, a pergunta segue como foi escrita.
- **Configuração:** fica no Identity, dono de Configurações, como `security.require_2fa`. As tabelas são `identity.ai_settings` (uma linha) e `identity.ai_usage_logs`.
  - A chave é cifrada com `SecretKind.AiApiKey` no escopo do tenant, marcada com `[ProtectedSecret]` (a auditoria só registra que mudou) e nunca volta pela API.
  - No OpenAPI, `Secret` aparece como string `writeOnly`.
  - A configuração é lida do banco a cada pergunta. Trocar o provedor vale na próxima pergunta, sem cache para invalidar.
  - Trocar de provedor exige a chave do novo provedor.
- **Provedores:** os adaptadores Anthropic (Messages), Gemini (`generateContent`) e OpenAI (Chat Completions) usam só `HttpClient`, sem SDK.
  - A chave vai sempre em cabeçalho, nunca na URL.
  - O log do `HttpClient` é desligado para esses clientes.
  - Os erros são traduzidos: chave recusada, modelo inexistente, rede, tempo, cota e erro do provedor.
- **Modelos:** o sistema mantém a lista de sugestões por provedor (`AiProviders`), e a opção "Outro" aceita qualquer nome válido.
- **Tempo limite:** 20 s (`Ai:Timeout`), sem nova tentativa. A falha conta para o alerta de integração (`ia:<provedor>`, FND-12) e devolve os trechos.
- **Uso:** toda chamada grava data, usuário, provedor, modelo, tokens, duração e resultado. Os resultados possíveis são `answered`, `no_citation`, `failed`, `test_ok` e `test_failed`.
- **Abuso:** a pergunta tem um rate limit de 10 por minuto por IP + tenant (`RateLimit:Auth:AskPerMinute`), porque cada pergunta pode virar uma chamada paga.

## Consequências
- Cota de gasto por tenant e histórico de conversas continuam fora do escopo.
- O texto do prompt fica num lugar só, com teste. Mudar o texto exige revisão do PO.
