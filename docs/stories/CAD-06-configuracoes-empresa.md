# CAD-06 — Configurações da empresa

| Campo | Valor |
|---|---|
| ID | CAD-06 |
| Título | Dados da empresa (fonte da verdade no banco do tenant), logo com rota pública própria, numeração editável por tipo de documento e parâmetros (% de encargos e modo de precificação) |
| Tamanho | M (re-estimada: o backlog dizia P; logo com anexo, ClamAV e rota pública, numeração editável com concorrência e parâmetros com histórico) |
| Status | Ready |
| Requisitos | R-COM-4 · R-COM-3 (logo como anexo) · R-RH-2 (% de encargos do custo-hora, P-05) · R-COML-1 (modo de precificação, P-14) · R-SEC-5 · R-AJD-1 |
| Dependências | FND-09d (moldes e componentes), CAD-01a (`BrazilianDocuments`), CAD-01b (`ICepLookup`); Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | CAD-04a (% de encargos e `PayrollChargesChanged`), COM-01 (modo de precificação), COM-03 (logo no PDF), toda story que numera documento |
| Ordem de execução | 6 (Marco B — catálogo real) |

## User story
Como **proprietário**, quero **cadastrar os dados e o logo da empresa, ajustar a numeração dos documentos e os parâmetros de cálculo (encargos e precificação)**, para **que documentos, custos e preços saiam com o padrão da minha empresa sem eu repetir isso em cada tela**.

## Contexto e referências
- PRD R-COM-4 (numeração configurável por tipo), R-PLT-3 (seeds), R-RH-2 (custo-hora = salário + encargos estimados).
- `docs/04-fluxos-estados.md` §3.2 (fórmula do custo-hora com `%encargos` por tenant) e §4 (formatos padrão de numeração).
- `docs/06-pendencias-riscos.md` P-05 (encargos editáveis com valor inicial e histórico), P-14 (dois modos de precificação).
- **ADR 0016** (rota `GET /api/v1/public/tenant-logo`, numeração com `ON CONFLICT … GREATEST`, reinício anual bloqueado depois do primeiro número, regras do prefixo, `INumberFormatAdmin`, perfil da empresa no schema `settings`, garantia no Inventory), ADR 0014 (Configurações pertencem ao Identity), ADR 0008 (`settings.edit` só o Proprietário concede), ADR 0009 (allowlist de anexos sem SVG).
- Revisão pré-Fase 1: tabela 1.1 (CAD-06), §2.4 ("Salvar numeração" confirma o que não tem volta; ordem das abas), §2.5 (anexos).
- Padrões comuns a todos os cadastros: `docs/stories/README.md`.
- Código existente: `NumberFormats.Defaults`, `INumberSequence`, `identity.tenant_settings`, `AttachmentService`/`AttachmentPolicy`, `SettingsTabs`, `RecordHistory`, `TenantPublicContext(Name, LogoUrl, Status)`.

## Critérios de aceite
**Dados da empresa**
1. **Dado** um tenant novo ou existente, **quando** abro Configurações → Empresa pela primeira vez, **então** o perfil (`settings.company_profile`) nasce com razão social e nome fantasia copiados do catálogo (propositivo) e só falta confirmar; daí em diante o perfil do banco do tenant é a fonte da verdade desses dados.
2. **Dado** `settings.edit`, **quando** preencho razão social, nome fantasia, inscrição estadual, inscrição municipal, endereço (CEP com consulta pelo `ICepLookup`, logradouro, número, complemento, bairro, cidade, IBGE, UF), telefone e e-mail e clico "Salvar dados da empresa", **então** os dados são gravados, auditados e o toast diz "Dados da empresa salvos".
3. **Dado** o CNPJ da empresa, **quando** abro a tela, **então** ele aparece só para leitura, vindo do catálogo, com a `<Dica>` "O CNPJ é alterado pelo suporte da plataforma."; a API recusa CNPJ no corpo do `PUT` (`UnmappedMemberHandling.Disallow`, 400).
4. **Dado** um perfil com nome fantasia, **quando** abro a tela de login do tenant, **então** o `GET /api/v1/public/tenant-context` devolve o nome fantasia do perfil (ou o nome do catálogo, se o perfil ainda não existir).

