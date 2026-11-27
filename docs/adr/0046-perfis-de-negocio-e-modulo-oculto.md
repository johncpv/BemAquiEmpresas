# ADR 0046: Perfis de negócio, liberação efetiva por plano e perfil e o estado "oculto"

- Status: Aceito (16/11/2026). Decisão do PO PP-03 (16/11/2026): módulo fora do perfil fica **oculto**; o cadeado continua só para "no perfil, fora do plano". Escrita na story [PERF-01](../pdv/stories/PERF-01-perfis-liberacao-efetiva.md).
- Contexto: o plano do PDV ([PRD do PDV, seção 3](../pdv/01-PRD.md)) leva o produto a comércio, serviços e e-commerce. Até aqui a camada 1 do controle de acesso era só "plano + exceção manual" (FND-06, ADR 0002 e ADR 0010), e todo módulo não liberado aparecia no menu com cadeado: uma padaria veria Engenharia e Produção trancadas. A Konebras (fábrica, em produção) não pode perceber nenhuma mudança.
- Muda: a regra do `CLAUDE.md` "o botão do módulo aparece no menu com cadeado" (ADR 0002) passa a valer só para o módulo **no perfil e fora do plano**. Complementa a ADR 0003 (permissões `modulo.acao`, sem ação nova aqui) e a ADR 0010 (catálogo de 16 módulos).

## Decisão

### 1. Catálogo de perfis em código
- `BusinessProfileCatalog` em `BuildingBlocks/Modules`, ao lado do `ModuleCatalog`. Perfis: `factory` (Fábrica), `retail` (Comércio), `services` (Prestação de serviços) e `ecommerce` (E-commerce). Segmentos: `counter`, `supermarket`, `bakery`, `restaurant`, `snack_bar`, `food_truck` (de `retail`) e `workshop` (de `services`).
- Cada perfil lista códigos do `ModuleCatalog`. O teste de arquitetura falha com o nome do perfil e do código quando um perfil cita módulo inexistente.
- `factory` lista **exatamente** os 16 módulos de hoje (ADR 0010). Módulo novo (`catalog`, `sales`, `pos` e os demais do PDV) só entra num perfil na story dele e **nunca** entra em `factory` sem decisão explícita: é assim que a Konebras não vê nada do PDV.

### 2. Liberação efetiva e os três estados
- `liberado = (no plano E em algum perfil do tenant) OU exceção manual`. Perfis combinam por **união**.
- `tenant_modules` continua sendo a **única** tabela lida pela camada 1 (`RequireModule`, policy de permissão, ajuda). O perfil não é uma quarta camada: é uma entrada do cálculo que o `TenantModuleService.ApplyPlanAsync` grava em `tenant_modules`, depois do plano, sem marcar exceção e sem tocar linha de exceção manual. Aplicar é idempotente.
- Exceção manual passa a ser "diferente do herdado (plano E perfil)". Liberar à mão um módulo fora do perfil é exceção e o módulo aparece normalmente.
- Estado de cada módulo em `GET /me/permissions` (campo `hidden`) e em `GET /modules`:

| Liberado | No perfil | Estado | Menu, busca e ajuda | Rota direta |
|---|---|---|---|---|
| sim | qualquer | liberado | aparece (se o grupo habilita) | abre |
| não | sim | cadeado | aparece com cadeado | `ModuleNotReleased` + 403 `module_not_released` |
| não | não | **oculto** | não aparece | `ModuleNotReleased` + 403 `module_not_released` |

- **Oculto é só apresentação.** Quem barra continua sendo a camada 1: a API responde 403 `module_not_released` e o front mostra a tela padrão "Funcionalidade não liberada nessa versão do sistema." quando a URL é aberta direto. A ajuda e o "Pergunte ao BemAqui" já filtram por módulo liberado.
- O tenant resolvido (`ResolvedTenant.HiddenModules`) carrega os ocultos; o cache invalida por NOTIFY quando `tenant_profiles` muda (trigger igual ao de `tenant_modules`). Os ocultos entram no hash da versão de permissões **só quando existem**: mudar o perfil recarrega o menu, e um tenant sem oculto (a Konebras) mantém a versão de antes.

### 3. `catalog.tenant_profiles`
- Colunas: `tenant_id`, `profile_code`, `segment` (nulo ou um segmento do perfil), `applied_at`, `applied_by` (nulo = sistema), `removed_at`, `removed_by`. Único por (`tenant_id`, `profile_code`, `segment`) entre os não removidos, com segmento nulo contando como valor (`NULLS NOT DISTINCT`).
- **Remover perfil nunca apaga dado:** marca `removed_at`, e os módulos que só ele trazia ficam ocultos (a exceção manual continua valendo). O último perfil ativo não sai: troca-se aplicando outro antes.
- **Tenant sem perfil nenhum = sem filtro** (comportamento de antes desta ADR: só plano e exceção, e o não liberado com cadeado). É uma rede de segurança: a migration dá `factory` a todo tenant existente e o provisionamento aplica `factory` até a PERF-02 trazer a escolha do perfil na tela.
- Aplicar e remover perfil são auditados no catálogo (`TenantProfile`, `ProfileApplied` e `ProfileRemoved`); o que o sistema faz leva a origem ("Sistema · migration PERF-01", "Sistema · perfil padrão do provisionamento").

