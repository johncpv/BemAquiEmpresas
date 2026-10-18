# CAD-01a — Terceiros: cadastro único com papéis, endereços e contatos

| Campo | Valor |
|---|---|
| ID | CAD-01a |
| Título | Cadastro de terceiros PF/PJ com CPF/CNPJ validado (`BrazilianDocuments`), papéis (cliente, fornecedor, instalador, transportadora), situação, vários endereços e contatos, sem duplicar documento |
| Tamanho | G (quebra da CAD-01: a consulta de CNPJ e de CEP foi para a CAD-01b) |
| Status | Ready |
| Requisitos | R-TER-1 (PF/PJ, CPF/CNPJ validado) · R-TER-2 · R-PRP-1 · R-PRP-3 · R-SEC-5 · PRD §6 Desempenho (lista < 1 s com 50 mil) e LGPD (dados de PF) |
| Dependências | FND-09d (moldes e componentes); Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | MIG-01b, CAD-05b (condição de pagamento padrão do terceiro), CAD-01b, CAD-02b (códigos do fornecedor), CAD-06 e CAD-04a/04b (`BrazilianDocuments`), COM-01, CMP-02, COM-06 |
| Ordem de execução | 1 (Marco A — terceiros reais) |

## User story
Como **comercial, financeiro ou almoxarife**, quero **um único cadastro de terceiro, em que o mesmo CNPJ pode ser cliente, fornecedor, instalador e transportadora**, para **não cadastrar a mesma empresa duas vezes e ter endereço e contato prontos em todo documento**.

## Contexto e referências
- PRD §5.2: um único cadastro de Terceiro com papéis; um mesmo CNPJ pode ter vários papéis. R-TER-1, R-TER-2 (vários endereços — cobrança, entrega, obra —, contatos, IE, regime).
- `docs/03-modelo-dados.md` → `parties.parties`, `party_roles`, `party_addresses`, `party_contacts` (com os campos aprovados nesta story: `is_active`, `is_default`).
- `docs/02-arquitetura.md` §8 (`pg_trgm` em documento e nome; índices em FKs).
- ADR 0016 (seed patch, `BrazilianDocuments`, auditoria qualificada, IDOR, `Cache-Control`), ADR 0018 (LGPD), ADR 0010 item 9 (busca da topbar com dados quando os módulos tiverem dados).
- Revisão pré-Fase 1: D9 (CPF na lista), D10 (excluir × inativar), tabela 1.1 (permissões, documento, situação), §2.4 (padrões e correções da CAD-01a), §2.5 (segurança).
- Padrões comuns a todos os cadastros: `docs/stories/README.md` (Fase 1). Esta story usa os moldes **cadastro em seções** (criação) e **detalhe com abas** (edição) da FND-09d.
- `ModuleCatalog`: `parties` ("Cadastros", área Comercial) com `view`, `create`, `edit`, `delete`.

## Critérios de aceite
**Validador de documentos**
1. **Dado** o `BrazilianDocuments` (BuildingBlocks), **quando** valido CPF e CNPJ, **então** confere os dígitos verificadores, aceita o CNPJ alfanumérico (IN RFB 2.229/2024), aceita com ou sem máscara e normaliza para maiúsculas sem pontuação; números com todos os dígitos iguais são inválidos. É o mesmo validador usado pela CAD-06, CAD-04a e CAD-04b.

