# EXE-RH — Cargos e funcionários do RH para o perfil Serviços (sem folha nem ponto)

| Campo | Valor |
|---|---|
| ID | EXE-RH |
| Título | Recurso por perfil `hr.people_registry` (`services`): com o módulo `hr` fora do plano, o tenant de serviço passa a ter só Cargos e Funcionários do RH (e o parâmetro de encargos), para o custo-hora do cargo e o funcionário que lança horas; ponto, folha, férias, afastamentos e o resto do RH continuam com cadeado; `tenant_modules` não muda |
| Tamanho | P |
| Status | Done (28/11/2026) |
| Requisitos | [Proposta](../produto/execucao-pos-contrato.md) §13 (decisão 3) · ADR 0046 (adendo de 27/11/2026: projeto de serviço e cadastro do RH no perfil Serviços) · CAD-04a/b (cargos, funcionários, custo-hora) · USR-01 · R-SEC-5 · LGPD |
| Dependências | EXE-01a (recurso `projects.service_execution`, já na main) |
| Bloqueia | EXE-02 (cargo sugerido), EXE-04a (lançamento de horas) |
| Ordem de execução | 2 (depois da EXE-01b, em andamento) |

## User story
Como **dono de uma empresa de serviço sem folha no sistema**, quero **cadastrar cargos e funcionários com o custo da hora**, para **as horas da equipe virarem custo no projeto sem precisar contratar o RH completo.**

## Contexto e referências (conferido no código em 28/11/2026)
- `JobPosition` (HR.Domain) já tem `ReferenceSalary` (`[PersonalData]`), `MonthlyHours`, `HourlyCost` (6 casas) e `HourlyCostManual`; `Recalculate(chargesPct)` calcula pelo `HourlyCostCalculator` com o encargo do parâmetro `PayrollChargesPct` do RH. O salário de referência e a faixa exigem `hr.view_sensitive` (`PositionService`, CA 10 da CAD-04a).
- `IPositions` (sem salário) e `IHourlyCost.ForPositionAsync` são os contratos que Commercial e Production leem.
- `hr` está no `Core` de todo perfil, mas para Serviços é "opcional": liberado só se estiver no plano. O recurso por perfil (`RequireProfileFeature`) já existe (adendo de 17/11/2026 da ADR 0046).

## Critérios de aceite
1. **Dado** um tenant com o recurso `hr.people_registry` (perfil Serviços) e o módulo `hr` **não** liberado, **quando** acessa RH → Cargos e RH → Funcionários e as rotas deles, **então** funcionam como liberadas (regra "módulo `hr` liberado **ou** recurso", `RequireModuleOrProfileFeature("hr", "hr.people_registry")`, aplicada só a esses grupos de rota e ao parâmetro de encargos); ponto, folha, férias, afastamentos, exportação para a contadora e as demais rotas do RH respondem 403 `module_not_released` e a tela padrão.
2. **Dado** o menu, a busca e a ajuda, **quando** o tenant tem só o recurso, **então** a área "Pessoas" mostra só "Funcionários" e "Cargos" (os outros itens do RH ficam com cadeado, não somem: o RH está no perfil); a busca e a ajuda listam só os dois.
3. **[Segurança]** **Dado** as camadas 2 e 3, **quando** um usuário acessa pelo recurso, **então** valem como sempre: grupo com `hr` habilitado e `hr.view`/`hr.create`/`hr.edit`; o **custo-hora** do cargo é visto e editado só com `hr.edit`; o **salário de referência** e a faixa continuam exigindo `hr.view_sensitive` (e a leitura fica na auditoria); os dados de saúde exigem `hr.view_health`. Testes provam os três casos com o acesso pelo recurso.
4. **Dado** um cargo, **quando** é cadastrado sem folha, **então** o custo-hora nasce por um dos dois caminhos já existentes: **salário de referência** × (1 + encargos do parâmetro `PayrollChargesPct`) ÷ horas mensais (quem tem `hr.view_sensitive`), ou **valor manual** (`HourlyCostManual`, quem tem `hr.edit`). Cargo sem custo-hora mostra o selo "Sem custo-hora" na lista.
5. **Dado** o parâmetro de encargos, **quando** o tenant tem só o recurso, **então** ele aparece em Configurações → Parâmetros, seção RH, editável com `settings.edit`, com a dica "Encargos sobre o salário usados no custo-hora dos cargos."
6. **Dado** a Konebras (`factory`, `hr` liberado, sem o recurso), **quando** o deploy roda, **então** `tenant_modules` não muda (teste comparando antes e depois) e o RH dela é igual (mesmas rotas, menu e respostas).
7. **[Segurança]** **Dado** um tenant sem o recurso e sem `hr` no plano (ex.: Comércio), **quando** chama Cargos ou Funcionários, **então** 403 `module_not_released` (teste com o recurso dentro e fora).

## Regras de negócio e técnicas
- `hr.people_registry` = `[services]` em `BusinessProfileCatalog.Features`; `factory` fica fora (o RH dela já é liberado pelo plano).
- O recurso não grava nada em `tenant_modules`: é só a regra de rota e de menu.
- Funcionário pelo recurso usa o cadastro mínimo já existente (nome, cargo, admissão); campos de folha ficam ocultos quando o módulo não está liberado.

## Permissões, eventos e tabelas
- Nenhuma ação nova; nenhum seed patch (só o Proprietário tem `hr.*` por padrão).
- Recurso novo `hr.people_registry`; extensão `RequireModuleOrProfileFeature` em `BuildingBlocks/Web/ModuleEndpoints.cs`.
- Nenhuma tabela nova.

