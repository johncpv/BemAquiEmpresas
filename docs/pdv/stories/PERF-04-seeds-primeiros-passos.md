# PERF-04 — Pacotes de dados iniciais por perfil e segmento (locais, contas, formas de pagamento, tabela de preço, grupos, regras fiscais "A confirmar") e checklist "Primeiros passos" no Início

| Campo | Valor |
|---|---|
| ID | PERF-04 |
| Título | Pacotes de dados iniciais por perfil e segmento (locais, contas, formas de pagamento, tabela de preço, grupos, regras fiscais "A confirmar") e checklist "Primeiros passos" no Início |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (item 3) · [PRD do PDV](../01-PRD.md) seção 3.4 · ADR 0042 (valores pré-configurados "A confirmar") · R-PRP-1 (propositivo) |
| Dependências | PERF-02, CAT-01, PAG-01 |
| Bloqueia | PDV-01 (a loja e o caixa do seed) |
| Ordem de execução | 4 (Fase 10; fecha depois de CAT-01 e PAG-01) |

## User story
Como **dono de um comércio que acabou de entrar**, quero **encontrar o básico já cadastrado e uma lista do que falta**, para **fazer a primeira venda de teste em até 30 minutos**.

## Contexto e referências
- `ITenantSeeder` ordenado e seed idempotente por `seed_key` já existem (`units`, `locations`).
- O chip "A confirmar com a contadora", com "Marcar como conferido", é o `ToConfirmChip` da ADR 0042.

## Critérios de aceite
### Pacote do Comércio
1. **Dado** um tenant com o perfil `retail`, **quando** o seed roda, **então** existem: os locais "Loja" e "Depósito (CPD)"; as contas do tipo Caixa "Caixa PDV 01" e "Cofre" e a conta "Quebra de caixa"; as formas de pagamento Dinheiro, Pix, Débito, Crédito à vista, Crédito parcelado e Vale; o terceiro adquirente "Maquininha" com taxa e prazo zerados; a tabela de preço "Balcão"; os grupos Gerente de loja, Operador de caixa e Estoquista.
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