**Cadastro e documento**
2. **Dado** `parties.create`, **quando** abro "Novo terceiro", **então** a página em seções começa pelo campo "CPF ou CNPJ", que infere PF ou PJ pela quantidade de caracteres (o `ToggleGroup` PF/PJ continua editável), seguido de nome, papéis e do bloco "Endereço de cobrança".
3. **Dado** um CNPJ ou CPF com dígito errado, **quando** salvo, **então** a API responde 400 com "CNPJ inválido." ou "CPF inválido." no campo.
4. **Dado** um documento digitado, **quando** saio do campo, **então** a duplicidade é conferida **antes** de qualquer outra ação (inclusive a consulta à Receita da CAD-01b): se já existe (não excluído), a tela avisa "Este CNPJ já está cadastrado como {nome}." e oferece "Adicionar papel {papel}" e "Abrir cadastro"; a API de criação responde 409 `party_exists` com o id do existente e nunca cria um segundo registro.
5. **[Segurança]** **Dado** duas gravações simultâneas do mesmo documento, **quando** ambas chegam, **então** só uma é criada e a outra recebe 409 `party_exists` (índice único parcial `WHERE NOT is_deleted AND document IS NOT NULL`).
6. **Dado** um documento de terceiro excluído, **quando** tento cadastrá-lo de novo, **então** a tela oferece "Restaurar cadastro" em vez de criar outro.
7. **Dado** um terceiro PF ou PJ sem documento, **quando** salvo com nome e papel, **então** ele é gravado e mostra a `Tag` "Sem documento" na lista e no detalhe; o documento só passa a ser exigido na emissão fiscal ou de boleto (Fases 2 e 5).
8. **Dado** `parties.create` e os dados obrigatórios (nome, ao menos um papel) e o endereço de cobrança preenchido ou vazio, **quando** clico "Salvar terceiro", **então** o terceiro (e o endereço de cobrança, se preenchido, como padrão) é gravado, o toast diz "Terceiro salvo" e o detalhe abre com a faixa "Próximos passos" ("Adicionar endereço de entrega", "Adicionar contato").
9. **Dado** o tipo PF, **quando** cadastro, **então** peço CPF e nome; IE é opcional (produtor rural); `legal_name` guarda o nome da pessoa e o nome fantasia fica oculto.

**Papéis e situação**
10. **Dado** `parties.edit`, **quando** adiciono ou removo papéis no `ChipGroup` (Cliente, Fornecedor, Instalador, Transportadora), **então** a alteração é gravada e auditada; remover o último papel responde 400 "O terceiro precisa de ao menos um papel."
11. **Dado** `parties.edit`, **quando** clico "Inativar terceiro" e confirmo "Inativar", **então** a situação passa a Inativo (chip neutro), o terceiro some dos seletores (`IParties` só devolve ativos na busca) e continua visível nos documentos que o usam; "Reativar" desfaz.

**Endereços e contatos**
12. **Dado** `parties.edit`, **quando** adiciono endereços de cobrança, entrega e obra, **então** cada um tem CEP (8 dígitos), logradouro, número, complemento, bairro, cidade, código IBGE (7 dígitos, opcional) e UF (uma das 27); o primeiro endereço de cada tipo vira o padrão daquele tipo e posso trocar o padrão.
13. **[Segurança]** **Dado** a rota `/api/v1/parties/{partyId}/addresses/{addressId}` (e a de contatos), **quando** o endereço pertence a outro terceiro, **então** a API responde 404, sem ler nem alterar o registro (IDOR pai/filho).
14. **Dado** `parties.edit`, **quando** adiciono contatos com nome, cargo, e-mail, telefone e "Recebe cobrança", **então** são gravados; e-mail inválido responde 400 "E-mail inválido."

**Dados fiscais**
15. **Dado** `parties.edit`, **quando** informo IE (dígitos ou "ISENTO", validação só pelo formato), IM e regime tributário (Simples Nacional, MEI, Lucro Presumido, Lucro Real, Isento/Não contribuinte, Não informado), **então** são gravados.

