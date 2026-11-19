# PDV-02 — Tela de venda nos modos Toque e Teclado/leitor: item por leitor, busca ou grade, quantidade, observação, venda em espera, cliente opcional e CPF, tudo a partir do dado local

| Campo | Valor |
|---|---|
| ID | PDV-02 |
| Título | Tela de venda nos modos Toque e Teclado/leitor: item por leitor, busca ou grade, quantidade, observação, venda em espera, cliente opcional e CPF, tudo a partir do dado local |
| Tamanho | G |
| Status | Review: pronta em 18/11/2026 na branch `feat/pdv-02-tela-venda` (categorias e foto na grade dependem do catálogo do caixa; ver Implementação) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 4: PDV moderno e prático) · [PRD do PDV](../01-PRD.md) seções 1.2 e 5 · [Fluxos](../04-fluxos-estados.md#1-venda) · [Rascunho B](../adr/rascunho-B-impressao-perifericos.md) item 7 (leitor) |
| Dependências | OFF-01; PP-26 (CPF na venda) |
| Bloqueia | PDV-03, CX-01 |
| Paridade com o mercado | Requisitos mínimos 22 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 3 (Fase 11) |

## User story
Como **operador de caixa**, quero **lançar os itens tão rápido quanto bipo ou toco**, para **a fila andar**.

## Contexto e referências
- Metas: item adicionado em menos de 100 ms; venda de 3 itens em dinheiro em até 10 segundos.
- Padrão de tela dedicada do `/fab`: botões de 56 px ou mais, sem o dock.

## Critérios de aceite
### Lançar item
1. **Dado** uma sessão de caixa aberta, **quando** bipo um código de barras, **então** o item entra na venda em menos de 100 ms, com nome, quantidade 1 e preço da tabela da loja, sem tocar a rede.
2. **Dado** um código de embalagem (ex.: caixa com 12), **quando** bipo, **então** o item entra com a quantidade da embalagem.
3. **Dado** um código que não existe no catálogo local, **quando** bipo, **então** soa o alerta e aparece "Produto não encontrado: 789…", sem perder o foco; a venda não muda.
4. **Dado** o leitor em modo teclado, **quando** bipo enquanto o foco está em outro campo, **então** o app reconhece a leitura pelo tempo entre as teclas e pelo Enter final e lança o item, sem escrever o código no campo errado.
5. **Dado** o mesmo item bipado duas vezes, **quando** confiro a venda, **então** há uma linha com quantidade 2 (configurável por caixa para linhas separadas).

### Modos de tela
6. **Dado** um caixa no modo Teclado/leitor, **quando** opero, **então** o foco fica sempre no campo de código; `*` antes do código define a quantidade (`3*789…`); as teclas `F` abrem as funções (buscar, quantidade, remover, espera, cliente, pagar); nada exige mouse.
7. **Dado** um caixa no modo Toque, **quando** opero, **então** vejo a grade de categorias e produtos com foto, botões de 56 px ou mais, e a busca por nome.
8. **Dado** a busca por nome, **quando** digito 3 letras, **então** os resultados aparecem em menos de 100 ms, do dado local.

### Editar a venda
9. **Dado** uma linha, **quando** altero a quantidade, **então** o total recalcula na hora; produto pesável aceita 3 casas; produto por unidade não aceita fração.
10. **Dado** o **último item lançado**, **quando** o removo logo em seguida (bipei errado), **então** ele sai sem supervisor, e a remoção fica registrada. **Dado** qualquer outra linha, **quando** removo, **então** a ação exige `pos.cancel_item` ou PIN de supervisor (PDV-04). A loja pode desligar a remoção livre do último item.
11. **Dado** uma linha, **quando** adiciono observação, **então** ela fica gravada e sai no cupom.
12. **Dado** uma venda com itens, **quando** uso "Deixar em espera", **então** ela vai para a lista de vendas em espera do caixa e uma nova começa; "Retomar" a traz de volta; fechar o caixa com venda em espera pede para retomar ou descartar.
13. **Dado** uma venda com itens, **quando** uso "Descartar venda", **então** preciso informar o motivo, e o descarte fica registrado.

### Cliente
14. **Dado** "Cliente", **quando** informo um CPF ou CNPJ, **então** o dígito é validado na hora; o documento fica cifrado no aparelho, **fora dos eventos da venda** (num registro à parte, que sobe ao lado do lote e pode ser apagado); não é obrigatório.
15. **Dado** rede, **quando** busco um cliente cadastrado, **então** posso ligá-lo à venda; sem rede, a busca fica desabilitada com "Disponível com internet." e o CPF avulso continua funcionando.

### Geral
16. **Dado** qualquer estado de rede, **quando** faço os passos acima, **então** o comportamento é o mesmo; a diferença é só a faixa de estado.
17. **Dado** nenhuma sessão de caixa aberta, **quando** abro a tela de venda, **então** vejo "Abra o caixa para começar a vender." com o botão "Abrir caixa".
18. **Dado** a queda de energia ou o fechamento da aba no meio da venda, **quando** reabro, **então** a venda está como estava.

## Regras de negócio e técnicas
- Cada ação (item lançado, quantidade, remoção, espera, descarte) é um evento gravado no `LocalStore` antes de a tela confirmar.
- O preço vem só da tabela; trocar preço é a PDV-04.
- O número da venda `{loja}-{caixa}-{seq}` é atribuído quando a venda é concluída ou descartada, não quando começa.
- A tela usa os tokens do produto e respeita o tema; nada de cor literal.

## Permissões, eventos e tabelas
- **Permissões:** `pos.view`, `pos.create`; `pos.cancel_item`.
- **Endpoints:** nenhum novo; a tela só fala com o `LocalStore`. Busca de cliente: contrato existente de Terceiros, por endpoint na lista do PIN.
- **Eventos do aparelho:** `SaleStarted`, `LineAdded`, `LineChanged`, `LineRemoved`, `SaleParked`, `SaleResumed`, `SaleDiscarded`, `CustomerSet`.

## Telas e estados
- **Venda** (`/pdv/venda`): itens à esquerda, total grande à direita com `tabular-nums`, barra de funções. Vazio: "Bipe ou busque um produto para começar.". Erro de armazenamento: "Não foi possível gravar neste aparelho." com o que fazer.
- **Vendas em espera:** lista com hora, itens e total.
- **Ajuda:** `Ajuda/pdv-venda.md` (nova), com a tabela de atalhos; `<Dica>` em "Venda em espera".

## Plano de testes
- **Vitest:** reconhecimento do leitor (CA 4); `3*código` (CA 6); pesável × unidade (CA 9); espera e descarte (CA 12 e 13); recuperação da venda (CA 18).
- **Desempenho (Vitest de bancada, com 20 mil itens no `LocalStore`):** CA 1 e 8.
- **E2E no fechamento do marco:** venda de 3 itens em dinheiro.

## Fora do escopo
- Pagamento (PDV-03). Desconto e troca de preço (PDV-04).
- Complementos e combos (CAT-05). Balança no caixa (IMP-05).
- 200 itens por venda sem degradar (PDV-12).

## Pontos em aberto
- QC-08: valor a partir do qual o CPF é obrigatório na NFC-e (só importa na Fase 13).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Implementação (18/11/2026)

Tudo no app do caixa (`src/Web/src/pdv-app`, `src/Web/src/pdv/sale`, `src/Web/src/pages/pdv/sale`), sobre a porta `LocalStore` da OFF-01. A tela de venda abre depois da entrada por PIN (PDV-01), na mesma rota `/pdv/` do app do caixa.

| CA | Situação | Como |
|---|---|---|
| 1 | Feito | `LocalStore.lookupBarcode` (memória) e `SaleRegister.addFromInput`: nome, quantidade 1, preço e versão da tabela da loja; o evento é gravado antes de a linha aparecer. Bancada com 20 mil itens: p95 abaixo de 100 ms (Vitest) |
| 2 | Feito | O código de barras do catálogo do caixa passa a guardar a `packQuantity` que o servidor já mandava (`PosSyncBarcode`); o código da caixa lança a quantidade da embalagem. Catálogo baixado antes: vale 1 até o próximo delta do item |
| 3 | Feito | Alerta sonoro (Web Audio) e "Produto não encontrado: 789…" na região de aviso (`role=alert`), sem tirar o foco; a venda e o diário não mudam |
| 4 | Feito | `ScanDetector`: rajada de 6 ou mais caracteres com no máximo 50 ms entre teclas e Enter no fim. Fora do campo de código, a tela devolve o campo ao valor de antes da rajada, cancela o Enter e lança o item |
| 5 | Feito | Soma na linha (`LineChanged` com `same-item`); "Uma linha por lançamento" em Ajustes do caixa (guardado no aparelho, ou seja, por caixa) |
| 6 | Feito | Modo Teclado: o foco volta sempre ao campo de código; `3*código` (e `0,350*código` no pesável); F2 Buscar, F3 Quantidade, F4 Observação, F6 Remover, F7 Espera, F8 Cliente, F9 Descartar, F10 Pagar; setas escolhem a linha ou o produto da busca |
| 7 | Parcial | Modo Toque com grade de botões grandes (≥ 56 px) e busca por nome. **Categorias e foto:** o catálogo do caixa (OFF-01) não traz categoria e o produto não tem foto; a grade mostra os produtos em ordem de nome, com a sigla no lugar da foto. Falta: a categoria (e, quando existir, a foto) no `PosSyncItem` e no catálogo local |
| 8 | Feito | `searchItems` em memória a partir de 3 letras; bancada com 20 mil itens abaixo de 100 ms |
| 9 | Feito | `parseQuantity`: pesável até 3 casas; por unidade só inteiro ("Este produto é vendido por unidade: use um número inteiro."). Pesável lançado sem quantidade pede a quantidade |
| 10 | Feito | O último lançamento sai sem supervisor (se somou na linha, sai só ele); outra linha exige `pos.cancel_item` (ação da sessão do operador). O PIN do supervisor é da PDV-04: sem a ação, a tela explica e não remove. Tudo vira `LineRemoved` (ou `LineChanged` `remove-last`) com o operador. A chave "a loja desliga a remoção livre" existe no `SaleRegister` (`freeRemoveLast`), ligada; o parâmetro da loja chega com os parâmetros da loja no catálogo (CX-01) |
| 11 | Feito | `LineChanged` com a observação (até 140 caracteres), mostrada na linha; o cupom é da IMP-01 |
| 12 | Feito | `SaleParked` e `SaleResumed`; "Em espera" lista hora, itens e total; retomar põe a venda da tela em espera no lugar dela. "Fechar caixa" com venda na tela ou em espera pede para retomar ou descartar. `<Dica>` em "Venda em espera" |
| 13 | Feito | Motivo obrigatório (com motivos comuns); `closeSale` numa transação: número `{prefixo do caixa}{seq}` (meta `sale.seq`), os eventos saem do diário e sobem como **uma** entrada `pos.sale` da fila de saída |
| 14 | Feito | Dígito do CPF e do CNPJ (inclusive alfanumérico) na hora; o documento é cifrado (AES-GCM) num registro à parte (`customerDocs`), e o `CustomerSet` leva só o id do registro. "Tirar cliente" e o descarte apagam o registro |
| 15 | Feito | `GET /api/v1/pos/customers?term=` (módulo `pos`, `pos.create`, lista `[PosPinSessionAllowed]`), pelo `IParties` de Terceiros: só clientes ativos, 10 por busca, nome e documento mascarado. Sem rede: "Disponível com internet." e o CPF avulso continua |
| 16 | Feito | A tela só fala com o `LocalStore`; a única chamada de rede é a busca do cliente |
| 17 | Feito (provisório) | Sem caixa aberto: "Abra o caixa para começar a vender." com "Abrir caixa". Até a CX-01, abrir o caixa só marca no aparelho (meta `cash.session`), sem fundo de troco nem evento |
| 18 | Feito | Diário `saleEvents` no IndexedDB (Dexie versão 2, só acréscimo, durabilidade `strict`); `SaleRegister.open` refaz a venda da tela e as em espera |

**Decisões e desvios:**
- **Onde ficam os eventos:** num diário à parte (`saleEvents`), não direto na fila de saída. Se cada lançamento fosse para a fila, a faixa diria "15 vendas a enviar" para uma venda só. A venda fecha (descarte aqui; conclusão na PDV-03) e sobe inteira, com o número, numa entrada `pos.sale`. O formato do lote é da OFF-02.
- **Chave do CPF (PP-26):** o WebCrypto não deriva chave de cifra de uma chave ECDSA não exportável. A chave AES-GCM do documento é não exportável, guardada ao lado da chave do aparelho (`bemaqui-pdv`) e apagada junto com ela (`forgetDeviceKey`): sem a chave do aparelho, o documento vira ilegível. A busca por índice cego (PP-26, item 3) entra com a devolução (PDV-06).
- **Rota:** a venda abre em `/pdv/` (o app do caixa tem uma rota só, dentro do escopo do service worker), não em `/pdv/venda`.
- **Sem rede depois de recarregar a página:** o token da sessão por PIN vive só em memória; recarregada a página sem rede, a venda continua guardada, mas a entrada do operador sem rede é da OFF-03.
- **Saldo (PP-02):** a tela não trata saldo; o "vender sem saldo" e o bloqueio são da PDV-05.
- Konebras (perfil fábrica): nada novo aparece, porque tudo está no app do caixa e na rota com o módulo `pos`.

**Testes:** Vitest `src/pdv/sale` (leitor, `3*código`, embalagem, pesável e unidade, soma e linhas separadas, remoção, observação, espera, descarte com número, CPF cifrado, recuperação e bancada de 20 mil itens) e `src/pages/pdv/sale/PdvSale.test.tsx` (caixa fechado, leitura no campo e com o foco em outro campo). .NET: `PosRulesUnitTests` (máscara do documento), `PosTests` (busca do cliente pela sessão por PIN), `PinSessionAllowListTests`, `EndpointAuthorizationTests` e `OpenApiDocumentTests`. E2E de venda de 3 itens: no fechamento do marco (plano de testes). **A conferir:** os testes .NET não rodaram nesta máquina (o Docker Desktop recusou montar o disco D:, "mkdir /run/desktop/mnt/host/d: file exists"); por isso a entrada `SearchPosCustomers` do `openapi.json` foi escrita no formato do gerador e o cliente Orval regenerado dela. A primeira rodada do CI confirma (`OpenApiDocumentTests` compara o documento inteiro).

Ajuda: `Ajuda/pdv-venda.md` (nova, com os atalhos), embutida na ajuda do app do caixa. Sem migration (o banco do servidor não mudou); o IndexedDB do aparelho ganhou a versão 2.

## Pendências de segurança
Revisão de segurança de 18/11/2026. Corrigidos nesta story: **achado 1** (rate limit `pos-customer-search` na busca de cliente, 20 por minuto por tenant, usuário e aparelho, configurável em `RateLimit:Auth:PosCustomerSearchPerMinute`; CPF mascarado como `***.***.247-**`, CNPJ inteiro), **achado 2** (busca só por nome; termo com 11 dígitos ou mais não sai do aparelho, para o CPF nunca ir na URL) e **achado 3** (diminuir a quantidade de uma linha que não é o último lançamento exige `pos.cancel_item`, com a mesma regra da remoção; todo `LineChanged` leva o `operatorId`).

Registrados, sem implementar:

- **Achado 4 (requisitos da OFF-02, que o servidor deve cumprir na subida):** a regra da venda roda no aparelho, que não é confiável; na subida, o servidor:
  - refaz o replay da venda a partir dos eventos, sem confiar no estado que o aparelho calculou;
  - valida `last-item` (remoção) e `remove-last` (desfazer o último lançamento que somou na linha): só valem para o último lançamento de fato, e a redução de quantidade de outra linha só vale com autorização;
  - lê o `freeRemoveLast` da configuração do servidor (a da loja ou do caixa), nunca o valor guardado no aparelho;
  - exige quem autorizou em `authorized` (o supervisor, não só o operador) e confere, pelo histórico de permissões, que essa pessoa tinha `pos.cancel_item` no instante do evento;
  - amarra o `operatorId` de cada evento ao log de sessões por PIN do aparelho (o operador tinha sessão aberta naquele caixa naquele horário);
  - gera o relatório de exceções por operador (remoções, reduções, descartes e eventos recusados na validação).
- **Achado 5:** limpar os `customerDocs` órfãos (documento cifrado sem venda aberta, em espera ou na fila de saída) ao abrir o caixa.
- **Achado 6:** alertar o operador quando houver 11 ou 14 dígitos seguidos na observação da linha ou no motivo do descarte (CPF ou CNPJ fora do campo cifrado); escapar a observação no XML do cupom e da NFC-e.
- **Achado 7:** o leitor de código de barras ignora o campo de PIN e a folha aberta (PDV-04), para uma leitura não cair no PIN do supervisor nem lançar item por trás de uma folha.
- **Achado 8:** testes de autorização da rota `GET /api/v1/pos/customers` para operador sem `pos.create`, módulo `pos` desligado e sessão de outro tenant.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 18/11/2026 | 1.0 | Implementada (branch `feat/pdv-02-tela-venda`): venda no `LocalStore` com diário de eventos, modos Toque e Teclado/leitor, leitor reconhecido em qualquer campo, espera, descarte com número, CPF cifrado fora dos eventos (PP-26) e busca do cliente cadastrado pela sessão por PIN. CA 7 parcial (sem categoria nem foto no catálogo do caixa); CA 17 provisório até a CX-01. | Claude (dev) |
| 18/11/2026 | 1.1 | Revisão de segurança: corrigidos os achados 1 a 3 (rate limit e máscara curta do CPF na busca de cliente, busca só por nome sem CPF na URL, redução de quantidade com a regra da remoção e `operatorId` em todo `LineChanged`); achados 4 a 8 registrados em "Pendências de segurança" (o 4 copiado para a OFF-02). | Claude (dev) |
