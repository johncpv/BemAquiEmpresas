# CAD-02b — Conversões de unidade por item e códigos do fornecedor

| Campo | Valor |
|---|---|
| ID | CAD-02b |
| Título | Fator de conversão de unidade por item (ex.: compra em kg, consumo em m²), serviço `IUnitConversion` e de-para do código e da unidade do fornecedor |
| Tamanho | M (quebra da CAD-02) |
| Status | Done |
| Requisitos | R-EST-3 · R-FIS-5 (de-para de itens do fornecedor, preparação) · R-PRP-3 · R-SEC-5 |
| Dependências | CAD-02a (itens e unidades), CAD-01a (fornecedores), FND-09d (`RowsEditor`, `Combobox`) |
| Bloqueia | MIG-01c (códigos do fornecedor do Dolibarr), EST-01, CMP-03, CMP-04, FIS-04, ENG-02 |
| Ordem de execução | 9 (Marco B — catálogo real) |

## User story
Como **almoxarife e comprador**, quero **dizer quanto vale cada unidade de compra ou de consumo na unidade de estoque do item e lembrar o código que cada fornecedor usa**, para **comprar em kg, consumir em m² e receber a nota do fornecedor sem converter na calculadora nem procurar o item de novo**.

## Contexto e referências
- PRD R-EST-3 (conversão de unidades por item, ex.: compra em kg, consumo em m², fator por item), R-FIS-5 (NF-e de entrada por XML com de-para de itens do fornecedor), FIS-04 (de-para "lembrado para a próxima vez").
- `docs/03-modelo-dados.md` → `inventory.item_unit_conversions` (`item_id`, `from_unit_id`, `to_unit_id`, `factor`) e `inventory.item_supplier_codes` (`item_id`, `party_id`, `supplier_code`, `supplier_unit`, `conversion_factor`).
- Backlog CAD-02 (CA): conversão kg → m² por item aplicada nos movimentos. Como os movimentos são do EST-01, o CA desta story testa o serviço de conversão que os movimentos vão usar.
- `ModuleCatalog`: `inventory.view`, `inventory.edit`.
- Decidido em 18/10/2026 (tabela 1.1): fator com 6 casas e resultado com 4, arredondado com `AwayFromZero`; conversão só por item; unidade do fornecedor em texto livre.
- ADR 0016 (IDOR pai/filho; id de outro módulo validado pelo Contracts do dono). Padrões comuns a todos os cadastros: `docs/stories/README.md`.

## Critérios de aceite
**Conversões**
1. **Dado** `inventory.edit` e o item "Chapa de aço 2 mm" com unidade de estoque KG, **quando** cadastro na aba Conversões "1 M2 = 15,7 KG" e clico "Salvar conversão", **então** a conversão é gravada, o toast diz "Conversão salva" e a prévia mostra "100 KG = 6,3694 M2".
2. **Dado** uma conversão, **quando** salvo, **então** uma das duas unidades é sempre a unidade de estoque do item, o fator é maior que zero, as unidades são diferentes e o par não se repete no item (409 "Este item já tem conversão entre {de} e {para}."); a conversão inversa é calculada, não gravada de novo.
3. **Dado** o item do CA 1, **quando** chamo `IUnitConversion.Convert(itemId, 31,4, KG, M2)`, **então** recebo 2,0000 M2; e `Convert(itemId, 2, M2, KG)` devolve 31,4000 KG. O resultado é arredondado para 4 casas (`numeric(18,4)`).
4. **Dado** uma conversão entre duas unidades que não são a de estoque (ex.: M2 → M), **quando** chamo `Convert`, **então** o serviço passa pela unidade de estoque (M2 → KG → M), com uma única regra de arredondamento no fim.
5. **Dado** um item sem conversão para a unidade pedida, **quando** chamo `Convert`, **então** recebo o erro `unit_conversion_missing` com "O item {código} não tem conversão de {de} para {para}. Cadastre em Estoque → Itens → Conversões.", e a chamada não converte com fator 1.
6. **Dado** a mesma unidade de origem e destino, **quando** chamo `Convert`, **então** recebo a mesma quantidade.
7. **Dado** um item com movimento, **quando** altero o fator de uma conversão, **então** a alteração é aceita, auditada e vale só para os próximos documentos; a tela avisa "Os movimentos já lançados não mudam." (os movimentos guardam a quantidade na unidade de estoque).

