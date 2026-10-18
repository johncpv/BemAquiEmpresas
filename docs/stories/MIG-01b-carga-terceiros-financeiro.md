# MIG-01b — Carga de terceiros, contatos, condições de pagamento e contas bancárias do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01b |
| Título | Importar `llx_societe`, `llx_socpeople`, `llx_c_payment_term` e `llx_bank_account` (com saldo na data de corte), juntando documentos repetidos num terceiro com vários papéis |
| Tamanho | M (quebra da MIG-01) |
| Status | Draft |
| Requisitos | R-TER-5 · R-TER-1 · R-TER-2 · R-FIN-2 (saldo inicial) · PRD §6 LGPD |
| Dependências | MIG-01a, CAD-01a, CAD-05b (CAD-05a para a conta contábil padrão, se usada) |
| Bloqueia | MIG-01c (códigos do fornecedor), MIG-01e (títulos) |
| Ordem de execução | 12 |

## User story
Como **proprietário**, quero **trazer do Dolibarr todos os clientes e fornecedores com contatos, condições de pagamento e as contas bancárias com o saldo do dia da virada**, para **não recadastrar nada e começar o caixa do saldo certo**.

## Contexto e referências
- PRD R-TER-5 (importação de clientes e fornecedores), §5.2 (um terceiro com vários papéis; o mesmo CNPJ pode ter vários papéis).
- MIG-01a (mapeamento, staging, chaves externas, simulação, relatório) e `docs/operacao/migracao-dolibarr.md`.
- `docs/06-pendencias-riscos.md` P-07: 118 terceiros no Dolibarr; 166 lançamentos bancários (só o saldo migra, MIG-01a ponto 4).
- CAD-01a (regras do terceiro: documento validado e único, papéis, endereços, contatos), CAD-05b (contas bancárias, saldo inicial, condições de pagamento com parcelas somando 100%).

## Critérios de aceite
**Terceiros**
1. **Dado** um `llx_societe` com `client` = 1 ou 3, **quando** a carga roda, **então** o terceiro recebe o papel Cliente; com `fournisseur` = 1, o papel Fornecedor; com os dois, os dois papéis no mesmo terceiro.
2. **Dado** o campo de documento definido no mapeamento (CNPJ ou CPF, conforme `fk_typent` ou o tamanho do número), **quando** o documento é válido, **então** o terceiro entra como PJ ou PF com o documento normalizado; quando é inválido ou falta, o registro vai para o relatório como erro "CNPJ inválido no Dolibarr (id {rowid}, {nome})." e não é importado (o PO corrige no Dolibarr e reexecuta).
3. **Dado** dois ou mais `llx_societe` com o mesmo documento (ex.: um cadastro de cliente e outro de fornecedor), **quando** a carga roda, **então** nasce um único terceiro com a união dos papéis, as chaves externas dos dois apontam para ele e o relatório mostra "mesclado" com os ids de origem; os dados principais vêm do registro mais recente (`tms`), e os endereços e contatos dos dois são mantidos.
4. **Dado** um documento que já existe no BemAqui sem chave externa (cadastrado à mão antes da carga), **quando** a carga roda, **então** o terceiro existente recebe a chave externa e os papéis que faltam, sem sobrescrever os dados (resultado "vinculado a cadastro existente").
5. **Dado** o endereço do `llx_societe` (`address`, `zip`, `town`, `fk_departement` → UF via `llx_c_departements`), **quando** é importado, **então** vira o endereço de cobrança padrão; o número e o complemento são separados do logradouro quando possível e, quando não, ficam no logradouro com o aviso "endereço sem número separado" no relatório.
6. **Dado** um `llx_societe` com `status` = 0 (encerrado no Dolibarr), **quando** a carga roda, **então** segue a regra do ponto em aberto 2 (proposta: importar e excluir logicamente, para manter o histórico dos títulos).
7. **Dado** `note_private` e `note_public`, **quando** importados, **então** vão para `notes`, com o prefixo da origem.
8. **Dado** `cond_reglement`, **quando** importado, **então** a condição de pagamento padrão do terceiro aponta para a condição importada no CA 11.

**Contatos**
9. **Dado** `llx_socpeople` ligado a um `llx_societe` importado, **quando** a carga roda, **então** vira contato do terceiro (nome = `firstname` + `lastname`, cargo = `poste`, e-mail validado, telefone = `phone` ou `phone_mobile`); e-mail inválido entra vazio com aviso no relatório; contato de terceiro não importado vai para "ignorado: terceiro não importado".

**Condições de pagamento**
10. **Dado** `llx_c_payment_term` ativo, **quando** a carga roda, **então** vira uma condição com uma parcela de 100% em `nbjour` dias; condições com regra de "fim do mês" (`type_cdr`) ou deslocamento (`decalage`) entram com o aviso "regra de fim de mês não suportada; conferir" no relatório.
11. **Dado** uma condição do Dolibarr com o mesmo nome de uma condição do seed (ex.: "30 dias"), **quando** a carga roda, **então** é vinculada à existente, sem duplicar.

