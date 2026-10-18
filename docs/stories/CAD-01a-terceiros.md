# CAD-01a — Terceiros: cadastro único com papéis, endereços e contatos

| Campo | Valor |
|---|---|
| ID | CAD-01a |
| Título | Cadastro de terceiros PF/PJ com CPF/CNPJ validado, papéis (cliente, fornecedor, instalador, transportadora), vários endereços e contatos, sem duplicar documento |
| Tamanho | G (quebra da CAD-01: a consulta de CNPJ foi para a CAD-01b, para não travar o cadastro na escolha do provedor) |
| Status | Draft |
| Requisitos | R-TER-1 (PF/PJ, CPF/CNPJ validado) · R-TER-2 · R-PRP-1 · R-PRP-3 · R-SEC-5 · PRD §6 Desempenho (lista < 1 s com 50 mil) e LGPD (dados de PF) |
| Dependências | CAD-06 (validador de CPF/CNPJ), CAD-05b (condição de pagamento padrão); Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | CAD-01b, CAD-02b (códigos do fornecedor), MIG-01b, COM-01, CMP-02, COM-06 |
| Ordem de execução | 4 |

## User story
Como **comercial, financeiro ou almoxarife**, quero **um único cadastro de terceiro, em que o mesmo CNPJ pode ser cliente, fornecedor, instalador e transportadora**, para **não cadastrar a mesma empresa duas vezes e ter endereço, contato e condição de pagamento prontos em todo documento**.

## Contexto e referências
- PRD §5.2: um único cadastro de Terceiro com papéis; um mesmo CNPJ pode ter vários papéis. R-TER-1, R-TER-2 (vários endereços — cobrança, entrega, obra —, contatos, IE, regime, condições padrão de pagamento).
- `docs/03-modelo-dados.md` → `parties.parties` (`kind` PF/PJ, `document`, `legal_name`, `trade_name`, `state_registration`, `municipal_registration`, `tax_regime`, `email`, `phone`, `notes`, `default_payment_term_id`), `party_roles` (Customer/Supplier/Installer/Carrier), `party_addresses` (Billing/Delivery/Site, `zip`, `street`, `number`, `complement`, `district`, `city`, `city_ibge`, `state`), `party_contacts` (`name`, `role`, `email`, `phone`, `receives_billing`).
- `docs/02-arquitetura.md` §8 (`pg_trgm` em documento e nome; índices em FKs).
- Backlog (CA): CNPJ/CPF validados; mesmo documento não duplica (adiciona papel). A consulta que preenche razão social e endereço está na CAD-01b.
- `ModuleCatalog`: `parties` ("Cadastros", área Comercial) com `view`, `create`, `edit`, `delete`. **Nenhum grupo do seed tem `parties` hoje** (ponto em aberto 1).
- Código existente: `ISoftDeletable`, `IAuditable`, `[PersonalData]` (mascara no diff), `DataTable` server-side, `useZodForm`/`applyServerErrors`, `RecordHistory`, `UnsavedChangesGuard`, slug `cadastros` em `navigation/modules.ts`.
- Protótipo: não tem tela de terceiros; seguir o padrão de listagem e de detalhe com abas (Usuários e acessos).

## Critérios de aceite
**Cadastro e documento**
1. **Dado** `parties.create`, **quando** cadastro um terceiro PJ com CNPJ válido, razão social e ao menos um papel, e clico "Salvar terceiro", **então** ele é gravado e a confirmação diz "Terceiro salvo", com a ação "Adicionar endereço" (próximo passo).
2. **Dado** um CNPJ ou CPF com dígito verificador errado, **quando** salvo, **então** a API responde 400 com "CNPJ inválido." ou "CPF inválido." no campo; com ou sem máscara é aceito e gravado só com os caracteres significativos; o CNPJ alfanumérico (IN RFB 2.229/2024) é aceito (validador da CAD-06).
3. **Dado** um documento já cadastrado (não excluído) como Cliente, **quando** começo a cadastrar o mesmo documento como Fornecedor, **então** a tela avisa "Este CNPJ já está cadastrado como {nome}." e oferece "Adicionar papel Fornecedor" e "Abrir cadastro"; a API responde 409 `party_exists` com o id do existente e nunca cria um segundo registro.
4. **[Segurança]** **Dado** duas gravações simultâneas do mesmo documento, **quando** ambas chegam, **então** só uma é criada e a outra recebe 409 `party_exists` (índice único parcial no banco, `WHERE NOT is_deleted`).
5. **Dado** um documento de terceiro excluído, **quando** tento cadastrá-lo de novo, **então** a tela oferece "Restaurar cadastro" em vez de criar outro.
6. **Dado** o tipo PF, **quando** cadastro, **então** peço CPF e nome; IE é opcional (produtor rural); `legal_name` guarda o nome da pessoa e o nome fantasia fica oculto.