**Códigos do fornecedor**
8. **Dado** `inventory.edit`, **quando** cadastro na aba Fornecedores o fornecedor, o código que ele usa, a unidade dele e o fator para a unidade de estoque, **então** o vínculo é gravado ("Código do fornecedor salvo"); o fornecedor é escolhido no `Combobox`, que busca só terceiros ativos com o papel Fornecedor por `IParties`.
9. **Dado** um terceiro sem o papel Fornecedor, **quando** tento vincular, **então** a API responde 400 "{nome} não é fornecedor. Adicione o papel Fornecedor no cadastro." e a tela leva ao cadastro do terceiro (R-PRP-5).
10. **Dado** o mesmo código do mesmo fornecedor já ligado a outro item, **quando** salvo, **então** a API responde 409 "O código {código} do fornecedor {nome} já está ligado ao item {item}."
11. **Dado** FIS-04 ou CMP-04, **quando** recebem uma linha de nota com CNPJ do fornecedor e código do produto, **então** chamam `IItemSupplierCodes.Find(partyId, supplierCode)` de `Inventory.Contracts` e recebem o item, a unidade e o fator, ou "não encontrado".
12. **Dado** o fornecedor excluído logicamente (CAD-01a), **quando** abro o item, **então** o vínculo continua visível com o chip "Fornecedor excluído" e não é usado pelo `Find`.

**Segurança e remoção**
13. **[Segurança]** **Dado** as rotas `/api/v1/inventory/items/{itemId}/conversions/{conversionId}` e `/supplier-codes/{codeId}`, **quando** o registro filho pertence a outro item, **então** a API responde 404, sem ler nem alterar (IDOR pai/filho); o `party_id` enviado é validado por `IParties` (existe e é Fornecedor), nunca lido direto de `parties.*`.
14. **Dado** `inventory.edit`, **quando** clico "Remover conversão" ou "Remover código do fornecedor" e confirmo "Remover", **então** o vínculo é excluído logicamente e auditado; os movimentos já lançados não mudam.

**Permissões (3 camadas)**
15. **Dado** o módulo `inventory` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released`; sem grupo que habilite `inventory`, 403 `forbidden`; com `inventory.view` sem `edit`, as abas ficam em modo leitura e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:`.

**Auditoria**
16. **Dado** qualquer alteração de conversão ou código do fornecedor, **quando** abro o histórico do item, **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Fator** `numeric(18,6)` (decidido; registrado no 03): "1 {from} = {factor} {to}".
- **Arredondamento** do `Convert`: 4 casas, `MidpointRounding.AwayFromZero`, aplicado uma vez só no fim do cálculo.
- **Unicidade:** (`item_id`, par de unidades sem ordem) em conversões; (`party_id`, `supplier_code`) em códigos do fornecedor, entre os não excluídos.
- `IUnitConversion` e `IItemSupplierCodes` ficam em `Inventory.Contracts`; a validação do papel Fornecedor usa `IParties` de `Parties.Contracts` (CAD-01a).
- `supplier_unit` é texto livre (unidade como vem na nota, ex.: "CX", "KG"), porque a nota pode trazer unidades que o tenant não cadastrou.
- **Ids:** Guid v7. **Auditoria:** `IAuditable` nas duas tabelas.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `inventory.view`, `inventory.edit`.
- **Eventos:** nenhum.
- **Tabelas:** `inventory.item_unit_conversions` e `inventory.item_supplier_codes` (03).

