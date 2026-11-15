# INFRA-01 — Hospedagem no Brasil (condicionada): API, PostgreSQL e EmissorFiscal em datacenter de São Paulo, em VPS dedicada ao produto, com arquivamento contínuo de WAL e restauração a um ponto no tempo

| Campo | Valor |
|---|---|
| ID | INFRA-01 |
| Título | Hospedagem no Brasil (condicionada): API, PostgreSQL e EmissorFiscal em datacenter de São Paulo, em VPS dedicada ao produto, com arquivamento contínuo de WAL e restauração a um ponto no tempo |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) seção Hospedagem · **Decisão do PO de 15/11/2026 (PP-07): ficar na VPS atual por enquanto** · `docs/06` riscos "VPS compartilhada" e "RPO de 24 h" · ADR 0004 · ADR 0018 e 0037 (LGPD) |
| Dependências | Um dos gatilhos da PP-07 disparar; orçamento; medição do SPK-02 |
| Bloqueia | Nada no MVP. Só é executada se um gatilho disparar |
| Ordem de execução | Fase 10, condicionada a gatilho (fora do caminho do MVP) |

## User story
Como **proprietário do produto**, quero **ter pronto o plano de levar o sistema para o Brasil, numa máquina só dele, com backup contínuo**, para **executá-lo sem improviso no dia em que um dos gatilhos disparar**.

## Contexto e referências
- **Decisão do PO em 15/11/2026 (PP-07): não migrar antes do piloto.** O sistema fica na VPS atual. Esta story saiu do caminho crítico do MVP e só é executada se um gatilho disparar.
- **Gatilhos para reabrir a decisão:** (1) alguma SEFAZ recusar IP de fora do país; (2) latência medida acima da meta (SPK-02; PP-33); (3) primeiro cliente de volume; (4) exigência de LGPD (do cliente, de contrato ou da autoridade) sobre dado fora do país.
- **Consequências aceitas enquanto não migra:** latência maior na NFC-e online, no Pix e no painel; serviço público brasileiro que não responde a IP de fora (já aconteceu com o IBGE); dado pessoal fora do país; RPO de 24 h.
- **O que continua valendo sem migrar:** tudo o que o caixa precisa roda local e offline; tabelas públicas embutidas no sistema em vez de consultadas em serviço externo; o SPK-02 mede, a partir da VPS atual, a latência e a eventual restrição de IP da SEFAZ; o backup fica mais frequente (INFRA-02).
- Hoje: Docker Compose numa VPS compartilhada com cerca de dez projetos, atrás do `stack-nginx`, com `pg_dump` diário.

## Critérios de aceite
1. **Dado** um gatilho da PP-07 disparado e a decisão do PO de migrar, **quando** o ambiente novo fica pronto, **então** API, PostgreSQL e EmissorFiscal rodam num datacenter em São Paulo, numa VPS que não hospeda outros projetos.
2. **Dado** o PostgreSQL do ambiente novo, **quando** confiro, **então** há backup base periódico e arquivamento contínuo de WAL, cifrados e copiados para fora da VPS.
3. **Dado** um banco de tenant de teste, **quando** ensaio a restauração para um instante escolhido (ex.: 14h37 de ontem), **então** o banco volta àquele instante, e o passo a passo fica escrito em `docs/operacao/`.
4. **Dado** a meta de perda máxima de dados (proposta: até 15 minutos), **quando** meço o atraso do arquivamento, **então** ele fica dentro da meta e há alerta quando passa.
5. **Dado** o corte dos tenants em produção, **quando** executo o plano, **então** há dump local antes, janela combinada, DNS com TTL reduzido antes, conferência de contagens depois e caminho de volta escrito; nenhum dado é perdido.
6. **Dado** o ambiente novo no ar, **quando** meço a latência a partir de uma rede em São Paulo e a emissão de uma nota em homologação, **então** os números ficam registrados ao lado dos do SPK-02.
7. **Dado** o monitor externo, **quando** a API ou o banco caem, **então** o alerta chega em até 5 minutos.
8. **[Segurança]** **Dado** o ambiente novo, **quando** confiro, **então** o Postgres está em rede interna, os containers rodam sem root e com limites, os segredos e o key ring do Data Protection foram migrados sem exposição, e os certificados (TLS, mTLS do Inter) foram reinstalados.
9. **Dado** nenhum gatilho disparado, **quando** o MVP vai ao piloto, **então** esta story continua em `Draft` e isso não bloqueia o marco; os gatilhos são conferidos no fechamento de cada fase e registrados.

## Regras de negócio e técnicas
- Uma região só. Multi-região não se justifica.
- Uma instância da API, escala vertical; o SignalR segue sem backplane.
- Nunca remover volume com nome ao limpar o Docker.
- Os domínios próprios dos clientes (`tenant_domains`) continuam resolvendo.

## Permissões, eventos e tabelas
- Nenhuma mudança de produto.

## Telas e estados
- Nenhuma tela. **Documentação:** `docs/operacao/` ganha o roteiro de restauração a um ponto no tempo e o plano de corte.

## Plano de testes
- Ensaio de restauração a um ponto no tempo (CA 3), repetido todo mês depois da migração.
- Ensaio do corte num ambiente de teste antes do corte real.
- Medição de latência antes e depois (CA 6).

## Fora do escopo
- Segunda instância da API e backplane do SignalR.
- Rever o limite de chamadas ao Emissor (lacuna 31; NFCE-00).
- Backup mais frequente na VPS atual (INFRA-02).

## Pontos em aberto
- Orçamento e provedor, quando um gatilho disparar.
- Como o backup externo previsto para o go-live da Konebras se encaixa no ambiente novo.

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