## Telas e estados
- RH → Cargos e Funcionários como hoje; com só o recurso, os campos e abas de folha e ponto não aparecem. Estados de vazio ("Nenhum cargo ainda." com "Criar cargo"), carregando e erro como hoje.
- Ajuda: `Ajuda/rh-cargos.md` e `Ajuda/rh-funcionarios.md` ganham "Para empresas de serviço sem folha" e declaram `recurso: hr.people_registry` além do módulo.

## Plano de testes
- **Integração:** rotas liberadas e bloqueadas pelo recurso; camadas 2 e 3; `hr.view_sensitive` e `hr.view_health` com acesso pelo recurso; custo-hora pelo salário de referência e manual; `tenant_modules` da Konebras inalterado.
- **Front (Vitest):** menu "Pessoas" com só os dois itens.

## Tarefas
- [x] `BusinessProfileCatalog.Features` + `RequireModuleOrProfileFeature`; grupos de rota de cargos, funcionários e parâmetro de encargos.
- [x] `/me/permissions` e menu: itens do RH pelo recurso; ajuda.
- [x] Testes do CA 3, 6 e 7.

## Implementação (28/11/2026)
- **Servidor:** `BusinessProfileCatalog.PeopleRegistryFeature` (`hr.people_registry` = `[services]`) e `PartialModuleFeatures` (recurso → módulo que ele abre em parte). `ResolvedTenant.IsPartiallyReleased`/`IsAccessible`. `RequireModuleOrProfileFeature` grava `RequiresModuleMetadata(ModuleCode, OrFeature)`; o `PermissionHandler` e o filtro aceitam "módulo liberado OU recurso" só nessas rotas. A permissão efetiva (`PermissionService`), o editor de grupos e a ajuda passam a considerar o módulo aberto em parte; `/me/permissions` ganha `partial` no módulo.
- **Rotas abertas pelo recurso:** `/hr/positions/*` (inclusive `/sensitive`, custo-hora manual, faixa e exportação) e o cadastro mínimo de `/hr/employees/*` (lista, detalhe, `/sensitive`, criar, dados gerais e pessoais, troca de cargo, linha do tempo, desligamento, vínculo com usuário, convite, exportação). Continuam só com o módulo: jornadas, sindicatos, reajuste salarial, afastamentos, complemento (dependentes, pagamento, documentos), holerites, ponto, férias, rescisão, folha, parâmetros do RH, exportação para a contadora e o autoatendimento.
- **Parâmetro de encargos:** já era de Configurações (`/settings/parameters/payroll-charges`, `settings.edit`), sem depender do `hr`; a seção Encargos ganhou a dica do CA 5. A seção de parâmetros da folha some com o RH em parte.
- **Tela:** o RH em parte aparece sem cadeado, só com Funcionários e Cargos (menu e busca); as outras URLs do RH mostram a tela padrão de não liberado. No funcionário, só as abas Dados gerais, Dados pessoais, Linha do tempo e Histórico; sem jornada, sindicato, holerites, afastamento e rescisão. No cargo, sem jornada (horas mensais digitadas). Selo "Sem custo-hora" na lista de cargos.
- **Ajuda:** `rh-cargos` e `rh-funcionarios` com "Para empresas de serviço sem folha"; essas e as abas abertas do funcionário (`rh-funcionario`, `-novo`, `-pessoais`, `-linha-do-tempo`, `-historico`) declaram `recurso: hr.people_registry`, que vale como "módulo liberado OU recurso" (os demais recursos seguem "módulo E recurso").

## Dev Notes
- `src/Modules/HR/BemAqui.HR.Domain/JobPosition.cs`, `HourlyCostCalculator.cs`; `BemAqui.HR.Application/PositionService.cs` (`SensitiveAction`, `PayrollChargesPctAsync`); `BemAqui.HR.Contracts/PositionsContracts.cs`, `HrContracts.cs`.
- `src/BuildingBlocks/BemAqui.BuildingBlocks/Modules/BusinessProfileCatalog.cs`, `Web/ModuleEndpoints.cs`.

## Fora do escopo
- Ponto, folha e o RH completo para Serviços (pelo plano, como hoje). Custo-hora por funcionário.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 28/11/2026 | 0.1 | **Revisão combinada de 28/11/2026** (arquitetura, segurança, UX): CA 14 a 16 da antiga EXE-04 viram esta story; custo-hora sem folha pelo salário de referência + encargos ou manual (o cadastro já existe na CAD-04a); `hr.view_sensitive`/`hr.view_health` testados pelo recurso; `tenant_modules` da Konebras inalterado | Claude |
| 28/11/2026 | 1.0 | **Implementada.** Dúvidas de PO resolvidas pela opção mais segura (a confirmar): (1) CA 2 dizia "mostra só os dois" e "os outros ficam com cadeado": seguido o adendo de 27/11 da ADR 0046 — o menu e a busca mostram só Funcionários e Cargos, e a URL direta das outras telas do RH mostra a tela padrão de não liberado (a API responde 403 `module_not_released`); (2) **desligamento** entra no cadastro mínimo, porque desativa o login de quem saiu (USR-01, segurança), mas o cálculo de rescisão não; (3) **reajuste salarial, afastamentos e complemento** (dependentes, pagamento, documentos) ficam só com o módulo; (4) o "parâmetro de encargos" já era rota de Configurações (`settings.edit`), sem grupo novo; (5) a ajuda das abas abertas do funcionário também declara o recurso (toda tela tem ajuda). `hr.view_health` pelo recurso: o afastamento não sai por rota nenhuma do recurso, e na exportação da lista o tipo e o motivo só saem com a ação (teste). | Claude |
