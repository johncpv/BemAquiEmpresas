# PAT-01a — Patrimônio: cadastro de bens

| Campo | Valor |
|---|---|
| ID | PAT-01a |
| Título | Cadastro de bens (máquina, ferramenta, mobiliário, veículo, TI) com etiqueta `PAT-00001`, marca, modelo, série, aquisição (data, valor, NF), local, responsável, centro de trabalho e foto; situação Ativo, Em manutenção e Baixado; lista com filtros e ficha com abas; primeiras permissões do módulo `assets` |
| Tamanho | M (quebra da PAT-01 em 01a cadastro e 01b etiquetas QR e leitura pelo app) |
| Status | Draft |
| Requisitos | R-PAT-1 · R-COM-3 · R-COM-4 (numeração da etiqueta) · R-SEC-5 · R-PRP-1 · R-PRP-3 |
| Dependências | CAD-04b (`IEmployees` para o responsável), CAD-03 (`IWorkCenters`), CAD-01a (`IParties`, fornecedor), CAD-06 (numeração), FND-09d; Fase 5 FIS-04 (vínculo opcional com a NF de entrada) |
| Bloqueia | PAT-01b, PAT-02, PAT-03a |
| Ordem de execução | 10 (Marco 6.4 "Patrimônio"; pode correr em paralelo aos Marcos 6.1 a 6.3) |

## User story
Como **supervisor de produção ou administrativo**, quero **cadastrar cada bem da empresa com onde está, quem responde por ele e quanto custou**, para **saber o que a empresa tem e preparar a manutenção e o custo das máquinas**.

## Contexto e referências
- PRD R-PAT-1 ("tipo (máquina, ferramenta, mobiliário, veículo, TI), marca, modelo, nº de série, data e valor de aquisição, NF, local, responsável, foto"); R-PAT-5 (máquina vinculada a centro de trabalho); R-PAT-6 (depreciação: P, Fase 9).
- `03-modelo-dados` → `assets.assets` (`tag`, `name`, `type`, `brand`, `model`, `serial_number`, `acquisition_date`, `acquisition_value`, `invoice_ref`, `location_id`, `responsible_employee_id`, `work_center_id`, `status` Active/Maintenance/Disposed, `photo`).
- `04-fluxos` §4: numeração `PAT-00001 (etiqueta)`, sem ano.
- `ModuleCatalog`: `assets` ("Patrimônio", área Fábrica, `view`, `create`, `edit`, `delete`, sem `approve`). **Nenhum grupo do seed tem `assets`** hoje (só o Proprietário): as permissões padrão entram por seed patch (proposta, Pontos em aberto 1).
- Padrões comuns dos cadastros (README, Fase 1); ADR 0016 (excluir × inativar, seed patch, `I*UsageProvider`), ADR 0019 §7 (anexos por dono).

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `assets-permissions` (proposta) roda, **então** a Supervisão de produção ganha `assets.view/create/edit` e o Almoxarifado `assets.view` (Pontos em aberto 1), uma vez, com o `perm_version` dos membros incrementado; `assets.delete` fica só com o Proprietário (D10).

**Cadastro**
2. **Dado** `assets.create`, **quando** clico "Novo bem" e informo o obrigatório (nome, tipo) e o opcional (marca, modelo, número de série, data de aquisição, valor de aquisição `numeric(18,2)`, referência da NF, fornecedor, local, responsável, centro de trabalho, observação) e clico "Salvar bem", **então** o bem nasce `Active` com a etiqueta `PAT-00001` (`INumberSequence`, tipo `PAT`, sem ano, 5 dígitos, índice único) e o detalhe abre com o `NextSteps` "Imprimir etiqueta" (PAT-01b) e "Criar plano de manutenção" (PAT-03a, só para máquina e veículo).
3. **Dado** o tipo "Máquina", **quando** escolho o centro de trabalho (`IWorkCenters`, só ativos), **então** o local vem sugerido como o do último bem do mesmo centro, e o responsável como o funcionário mais comum entre os bens do centro (propositivo; só muda enquanto eu não mexi).
4. **Dado** o responsável, **quando** escolho, **então** o seletor traz só funcionários ativos pelo `IEmployees` (id, nome, matrícula, cargo), sem dado sensível; funcionário desligado continua aparecendo nos bens que já tinha, com a marca "Desligado" e o aviso "Troque o responsável" (link para a movimentação da PAT-02).
5. **Dado** o local, **quando** escolho, **então** uso o cadastro de locais do patrimônio (proposta: `assets.sites`, cadastro simples de setores, com seed "Fábrica" e "Escritório"; Pontos em aberto 2), com criação rápida no combobox.
6. **Dado** o número de série, **quando** já existe outro bem não excluído com a mesma marca, modelo e série, **então** 409 "Já existe o bem {etiqueta} com esta série." com "Abrir bem".
7. **Dado** a referência da NF, **quando** a Fase 5 (FIS-04) existir, **então** posso escolher a NF de entrada pelo seletor do Fiscal (proposta: `IIncomingInvoices`), e o fornecedor e a data vêm dela (R-PRP-3); antes disso, é um texto livre (número e série) e o fornecedor pelo `IParties` (papel Fornecedor).

