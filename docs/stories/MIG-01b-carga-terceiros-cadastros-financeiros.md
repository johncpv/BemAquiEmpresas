# MIG-01b — Carga de terceiros, contatos, condições de pagamento e contas bancárias do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01b |
| Título | Importar `llx_societe` (papéis, documento, endereço, situação), `llx_socpeople`, `llx_c_payment_term` (de-para com o seed) e `llx_bank_account` (só o cadastro, sem saldo), juntando documentos repetidos num terceiro com vários papéis |
| Tamanho | M (quebra da MIG-01) |
| Status | Ready |
| Requisitos | R-TER-5 · R-TER-1 · R-TER-2 · R-FIN-2 (cadastro das contas) · PRD §6 LGPD |
| Dependências | MIG-01a, CAD-01a, CAD-05b. ~~Pré-condição: painel da plataforma fora do modo aberto~~ (dispensada pelo PO em 18/10/2026, adendo da ADR 0018). Origem: API REST do Dolibarr (ADR 0017, adendo 2) |
| Bloqueia | MIG-01c (fornecedores dos códigos do fornecedor), GOL-01 |
| Ordem de execução | 4 (Marco A — terceiros reais) |

## User story
Como **proprietário**, quero **trazer do Dolibarr todos os clientes e fornecedores com contatos, e também as condições de pagamento e o cadastro das contas bancárias**, para **não recadastrar nada e ver os meus terceiros reais na homologação logo no começo da fase**.

## Contexto e referências
- PRD R-TER-5, §5.2 (um terceiro com vários papéis).
- MIG-01a (snapshot, staging, chaves externas, simulação, relatório, tela) e `docs/operacao/migracao-dolibarr.md`.
- **ADR 0017** (só cadastros; contas bancárias sem saldo; nada de IBAN, BIC, titular nem `birthday`), **ADR 0018** (painel da plataforma fora do modo aberto antes da primeira carga real; expurgo da staging).
- Decisão do PO (18/10/2026): a migração traz todos os cadastros, inclusive condições de pagamento e contas bancárias e caixas sem saldo. Saldos, títulos e movimentos começam no BemAqui.
- Revisão pré-Fase 1: tabela 1.1 (MIG-01b e CAD-01a: instaladores pela lista do PO, prospects, encerrados inativos), §2.5 (`note_private`, campos proibidos).
- CAD-01a (regras do terceiro), CAD-05b (condições, contas, conta sem saldo com `Tag` "Saldo inicial não informado").

## Critérios de aceite
**Pré-condição**
1. ~~**[Segurança]** **Dado** um ambiente que não é Development com `Platform:AllowedNetworks` vazio (painel da plataforma em modo aberto), **quando** rodo uma carga real desta story, **então** o comando recusa com "Feche a allowlist da plataforma antes da primeira carga real (ADR 0018)." e código ≠ 0; a simulação (`--dry-run`) continua permitida. O runbook pede também o arquivo `.allow` do nginx com os IPs do PO.~~ **Dispensado pelo PO em 18/10/2026 (adendo da ADR 0018): a carga não depende da allowlist.**

**Terceiros**
2. **Dado** um `llx_societe` com `client` = 1 ou 3, **quando** a carga roda, **então** o terceiro recebe o papel Cliente; com `fournisseur` = 1, o papel Fornecedor; com os dois, os dois papéis no mesmo terceiro.
3. **Dado** um `llx_societe` com `client` = 2 (prospect), **quando** a carga roda, **então** entra como Cliente, com a observação "Prospect no Dolibarr".
4. **Dado** a lista de ids de instaladores e transportadoras no arquivo de de-para (ou a categoria de fornecedor indicada nele), **quando** a carga roda, **então** esses terceiros recebem o papel Instalador ou Transportadora; sem a lista, entram como Fornecedor e o relatório avisa "papel especial não informado no de-para".
5. **Dado** o documento na coluna definida no mapeamento (CNPJ ou CPF conforme `fk_typent` ou a quantidade de caracteres), **quando** é válido (`BrazilianDocuments`), **então** o terceiro entra como PJ ou PF com o documento normalizado; quando é inválido, vai para o relatório como erro "CNPJ inválido no Dolibarr (id {rowid}, {nome})." e não é importado; quando falta, entra sem documento, com a `Tag` "Sem documento" (CAD-01a) e um aviso.
6. **Dado** dois ou mais `llx_societe` com o mesmo documento, **quando** a carga roda, **então** nasce um único terceiro com a união dos papéis, as chaves externas de todos apontam para ele e o relatório mostra "mesclado" com os ids de origem; os dados principais vêm do registro mais recente (`tms`), e os endereços e contatos de todos são mantidos.
7. **Dado** um documento que já existe no BemAqui sem chave externa (cadastrado à mão), **quando** a carga roda, **então** o terceiro existente recebe a chave externa e os papéis que faltam, sem sobrescrever os dados ("vinculado a cadastro existente").
8. **Dado** o endereço do `llx_societe` (`address`, `zip`, `town`, `fk_departement` → UF por `llx_c_departements`), **quando** é importado, **então** vira o endereço de cobrança padrão; o número e o complemento são separados do logradouro quando possível e, quando não, ficam no logradouro com o aviso "endereço sem número separado".
9. **Dado** um `llx_societe` com `status` = 0 (encerrado), **quando** a carga roda, **então** entra **inativo** (não excluído).
10. **[Segurança]** **Dado** `note_private` e `note_public`, **quando** importados, **então** o HTML do Dolibarr é convertido para texto, sem caracteres de controle e bidi, e o resultado vai para `notes` com o prefixo da origem ("Dolibarr — nota privada:").

