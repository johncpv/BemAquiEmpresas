# ADR 0002 — Acesso por grupos de usuários e módulos liberados por versão

- Status: Aceito (16/10/2026)
- Decisão: usuário pertence a um ou mais grupos; grupo habilita módulos e marca ações (view, create, edit, approve, delete). Permissão efetiva = união dos grupos ∩ módulos liberados para o tenant (plano/versão). Sem permissão direta no usuário.
- Módulo não liberado: botão com cadeado e tela "Funcionalidade não liberada nessa versão do sistema."; API 403 `module_not_released`. **Revista pela ADR 0046 (16/11/2026):** o cadeado vale para módulo no perfil de negócio e fora do plano; módulo fora de todos os perfis do tenant fica oculto no menu (a API e a tela continuam as mesmas).
- Motivo: o dono gerencia acesso por função, sem configurar pessoa a pessoa; o SaaS libera módulos por versão sem deploy.
- Consequências: catálogo de módulos por seed, cache de permissões por usuário, grupo Proprietário de sistema. Ver PRD SEC-4/9–12 e 04-fluxos §3.7.
