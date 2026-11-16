# PDV-02 — Tela de venda nos modos Toque e Teclado/leitor: item por leitor, busca ou grade, quantidade, observação, venda em espera, cliente opcional e CPF, tudo a partir do dado local

| Campo | Valor |
|---|---|
| ID | PDV-02 |
| Título | Tela de venda nos modos Toque e Teclado/leitor: item por leitor, busca ou grade, quantidade, observação, venda em espera, cliente opcional e CPF, tudo a partir do dado local |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
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

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