**Papéis**
7. **Dado** `parties.edit`, **quando** adiciono ou removo papéis (Cliente, Fornecedor, Instalador, Transportadora), **então** a alteração é gravada e auditada; remover o último papel responde 400 "O terceiro precisa de ao menos um papel."
8. **Dado** a lista de terceiros, **quando** filtro por papel, **então** vejo só os terceiros com aquele papel, e cada linha mostra os papéis como chips.

**Endereços e contatos**
9. **Dado** `parties.edit`, **quando** adiciono endereços de cobrança, entrega e obra, **então** cada um tem CEP (8 dígitos), logradouro, número, complemento, bairro, cidade, código IBGE (7 dígitos, opcional) e UF (uma das 27); o primeiro endereço vira o padrão de cobrança e de entrega (propositivo) e posso trocar o padrão de cada tipo.
10. **Dado** `parties.edit`, **quando** adiciono contatos com nome, cargo, e-mail, telefone e "recebe cobrança", **então** são gravados; e-mail inválido responde 400 "E-mail inválido."; ao menos um contato pode ser marcado para receber cobrança (usado pela régua e pelo boleto nas Fases 2 e 5).

**Dados fiscais e comerciais**
11. **Dado** `parties.edit`, **quando** informo IE (dígitos ou "ISENTO"), IM e regime tributário (lista do ponto em aberto 4), **então** são gravados.
12. **Dado** as condições de pagamento da CAD-05b, **quando** escolho a "Condição de pagamento padrão", **então** só aparecem condições ativas e ela vem pré-preenchida nos documentos das próximas fases (R-PRP-1, via `IParties`).

**Lista, busca e exclusão**
13. **Dado** `parties.view`, **quando** abro Cadastros → Terceiros, **então** vejo a DataTable server-side com nome (razão social e fantasia), documento formatado, papéis, cidade/UF e situação, com busca por nome, fantasia ou documento (com ou sem máscara) e ordenação.
14. **Dado** 50 mil terceiros no banco, **quando** busco por parte do nome ou do documento, **então** a primeira página responde em menos de 1 s (PRD §6; índice `pg_trgm`).
15. **Dado** `parties.delete`, **quando** excluo um terceiro e confirmo "Excluir", **então** ele é excluído logicamente; documentos das próximas fases que o referenciam continuam mostrando o nome.

**Contrato para os outros módulos**
16. **Dado** um módulo que precisa de terceiros (Commercial, Purchasing, Finance, Inventory), **quando** consulta, **então** usa `IParties` de `Parties.Contracts` (resumo por id, busca por papel e termo, endereço e contato padrão, condição padrão, "tem o papel X?"); o teste de arquitetura impede outra referência.

**LGPD e auditoria**
17. **[Segurança]** **Dado** um terceiro PF ou um contato, **quando** altero CPF, e-mail ou telefone, **então** o diff da auditoria mostra esses campos mascarados (`[PersonalData]`), e o histórico (`RecordHistory`) mostra o restante antes/depois.
18. **[Segurança]** **Dado** os logs da API, **quando** um terceiro é criado ou buscado, **então** CPF, e-mail e telefone não aparecem em log (máscara da FND-12).

**Permissões (3 camadas)**
19. **Dado** o módulo `parties` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite `parties`, 403 `forbidden` e o módulo some do dock; com `parties.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`.

## Regras de negócio e técnicas
- **Documento:** obrigatório para PJ; para PF, obrigatório nesta proposta (ponto em aberto 3). Gravado normalizado (maiúsculas, sem máscara); exibido formatado.
- **Unicidade:** índice único parcial em `document` (`WHERE NOT is_deleted AND document IS NOT NULL`).
- **Busca:** índices `gin_trgm_ops` em `legal_name`, `trade_name` e `document` (02 §8).
- **Endereço padrão:** `is_default` por tipo (**proposta**, não está no 03), único por terceiro e tipo.
- **Inscrição estadual:** só formato ("ISENTO" ou dígitos); validação por UF fica no ponto em aberto 5.
- **Soft delete** em `parties`; endereços e contatos são filhos e seguem o terceiro.
- **`[PersonalData]`:** `document`, `email` e `phone` quando `kind = PF`; `email` e `phone` dos contatos. Como o atributo é por propriedade, a proposta é marcar sempre `email`/`phone` de contato e tratar o documento de PF por um campo separado ou por regra no interceptor (decidir na implementação, sem perder a máscara).
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em terceiro, papel, endereço e contato.
- **Condição padrão:** `default_payment_term_id` guarda só o Id; a validação de "condição ativa" usa o Contracts do dono (CAD-05b).

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `parties.view`, `parties.create`, `parties.edit`, `parties.delete`.
- **Eventos:** nenhum nesta story (sem consumidor). `PartyMerged` fica para a MIG-01b se a fusão de duplicados precisar avisar outro módulo.
- **Tabelas:** `parties.parties`, `party_roles` (único por terceiro e papel), `party_addresses` (+ `is_default`, **proposta**), `party_contacts`, todas do 03.