### 4. Segmento
- Segmento **não altera a liberação**: só é lido pelos dados iniciais (PERF-04) e pelos padrões de tela. Se a alimentação (restaurante e KDS, Fase 15) precisar liberar módulo por segmento, isso vem como adendo a esta ADR.

### 5. Konebras
- A migration `TenantProfiles` do catálogo é só acréscimo: cria `tenant_profiles`, o trigger de NOTIFY e grava `factory` (sistema, auditado) para todo tenant sem perfil. **Não toca `tenant_modules`**: como `factory` lista todos os módulos de hoje, o herdado não muda.
- Teste de regressão (integração): retrato de `GET /me/permissions` de um usuário de cada grupo do seed, com uma exceção manual, antes e depois do backfill e de um recálculo; tem de ser idêntico, inclusive a versão.

### 6. Áreas do dock e vocabulário (PERF-03, PP-25)
- **Área "Loja"** (`ModuleAreas.Store`) entre "Comercial" e "Fábrica", para os módulos do PDV (Catálogo, Vendas, Caixa). Área sem módulo visível some do dock; o primeiro módulo dela é Vendas (`sales`, VND-01), e o perfil `factory` nunca recebe módulo dela sem decisão explícita.
- **Vocabulário em código** (`BusinessVocabulary`, ao lado do catálogo): dicionário chave → termo por perfil, com **teto de 30 chaves** (PP-25; o teste de unidade falha acima disso). Toda chave existe nos **termos padrão**, que são os do perfil `factory` (como o sistema fala hoje). O tenant não edita termo nesta versão. Vale para rótulo de interface e ajuda; nome de permissão, código de módulo, texto de documento fiscal e o campo `area` de `/me/permissions` (identificador) não mudam.
- **Combinação:** com dois perfis que definem a mesma chave, vale a **ordem fixa do catálogo**: `factory`, `retail`, `services`, `ecommerce` (não a ordem de aplicação). Como `factory` declara os termos padrão, tenant com `factory` (com ou sem outros perfis) vê sempre o padrão; tenant sem perfil (rede de segurança do §3) também.
- **Rótulo de área** sai do vocabulário (`area.factory`): "Fábrica" no padrão, "Retaguarda" em `retail`, `services` e `ecommerce`.
- **Onde chega:** `ResolvedTenant.Vocabulary` (recarrega pelo mesmo NOTIFY de `tenant_profiles`); `GET /me/permissions` ganha `areaLabels` (área → rótulo) e `vocabulary` (chave → termo), **só com o que difere do padrão** (a Konebras recebe os dois vazios). O vocabulário entra no hash da versão de permissões só quando não é vazio, como os ocultos. O `tenant-context` público não muda: o login não usa termo.
- **Ajuda:** o termo é marcado no `.md` como `{{chave|Padrão}}`; o `HelpAccess` entrega a ajuda já com o termo do tenant (painel, busca e Pergunte ao BemAqui), e o renderizador do front resolve o que vier sem resolver. Padrão em minúscula deixa o termo em minúscula. Chave desconhecida derruba a subida, como qualquer ajuda inválida.
- **Front:** `useTerm`, `<Term k="...">Padrão</Term>` e `useAreaLabel` (`navigation/vocabulary.tsx`); o dock casa módulo e área pelo texto do `ModuleAreas` (`key`) e mostra o rótulo.

## Consequências
- `CLAUDE.md`: a regra da camada 1 passa a dizer "no perfil e fora do plano: cadeado; fora do perfil: oculto".
- Próximas stories: PERF-02 (perfil no provisionamento e aba Perfis, com os endpoints da plataforma), PERF-04 (dados iniciais por perfil e segmento). A PERF-03 (§6) entregou a área "Loja" e o vocabulário.
- Termo novo entra no `BusinessVocabulary.Defaults` e no(s) perfil(is) na story que precisa dele, dentro do teto; a lista inicial (só `area.factory`) aguarda a validação do PO (PERF-03, pontos em aberto).
- Módulo novo entra no `ModuleCatalog` e no(s) perfil(is) dele na mesma story; o teste de arquitetura impede perfil com código inexistente, e o teste de unidade do `factory` impede que um módulo novo apareça para a Konebras sem querer.

