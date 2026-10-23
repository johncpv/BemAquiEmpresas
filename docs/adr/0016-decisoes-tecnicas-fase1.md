# ADR 0016: Decisões técnicas da Fase 1

- Status: Aceito (18/10/2026). O PO (John) aprovou as recomendações da revisão pré-Fase 1 (D1 a D11, com a exceção do escopo da migração registrada na ADR 0017).
- Contexto: a revisão `docs/revisoes/2026-10-18-revisao-pre-fase1.md` (§2.1 e §2.3) apontou decisões técnicas que as stories da Fase 1 deixavam em aberto: rota do logo, falhas da numeração, permissões padrão nos tenants já provisionados, leitura de dado sensível, dono das condições de pagamento, exclusão em estoque e produção e alguns ajustes de arquitetura.
- Complementa as ADRs 0006, 0008, 0009 e 0010.

## Decisão

### Rota anônima do logo (adendo à lista de rotas anônimas da ADR 0009)
- `GET /api/v1/public/tenant-logo`, sem login.
  - O tenant vem do host da requisição, sem parâmetro de id.
  - Só é servido o logo atual com `scan_status = Clean`. Nenhum outro anexo sai por essa rota.
  - Cabeçalhos: `Content-Type` da allowlist (PNG, JPG ou WebP), `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'` e `ETag`.
  - Cache por host e rate limit próprio.
- O `tenant-context` devolve só a URL do logo, nunca a imagem em data URI.
- O logo aceita PNG, JPG e WebP de até 1 MB e até 1024 px no maior lado.

### Numeração
- **"Próximo número":** passa a ser `INSERT … ON CONFLICT (doc_type, year) DO UPDATE SET last_value = GREATEST(number_sequences.last_value, EXCLUDED.last_value)`. Hoje, quando não existe linha no ano, o `UPDATE` não grava nada.
- **Reinício anual:** a troca de "reinicia por ano" fica bloqueada depois do primeiro número emitido do tipo, porque ela troca a chave da sequência e a reinicia.
- **Unicidade:** toda tabela de documento numerado tem índice único em `number`.
- **Gravação dos formatos:** pelo `INumberFormatAdmin` (BuildingBlocks), em `shared.number_formats`, só com as exceções ao padrão de `04-fluxos` §4.
- **Regras do prefixo:** de 1 a 6 caracteres (A–Z, 0–9), único entre os tipos; de 3 a 8 dígitos. Nenhum tipo de documento novo na Fase 1: o código do item é digitado (mantém a `ref` do Dolibarr) e a matrícula do funcionário é texto opcional.

### Seed patch (adendo às ADRs 0008 e 0010)
- Tabela `identity.seed_patches` (`key` única, `applied_at`), versionada e idempotente.
- Um patch só **acrescenta** concessões (`group_modules` e `group_permissions`) aos grupos que têm `seed_key`. Nunca remove nem altera o que o tenant mudou.
- Ao aplicar, incrementa o `perm_version` dos membros afetados, para o cache de permissões recarregar.
- É aplicado pelo sistema no deploy (migrator), fora da regra de delegação da ADR 0008, e auditado com o usuário de sistema.
- **Seeds em geral:** todo seed de cadastro roda também nos tenants existentes no próximo deploy. A busca por `seed_key` usa `IgnoreQueryFilters()`, para não recriar o registro que o tenant excluiu.

### Dados sensíveis por rota e registro de leitura
- O detalhe de uma entidade com dado sensível tem duas rotas:
  - `GET /{id}` devolve o resumo, sem nenhum campo sensível, com a permissão de `view` do módulo;
  - `GET /{id}/sensitive` devolve os campos sensíveis e exige a ação específica (ex.: `hr.view_sensitive`).
- A leitura de `/sensitive` é registrada como `action = "SensitiveRead"` por `IAuditWriter.RecordReadAsync`, na mesma requisição. Só o Proprietário vê esses registros.
- Um teste de arquitetura impede propriedade sensível no DTO de resumo e nos DTOs de lista.
- A escrita também é separada: dados gerais, dados pessoais e troca de salário são comandos diferentes, desserializados com `UnmappedMemberHandling.Disallow`. Um `PUT` sem os campos sensíveis não pode anulá-los.
- Ordenar ou filtrar por campo sensível sem a permissão é ignorado.
- Resposta com dado pessoal leva `Cache-Control: no-store`.