**Lista, busca e exclusão**
16. **Dado** `parties.view`, **quando** abro Cadastros → Terceiros, **então** vejo a DataTable server-side com nome (razão social e fantasia; a primeira coluna leva ao detalhe), documento, papéis (chips), cidade/UF e situação, com filtros por papel e situação ("Inativos") na URL, e o vazio filtrado com "Limpar filtros".
17. **[Segurança]** **Dado** um terceiro PF, **quando** aparece na lista ou na busca, **então** o CPF vem mascarado do servidor (`***.456.789-**`); completo só no detalhe; a busca por CPF é sempre exata (11 dígitos, com ou sem máscara), e parte de um CPF não encontra nada. O CNPJ (dado público de PJ) aceita busca parcial.
18. **Dado** 50 mil terceiros no banco, **quando** busco por parte do nome (com ou sem acento) ou do CNPJ, **então** a primeira página responde em menos de 1 s (PRD §6; `pg_trgm` + `unaccent`).
19. **Dado** `parties.view` e a busca da topbar (Ctrl K), **quando** digito ao menos 3 caracteres, **então** a paleta mostra até 5 terceiros (nome e papéis, CPF mascarado) com link para o detalhe; sem `parties.view`, nenhum terceiro aparece (ADR 0010 item 9).
20. **Dado** `parties.delete` e um terceiro sem uso, **quando** clico "Excluir terceiro" e confirmo "Excluir", **então** ele é excluído logicamente ("Terceiro excluído"); com uso (código de fornecedor e, nas próximas fases, documentos — consultado por `IPartyUsageProvider`), a API responde 409 "O terceiro está em uso. Inative-o." e o `ConfirmDialog` oferece "Inativar" (`recovery`).

**Contrato para os outros módulos**
21. **Dado** um módulo que precisa de terceiros (Commercial, Purchasing, Finance, Inventory), **quando** consulta, **então** usa `IParties` de `Parties.Contracts` (resumo por id, inclusive de terceiro excluído, para documentos antigos; busca por papel e termo só com ativos; endereço e contato padrão; "tem o papel X?"); o teste de arquitetura impede outra referência.

**Permissões padrão (seed patch)**
22. **Dado** um tenant novo ou um tenant existente no próximo deploy, **quando** o seed patch `parties-default-permissions` roda, **então** os grupos do seed Comercial, Financeiro e Almoxarifado ganham `parties.view`, `create` e `edit`, e Engenharia ganha `parties.view`; `parties.delete` fica só com o Proprietário; o patch só acrescenta, roda uma vez (`identity.seed_patches`), não toca grupo sem `seed_key` e incrementa o `perm_version` dos membros (ADR 0016).

**LGPD, auditoria e log**
23. **[Segurança]** **Dado** um terceiro ou contato, **quando** altero documento, e-mail, telefone, nome do contato ou observações, **então** o diff da auditoria mostra esses campos mascarados (`[PersonalData]`; `document` sempre, mesmo de PJ), e o `RecordHistory` mostra o restante antes/depois.
24. **[Segurança]** **Dado** o teste de arquitetura com allowlist, **quando** roda, **então** toda propriedade de entidade auditável do Parties é `[PersonalData]` ou está na lista de não sensíveis (`kind`, `legal_name`, `trade_name`, `state_registration`, `municipal_registration`, `tax_regime`, `is_active`, papéis, campos de endereço de PJ).
25. **[Segurança]** **Dado** valores canário de CPF, e-mail e telefone, **quando** crio, busco e altero terceiros, **então** nenhum deles aparece no JSON bruto de `audit.audit_log` nem no sink de log; o `SensitivePropertyMask` passa a mascarar `cpf`, `document`, `rg`, `pis`, `email`, `phone`, `salary`, `pix`, `bank` e `birth`, e o Serilog respeita `[PersonalData]`.
26. **[Segurança]** **Dado** o `AuditableEntityRegistry`, **quando** dois módulos registram entidades com o mesmo nome curto (ex.: `Address`), **então** o registro usa o nome qualificado e lança exceção em duplicata (teste de unidade), e o histórico de um módulo nunca aparece com a permissão de outro.
27. **[Segurança]** **Dado** as respostas de detalhe, lista e busca de terceiros, **quando** saem da API, **então** levam `Cache-Control: no-store`.