## Adendo de 17/11/2026: recurso por perfil dentro de um módulo comum (PAG-01)
- Decisão do PO: as formas de pagamento da venda (PAG-01) ficam no Financeiro, que é de todos os perfis, mas a fábrica (Konebras) não pode ver nada delas.
- `BusinessProfileCatalog.Features` lista os recursos por perfil (`finance.payment_methods` → `retail`, `services`, `ecommerce`). Tenant sem nenhum desses perfis, inclusive o sem perfil, não tem o recurso. `factory` nunca entra num recurso sem decisão explícita.
- Servidor: `RequireProfileFeature(modulo, recurso)` no grupo de rotas responde 403 `module_not_released`, o mesmo código da camada 1. A ajuda declara `recurso:` no cabeçalho e some para quem não tem o recurso.
- Front: `GET /me/permissions` passa a trazer `profiles` (perfis ativos do tenant). A aba do módulo declara `profiles` e some do menu e da busca; a rota direta mostra a tela padrão `ModuleNotReleased` (`ProfileGate`). O papel de terceiro "Adquirente" segue a mesma regra nos formulários e no filtro de Terceiros.
- A versão de permissões só inclui os perfis quando há algum além de `factory`: a Konebras mantém a versão de antes.

## Adendo de 17/11/2026: pacotes de dados iniciais por perfil e segmento (PERF-04)
- **Cada módulo é dono do próprio pacote.** O módulo registra um `IProfileSeedPackage` (BuildingBlocks/Tenancy) com uma chave de `BusinessProfileSeeds` (BuildingBlocks/Modules); o pacote grava só nas tabelas do módulo e lê os outros pelos contratos (o Financeiro acha a adquirente "Maquininha" pelo `IParties`).
- **O perfil só diz quais pacotes rodam** (`BusinessProfileSeeds.ByProfile` e `BySegment`). `factory` não tem pacote nenhum, e nunca terá sem decisão explícita: a Konebras não ganha nem muda nada (teste de regressão com o retrato de todas as tabelas antes e depois do seed).
- **Quem roda:** o `ProfileSeedRunner`, um `ITenantSeeder` de ordem alta (depois dos seeders básicos), lê os perfis e os segmentos do tenant resolvido (`ResolvedTenant.Segments` é novo). Ele roda no provisionamento (etapa de dados iniciais, depois da etapa do perfil), a cada `migrate` e ao adicionar um perfil pela aba "Perfis" (`TenantProfileSeeding`). Chave sem implementação é pulada (módulo não carregado); o `catalog.retail` é do Catálogo (CAT-01), que reusa os próprios cadastros padrão.
- **Regras de todo pacote:** idempotente pela `seed_key`; o que o tenant excluiu não volta; nada que já existe é alterado (nome, conta, adquirente, conferência e permissões ficam como o tenant deixou); nome já usado por outro registro do tenant não é duplicado. Remover o perfil não apaga nada.
- **Recurso por perfil `dashboard.first_steps`** (adendo da PAG-01): o checklist "Primeiros passos" e a ajuda dele só existem para o Comércio.

## Adendo de 18/11/2026: módulo `pos` e o pacote do PDV (PDV-01)
- O módulo `pos` (Caixa, tela dedicada, área "Loja") entra **só no perfil Comércio**; na Fábrica fica oculto, como o `catalog` e o `sales`. Os planos já gravados recebem `pos` e `sales` por migration do catálogo (`PosModuleInPlans`), só acréscimo em `plan_modules`.
- Pacote `pos.retail` no fim dos pacotes do Comércio: a loja "Loja" (código `LOJA`) ligada ao local "Loja" e à tabela "Balcão", com o caixa 01 na conta "Caixa PDV 01". Os grupos do Comércio recebem as ações das vendas, do catálogo e do caixa, e nasce o grupo "Fiscal de caixa" (seed patch `pos-retail-groups` para os grupos já criados).

## Adendo de 27/11/2026: projeto de serviço e cadastro do RH no perfil Serviços (EXE-01 e EXE-04)
- Decisão do PO ([proposta de execução depois do contrato](../produto/execucao-pos-contrato.md) §13): o projeto ganha o tipo Fabricação ou Serviço. Tudo o que é do projeto de serviço (tipo, início, aceite do cliente, modelos de etapas, etapas, horas, marco por etapa) fica atrás do recurso por perfil **`projects.service_execution` = `[services]`**. `factory` fica de fora: a Konebras não vê nada (a EXE-08 decide com um caso real).
- **Cadastro do RH:** o custo das horas vem do cargo e o lançamento exige funcionário. No perfil Serviços o RH é opcional (cadeado). O recurso **`hr.people_registry` = `[services]`** libera **só** os grupos de rota de **cargos** e **funcionários** (e os contratos `IEmployees`, `IPositions`, `IHourlyCost`) quando o módulo `hr` não está liberado no plano. A regra no servidor é "módulo liberado **ou** o tenant tem o recurso" (`RequireModuleOrProfileFeature`), aplicada só a esses grupos; ponto, folha, afastamentos, férias e o resto do RH continuam com cadeado e 403 `module_not_released`. `tenant_modules` não muda.
- As camadas 2 e 3 não mudam: o usuário precisa de grupo com `hr` habilitado e das ações `hr.*`; dado sensível continua exigindo `hr.view_sensitive`/`hr.view_health`. Nenhum seed patch concede `hr.*` a outros grupos além do Proprietário.
- No menu, a área "Pessoas" mostra só "Funcionários" e "Cargos" para quem tem o cadastro pelo recurso. A ajuda declara `recurso:` como no adendo de 17/11/2026.
