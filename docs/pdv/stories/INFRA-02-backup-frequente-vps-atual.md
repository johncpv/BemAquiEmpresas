# INFRA-02 — Backup mais frequente na VPS atual e registro das consequências aceitas de não migrar: redução da perda máxima de dados sem trocar de máquina, com restauração ensaiada

| Campo | Valor |
|---|---|
| ID | INFRA-02 |
| Título | Backup mais frequente na VPS atual e registro das consequências aceitas de não migrar: redução da perda máxima de dados sem trocar de máquina, com restauração ensaiada |
| Tamanho | P |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | **Decisão do PO de 15/11/2026 (PP-07): ficar na VPS atual**, com a mitigação "backup mais frequente" · `docs/06` risco "Perda de até 24 h de dados" · ADR 0004 |
| Dependências | — |
| Bloqueia | A ida do MVP ao piloto com lojista |
| Ordem de execução | Fase 10, em paralelo (antes de o MVP ir ao piloto) |

## User story
Como **proprietário do produto**, quero **que uma loja em piloto nunca perca um dia inteiro de vendas, mesmo sem migrar de máquina**, para **ficar na VPS atual com um risco que eu conheço**.

## Contexto e referências
- O RPO de 24 h foi aceito pelo PO em 16/10/2026 para a fábrica. Para o varejo, um dia de vendas perdido é mais grave. O PO decidiu não migrar agora (PP-07) e manter as mitigações que não dependem de migrar.
- O PDV local-first já reduz a exposição: vendas não enviadas ficam no aparelho. O que se perde numa falha do servidor é o que já subiu depois do último backup.

## Critérios de aceite
1. **Dado** a VPS atual, **quando** configuro o backup dos bancos de tenant do produto, **então** ele roda em intervalo menor que o diário (proposta: a cada hora no horário comercial, ou arquivamento de WAL se a VPS compartilhada permitir), cifrado e copiado para fora da VPS.
2. **Dado** o backup mais frequente, **quando** meço por uma semana, **então** o relatório traz o intervalo real, o tamanho, o tempo de cada execução e o efeito na carga da VPS compartilhada com os outros projetos.
3. **Dado** um banco de tenant de teste, **quando** ensaio a restauração a partir do backup mais recente, **então** o banco volta, o tempo de restauração fica registrado e o passo a passo está em `docs/operacao/`.
4. **Dado** uma falha do backup, **quando** ela acontece, **então** há alerta em até 1 hora.
5. **Dado** a decisão da PP-07, **quando** esta story fecha, **então** `docs/06-pendencias-riscos.md` do plano traz a perda máxima de dados **medida** (não a proposta) e a conferência dos quatro gatilhos de reabrir a decisão.

## Regras de negócio e técnicas
- Jobs de backup por tenant; nada de volume com nome removido.
- O backup do piloto não pode degradar os outros projetos da VPS: se degradar, o intervalo sobe e o fato vira gatilho para reabrir a PP-07.

## Permissões, eventos e tabelas
- Nenhuma mudança de produto.

## Telas e estados
- Nenhuma tela. **Documentação:** `docs/operacao/` ganha o roteiro de restauração.

## Plano de testes
- Ensaio de restauração (CA 3), repetido todo mês.
- Simulação de falha do backup (CA 4).

## Fora do escopo
- Migrar de máquina ou de país (INFRA-01).
- Restauração a um ponto no tempo, se a VPS compartilhada não permitir arquivar WAL.

## Pontos em aberto
- Se a VPS compartilhada permite arquivamento de WAL sem afetar os outros projetos.

## Definition of Done
- [ ] Critérios de aceite atendidos e evidências guardadas.
- [ ] Roteiros escritos em `docs/operacao/`.
- [ ] Restauração ensaiada.
- [ ] Decisão e medições registradas nas pendências.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