**Logo**
5. **Dado** um arquivo PNG, JPG ou WebP de até 1 MB e até 1024 px no maior lado, **quando** envio em "Trocar logo", **então** ele passa pela allowlist e pelos magic bytes (FND-07) e fica "Aguardando verificação" até o ClamAV marcar `Clean`; só então aparece no topo do sistema e no login.
6. **[Segurança]** **Dado** um SVG, um HTML renomeado para `.png`, um arquivo acima de 1 MB ou de 1024 px ou um arquivo infectado, **quando** envio, **então** o upload é recusado (o infectado nunca é publicado) com o motivo em PT-BR, e o logo anterior continua valendo.
7. **[Segurança]** **Dado** um visitante sem login, **quando** pede `GET /api/v1/public/tenant-logo`, **então** recebe só o logo atual `Clean` do tenant do host (sem parâmetro de id), com `Content-Type` da allowlist, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'` e `ETag`; repetir com `If-None-Match` responde 304; acima do rate limit, 429; nenhum outro anexo do tenant sai sem login, e o `tenant-context` traz só a URL do logo, nunca data URI.
8. **[Segurança]** **Dado** o `AttachmentOwnerRegistry`, **quando** o dono `TenantLogo` é registrado, **então** declara módulo `settings`, leitura `settings.view`, escrita `settings.edit`, tipos PNG/JPG/WebP, máximo de 1 MB e a verificação de que o perfil existe.

**Numeração**
9. **Dado** `settings.view` em Configurações → Numeração, **quando** abro a aba, **então** vejo cada tipo de documento de `04-fluxos` §4 com prefixo, reinício anual, dígitos, último número emitido e a prévia do próximo (ex.: "ORC-2026-0001").
10. **Dado** `settings.edit`, **quando** troco o prefixo ou os dígitos de um tipo e clico "Salvar numeração", **então** o `ConfirmDialog` mostra o que não tem volta ("O próximo orçamento sairá como ORC-2026-0017. Os números já emitidos não mudam.") e, ao confirmar "Salvar numeração", o formato é gravado pelo `INumberFormatAdmin`, a sequência continua do último valor e o toast diz "Numeração salva".
11. **Dado** `settings.edit`, **quando** informo um "próximo número" menor ou igual ao último emitido, **então** a API responde 409 "O próximo número precisa ser maior que o último emitido ({último})."; quando informo um maior, inclusive para um ano sem nenhum número ainda, a sequência passa a partir dele (`INSERT … ON CONFLICT DO UPDATE SET last_value = GREATEST(…)`).
12. **Dado** um tipo com ao menos um número emitido, **quando** tento trocar "Reinicia por ano", **então** a API responde 409 "Não é possível mudar o reinício anual depois do primeiro número emitido." e o controle aparece desabilitado com a `<Dica>` do motivo.
13. **Dado** um prefixo vazio, com mais de 6 caracteres, com caractere fora de `A–Z` e `0–9` ou já usado por outro tipo, ou dígitos fora de 3 a 8, **quando** salvo, **então** a API responde 400 com a mensagem do campo.
14. **Dado** uma troca de formato salva enquanto 50 documentos do mesmo tipo são numerados em paralelo, **quando** todos terminam, **então** não há número repetido (teste de concorrência da FND-07).

**Parâmetros**
15. **Dado** um tenant novo ou um tenant existente no próximo deploy, **quando** o seed roda, **então** "% de encargos estimados" nasce com 80,00% e "Modo de precificação" com "Markup sobre o custo", sem sobrescrever valor já gravado; o campo de encargos mostra a `<Dica>` "Valor sugerido. Confirme com a contadora."
16. **Dado** `settings.edit`, **quando** altero o "% de encargos estimados" e clico "Salvar % de encargos", **então** o `ConfirmDialog` avisa o impacto ("Os cargos com custo-hora calculado serão recalculados.") e, ao confirmar, o valor é gravado com o histórico (anterior, novo, quem e quando) e o evento `PayrollChargesChanged` vai para o outbox na mesma transação.
17. **Dado** um % de encargos fora de 0 a 200 ou com mais de 2 casas, **quando** salvo, **então** a API responde 400 "Informe um percentual entre 0 e 200, com até 2 casas."
18. **Dado** `settings.edit`, **quando** escolho no `ToggleGroup` "Markup sobre o custo" ou "Margem sobre o preço" e clico "Salvar modo de precificação", **então** o modo é gravado, com histórico; o % padrão de markup ou margem entra na COM-01.

