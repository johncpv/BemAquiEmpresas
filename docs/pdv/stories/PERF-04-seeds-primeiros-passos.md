# PERF-04 — Pacotes de dados iniciais por perfil e segmento (locais, contas, formas de pagamento, tabela de preço, grupos, regras fiscais "A confirmar") e checklist "Primeiros passos" no Início

| Campo | Valor |
|---|---|
| ID | PERF-04 |
| Título | Pacotes de dados iniciais por perfil e segmento (locais, contas, formas de pagamento, tabela de preço, grupos, regras fiscais "A confirmar") e checklist "Primeiros passos" no Início |
| Tamanho | M |
| Status | In Progress (parte 1 implementada em 17/11/2026, branch `feat/perf-04-seeds-primeiros-passos`; o pacote do Catálogo e os passos 2, 4 e 5 esperam a CAT-01, a CAT-02, a IMP-01 e a PDV-02) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (item 3) · [PRD do PDV](../01-PRD.md) seção 3.4 · ADR 0042 (valores pré-configurados "A confirmar") · R-PRP-1 (propositivo) |
| Dependências | PERF-02, CAT-01, PAG-01 |
| Bloqueia | PDV-01 (a loja e o caixa do seed) |
| Ordem de execução | 8 (Fase 10) |

## User story
Como **dono de um comércio que acabou de entrar**, quero **encontrar o básico já cadastrado e uma lista do que falta**, para **fazer a primeira venda de teste em até 30 minutos**.

## Contexto e referências
- `ITenantSeeder` ordenado e seed idempotente por `seed_key` já existem (`units`, `locations`).
- O chip "A confirmar com a contadora", com "Marcar como conferido", é o `ToConfirmChip` da ADR 0042.

## Critérios de aceite
### Pacote do Comércio
1. **Dado** um tenant com o perfil `retail`, **quando** o seed roda, **então** existem: os locais "Loja" e "Depósito"; as contas do tipo Caixa "Caixa PDV 01" e "Cofre" e a conta "Quebra de caixa"; as formas de pagamento Dinheiro, Pix, Débito, Crédito à vista, Crédito parcelado e Vale; o terceiro adquirente "Maquininha" com taxa e prazo zerados; a tabela de preço "Balcão"; uma categoria "Geral"; os grupos Gerente de loja, Operador de caixa e Estoquista.
2. **Dado** os valores de taxa, prazo e regra fiscal do seed, **quando** abro as telas, **então** cada um tem o chip "A confirmar" (taxas) ou "A confirmar com a contadora" (regras fiscais do Simples Nacional), com "Marcar como conferido" auditado.
3. **Dado** o seed rodando duas vezes, ou um perfil adicionado depois, **quando** confiro, **então** nada é duplicado e nenhum valor que o tenant já alterou é sobrescrito.
4. **Dado** um segmento de alimentação, **quando** o seed roda, **então** acrescenta categorias e unidades do segmento; estações de preparo e mesas de exemplo entram quando o módulo Restaurante existir (Fase 15).
5. **Dado** o perfil `factory`, **quando** o seed roda na Konebras, **então** nada é criado nem alterado.

### Primeiros passos
6. **Dado** o Proprietário de um tenant `retail` no primeiro login, **quando** abre o Início, **então** vê o cartão "Primeiros passos" com cinco passos e o progresso: (1) Empresa e regime tributário; (2) Importar produtos; (3) Formas de pagamento e taxas; (4) Impressora; (5) Abrir o caixa e fazer uma venda de teste.
7. **Dado** um passo, **quando** clico, **então** vou direto à tela certa; ao concluir a ação, o passo fica marcado sozinho (o sistema confere o dado, não um clique em "feito").
8. **Dado** os cinco passos concluídos, **quando** volto ao Início, **então** o cartão mostra "Tudo pronto" com o próximo passo sugerido e pode ser dispensado.
9. **Dado** um usuário sem a permissão da tela de um passo, **quando** vê o cartão, **então** o passo aparece sem link, com "Peça a quem administra o sistema.".