**Contatos**
11. **Dado** `llx_socpeople` ligado a um terceiro importado, **quando** a carga roda, **então** vira contato (nome = `firstname` + `lastname`, cargo = `poste`, e-mail validado, telefone = `phone` ou `phone_mobile`); e-mail inválido entra vazio com aviso; contato com `statut` = 0 vai para "ignorado: contato inativo no Dolibarr"; contato de terceiro não importado vai para "ignorado: terceiro não importado".
12. **[Segurança]** **Dado** o snapshot, **quando** a carga lê terceiros e contatos, **então** `birthday`, IBAN, BIC e titular de conta não existem no snapshot (fora do GRANT da MIG-01a) nem no BemAqui.

**Condições de pagamento**
13. **Dado** um `llx_c_payment_term`, **quando** a carga roda, **então** ele é ligado a uma condição do BemAqui pelo de-para (padrão: `nbjour` = 0 → "À vista"; `nbjour` = 30 → "30 dias"), sem duplicar; sem correspondente, é criado com uma parcela de 100% em `nbjour` dias, nome = `libelle` e situação = `active`; regra de fim de mês (`type_cdr`) ou deslocamento (`decalage`) gera o aviso "regra de fim de mês não suportada; conferir".
14. **Dado** `cond_reglement` do terceiro, **quando** importado, **então** a condição de pagamento padrão do terceiro aponta para a condição ligada no CA 13.

**Contas bancárias e caixas (só cadastro)**
15. **Dado** um `llx_bank_account`, **quando** a carga roda, **então** vira conta do tipo Caixa (`courant` = 2) ou Banco (demais), com nome = `label`, banco (código COMPE de `code_banque` quando válido, ou do de-para por conta), agência (`code_guichet`) e conta (`number` + `cle_rib`), situação inativa quando `clos` = 1; conta de Banco sem código COMPE válido nem de-para vai para erro "Conta {ref} sem código de banco: informe no de-para".
16. **Dado** a conta importada, **quando** é gravada, **então** entra **sem saldo inicial e sem `opening_date`**, mostra a `Tag` "Saldo inicial não informado" (CAD-05b CA 4) e nenhum movimento do `llx_bank` é lido; a primeira conta ativa vira a padrão se o tenant ainda não tiver uma.

**Importado do Dolibarr**
17. **Dado** um terceiro, condição ou conta importados, **quando** abro o detalhe, **então** vejo a `Tag` "Importado do Dolibarr" e a referência de origem (`import_source`, ex.: `llx_societe:42`).

**Reexecução e relatório**
18. **Dado** a carga feita, **quando** rodo de novo sem mudança no Dolibarr, **então** tudo sai como "sem alteração" e nada é duplicado; um terceiro editado no BemAqui depois da carga não é sobrescrito (conflito, MIG-01a CA 13).
19. **Dado** o relatório, **quando** confiro, **então** para `llx_societe`, `llx_socpeople`, `llx_c_payment_term` e `llx_bank_account` o total lido na extração = importados + atualizados + sem alteração + mesclados + vinculados + ignorados + conflitos + erros.

**LGPD e auditoria**
20. **[Segurança]** **Dado** terceiros PF e contatos, **quando** importados, **então** CPF, e-mail e telefone seguem mascarados na auditoria, no log e no relatório (MIG-01a CA 18 e 21).

## Regras de negócio e técnicas
- Grava só por `IPartyImport` (`Parties.Contracts`), `IPaymentTermImport` e `IBankAccountImport` (`Finance.Contracts`), criadas nesta story; cada uma chama os handlers da Application com as mesmas validações da tela (CAD-01a, CAD-05b).
- A fusão por documento (CA 6) usa o documento normalizado como chave de agrupamento dentro da carga.
- `import_source` (texto, anulável, gravado só pelo `I*Import`) é acrescentado a `parties.parties`, `finance.payment_terms` e `finance.bank_accounts` nesta story.
- O de-para (papéis especiais, condições, bancos) fica no arquivo `--mapping` da MIG-01a.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova (CLI, MIG-01a; tela da importação com `settings.view`).
- **Eventos:** nenhum novo.
- **Tabelas:** grava em `parties.*` e em `finance.payment_terms`/`bank_accounts` pelos Contracts, com a coluna `import_source`; registra em `migration.*`.