### Auditoria e log sem dado pessoal
- `AuditableEntityRegistry` passa a usar o nome qualificado do tipo e lança exceção em duplicata. Hoje, `Address` do HR e do Parties colidiriam, e o histórico do HR ficaria visível com `parties.view`.
- Histórico (auditoria) de entidade do HR exige `hr.view_sensitive`.
- Teste de arquitetura com allowlist: toda propriedade de entidade auditável do HR, e de PF e contato no Parties, é `[PersonalData]` ou está na lista de campos não sensíveis.
- Teste de canário: grava valores conhecidos e lê o JSON bruto de `audit.audit_log` e o sink de log, que não podem contê-los.
- `SensitivePropertyMask` ganha `cpf`, `document`, `rg`, `pis`, `email`, `phone`, `salary`, `pix`, `bank` e `birth`. O Serilog respeita `[PersonalData]`. `Include Error Detail` continua desligado.

### Anexos
- `AttachmentOwnerRegistry` por tipo de dono declara: módulo, ação de leitura, ação de escrita, tipos aceitos, tamanho máximo e verificação de que o dono existe.
- O logo (`TenantLogo`) aceita só imagem de até 1 MB.

### Data de corte e `OpeningBalance`
- A importação da Fase 1 não traz saldos (ADR 0017). Quando houver saldo de partida digitado no sistema, vale a posição no **fim** do dia D: o movimento de abertura entra em D 23:59:59 America/Sao_Paulo, gravado em UTC.
- **`OpeningBalance` ("Saldo inicial")** é um tipo próprio de movimento de estoque, e não `Adjustment`. Entra no `03-modelo-dados.md` com a EST-01, para o saldo inicial digitado no sistema.

### Dono das condições de pagamento
- O Finance é o dono: tabela `finance.payment_terms`, exposta por `IPaymentTerms` em `Finance.Contracts` (lista, validação de "ativa" e `Split`).
- O Parties guarda só o id (`parties.parties.default_payment_term_id`).
- O uso inverso ("a condição está em uso?") é consultado pelo Finance por `IPaymentTermUsageProvider`, implementado pelos módulos que guardam o id (o Parties primeiro).
- Corrige o `03-modelo-dados.md`, que punha `payment_terms` em `parties`.

### Excluir × inativar (D10)
- Padrão único em todos os cadastros: "Excluir" (exclusão lógica) quando o registro não tem uso; "Inativar" quando tem. O servidor recusa a exclusão com uso (409) e o diálogo oferece "Inativar".
- A ação `delete` entra no `ModuleCatalog` de `inventory` e de `production` (na CAD-03, que é a primeira story desses módulos). Nenhum grupo do seed recebe `delete`; fica com o Proprietário.

### Expurgo controlado
- A `audit.audit_log` continua imutável. Se um dado pessoal chegar a ela por engano, a correção é uma migration específica, revisada, que reescreve só o campo afetado, desligando e religando o trigger dentro da mesma transação, e registra o expurgo num log próprio (`audit.purge_log`: quando, qual migration, quantas linhas, motivo).
- Esse é o único caminho de alteração da auditoria. Ver ADR 0018.

### Ajustes de arquitetura
- **Posse de tabela:** um teste confere, no `TenantDbContext.Model`, se cada assembly mapeia só tabelas do próprio schema. Outro varre o SQL cru dos módulos procurando referência a schema alheio.
- **Garantia padrão:** fica no Inventory (`inventory.warranty_defaults`), e não no Identity. O enum `ItemType` nasce na CAD-02a, em `Inventory.Contracts`. O `ITenantParameters` do Identity fica só com o % de encargos e o modo de precificação.
- **Perfil da empresa:** tabela `settings.company_profile`. O schema `settings` pertence ao módulo Identity, dono de Configurações (ADR 0014).
- **Capacidade:** `IWorkCenterCapacity` recebe o id do centro. Mudança de calendário não publica evento: o planejamento (PLN-01) lê o calendário por chamada síncrona quando recalcula.
- **Terceiros:** `IParties` resolve terceiro excluído pelo id (para documentos antigos mostrarem o nome). A busca usa `unaccent` e `pg_trgm`.
- **Contas bancárias:** sem `credentials_secret` na Fase 1; a coluna entra na BNK-01.
- **IDOR pai/filho:** toda rota `/{paiId}/filhos/{id}` confere o vínculo; id de outro módulo é validado pelo Contracts do dono.
- **Endpoints:** teste de arquitetura garante que todo endpoint de módulo tem `RequireModule` e uma policy `perm:`.
- **`ModuleCatalog.Phase`:** é só informativo (ordem de entrega). A liberação vale pelo `tenant_modules`; o plano completo da Konebras libera todos os módulos (ADR 0010 item 4).
- **Migrations:** uma por story, regerada no rebase e nunca editada à mão, para evitar conflito no `ModelSnapshot` único.