**Permissões (3 camadas)**
28. **Dado** o módulo `parties` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite `parties`, 403 `forbidden` e o módulo some do dock; com `parties.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. O teste de arquitetura confere que todo endpoint do módulo tem `RequireModule` e uma policy `perm:`.

## Regras de negócio e técnicas
- **Documento:** opcional para PF e PJ (tabela 1.1); quando informado, validado e único. Gravado normalizado; exibido formatado.
- **Unicidade:** índice único parcial em `document` (`WHERE NOT is_deleted AND document IS NOT NULL`).
- **Busca:** índices `gin_trgm_ops` sobre `unaccent(legal_name)`, `unaccent(trade_name)` e `document` (CNPJ). CPF só por igualdade.
- **Situação:** `is_active` (aprovado, tabela 1.1). Excluir quando sem uso, inativar quando com uso (D10).
- **Endereço padrão:** `is_default` por tipo, único por terceiro e tipo (índice parcial).
- **Inscrição estadual:** só formato ("ISENTO" ou dígitos).
- **Fusão de duplicados:** só na migração (MIG-01b); na tela, o índice único impede o duplicado.
- **Soft delete** em `parties`; endereços e contatos são filhos e seguem o terceiro.
- **`[PersonalData]`:** `document`, `email`, `phone` e `notes` do terceiro; `name`, `email` e `phone` do contato.
- **Condição de pagamento padrão:** entra na CAD-05b (campo e validação pelo `IPaymentTerms` do Finance).
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em terceiro, papel, endereço e contato, com nome qualificado no registro.
- **Migration:** uma só, regerada no rebase, nunca editada à mão.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `parties.view`, `parties.create`, `parties.edit`, `parties.delete`. Concessões padrão por seed patch (CA 22).
- **Eventos:** nenhum nesta story.
- **Tabelas:** `parties.parties` (+ `is_active`), `party_roles` (único por terceiro e papel), `party_addresses` (+ `is_default`), `party_contacts`; `identity.seed_patches` (ADR 0016), criada aqui.

## Telas e estados
- Molde de lista: **Cadastros → Terceiros** (`/app/cadastros/terceiros`), h1 "Terceiros" com a linha "Clientes, fornecedores, instaladores e transportadoras num cadastro só."; "Novo terceiro" dentro de `<Can>`.
- Molde de criação: **Novo terceiro** (`/app/cadastros/terceiros/novo`), página em seções: Identificação (CPF ou CNPJ, PF/PJ, nomes, papéis), Endereço de cobrança, Contato principal (opcional).
- Molde de detalhe: **Terceiro** (`/app/cadastros/terceiros/$id`), abas na URL: Dados, Endereços, Contatos, Histórico de alterações (sempre a última).
- **Verbos:** "Salvar terceiro" / "Terceiro salvo"; "Salvar endereço"; "Salvar contato"; "Inativar terceiro" / "Terceiro inativado"; "Excluir terceiro" / "Terceiro excluído"; "Restaurar cadastro" / "Cadastro restaurado". Os diálogos repetem o verbo e o botão de sair diz "Voltar".
- **Estados:** carregando (skeleton); vazio ("Cadastre o primeiro cliente ou fornecedor." + "Novo terceiro"); vazio filtrado; erro com motivo e "Tentar de novo"; modo leitura sem `parties.edit`; guarda de alteração não salva.
- **Propositivo:** documento primeiro e duplicidade antes de tudo (CA 4); papel pré-marcado conforme a tela de origem (ex.: aberto a partir de Compras, vem Fornecedor); primeiro endereço de cada tipo vira padrão; faixa "Próximos passos" depois de criar.
- **400 px e teclado:** um CA de E2E para a criação e para o detalhe (padrões comuns, item 12).
- **Ajuda:** `Ajuda/cadastros-terceiros.md` (lista, `padrao: true`), `Ajuda/cadastros-terceiro-novo.md` e `Ajuda/cadastros-terceiro.md` com uma ajuda por aba (`dados`, `enderecos`, `contatos`, `historico`); `<Dica>` em "Papel", "Regime tributário", "Sem documento" e "Recebe cobrança".

## Plano de testes
- **Unidade:** `BrazilianDocuments` com CPF, CNPJ numérico e alfanumérico, máscara e dígitos repetidos (CA 1); inferência PF/PJ (CA 2); regra de último papel (CA 10); endereço padrão único por tipo (CA 12); registro qualificado da auditoria (CA 26).
- **Integração (Testcontainers):** criar PJ, PF e sem documento (CA 7–9); duplicidade com 409 e gravação simultânea (CA 4, 5); restaurar (CA 6); inativar e reativar (CA 11); endereços, IDOR e contatos (CA 12–14); dados fiscais (CA 15); lista com CPF mascarado e busca exata (CA 16, 17); 50 mil registros abaixo de 1 s (CA 18); busca da topbar com e sem permissão (CA 19); excluir com e sem uso (CA 20, provedor de uso de teste); seed patch aplicado uma vez, sem tocar grupo editado (CA 22); máscara e canário na auditoria e no log (CA 23, 25); `no-store` (CA 27); três camadas (CA 28).
- **Arquitetura:** só `Parties.Contracts` é referenciado por outros módulos (CA 21); allowlist de `[PersonalData]` (CA 24); todo endpoint com `RequireModule` e `perm:` (CA 28).
- **E2E (Playwright):** cadastrar cliente com endereço de cobrança, tentar o mesmo CNPJ como fornecedor e adicionar o papel; criação e detalhe a 400 px e só pelo teclado.

## Fora do escopo
- Consulta de CNPJ e de CEP: CAD-01b.
- Condição de pagamento padrão do terceiro: CAD-05b.
- Histórico comercial e financeiro do terceiro (R-TER-3) e pontualidade (R-TER-4): CA da COM-05 ou da story "Terceiro 360" (Fase 2).
- Importação do Dolibarr e fusão de duplicados: MIG-01b.
- Anexos no terceiro.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1): permissões padrão de `parties` e aplicação por seed patch; PF ou PJ sem documento aceito com "Sem documento"; regimes tributários conforme a lista do CA 15; IE só pelo formato; fusão de duplicados só na migração; situação Ativo/Inativo.
- Decidido em 18/10/2026 (D9): CPF mascarado na lista e na busca, busca por CPF só exata.
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `parties`, extensões `pg_trgm` e `unaccent`, `identity.seed_patches`, não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; seed patch das permissões padrão aplicado.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro; modo leitura.
- [ ] Ajuda da lista, da criação e de cada aba escrita; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e registro qualificado.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `BrazilianDocuments` no BuildingBlocks, com os casos de teste espelhados no front (FND-09d).
- [ ] Entidades `Party`, `PartyRole`, `PartyAddress`, `PartyContact` (Domain) com as regras de documento, papéis, situação e padrão.
- [ ] `IPartiesDb`, configurações `internal`, migration com índices parciais, `pg_trgm` e `unaccent`.
- [ ] `identity.seed_patches` e o mecanismo de seed patch (ADR 0016); patch `parties-default-permissions`.
- [ ] `AuditableEntityRegistry` com nome qualificado; `SensitivePropertyMask` e Serilog com `[PersonalData]`.
- [ ] Endpoints `/api/v1/parties` (CRUD, verificação de documento, papéis, endereços, contatos, inativar, restaurar) com `RequireModule("parties")`.
- [ ] `IParties` e `IPartyUsageProvider` em `Parties.Contracts`; provedor da busca da topbar.
- [ ] Telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD de PF e contatos, canário), @qa.
- Gate: CA 5 (duplicidade concorrente), CA 17 (CPF mascarado), CA 18 (desempenho), CA 22 (seed patch) e CA 25 (canário) obrigatórios.

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

- **Correções feitas na validação (18/10/2026, decisões do PO e revisão pré-Fase 1):** `BrazilianDocuments` trazido da CAD-06; CA da condição de pagamento padrão movido para a CAD-05b; endereço de cobrança no formulário de criação; duplicidade antes da consulta à Receita; documento opcional com `Tag` "Sem documento"; situação Ativo/Inativo e excluir × inativar; CPF mascarado e busca exata; busca da topbar; seed patch das permissões; CAs de segurança da §2.5 (IDOR, `no-store`, canário, allowlist, registro qualificado); verbos específicos e moldes da FND-09d.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, D9, D10, tabela 1.1) e correções da revisão pré-Fase 1 (§2.1 a §2.5); ordem 1 do Marco A | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