**Leitura por outros módulos**
19. **Dado** o HR (CAD-04a) ou o Commercial (COM-01), **quando** leem o % de encargos ou o modo de precificação, **então** usam `ITenantParameters` de `Identity.Contracts`, sem exigir `settings.view` do usuário; o teste de arquitetura garante a referência só via Contracts. A garantia padrão não fica aqui (Inventory, CAD-02a).

**Permissões (3 camadas)**
20. **Dado** um usuário sem `settings.view`, **quando** chama qualquer endpoint desta story, **então** recebe 403 `forbidden` e não vê Configurações no menu; com `settings.view` sem `settings.edit`, as abas abrem em modo leitura e todo `PUT`/`POST` responde 403 `forbidden`.
21. **Dado** o módulo `settings` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`.

## Regras de negócio e técnicas
- **Dono:** módulo Identity (ADR 0014). Endpoints em `/api/v1/settings/company`, `/api/v1/settings/numbering`, `/api/v1/settings/parameters`, com `RequireModule("settings")` e `perm:settings.view|edit`.
- **Perfil da empresa:** `settings.company_profile`, uma linha por tenant; o CNPJ não é gravado no perfil (vem do catálogo).
- **Parâmetros** em `identity.tenant_settings`: `payroll.charges_pct` (`numeric(5,2)`, exibido "80,00%") e `pricing.mode` (`markup` | `margin`). Mudar o % não altera apontamentos já feitos (snapshot no apontamento, `04-fluxos` §3.2).
- **Numeração:** padrão em `NumberFormats.Defaults`; o tenant grava só o que mudou, em `shared.number_formats`, pelo `INumberFormatAdmin`. `INumberSequence.NextAsync` lê o formato efetivo na mesma transação. Nenhum tipo de documento novo na Fase 1. Toda tabela de documento numerado tem índice único em `number` (ADR 0016), na story que a cria.
- **Logo:** anexo `owner_type = "TenantLogo"`, `owner_id` = id do perfil. Trocar o logo mantém o anterior até o novo ficar `Clean`.
- **Datas:** `updated_at` em UTC, exibido em America/Sao_Paulo. **Ids:** Guid v7.
- **Auditoria:** perfil, formatos de numeração e parâmetros são `IAuditable`. E-mail e telefone da empresa não são dado pessoal.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `settings.view`, `settings.edit`.
- **Eventos (outbox):** `PayrollChargesChanged` (`{ tenantId, oldPct, newPct, changedAt }`), consumido pela CAD-04a.
- **Tabelas:** `settings.company_profile` (criada aqui: `legal_name`, `trade_name`, `state_registration`, `municipal_registration`, `zip`, `street`, `number`, `complement`, `district`, `city`, `city_ibge`, `state`, `phone`, `email`, `logo_attachment_id`); `shared.number_formats` (criada aqui: `doc_type` único, `prefix`, `yearly`, `digits`); `identity.tenant_settings` (ganha as chaves acima); `shared.number_sequences` e `shared.attachments` (existentes).

## Telas e estados
- Abas de Configurações na ordem: **Empresa, Numeração, Parâmetros, Segurança, IA** (e Importação, da MIG-01a, por último).
- **Empresa** (`/app/configuracoes/empresa`): página em seções Identificação (CNPJ só leitura), Endereço, Contato e Logo (prévia, chip "Aguardando verificação", "Trocar logo").
- **Numeração** (`/app/configuracoes/numeracao`): tabela com Tipo, Prefixo, Reinicia por ano, Dígitos, Último emitido e Próximo (prévia ao vivo); edição em painel lateral com "Salvar numeração" e confirmação; `<Dica>` em "Próximo número": "Use para continuar a numeração do sistema antigo. Só pode aumentar."
- **Parâmetros** (`/app/configuracoes/parametros`): "% de encargos estimados" (`PercentInput`, `<Dica>` com a fórmula) e "Modo de precificação" (`ToggleGroup`), cada um com o histórico.
- **Verbos:** "Salvar dados da empresa", "Trocar logo", "Salvar numeração", "Salvar % de encargos", "Salvar modo de precificação"; o diálogo repete o verbo e sai com "Voltar".
- **Estados:** carregando; erro com "Tentar de novo"; vazio só na numeração sem documentos ("Nenhum documento emitido ainda. O primeiro sairá como {prévia}."); modo leitura sem `settings.edit`.
- **Propositivo:** dados do catálogo pré-preenchidos; CEP preenche o endereço; prévia do próximo número ao vivo; depois de salvar o % de encargos, a faixa oferece "Ver cargos" (link para a CAD-04a, quando existir).
- **400 px e teclado:** um CA de E2E para a aba Numeração.
- **Ajuda:** `Ajuda/configuracoes-empresa.md` (aba `empresa`), `Ajuda/configuracoes-numeracao.md` (aba `numeracao`), `Ajuda/configuracoes-parametros.md` (aba `parametros`).

## Plano de testes
- **Unidade:** formato efetivo = padrão + exceção (CA 10); "próximo número só sobe" (CA 11); reinício anual bloqueado (CA 12); regras do prefixo e dígitos (CA 13); faixa do % (CA 17).
- **Integração (Testcontainers):** perfil inicial a partir do catálogo e fonte da verdade (CA 1, 4); salvar e auditar (CA 2); CNPJ recusado no corpo (CA 3); logo válido, SVG, HTML disfarçado, acima de 1 MB e de 1024 px, ClamAV falso infectado (CA 5, 6); rota pública com cabeçalhos, 304, 429 e sem expor outro anexo (CA 7); registro do dono (CA 8); próximo número em ano sem linha (CA 11); 50 numerações paralelas (CA 14); seed idempotente em tenant novo e existente (CA 15); evento no outbox na mesma transação (CA 16); `ITenantParameters` (CA 19); três camadas (CA 20, 21).
- **Arquitetura:** HR e Commercial só leem parâmetros via `Identity.Contracts` (CA 19); todo endpoint com `RequireModule` e `perm:`.
- **E2E (Playwright):** trocar o logo e ver no login; mudar o prefixo, ver a confirmação e a prévia; aba Numeração a 400 px e só pelo teclado; usuário com `settings.view` vê tudo em modo leitura.

## Fora do escopo
- Garantia padrão por tipo de item: CAD-02a (Inventory, ADR 0016).
- Regra detalhada de precificação e % padrão de markup ou margem: COM-01.
- Cadastro fiscal do tenant (certificado, série): FIS-02.
- Alteração do CNPJ e dos dados do catálogo pelo super admin (FND-03b).
- Recálculo do custo-hora: CAD-04a.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1 e ADR 0016): perfil do banco do tenant como fonte da verdade, login com o nome fantasia, CNPJ só leitura alterado pelo super admin; rota pública própria do logo; logo PNG/JPG/WebP até 1 MB e 1024 px; encargos com 80,00% e faixa de 0 a 200% com 2 casas; markup como modo padrão, % padrão na COM-01; garantia no Inventory; prefixo de 1 a 6 caracteres e 3 a 8 dígitos; nenhum tipo de documento novo.
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `settings`, `shared.number_formats`, não destrutiva) e seeds idempotentes.
- [ ] Permissões só do `ModuleCatalog` (`settings.view`, `settings.edit`).
- [ ] Telas com estados de carregando, vazio e erro; modo leitura.
- [ ] Ajuda das três abas escrita; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada (perfil, numeração, parâmetros).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `settings.company_profile`, endpoints do perfil e `LogoUrl`/nome fantasia no `tenant-context`.
- [ ] Logo pelo `AttachmentService` com `TenantLogo` no `AttachmentOwnerRegistry`; rota `GET /api/v1/public/tenant-logo` com cache e rate limit.
- [ ] `shared.number_formats`, `INumberFormatAdmin`, formato efetivo no `NumberSequenceService` e "próximo número" com `ON CONFLICT … GREATEST`.
- [ ] Parâmetros em `tenant_settings`, seeds, `ITenantParameters` em `Identity.Contracts`, evento `PayrollChargesChanged`.
- [ ] Abas Empresa, Numeração e Parâmetros na ordem nova; três arquivos de ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (rota pública do logo e upload), @qa.
- Gate: CA 6 e CA 7 (upload e rota pública), CA 11 e CA 12 (numeração) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** valores da tabela 1.1 nos CAs (logo, encargos, precificação, prefixo); fonte da verdade e CNPJ só leitura; rota do logo conforme a ADR 0016; correções da numeração ("próximo número" em ano sem linha, reinício anual bloqueado, confirmação de "Salvar numeração"); garantia e enum `ItemType` levados para a CAD-02a; `BrazilianDocuments` levado para a CAD-01a; ordem das abas; seeds também nos tenants existentes; ordem 6 do Marco B.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada a partir do backlog, do PRD e das pendências P-05, P-11 e P-14 | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, tabela 1.1) e ADR 0016; garantia para a CAD-02a, validador para a CAD-01a; ordem 6 do Marco B | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
