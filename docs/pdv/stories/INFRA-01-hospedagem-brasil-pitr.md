# INFRA-01 — Hospedagem no Brasil: API, PostgreSQL e EmissorFiscal em datacenter de São Paulo, em VPS dedicada ao produto, com arquivamento contínuo de WAL e restauração a um ponto no tempo ensaiada

| Campo | Valor |
|---|---|
| ID | INFRA-01 |
| Título | Hospedagem no Brasil: API, PostgreSQL e EmissorFiscal em datacenter de São Paulo, em VPS dedicada ao produto, com arquivamento contínuo de WAL e restauração a um ponto no tempo ensaiada |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos e spikes |
| Requisitos | [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) seção Hospedagem · `docs/06` riscos "VPS compartilhada" e "RPO de 24 h" (mitigação já registrada: VPS dedicada ao entrar o 2º tenant) · ADR 0004 · ADR 0018 e 0037 (LGPD) |
| Dependências | PP-07 decidida, com orçamento; medição de latência do SPK-02 |
| Bloqueia | A ida do MVP a cliente externo; a versão pública do PDV fiscal |
| Ordem de execução | Fase 10, em paralelo (pré-condição para o MVP ir a cliente) |

## User story
Como **proprietário do produto**, quero **o sistema hospedado no Brasil, numa máquina só dele, com backup contínuo**, para **o caixa não esperar uma viagem ao exterior a cada nota e uma loja nunca perder um dia de vendas**.

## Contexto e referências
- Hoje: Docker Compose numa VPS compartilhada com cerca de dez projetos, atrás do `stack-nginx`, com `pg_dump` diário (RPO de 24 h aceito pelo PO em 16/10/2026 para a fábrica).
- A VPS foi informada como nos EUA. Estimativa não medida: 110 a 180 ms de ida e volta até o Brasil. Cada NFC-e cruzaria o hemisfério duas vezes.
- A Konebras está em produção. A migração é uma operação de corte, com dump antes, como em todo deploy.

## Critérios de aceite
1. **Dado** o ambiente novo, **quando** confiro, **então** API, PostgreSQL e EmissorFiscal rodam num datacenter em São Paulo, numa VPS que não hospeda outros projetos.
2. **Dado** o PostgreSQL, **quando** confiro, **então** há backup base periódico e arquivamento contínuo de WAL, cifrados e copiados para fora da VPS.
3. **Dado** um banco de tenant de teste, **quando** ensaio a restauração para um instante escolhido (ex.: 14h37 de ontem), **então** o banco volta àquele instante, e o passo a passo fica escrito em `docs/operacao/`.
4. **Dado** a meta de perda máxima de dados decidida na PP-07 (proposta: até 15 minutos), **quando** meço o atraso do arquivamento, **então** ele fica dentro da meta e há alerta quando passa.
5. **Dado** o corte da Konebras, **quando** executo o plano, **então** há dump local antes, janela combinada, DNS com TTL reduzido antes, conferência de contagens depois e caminho de volta escrito; nenhum dado é perdido.
6. **Dado** o ambiente novo no ar, **quando** meço a latência a partir de uma rede em São Paulo e a emissão de uma NF-e em homologação, **então** os números ficam registrados ao lado dos do SPK-02.
7. **Dado** o monitor externo, **quando** a API ou o banco caem, **então** o alerta chega em até 5 minutos.
8. **[Segurança]** **Dado** o ambiente novo, **quando** confiro, **então** o Postgres está em rede interna, os containers rodam sem root e com limites, os segredos e o key ring do Data Protection foram migrados sem exposição, e os certificados (TLS, mTLS do Inter) foram reinstalados.

## Regras de negócio e técnicas
- Uma região só. Multi-região não se justifica.
- Uma instância da API, escala vertical; o SignalR segue sem backplane.
- Nunca remover volume com nome ao limpar o Docker.
- Os domínios próprios dos clientes (`tenant_domains`) continuam resolvendo.

## Permissões, eventos e tabelas
- Nenhuma mudança de produto.

## Telas e estados
- Nenhuma tela. **Documentação:** `docs/operacao/` ganha o roteiro de restauração a um ponto no tempo e o plano de corte; `docs/02-arquitetura.md` §7 é atualizado na integração do plano.

## Plano de testes
- Ensaio de restauração a um ponto no tempo (CA 3), repetido todo mês.
- Ensaio do corte num ambiente de teste antes do corte real.
- Medição de latência antes e depois (CA 6).

## Fora do escopo
- Segunda instância da API e backplane do SignalR.
- Rever o limite de chamadas ao Emissor (lacuna 31; NFCE-00).

## Pontos em aberto
- PP-07: orçamento, provedor e a meta de perda máxima de dados.
- Como o backup externo previsto para o go-live da Konebras se encaixa no ambiente novo.

## Definition of Done
- [ ] Critérios de aceite atendidos e evidências guardadas.
- [ ] Roteiros escritos em `docs/operacao/`.
- [ ] Restauração a um ponto no tempo ensaiada.
- [ ] Corte da Konebras concluído e conferido.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