**Foto e documentos**
8. **Dado** `assets.edit`, **quando** envio a foto (PNG, JPG ou WebP até 5 MB) e documentos (PDF, PNG, JPG até 10 MB: nota, manual, garantia), **então** usam os donos de anexo `AssetPhoto` e `Asset` (leitura `assets.view`, escrita `assets.edit`, `OwnerExists`, ClamAV, ADR 0019 §7); a foto aparece na ficha e na lista (miniatura), só depois do `Clean`.

**Lista e ficha**
9. **Dado** `assets.view`, **quando** abro Patrimônio → Bens, **então** vejo etiqueta (link), nome, tipo, local, responsável, centro e situação (chips Ativo ok, Em manutenção alerta, Baixado neutro), com busca por etiqueta, nome e série e filtros por tipo, local, centro, responsável e situação na URL; o valor de aquisição aparece numa coluna só para quem tem `assets.edit` (proposta, Pontos em aberto 3).
10. **Dado** a ficha do bem, **quando** abro, **então** as abas na URL são: Dados, Documentos, Movimentações (PAT-02), Manutenção (PAT-03a) e Histórico de alterações (última).
11. **Dado** 5 mil bens, **quando** abro a lista com filtro, **então** responde em menos de 1 s.

**Excluir × inativar**
12. **Dado** `assets.delete` e um bem sem movimentação, manutenção nem custo, **quando** excluo, **então** é exclusão lógica; com uso, 409 "Bem com histórico não pode ser excluído. Use Baixar bem." com o atalho para a baixa (PAT-02).

**Permissões e contratos**
13. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released` e `ModuleNotReleased` na tela; sem grupo, 403 `forbidden`; com `assets.view` só, modo leitura. `RequireModule` e `perm:`; IDOR em `/assets/{id}/attachments/{attId}`; ids de funcionário, centro e terceiro validados pelos Contracts dos donos.
14. **Dado** o Assets guardando ids de funcionário, centro de trabalho, terceiro e local de outros módulos, **quando** o dono tenta excluir o registro, **então** o Assets implementa os `I*UsageProvider` (`IWorkCenterUsageProvider`, `IPartyUsageProvider` e um novo para o HR, proposta) e o cadastro de origem só inativa.
15. **Dado** `IAssets` em `Assets.Contracts` (proposta: id, etiqueta, nome, tipo, centro, situação), **quando** a Produção ou o Planejamento consultam, **então** recebem só isso, sem valor nem responsável.
16. **Dado** qualquer alteração, **quando** abro o histórico, **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Tipos** (`AssetType`): `Machine`, `Tool`, `Furniture`, `Vehicle`, `It` (R-PAT-1).
- **Etiqueta:** gerada na criação, imutável, nunca reaproveitada; é o que o QR code leva (PAT-01b).
- **Situação:** `Active` na criação; `Maintenance` pela OS de manutenção (PAT-03a); `Disposed` pela baixa (PAT-02).
- **Tabelas (propostas):** `assets.assets` (03, + `supplier_party_id`, `incoming_invoice_id`, `notes`, `site_id` no lugar de `location_id`, soft delete, `xmin`; `photo` vira o anexo `AssetPhoto` mais recente `Clean`, como o logo); `assets.sites` (`name` único, `is_active`, `seed_key`).
- Dinheiro `numeric(18,2)`; datas de negócio em `date`; Guid v7; `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `assets.view`, `assets.create`, `assets.edit`, `assets.delete` (já no `ModuleCatalog`); seed patch `assets-permissions` (proposta).
- **Eventos:** nenhum nesta story.
- **Contratos:** `IAssets` (proposta); consumidores de `IEmployees`, `IWorkCenters`, `IParties`.
- **Tabelas:** `assets.assets`, `assets.sites`; donos de anexo `Asset` e `AssetPhoto`; tipo de numeração `PAT`.