## Telas e estados
- Sem tela nova. Os registros importados aparecem em Cadastros → Terceiros e em Financeiro, com a `Tag` "Importado do Dolibarr" no detalhe; as pendências ficam em Configurações → Importação do Dolibarr (MIG-01a).
- **Ajuda:** pergunta frequente "Os clientes do Dolibarr já estão aqui?" em `Ajuda/cadastros-terceiros.md` e "Por que a conta importada está sem saldo?" em `Ajuda/financeiro-contas.md`.

## Plano de testes
- **Unidade:** `client`/`fournisseur`/prospect → papéis (CA 2, 3); papéis especiais pelo de-para (CA 4); PF/PJ e documento (CA 5); separação do número (CA 8); HTML → texto sem controle e bidi (CA 10); de-para de condições (CA 13); tipo e COMPE da conta (CA 15).
- **Integração (Testcontainers PostgreSQL + MariaDB, fixture anonimizada):** recusa sem allowlist fora de Development (CA 1); papéis, documento inválido e ausente, fusão e vínculo (CA 2–7); encerrado inativo (CA 9); notas (CA 10); contatos (CA 11); campos proibidos fora do snapshot (CA 12); condições e vínculo com o seed (CA 13, 14); contas sem saldo e conta padrão (CA 15, 16); `import_source` e `Tag` (CA 17); reexecução e conflito (CA 18); soma pelo total lido (CA 19); máscara (CA 20).
- **E2E (Playwright):** terceiro e conta importados mostram a `Tag` "Importado do Dolibarr"; conta importada oferece "Informar saldo inicial".
- **Ensaio manual:** simulação e primeira carga real na homologação, com o snapshot da base real, conferência do relatório com o PO.

## Fora do escopo
- Itens, locais e códigos do fornecedor: MIG-01c.
- Saldos bancários, movimentos bancários, títulos, pagamentos e recebimentos (decisão do PO de 18/10/2026; o saldo é informado na CAD-05b).
- Modos de pagamento (`llx_c_paiement`) e contas bancárias de terceiros (`llx_societe_rib`): não migram (ADR 0017).

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1 e decisão do PO): instaladores e transportadoras pela lista de ids do PO no de-para, com padrão Fornecedor; prospects como Cliente com observação; encerrados entram inativos; condições por de-para com o seed; contas só com o cadastro, sem saldo.
- **Pré-condições externas (não bloqueiam o `Ready`, porque têm padrão):**
  - a lista de ids de instaladores e transportadoras do PO no arquivo de de-para (sem ela, entram como Fornecedor);
  - o código COMPE das contas sem `code_banque` válido, no de-para (sem ele, a conta vai para erro e é cadastrada à mão);
  - a coluna do CNPJ/CPF na base real (`idprof1`, `siren` ou `siret`), confirmada no inventário da MIG-01a.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (PostgreSQL + MariaDB) e E2E passando.
- [ ] `IPartyImport`, `IPaymentTermImport` e `IBankAccountImport` nos Contracts; coluna `import_source` por migration.
- [ ] Mapeamento e de-para atualizados em `docs/operacao/migracao-dolibarr.md`.
- [ ] Painel da plataforma fora do modo aberto antes da primeira carga real (ADR 0018).
- [ ] Simulação e carga na homologação conferidas com o PO.
- [ ] Auditoria e máscara de dados pessoais conferidas; ajuda atualizada.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PartiesLoader`, `ContactsLoader`, `PaymentTermsLoader`, `BankAccountsLoader` (`IDolibarrLoader`).
- [ ] `IPartyImport`, `IPaymentTermImport`, `IBankAccountImport` e `import_source`.
- [ ] Fusão por documento e vínculo com cadastro existente; conversão de HTML para texto.
- [ ] Verificação da allowlist antes da carga real.
- [ ] Fixture anonimizada com duplicado, PF, prospect, encerrado, documento inválido e ausente, condição com fim de mês, conta de caixa e conta sem COMPE.

## Quality gates
- Agentes previstos: @dev, @data-engineer, cybersecurity (notas, campos proibidos, allowlist), @qa.
- Gate: CA 1 (allowlist), CA 6 (fusão), CA 16 (conta sem saldo) e CA 18 (reexecução) obrigatórios.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela nova; `Tag` de importado, pendências na tela da MIG-01a e ajuda) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** contas bancárias só com o cadastro, sem saldo e sem `opening_date` (decisão do PO; a D5 fica sem efeito); condições por de-para com o seed; prospects, encerrados inativos e papéis especiais pela lista do PO; terceiro sem documento aceito; notas convertidas para texto e campos proibidos fora do snapshot; pré-condição da allowlist (ADR 0018) como CA; `Tag` "Importado do Dolibarr"; contagens pelo total lido; arquivo renomeado para `MIG-01b-carga-terceiros-cadastros-financeiros.md`.
- **Pré-condições para `Ready`:** nenhuma bloqueante. As externas (lista de instaladores, COMPE, coluna do documento) têm padrão definido.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO: só cadastros, contas sem saldo, tabela 1.1, D8; correções da revisão pré-Fase 1; ordem 4 do Marco A; arquivo renomeado | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
