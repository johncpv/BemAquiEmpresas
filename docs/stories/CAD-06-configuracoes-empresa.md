# CAD-06 — Configurações da empresa

| Campo | Valor |
|---|---|
| ID | CAD-06 |
| Título | Dados da empresa, logo, numeração editável por tipo de documento e parâmetros (% de encargos, garantia padrão, modo de precificação) |
| Tamanho | M (re-estimada: o backlog dizia P; logo com anexo e ClamAV, numeração editável com concorrência e três parâmetros com histórico) |
| Status | Draft |
| Requisitos | R-COM-4 · R-COM-3 (logo como anexo) · R-RH-2 (% de encargos do custo-hora, P-05) · R-PRJ-7 (garantia padrão, P-11) · R-COML-1 (modo de precificação, P-14) · R-SEC-5 · R-AJD-1 |
| Dependências | Fase 0 concluída: FND-06c (`settings.view`/`settings.edit`, Configurações → Segurança), FND-07 (numeração, anexos, auditoria, outbox), FND-09c (formulário e histórico), FND-14 (ajuda) |
| Bloqueia | CAD-04a (% de encargos), CAD-02a (garantia padrão por tipo de item), CAD-01a (validador de CNPJ), COM-01 (modo de precificação), COM-03 (logo no PDF), toda story que numera documento |
| Ordem de execução | 1 |

## User story
Como **proprietário**, quero **cadastrar os dados e o logo da empresa, ajustar a numeração dos documentos e os parâmetros de cálculo (encargos, garantia e precificação)**, para **que documentos, custos e prazos saiam com o padrão da minha empresa sem eu repetir isso em cada tela**.

## Contexto e referências
- PRD R-COM-4 (numeração sequencial configurável por tipo de documento), R-PLT-3 (seeds no provisionamento), R-RH-2 (custo-hora = salário + encargos estimados).
- `docs/04-fluxos-estados.md` §3.2 (fórmula do custo-hora com `%encargos` configurável por tenant) e §4 (formatos padrão de numeração).
- `docs/06-pendencias-riscos.md`: **P-05** (encargos: parâmetro editável em Configurações, com valor inicial sugerido e histórico de alteração), **P-11** (garantia: meses padrão por tipo de item, sobrescrevível no item; `elev_garantia` do Dolibarr entra na migração), **P-14** (precificação: os dois modos, escolhidos em Configurações; regra detalhada antes da COM-01).
- ADR 0010 item 2 (`settings` guarda encargos, garantia, markup ou margem, IA, 2FA e numeração), ADR 0014 (Configurações pertencem ao módulo Identity, como `security.require_2fa`), ADR 0008 (`settings.edit` só o Proprietário concede), ADR 0009 (rotas anônimas: `GET /api/v1/public/tenant-context` devolve nome e logo; allowlist de anexos sem SVG).
- Código existente a reaproveitar: `NumberFormats.Defaults` e `INumberSequence` (FND-07, comentário "editáveis por tenant em Configurações, CAD-06"), `identity.tenant_settings` e `SecuritySettings`, `AttachmentService`/`AttachmentPolicy` (ClamAV, magic bytes), `SettingsTabs` (abas por rota), `RecordHistory` (FND-09c), `TenantPublicContext(Name, LogoUrl, Status)` hoje com `LogoUrl: null`.
- Protótipo: não tem tela de Configurações → Empresa; seguir o padrão das abas já existentes (Segurança, Inteligência artificial).