## Telas e estados
- **Detalhe do item, aba Conversões** (`/app/estoque/itens/$id?aba=conversoes`): lista "1 {de} = {fator} {para}" com prévia de conversão ("Converter 100 KG em M2 → 6,3694"); edição no `RowsEditor`, com "Remover {linha n}".
- **Detalhe do item, aba Fornecedores** (`?aba=fornecedores`): fornecedor (`Combobox` sobre `IParties`), código, unidade do fornecedor, fator; último preço fica para a CMP-03.
- **Estados:** carregando; vazio ("Este item não tem conversões. Cadastre se ele é comprado ou consumido em outra unidade." + "Nova conversão"; "Nenhum código de fornecedor. Ele é preenchido sozinho no primeiro recebimento por nota, ou cadastre agora." + "Vincular fornecedor"); erro com motivo e "Tentar de novo".
- **Verbos:** "Salvar conversão" / "Conversão salva"; "Salvar código do fornecedor" / "Código do fornecedor salvo"; "Remover" / "Removido".
- **Propositivo:** a unidade de estoque vem preenchida num dos lados; a prévia aparece enquanto digita o fator.
- **400 px e teclado:** um CA de E2E para a aba Conversões.
- **Ajuda:** abas `conversoes` e `fornecedores` em `Ajuda/estoque-item.md` (uma ajuda por aba); `<Dica>` em "Fator" com o exemplo da chapa.

## Plano de testes
- **Unidade:** `Convert` direto, inverso, passando pela unidade de estoque e com arredondamento (CA 1, 3, 4, 6); erro sem conversão (CA 5); validação do par (CA 2).
- **Integração (Testcontainers):** gravação e unicidade (CA 2, 10); papel Fornecedor via `IParties` (CA 9); `Find` com fornecedor ativo e excluído (CA 11, 12); fator alterado com aviso (CA 7); IDOR e validação do `party_id` (CA 13); remover (CA 14); três camadas (CA 15); auditoria (CA 16).
- **Arquitetura:** Inventory só referencia `Parties.Contracts`.
- **E2E (Playwright):** cadastrar a conversão da chapa e ver a prévia; aba Conversões a 400 px e só pelo teclado.

## Fora do escopo
- Uso das conversões nos movimentos, requisições e recebimentos: EST-01, EST-03, CMP-04.
- Preenchimento automático do de-para no recebimento da NF-e: FIS-04.
- Último preço e prazo do fornecedor: CMP-02/CMP-03.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1): fator `numeric(18,6)` e resultado com 4 casas, `AwayFromZero`; conversão só por item (R-EST-3); unidade do fornecedor em texto livre.
- Nenhum ponto em aberto.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [x] Migration criada (não destrutiva).
- [x] Permissões usadas só do `ModuleCatalog`.
- [x] Abas com estados de carregando, vazio e erro.
- [x] Ajuda das abas escrita; teste de cobertura passando.
- [x] Padrões e próximo passo propositivos avaliados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado (precisão do fator); story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `ItemUnitConversion` e `ItemSupplierCode` com as regras de par e unicidade.
- [x] `UnitConverter` (Domain) + `IUnitConversion` e `IItemSupplierCodes` em `Inventory.Contracts`.
- [x] Endpoints em `/api/v1/inventory/items/{id}/conversions` e `/supplier-codes`.
- [x] Abas Conversões e Fornecedores; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 3 e CA 4 (conversão e arredondamento) obrigatórios.

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

- **Correções feitas na validação:** —
- **Correções feitas na revalidação (18/10/2026):** pontos em aberto decididos (tabela 1.1); IDOR pai/filho e validação do `party_id` pelo Contracts (§2.5); remover vínculo; verbos e CA de 400 px e teclado (§2.4); ordem 9 do Marco B.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-02) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, tabela 1.1) e correções da revisão pré-Fase 1 (§2.4, §2.5); ordem 9 do Marco B | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 19/10/2026 | 1.0 | Implementada. Desvios: validação e terceiro sem papel Fornecedor respondem 422 (convenção da API), não 400; as abas usam lista e painel lateral, não o `RowsEditor` (cada linha tem verbo próprio, "Salvar conversão"); o histórico de cada conversão e de cada código fica no painel dela (o histórico da aba do item mostra o item); o terceiro com código de fornecedor passa a não ser excluído (`IPartyUsageProvider`), só inativado; o `Convert` responde 409 `unit_conversion_missing` | @dev |
