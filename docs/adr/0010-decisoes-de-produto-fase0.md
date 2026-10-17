# ADR 0010 — Decisões de produto da Fase 0

- Status: Aceito (16/10/2026). O PO (John) aprovou as recomendações.
- Responde aos pontos de produto em aberto 1 a 14 de `docs/stories/README.md`.

| # | Decisão |
|---|---|
| 1 | **Catálogo de módulos:** os 13 módulos do CLAUDE.md, mais `settings` (Configurações). Os módulos, as ações e o significado de "aprovar" em cada um seguem a proposta da FND-06. |
| 2 | **`settings` é um módulo do catálogo**, com as ações `view` e `edit`. Ali ficam os parâmetros (encargos, garantia, markup ou margem, IA, 2FA, numeração). |
| 3 | **Permissões padrão dos grupos:**<br>• o Financeiro **não** tem `finance.approve`, porque o Proprietário aprova tudo por padrão;<br>• o RH tem `hr.view_sensitive`;<br>• os demais grupos seguem a proposta da FND-06b. |
| 4 | **Plano da Konebras:** o completo, com todos os módulos liberados. |
| 5 | **Senha:** no mínimo 12 caracteres, sem regras de composição, conferida contra uma lista local de senhas vazadas.<br>**Convite:** válido por 72 h.<br>**2FA:** 10 códigos de recuperação de uso único; o token intermediário vale 5 min. |
| 6 | **Rate limit:** 5 erros bloqueiam a conta por 15 min, com e-mail ao titular. O limite por IP real é de 20 tentativas por minuto. A janela de tolerância para refresh concorrente é de 10 s. |
| 7 | **Painel da plataforma:** acesso só a partir de IPs liberados, configurados no nginx; hoje, os IPs do PO. Fora da allowlist, responde 404. Para acessar de fora, o PO adiciona o IP via SSH. |
| 8 | **Módulo liberado mas sem permissão:** "Você não tem acesso a esta funcionalidade. Fale com o administrador." |
| 9 | **Tela inicial da Fase 0:** boas-vindas com atalhos para os módulos a que o usuário tem acesso. A busca da topbar só navega por telas até os módulos terem dados. |
| 10 | **E-mail dos usuários:** só aparece para quem tem `identity.view`. |
| 11 | **IA:**<br>• se a pergunta tiver dado da empresa, o sistema avisa antes de enviar e o usuário confirma;<br>• o tempo limite é de 20 s;<br>• o modelo é escolhido de uma lista por provedor, com a opção "outro" para digitar. |
| 12 | **FND-10 dividida:**<br>• **FND-10a:** homologação no ar logo depois da FND-01;<br>• **FND-10b:** hardening, backup cifrado e monitor externo. |
| 13 | **Produção:** o pipeline fica desligado até a GOL-01 (ADR 0005 D1). |
| 14 | **Alertas:** chegam por e-mail. O monitor externo é o UptimeRobot (plano gratuito), fora da VPS. |