## Critérios de aceite
**Dados da empresa**
1. **Dado** um usuário com `settings.edit` em Configurações → Empresa, **quando** preenche razão social, nome fantasia, CNPJ, inscrição estadual, inscrição municipal, endereço (CEP, logradouro, número, complemento, bairro, cidade, UF), telefone e e-mail e clica "Salvar dados", **então** os dados são gravados, auditados (antes/depois) e a confirmação diz "Dados salvos".
2. **Dado** um CNPJ com dígito verificador errado, **quando** salvo, **então** a API responde 400 com "CNPJ inválido." no campo; CNPJ com ou sem máscara é aceito e gravado só com os caracteres significativos. O validador aceita o CNPJ alfanumérico (IN RFB 2.229/2024) e é o mesmo usado pela CAD-01a.
3. **Dado** um tenant recém-provisionado, **quando** abro Configurações → Empresa pela primeira vez, **então** razão social, nome fantasia e CNPJ vêm preenchidos com os dados do catálogo (propositivo, R-PRP-3), e só falta confirmar.

**Logo**
4. **Dado** um arquivo PNG, JPG ou WebP dentro do limite de tamanho, **quando** envio em "Trocar logo", **então** ele passa pela allowlist e pelos magic bytes (FND-07) e fica "Aguardando verificação" até o ClamAV marcar `Clean`; só então aparece no topo do sistema e no `GET /api/v1/public/tenant-context` (`LogoUrl`).
5. **[Segurança]** **Dado** um SVG, um HTML renomeado para `.png`, um arquivo acima do limite ou um arquivo infectado, **quando** envio, **então** o upload é recusado (ou, no infectado, nunca publicado) e a mensagem diz o motivo em PT-BR; o logo anterior continua valendo.
6. **[Segurança]** **Dado** um visitante sem login, **quando** pede o logo pela rota pública definida no ponto em aberto 2, **então** recebe só o logo atual do tenant do host, com `Content-Type` da allowlist e `X-Content-Type-Options: nosniff`; nenhum outro anexo do tenant é servido sem login.

**Numeração**
7. **Dado** `settings.view` em Configurações → Numeração, **quando** abro a aba, **então** vejo cada tipo de documento de `04-fluxos` §4 com prefixo, reinício anual (sim/não), quantidade de dígitos, último número emitido e a prévia do próximo (ex.: "ORC-2026-0001").
8. **Dado** `settings.edit`, **quando** troco o prefixo ou a quantidade de dígitos de um tipo e clico "Salvar numeração", **então** o próximo documento daquele tipo usa o formato novo, a sequência continua do último valor (não reinicia) e os números já emitidos não mudam.
9. **Dado** `settings.edit`, **quando** informo um "próximo número" menor ou igual ao último emitido, **então** a API responde 409 com "O próximo número precisa ser maior que o último emitido ({último})."; quando informo um maior, a sequência passa a partir dele (uso típico: continuar a numeração do Dolibarr).
10. **Dado** um prefixo vazio, com caractere fora de `A–Z` e `0–9`, ou já usado por outro tipo, **quando** salvo, **então** a API responde 400 com a mensagem do campo (regras de tamanho no ponto em aberto 8).
11. **Dado** uma troca de formato salva enquanto 50 documentos do mesmo tipo são numerados em paralelo, **quando** todos terminam, **então** não há número repetido (reaproveita o teste de concorrência da FND-07).

**Parâmetros**
12. **Dado** `settings.edit` em Configurações → Parâmetros, **quando** altero "% de encargos estimados" e confirmo "Salvar parâmetro", **então** o valor é gravado, aparece no histórico do parâmetro (valor anterior, novo, quem e quando, via `RecordHistory`) e o evento `PayrollChargesChanged` é publicado no outbox na mesma transação.
13. **Dado** um tenant novo, **quando** é provisionado, **então** "% de encargos estimados" nasce com o valor inicial sugerido (seed idempotente; valor no ponto em aberto 4), com a `<Dica>` "Valor sugerido. Confirme com a contadora."
14. **Dado** `settings.edit`, **quando** informo os meses de garantia padrão por tipo de item (`Finished`, `Component`, `Service`…) e salvo, **então** o valor é gravado e a CAD-02a passa a usá-lo como padrão do item novo do tipo. A lista de tipos é o enum `ItemType` de `Inventory.Contracts`, criado nesta story (a CAD-02a vem depois), para o Identity não depender do domínio do Inventory.
15. **Dado** `settings.edit`, **quando** escolho o modo de precificação "Markup sobre o custo" ou "Margem sobre o preço" (P-14) e salvo, **então** o modo é gravado e exposto para a COM-01 pelo contrato de parâmetros.
16. **Dado** um valor fora da faixa (encargos negativos ou acima do teto, meses de garantia negativos), **quando** salvo, **então** a API responde 400 com a mensagem do campo em PT-BR.