## Telas e estados
- **Cadastros → Terceiros** (`/app/cadastros/terceiros`): cabeçalho "Terceiros" com "Novo terceiro"; filtros por papel e situação; DataTable.
- **Detalhe** (`/app/cadastros/terceiros/$id`), abas: Dados (PF/PJ, documento, nomes, IE, IM, regime, e-mail, telefone, condição padrão, observações), Endereços, Contatos, Histórico.
- **Novo terceiro:** escolha PF/PJ, documento primeiro (para detectar duplicidade antes de digitar o resto), papéis em chips.
- **Estados:** carregando (skeleton); vazio ("Cadastre o primeiro cliente ou fornecedor." + "Novo terceiro"; a importação do Dolibarr é feita pela CLI da MIG-01, sem botão na tela); erro com motivo e "Tentar de novo"; aviso de alterações não salvas (`UnsavedChangesGuard`).
- **Propositivo:** documento duplicado oferece "Adicionar papel" (CA 3); papel pré-marcado conforme a tela de origem (ex.: aberto a partir de Compras, vem Fornecedor); primeiro endereço vira padrão; depois de salvar, oferece "Adicionar endereço" e "Adicionar contato".
- **Ajuda:** `Ajuda/cadastros-terceiros.md` (lista, `padrao: true`) e `Ajuda/cadastros-terceiro.md` com uma ajuda por aba (`dados`, `enderecos`, `contatos`, `historico`); `<Dica>` em "Papel", "Regime tributário" e "Recebe cobrança".

## Plano de testes
- **Unidade:** normalização e validação de documento (CA 2); regra de último papel (CA 7); endereço padrão único por tipo (CA 9).
- **Integração (Testcontainers):** criar PJ e PF (CA 1, 6); duplicidade com 409 e com gravação simultânea (CA 3, 4); restaurar excluído (CA 5); papéis, endereços e contatos (CA 7–11); condição padrão ativa via Contracts (CA 12); lista e busca com 50 mil registros abaixo de 1 s (CA 13, 14); exclusão lógica (CA 15); máscara na auditoria (CA 17); três camadas (CA 19).
- **Arquitetura:** só `Parties.Contracts` é referenciado por outros módulos (CA 16); `RequirePermission` só com códigos do catálogo.
- **E2E (Playwright):** cadastrar cliente, tentar o mesmo CNPJ como fornecedor e adicionar o papel.

## Fora do escopo
- Consulta de CNPJ e preenchimento automático: CAD-01b.
- Histórico comercial e financeiro do terceiro (orçamentos, contratos, títulos, compras, R-TER-3) e indicador de pontualidade (R-TER-4): entram nas stories que criam esses documentos (Fases 2 e 4).
- Importação do Dolibarr (R-TER-5): MIG-01b.
- Anexos no terceiro (R-COM-3 cita documentos, não cadastros).

## Pontos em aberto
1. **Permissões padrão de `parties` nos grupos** (pré-condição): nenhum grupo do seed tem `parties`. Sugestão: Comercial e Financeiro com `view`, `create`, `edit`; Almoxarifado com `view`. O `GroupSeeder` não recria grupos existentes, então a Konebras (já provisionada em homologação) precisa de uma migração de dados ou de ajuste manual pelo Proprietário.
2. **Fusão de duplicados**: quando dois terceiros já existentes têm o mesmo documento (caso da importação), a fusão é só na MIG-01b ou também uma ação de tela.
3. **PF sem CPF** (consumidor final, prospect): aceitar ou exigir.
4. **Lista de regimes tributários** (sugestão: Simples Nacional, MEI, Lucro Presumido, Lucro Real, Isento/Não contribuinte, Não informado).
5. **Validação da IE por UF:** só formato (proposta) ou dígito verificador por estado.
6. **Mascaramento parcial do CPF na lista** (ex.: `***.456.789-**`) para quem tem só `parties.view`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `parties`, extensão `pg_trgm`, não destrutiva).
- [ ] Permissões usadas só do `ModuleCatalog`; permissões padrão dos grupos decididas e aplicadas.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda da lista e de cada aba escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada, com `[PersonalData]`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (`is_default`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Party`, `PartyRole`, `PartyAddress`, `PartyContact` (Domain) com as regras de documento, papéis e padrão.
- [ ] `IPartiesDb`, configurações `internal`, migration com índices únicos parciais e `pg_trgm`.
- [ ] Endpoints `/api/v1/parties` (CRUD, papéis, endereços, contatos, restaurar) com `RequireModule("parties")`.
- [ ] `IParties` em `Parties.Contracts`.
- [ ] Telas de lista e detalhe com abas; ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD de PF e contatos), @qa.
- Gate: CA 4 (duplicidade concorrente) e CA 14 (desempenho) obrigatórios.

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

- **Correções feitas na validação:** Estado vazio: retirado o botão "Importar do Dolibarr" (a MIG-01 é só CLI, sem tela).
- **Pré-condições para `Ready`:** Ponto em aberto 1 (permissões padrão de `parties` nos grupos e aplicação no tenant já provisionado); dono das condições de pagamento (CAD-05b).
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