## Regras de negócio e técnicas
- Cada módulo é dono do próprio seed (Finance das contas e formas; Inventory dos locais; Catalog da tabela de preço; Identity dos grupos). O perfil só diz quais pacotes rodam.
- Os grupos do seed nascem só com permissões de módulos que já existem; as do `pos` e do `sales` entram na PDV-01.
- O progresso do checklist é calculado; não há tabela de "passo concluído".

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova.
- **Endpoints:** `GET /api/v1/dashboard/first-steps` (calculado por contratos dos módulos, padrão da ADR 0028).
- **Tabelas:** nenhuma; `seed_key` nas tabelas que recebem seed novo.

## Telas e estados
- **Início:** cartão "Primeiros passos". Carregando: esqueleto; erro: "Não foi possível conferir os primeiros passos." com "Tentar de novo".
- **Ajuda:** `Ajuda/inicio-primeiros-passos.md` (nova).

## Plano de testes
- **Integração (Testcontainers):** seed do `retail` completo (CA 1); idempotência e não sobrescrever (CA 3); `factory` intocado (CA 5); progresso calculado a cada passo (CA 7).
- **Vitest:** cartão com passos sem permissão (CA 9).

## Fora do escopo
- Passo de NFC-e no checklist (entra com a NFCE-01).
- Seeds de Serviços e de E-commerce (Fases 14 e 17).

## Pontos em aberto
- A meta de 30 minutos precisa ser medida com o lojista piloto (PP-35).

## Implementação, parte 1 (17/11/2026): o que entrou e o que espera a CAT-01 e a CAT-02
A story depende da CAT-01, que ainda não está na `main`. Esta parte entrega tudo o que não depende do Catálogo e deixa o ponto de extensão pronto.