**Leitura por outros módulos**
17. **Dado** o HR calculando custo-hora (CAD-04a), **quando** lê o % de encargos, **então** usa `ITenantParameters` de `Identity.Contracts`, sem acessar `identity.tenant_settings` e sem exigir `settings.view` do usuário; o teste de arquitetura garante a referência só via Contracts.

**Permissões (3 camadas)**
18. **Dado** um usuário sem `settings.view`, **quando** chama qualquer endpoint desta story, **então** recebe 403 `forbidden` e não vê Configurações no menu; com `settings.view` e sem `settings.edit`, as abas abrem em modo leitura (botões ocultos por `<Can>`) e todo `PUT`/`POST` responde 403 `forbidden`.
19. **Dado** o módulo `settings` não liberado para o tenant, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`.

## Regras de negócio e técnicas
- **Dono:** módulo Identity, dono de Configurações (ADR 0014). Endpoints em `/api/v1/settings/empresa`, `/api/v1/settings/numeracao`, `/api/v1/settings/parametros`, com `RequireModule("settings")` e `RequirePermission("settings.view"|"settings.edit")`.
- **Parâmetros** em `identity.tenant_settings` (chave única, auditada), chaves propostas: `payroll.charges_pct`, `warranty.months.<ItemType>`, `pricing.mode` (`markup` | `margin`).
- **% de encargos:** percentual com 2 casas (`numeric(5,2)`, proposta), exibido como "80,00%". Mudar o parâmetro não altera apontamentos já feitos (o snapshot do custo-hora é gravado no apontamento, `04-fluxos` §3.2).
- **Numeração:** o formato padrão continua em `NumberFormats.Defaults`; o tenant grava só o que mudou. `INumberSequence.NextAsync` passa a ler o formato efetivo (padrão + exceção do tenant) na mesma transação. "Próximo número" só sobe: atualiza `shared.number_sequences.last_value` com `UPDATE … WHERE last_value < novo − 1`, nunca para baixo. A troca de formato vale do próximo documento em diante.
- **Logo:** anexo de `owner_type = "TenantLogo"`, `owner_id` = id do perfil da empresa, registrado no `AttachmentOwnerRegistry` com o módulo `settings`. Só PNG, JPG e WebP (o SVG já é recusado pela `AttachmentPolicy`). Trocar o logo mantém o anterior até o novo ficar `Clean`.
- **CNPJ:** validador em `SharedKernel` (proposta: `BrazilianDocuments.IsValidCnpj/IsValidCpf`), com dígitos verificadores e o CNPJ alfanumérico; reutilizado pela CAD-01a, CAD-04a e CAD-04b.
- **Datas:** `updated_at` em UTC (`timestamptz`), exibido em America/Sao_Paulo.
- **Ids:** Guid v7 (`Entity`).
- **Auditoria:** perfil da empresa, formatos de numeração e parâmetros são `IAuditable`. E-mail e telefone da empresa não são dado pessoal (PJ); não levam `[PersonalData]`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `settings.view`, `settings.edit`. Nenhuma ação nova.
- **Eventos (outbox):** `PayrollChargesChanged` (`{ tenantId, oldPct, newPct, changedAt }`), consumido pela CAD-04a. **Proposta**, não está no `04-fluxos` §2.
- **Tabelas:**
  - `identity.tenant_settings`: existe (FND-05); ganha as chaves acima.
  - `identity.company_profile` (**proposta**, não está no 03): `legal_name`, `trade_name`, `document`, `state_registration`, `municipal_registration`, `zip`, `street`, `number`, `complement`, `district`, `city`, `city_ibge`, `state`, `phone`, `email`, `logo_attachment_id`, `updated_at`, `updated_by`; uma linha por tenant.
  - `shared.number_formats` (**proposta**, não está no 03): `doc_type` (único), `prefix`, `yearly`, `digits`, `updated_at`, `updated_by`; só as exceções ao padrão. Fica em `shared` porque o `INumberSequence` é building block.
  - `shared.number_sequences` e `shared.attachments`: existem (FND-07).

## Telas e estados
- **Configurações → Empresa** (`/app/configuracoes/empresa`): formulário RHF + Zod em blocos "Identificação", "Endereço", "Contato" e "Logo" (prévia do logo atual, chip "Aguardando verificação" quando houver, botão "Trocar logo").
- **Configurações → Numeração** (`/app/configuracoes/numeracao`): tabela com Tipo de documento, Prefixo, Reinicia por ano, Dígitos, Último emitido e Próximo (prévia ao vivo enquanto edita). Edição na linha com "Salvar numeração". `<Dica>` em "Próximo número": "Use para continuar a numeração do sistema antigo. Só pode aumentar."
- **Configurações → Parâmetros** (`/app/configuracoes/parametros`): "% de encargos estimados" com `<Dica>` da fórmula do custo-hora, "Garantia padrão (meses) por tipo de item" e "Modo de precificação" (ToggleGroup). Cada bloco com o histórico de alterações (`RecordHistory`).
- As três abas entram no `SettingsTabs` e nas `tabs` de `settings` em `navigation/modules.ts`, depois de Segurança e Inteligência artificial.
- **Estados:** carregando (skeleton do formulário); erro com "Tentar de novo"; vazio só na numeração sem documentos emitidos ("Nenhum documento emitido ainda. O primeiro sairá como {prévia}."). Modo leitura sem `settings.edit`.
- **Propositivo:** dados do catálogo pré-preenchidos (CA 3); prévia do próximo número ao vivo; depois de salvar o % de encargos, oferece "Ver cargos recalculados" (link para a CAD-04a, quando existir).
- **Ajuda:** `Ajuda/configuracoes-empresa.md` (aba `empresa`), `Ajuda/configuracoes-numeracao.md` (aba `numeracao`), `Ajuda/configuracoes-parametros.md` (aba `parametros`).

## Plano de testes
- **Unidade:** validador de CNPJ (numérico e alfanumérico, com e sem máscara) (CA 2); formato efetivo = padrão + exceção (CA 8); regra do "próximo número só sobe" (CA 9); validação de prefixo (CA 10); faixas dos parâmetros (CA 16).
- **Integração (Testcontainers):** salvar e auditar dados da empresa (CA 1, 3); logo com tipo válido, SVG, HTML disfarçado, arquivo grande e ClamAV falso marcando infectado (CA 4, 5); rota pública do logo sem expor outro anexo (CA 6); troca de formato com 50 numerações paralelas sem duplicidade (CA 11); evento `PayrollChargesChanged` no outbox na mesma transação (CA 12); seed idempotente do % de encargos (CA 13); garantia e modo de precificação lidos por `ITenantParameters` (CA 14, 15, 17); três camadas de permissão (CA 18, 19).
- **Arquitetura:** HR e Commercial só leem parâmetros via `Identity.Contracts` (CA 17); `RequirePermission` só com códigos do catálogo (teste existente).
- **E2E (Playwright):** trocar o logo e ver no login; mudar o prefixo e ver a prévia; usuário com `settings.view` vê a tela em modo leitura.

## Fora do escopo
- Regra detalhada de precificação (markup × margem, arredondamento, % padrão): COM-01, depois do aprofundamento da P-14.
- Cadastro fiscal do tenant (certificado A1, regime, série da NF-e): FIS-02.
- Edição de dados do catálogo pelo super admin (FND-03b).
- Recálculo do custo-hora dos cargos: CAD-04a (esta story só publica o evento).

## Pontos em aberto
1. **Fonte da verdade de razão social, nome fantasia e CNPJ** (pré-condição): hoje estão em `catalog.tenants` (super admin) e o login usa o nome do catálogo. Proposta: o tenant edita o perfil no banco dele; o login passa a usar o nome fantasia do perfil quando existir; o CNPJ do catálogo vira somente leitura no tenant. Precisa de decisão do PO.
2. **Rota anônima do logo** (pré-condição): a ADR 0009 permite o `tenant-context` com "nome e logo", mas servir o arquivo exige uma rota pública (`GET /api/v1/public/tenant-logo`, proposta) ou o logo em data URI dentro do `tenant-context`. Registrar em ADR antes de implementar.
3. **Limite do logo:** sugestão de 1 MB e lado máximo de 1024 px.
4. **Valor inicial sugerido do % de encargos** (pré-condição do seed): a P-05 fala em "padrão sugerido pela contadora", sem número. Sem o valor, a proposta é nascer vazio e exigir o preenchimento antes do primeiro cargo (CAD-04a).
5. **Faixa válida do % de encargos** (sugestão: 0 a 200%, 2 casas).
6. **Modo de precificação padrão** e se o % padrão de markup ou margem entra nesta tela ou na COM-01 (P-14).
7. **Garantia:** quais tipos de item têm garantia e os meses padrão de cada um (P-11).
8. **Regras do prefixo:** tamanho (sugestão 1 a 6), se dois tipos podem compartilhar prefixo (proposta: não), faixa de dígitos (sugestão 3 a 8).
9. **Tipos de documento da Fase 1:** código de item, movimento de estoque ("Saldo inicial", MIG-01d) e matrícula de funcionário não estão em `04-fluxos` §4. Definir se entram na numeração (sugestão: `MOV-AAAA-00001` para movimento; código de item e matrícula opcionais).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migrations criadas (não destrutivas).
- [ ] Permissões usadas só do `ModuleCatalog` (`settings.view`, `settings.edit`).
- [ ] Telas com estados de carregando, vazio e erro; modo leitura sem `settings.edit`.
- [ ] Ajuda das três abas escrita; teste de cobertura de ajuda passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada (perfil, numeração, parâmetros).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado com as tabelas aceitas; ADR do logo público registrada; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Validador `BrazilianDocuments` (CPF, CNPJ numérico e alfanumérico) no `SharedKernel`, com testes.
- [ ] Entidade e endpoints do perfil da empresa; logo pelo `AttachmentService` com `owner_type = TenantLogo`.
- [ ] Rota pública do logo (depois da ADR) e `LogoUrl` no `tenant-context`.
- [ ] `shared.number_formats` e leitura do formato efetivo no `NumberSequenceService`; endpoint de "próximo número".
- [ ] Parâmetros em `tenant_settings`, seed do % de encargos, `ITenantParameters` em `Identity.Contracts`, evento `PayrollChargesChanged`.
- [ ] Abas Empresa, Numeração e Parâmetros; três arquivos de ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (rota pública do logo e upload), @qa.
- Gate: CA 5 e CA 6 (upload e rota pública) obrigatórios.

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

- **Correções feitas na validação:** CA 14: a lista de tipos de item vem do enum `ItemType` em `Inventory.Contracts`, criado aqui, porque a CAD-02a vem depois.
- **Pré-condições para `Ready`:** Pontos em aberto 1 (fonte da verdade dos dados da empresa), 2 (ADR da rota pública do logo) e 4 (valor inicial do % de encargos).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada a partir do backlog, do PRD e das pendências P-05, P-11 e P-14 | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