## Telas e estados
- **Patrimônio → Bens** (`/app/patrimonio/bens`), **Novo bem** (`/app/patrimonio/bens/novo`, página única em seções: Identificação, Aquisição, Onde está), **Bem** (`/app/patrimonio/bens/$id`, abas), **Patrimônio → Locais** (painel lateral). Entrada no `modules.ts` na área Fábrica.
- **Estados:** carregando; vazio ("Cadastre as máquinas e ferramentas para acompanhar manutenção e custo." + "Novo bem"); vazio filtrado; erro; modo leitura.
- **Propositivo:** local e responsável sugeridos pelo centro; "Duplicar bem" para itens iguais (copia tudo menos a série e a etiqueta).
- **400 px e teclado:** E2E da criação.
- **Ajuda:** `Ajuda/patrimonio-bens.md` (`padrao: true`), `Ajuda/patrimonio-bem-novo.md`, `Ajuda/patrimonio-bem.md` (uma por aba) e `Ajuda/patrimonio-locais.md`; `<Dica>` em "Etiqueta" e "Centro de trabalho".

## Plano de testes
- **Unidade:** sugestões (CA 3); regra de exclusão (CA 12).
- **Integração (Testcontainers):** seed patch (CA 1); criação com numeração `PAT` (CA 2); responsável pelo `IEmployees` e desligado (CA 4); série duplicada (CA 6); anexos por dono (CA 8); lista, filtros e valor por permissão (CA 9); desempenho com 5 mil (CA 11); exclusão × baixa (CA 12); três camadas e IDOR (CA 13); provedores de uso (CA 14); `IAssets` (CA 15).
- **E2E (Playwright):** cadastrar uma máquina com foto a 400 px.

## Fora do escopo
- Etiquetas e leitura pelo app: PAT-01b. Movimentação e baixa: PAT-02. Manutenção: PAT-03a/03b.
- **Depreciação** (R-PAT-6): Fase 9, pelo PRD §3.2 e §7 (Pontos em aberto 4).
- Importação dos bens do Dolibarr: não está no escopo da migração (ADR 0017).

## Pontos em aberto
1. **Permissões padrão** (PO): proposta Supervisão de produção com `assets.view/create/edit` e Almoxarifado com `assets.view`; o significado de "aprovar" não existe no módulo.
2. **Local do bem** (PO + arquiteto): cadastro próprio de locais do patrimônio (proposta) ou os locais de estoque (`inventory.locations`), que hoje são almoxarifados.
3. **Valor de aquisição visível** (PO): só com `assets.edit` (proposta) ou com `assets.view`.
4. **Depreciação gerencial** (PO): o PRD a põe na Fase 9 (P). Antecipar para a Fase 6 pediria vida útil, valor residual e método no cadastro, e uma ADR; esta story não cria esses campos.
5. Inventário físico do patrimônio (conferência periódica por leitura do QR): fora do MVP.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva; primeira do schema `assets`); seed de locais e seed patch idempotentes.
- [ ] Permissões só do `ModuleCatalog`; donos de anexo com `OwnerExists`.
- [ ] Telas com estados; ajuda de cada rota e aba; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IAssetsDb`, `Asset`, `AssetSite`; `IAssets` no Contracts; provedores de uso.
- [ ] Endpoints `/api/v1/assets/assets` e `/api/v1/assets/sites`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, design (lista e ficha), @qa.
- Gate: CA 2, 4, 9 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** quebra da PAT-01 em cadastro (01a) e etiqueta/leitura (01b); depreciação fora, como no PRD, com a antecipação como ponto em aberto 4; responsável pelo `IEmployees` sem dado sensível (CA 4); NF de entrada só quando a FIS-04 existir (CA 7).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PAT-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