**Revisão contra o código antes de codar:**
- `finance.bank_accounts` e `parties.parties` não tinham `seed_key`: entraram as duas colunas (migration `ProfileSeedKeys`, só acréscimo, índice único entre as não nulas).
- O tenant resolvido só trazia os códigos dos perfis; entrou `ResolvedTenant.Segments`, lido dos mesmos `tenant_profiles` (recarrega pelo mesmo NOTIFY).
- Tenant novo não tem conta do tipo Banco, e Pix e cartões precisam cair numa (PAG-01 CA 7). O pacote cria "Conta bancária principal" só quando não há nenhuma conta Banco ativa; o lojista completa banco, agência e conta.
- "Taxa e prazo zerados" seguem a PAG-01: é a **ausência** de taxa (a venda entra pelo valor cheio), não uma linha de 0%. Uma linha de 0% com vigência de hoje impediria o lojista de cadastrar a taxa real no mesmo dia (a nova tem de começar depois da vigente).
- O papel Adquirente é do terceiro, que é do Parties: a "Maquininha" nasce no pacote dos Terceiros (`parties.retail`) e o Financeiro a acha pelo contrato (`IParties.FindBySeedKeyAsync`), nunca pela tabela. A ordem dos pacotes garante o terceiro antes das formas.
- O "Quebra de caixa" entrou como conta do tipo Caixa, como diz o [modelo de dados](../03-modelo-dados.md#6-finance-extensões). A conta contábil da quebra no plano de contas continua na CX-03 ("A confirmar com a contadora").
- O seed do perfil roda dentro do `migrate` de todo deploy (é um `ITenantSeeder`), o que também cobre "perfil adicionado depois" mesmo se o seed da aba Perfis falhar.

**Entregue:**
- **Mecanismo** (adendo da [ADR 0046](../../adr/0046-perfis-de-negocio-e-modulo-oculto.md)): `IProfileSeedPackage` por módulo; `BusinessProfileSeeds` diz quais pacotes cada perfil e segmento roda (`factory` nenhum); `ProfileSeedRunner` (ordem 900) roda no provisionamento, a cada `migrate` e ao adicionar perfil na aba Perfis (`TenantProfileSeeding`, chamado pelo `POST /platform/tenants/{id}/profiles`; falha no seed não desfaz o perfil e a mensagem avisa).
- **CA 1 (parcial):** Terceiros: "Maquininha" (PJ, papel Adquirente). Financeiro: contas Caixa PDV 01, Cofre e Quebra de caixa; formas Dinheiro (no Caixa PDV 01), Pix, Cartão de débito, Cartão de crédito à vista, Cartão de crédito parcelado e Vale, todas "A confirmar", cartões e Vale com a Maquininha. Estoque: locais Loja (LOJA) e Depósito (DEP). Acessos: Gerente de loja, Operador de caixa e Estoquista, só com `dashboard`, `finance`, `inventory`, `purchasing` e `parties`.
- **CA 2 (parte das taxas):** as formas do seed abrem com o chip "A confirmar" e "Marcar como conferido" (da PAG-01) é auditado; com a Maquininha, os cartões já podem ser conferidos.
- **CA 3:** idempotente pela `seed_key`; não recria o excluído; não altera nome, conta, adquirente, conferência nem permissões; nome ou código já usado pelo tenant não é duplicado; o local "Loja" só vira padrão se não houver nenhum.
- **CA 4 (parcial):** segmentos de alimentação (padaria, restaurante, lanchonete e trailer) acrescentam as unidades G, ML, DZ, PCT e POR.
- **CA 5:** `factory` não tem pacote; teste de regressão compara o retrato de todas as tabelas do banco (contagem e hash do conteúdo, menos auditoria e outbox) antes e depois do seed.
- **CA 6 a 9:** `GET /api/v1/dashboard/first-steps` (calculado pelos contratos `ICompanyIdentity`, `IFiscalSetupStatus` novo e `IPaymentMethods`), recurso por perfil `dashboard.first_steps` (só Comércio). Cartão no Início com esqueleto, erro com "Tentar de novo", progresso, passo sem permissão sem link ("Peça a quem administra o sistema."), "Tudo pronto" com o próximo passo e "Dispensar" (guardado no navegador; não há tabela). Passo 1 concluído com empresa completa e regime conferido; passo 3 com todas as formas ativas conferidas. Ajuda `Ajuda/inicio-primeiros-passos.md`.

**Fica para depois (anotado):**
- **CAT-01:** o pacote `catalog.retail` (tabela de preço "Balcão" e categoria "Geral") e as categorias do segmento de alimentação. O ponto de extensão está pronto: o módulo Catálogo registra um `IProfileSeedPackage` com a chave `BusinessProfileSeeds.CatalogRetail`, que já está na lista do Comércio e hoje é pulada.
- **CAT-02:** o link do passo 2 "Importar produtos" (hoje "Em breve", sem link) e a regra de concluído (produto cadastrado).
- **IMP-01:** o passo 4 "Impressora" (hoje "Em breve").
- **PDV-01 e PDV-02:** o passo 5 "Abrir o caixa e fazer uma venda de teste" (hoje "Em breve"), as permissões `pos` e `sales` dos grupos do seed e a loja e o caixa do PDV. Enquanto houver passo "Em breve", o cartão não chega a "Tudo pronto".
- **CA 2, regras fiscais do Simples "A confirmar com a contadora":** não são do perfil Comércio nesta parte; o seed fiscal de hoje (`TaxRuleSeeder`, ADR 0042) não muda.
- **Mesas e estações de preparo** (CA 4): Fase 15.
- **Local padrão:** num tenant novo do Comércio o "Almoxarifado principal" do seed básico continua sendo o padrão; trocar para "Loja" seria alterar um valor existente. Fica para decidir com a PDV-01.

## Definition of Done
- [ ] Critérios de aceite atendidos (parte 1: ver acima; o pacote do Catálogo e os passos 2, 4 e 5 esperam a CAT-01, a CAT-02, a IMP-01 e a PDV-02).
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada (`ProfileSeedKeys`, só acréscimo); nenhuma permissão nova.
- [x] Telas com estados de carregando, vazio e erro; ajuda escrita em `Ajuda/` no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados; auditoria ligada (conferência da forma).
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog (parcial); decisões no adendo da ADR 0046.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 17/11/2026 | 1.0 | Parte 1 implementada: mecanismo de pacotes por perfil, pacotes do Financeiro, Estoque, Acessos e Terceiros, unidades de alimentação, gancho no provisionamento, no `migrate` e na aba Perfis, regressão da fábrica e o cartão "Primeiros passos". O pacote do Catálogo e os passos 2, 4 e 5 esperam a CAT-01, a CAT-02, a IMP-01 e a PDV-02. | Claude (dev) |
