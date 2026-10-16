# ADR 0002 — Acesso por grupos de usuários e módulos liberados por versão

- Status: Aceito (16/10/2026)
- Decisão: usuário pertence a um ou mais grupos; grupo habilita módulos e marca ações (view, create, edit, approve, delete). Permissão efetiva = união dos grupos ∩ módulos liberados para o tenant (plano/versão). Sem permissão direta no usuário.
- Módulo não liberado: botão com cadeado e tela "Funcionalidade não liberada nessa versão do sistema."; API 403 `module_not_released`.
- Motivo: o dono gerencia acesso por função, sem configurar pessoa a pessoa; o SaaS libera módulos por versão sem deploy.
- Consequências: catálogo de módulos por seed, cache de permissões por usuário, grupo Proprietário de sistema. Ver PRD SEC-4/9–12 e 04-fluxos §3.7.
