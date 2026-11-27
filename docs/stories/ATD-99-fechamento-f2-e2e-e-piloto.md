# ATD-99 — Fechamento da F2: E2E ponta a ponta e roteiro do piloto

| Campo | Valor |
|---|---|
| ID | ATD-99 |
| Título | Fechamento da feature "Conecta ↔ BemAqui Empresas": um E2E por fluxo principal (ERP + Conecta + ERP falso do Inter e canal falso), a rodada completa do CI (`workflow_dispatch`, E2E e carga) verde antes do deploy, e o roteiro do piloto do BemAqui usando o próprio Conecta com o próprio ERP |
| Tamanho | P |
| Status | Draft (27/11/2026) |
| Requisitos | CLAUDE.md do ERP ("E2E é por feature, não por story") · [Plano](../integracoes/00-plano-integracoes.md) §8 (piloto 21/12 a 23/12) |
| Dependências | INT-04, ATD-01, ATD-02, ATD-03a, ATD-03b, ATD-04, ATD-05a, ATD-05b, ATD-06a, ATD-06b |
| Repositórios | **ERP** (E2E e roteiro) e **Conecta** (ambiente de teste e roteiro) |
| Ordem | Última da F2 |

## Critérios de aceite
1. **Dado** os ambientes de teste dos dois produtos, **quando** a rodada completa roda, **então** há um E2E (Playwright) para cada fluxo: (a) pareamento, conexão e `ping`; (b) cliente identificado pelo telefone e por código, painel e 2ª via; (c) pedido pelo atendente até pago (Inter falso), com aviso de pagamento ao cliente que aceitou; (d) SAIR desligando avisos do Conecta e do ERP; (e) IA criando rascunho e o "Sim, confirmo" por botão; (f) WhatsApp do ERP pelo Conecta para um número de teste; (g) desconectar dos dois lados e reconectar sem pedido órfão; (h) pedido de orçamento da fábrica.
2. **Dado** o contrato, **quando** o CI de cada lado roda, **então** há teste de contrato: o Conecta valida as chamadas contra `docs/integracoes/openapi-integration-v1.json` (cópia versionada no Conecta, conferida por *hash*), e o ERP valida o corpo e os cabeçalhos dos avisos contra o recebedor do Conecta (vetores de assinatura compartilhados).
3. **Dado** o piloto (21/12 a 23/12, tenant `bemaqui` no ERP SaaS e conta do BemAqui no Conecta), **quando** roda, **então** segue o roteiro: conectar com pareamento; "Enviar teste" e "Trocar segredo" sem perder aviso; 10 clientes reais identificados (telefone e código); 5 pedidos com boleto do Inter em homologação; avisos com aceite e um SAIR; 20 conversas com a IA, incluindo tentativas de injeção; desconectar e reconectar; restaurar um backup do Conecta em cópia e conferir que a conexão pede "conectar de novo" (runbook).
4. **Dado** o fim do piloto, **quando** o PO avalia, **então** o relatório tem os números (pedidos, avisos, aceites, SAIR, falhas por motivo, uso das cotas) e a lista do que liga para outras empresas (IA confirmando e cobrando, avisos por canal não oficial) fica para decisão.

## Tarefas
- [ ] E2E dos fluxos (CA 1) e testes de contrato (CA 2).
- [ ] Rodada completa verde antes do deploy de produção da F2.
- [ ] Roteiro e relatório do piloto (CA 3 e 4); runbooks dos dois lados atualizados.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Criada nas revisões de 27/11/2026 (arquitetura) para fechar a F2 com E2E e o roteiro do piloto. | Claude (planejamento) |