## Consequências
- A CAD-06 implementa a rota do logo, a correção da numeração e o `INumberFormatAdmin`.
- A CAD-01a implementa o seed patch (permissões de `parties`), o registro qualificado da auditoria, o `BrazilianDocuments` e os testes de auditoria e log sem dado pessoal.
- A CAD-04a (primeira story do HR) implementa a rota `/sensitive`, o `SensitiveRead` e os comandos separados para salário; a CAD-04b e a CAD-04c reaproveitam.
- A CAD-05b cria `finance.payment_terms` e o campo de condição padrão no terceiro.
- A CAD-03 inclui `delete` em `inventory` e `production`.

## Adendo (20/10/2026, revisão pré-Fase 2, M6 e M7)

### Dono dos parâmetros (M6)
- **Cada módulo é dono dos próprios parâmetros.** Os parâmetros novos ficam numa tabela do schema do módulo (`<schema>.parameters`: `key` única, `value`, `updated_at`, `updated_by`; auditada) e são lidos pelo contrato do módulo (`IFinanceParameters` em `Finance.Contracts`, `ICommercialParameters` em `Commercial.Contracts`).
- **Configurações só monta a tela.** Configurações → Parâmetros mostra uma seção por módulo, cada uma com o endpoint e a permissão de edição do módulo dono (`finance.edit` não basta: a edição de parâmetro exige `settings.edit`, conferida pelo endpoint do dono).
- Os parâmetros que já existem em `identity.tenant_settings` (`security.require_2fa`, `payroll.charges_pct`, `pricing.mode`) ficam onde estão. Mudá-los de lugar exige migration com ADR.
- Parâmetros da Fase 2 e donos:

| Chave | Dono | Story |
|---|---|---|
| `pricing.default_pct`, `quotes.validity_days` | Commercial | COM-01a, COM-01b |
| `finance.discount_approval_pct`, `finance.discount_approval_amount`, `finance.cancel_approval_amount`, `finance.backdate_days` | Finance | FIN-01 |
| `finance.cutover_date`, `finance.cutover_completed_at` | Finance | FIN-00 |
| `finance.owner_can_approve_own`, `finance.payment_tolerance` | Finance | FIN-03a |
| `finance.transfer_notify_amount` | Finance | FIN-04 |
| `finance.late_fine_pct`, `finance.late_interest_month_pct` (opcionais, sem seed) | Finance | FIN-01 (só a chave; a sugestão do valor fica para a BNK-01) |

### Numeração com revisão (M7)
- **Exceção à regra "índice único em `number`"** para o orçamento, que mantém o número em todas as revisões:
  - índice único em `(number, revision)`;
  - índice único parcial em `(number) WHERE is_current`.
- Os dois índices e a coluna `is_current` nascem certos na **primeira migration de `commercial.quotes`** (COM-01a), para a COM-02 não precisar trocar índice.
- O número continua vindo do `INumberSequence` (tipo `ORC`) só na revisão 0. As revisões seguintes copiam o número.
- Nenhum outro documento tem essa exceção.

- Ver ADR 0019 (decisões técnicas da Fase 2).

## Adendo (22/10/2026, decisão do PO, CAD-04c1b): dado de saúde dentro do dado sensível do RH
- **Dado de saúde é uma camada dentro do dado sensível do RH.** O tipo **Doença** e o tipo **Acidente de trabalho** dos afastamentos, com o motivo e o tipo anterior do rastro, exigem a ação específica **`hr.view_health`** (ADR 0003, adendo de 21/10/2026), além da `hr.view_sensitive` que abre a rota `/leaves/sensitive`. Uma não implica a outra.
- **Leitura:** sem `hr.view_health`, a rota devolve o afastamento de saúde (agora ou numa versão anterior) com `healthProtected = true`, sem tipo nem motivo, só com as datas; a tela mostra "Afastado". Os demais tipos seguem com `hr.view_sensitive`. A leitura continua registrada como `SensitiveRead`.
- **Escrita:** registrar ou editar de ou para Doença ou Acidente de trabalho exige `hr.view_health` (403 `forbidden`); encerrar antes e cancelar mexem só nas datas e seguem com `hr.edit` + `hr.view_sensitive`.
- **Auditoria e log:** nada muda: tipo, motivo e fim previsto continuam `[PersonalData]` (mascarados no diff), fora do log e dos eventos. O rastro de mudança do afastamento (`LeaveChange`) segue a mesma allowlist.
- **Concessão e seed:** `hr.view_health` é reservada ao Proprietário para conceder (ADR 0008, adendo de 21/10/2026). O seed patch `hr-view-health` a dá ao grupo RH do seed (o Proprietário tem tudo). A RH-01b usa a mesma ação para o atestado, sem seed patch novo.