**Contas bancárias**
12. **Dado** `llx_bank_account` ativo, **quando** a carga roda com `--cutoff`, **então** vira conta Banco (ou Caixa, quando `courant` = 2) com banco, agência e conta, e o saldo inicial é a soma de `llx_bank.amount` da conta até a data de corte, com `opening_date` = data de corte; o relatório mostra o saldo calculado para conferência com o extrato.
13. **Dado** uma conta já importada e com movimentação no BemAqui, **quando** a carga roda de novo com outro saldo, **então** o saldo não é alterado e o relatório mostra "conflito: a conta já tem movimentação" (regra da CAD-05b CA 4).

**Reexecução e relatório**
14. **Dado** a carga feita, **quando** rodo de novo sem mudança no Dolibarr, **então** todos os registros saem como "sem alteração" e nada é duplicado (MIG-01a CA 7).
15. **Dado** o relatório da carga, **quando** confiro os totais, **então** lidos de `llx_societe` = importados + mesclados + vinculados + ignorados + erros, e os 118 terceiros do Dolibarr (P-07) estão todos contados.

**LGPD e auditoria**
16. **[Segurança]** **Dado** terceiros PF e contatos, **quando** importados, **então** CPF, e-mail e telefone seguem mascarados na auditoria e no relatório (MIG-01a CA 15 e 17).

## Regras de negócio e técnicas
- Grava só por `IPartyImport` (`Parties.Contracts`) e pela interface de importação do dono das condições e contas (CAD-05b), nunca por tabela de outro módulo.
- `IPartyImport.Upsert(externalRef, dados)` aplica as mesmas validações da CAD-01a (documento, papel, e-mail) e devolve o resultado para o relatório.
- A fusão por documento (CA 3) usa o documento normalizado como chave de agrupamento dentro da carga.
- O saldo bancário é `numeric(18,2)`; a soma dos lançamentos é feita no MariaDB com `SUM` e convertida pela regra da MIG-01a CA 16.
- Instalador e Transportadora não existem no Dolibarr padrão; ver ponto em aberto 1.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma (CLI, MIG-01a).
- **Eventos:** nenhum novo.
- **Tabelas:** grava em `parties.*` e nas tabelas da CAD-05b pelos Contracts; registra em `migration.*` (MIG-01a).

## Telas e estados
- Sem tela. Os terceiros importados aparecem em Cadastros → Terceiros; o histórico de cada um mostra a importação.
- **Ajuda:** pergunta frequente "Os clientes do Dolibarr já estão aqui?" em `Ajuda/cadastros-terceiros.md`, apontando para o relatório da importação.

## Plano de testes
- **Unidade:** mapeamento `client`/`fournisseur` → papéis (CA 1); detecção de PF/PJ e validação do documento (CA 2); separação de número do logradouro (CA 5); condição com `nbjour` (CA 10).
- **Integração (Testcontainers PostgreSQL + MariaDB, fixture anonimizada):** papéis, documento inválido, fusão de duplicados e vínculo com cadastro existente (CA 1–4); encerrado (CA 6); contatos (CA 9); condições e vínculo com o seed (CA 10, 11); saldo na data de corte e conflito com movimentação (CA 12, 13); reexecução (CA 14); soma do relatório (CA 15); máscara (CA 16).
- **Ensaio manual:** simulação contra a cópia da base real e conferência do relatório com o PO (118 terceiros, saldos com o extrato).

## Fora do escopo
- Itens, locais e códigos do fornecedor: MIG-01c.
- Títulos: MIG-01e.
- Lançamentos bancários individuais.

## Pontos em aberto
1. **Instaladores e transportadoras:** como identificá-los no Dolibarr (categoria de fornecedor `llx_categorie_fournisseur`, extrafield ou lista manual do PO).
2. **Terceiro encerrado** (`status` = 0): importar e excluir logicamente (proposta), importar ativo ou ignorar.
3. **Prospects** (`client` = 2): importar como Cliente, ignorar ou marcar com observação.
4. **Campo do CNPJ/CPF** na base real (`idprof1`, `siren`, `siret` ou extrafield): confirmar no inventário da MIG-01a.
5. **Saldo bancário de partida:** soma dos lançamentos até a data de corte (proposta) ou saldo informado pelo PO a partir do extrato.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e integração (Testcontainers PostgreSQL + MariaDB) passando.
- [ ] `IPartyImport` e a interface de importação da CAD-05b nos Contracts.
- [ ] Mapeamento atualizado em `docs/operacao/migracao-dolibarr.md`.
- [ ] Simulação contra a cópia da base real conferida com o PO.
- [ ] Auditoria e máscara de dados pessoais conferidas.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PartiesLoader`, `PaymentTermsLoader`, `BankAccountsLoader` (`IDolibarrLoader`).
- [ ] `IPartyImport` em `Parties.Contracts`; interface de importação de condições e contas.
- [ ] Fusão por documento e vínculo com cadastro existente.
- [ ] Fixture anonimizada com casos de duplicado, PF, documento inválido e conta com saldo.

## Quality gates
- Agentes previstos: @dev, @data-engineer, @qa.
- Gate: CA 3 (fusão) e CA 14 (reexecução) obrigatórios.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela; ajuda só com a pergunta frequente) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** MIG-01a pronta; pontos em aberto 1 (instaladores e transportadoras) e 4 (campo do documento).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
