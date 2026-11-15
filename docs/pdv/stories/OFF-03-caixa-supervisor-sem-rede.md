# OFF-03 — Caixa e supervisor sem rede: abrir turno, PIN de operador e de supervisor conferidos no aparelho, sangria, suprimento e fechamento offline, janela máxima sem sincronizar e o que fica desabilitado

| Campo | Valor |
|---|---|
| ID | OFF-03 |
| Título | Caixa e supervisor sem rede: abrir turno, PIN de operador e de supervisor conferidos no aparelho, sangria, suprimento e fechamento offline, janela máxima sem sincronizar e o que fica desabilitado |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 5) · [Rascunho A](../adr/rascunho-A-offline.md) itens 9 e 11 e fase F2 · ADR 0026 §6 · PP-10 e PP-11 |
| Dependências | OFF-02, CX-02, PDV-04; PP-10 decidida |
| Bloqueia | A primeira versão pública do PDV |
| Paridade com o mercado | Requisitos mínimos 1, 6 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 13 (Fase 11) |

## User story
Como **dono de um trailer ou de uma loja com internet ruim**, quero **abrir, operar e fechar o caixa sem rede**, para **trabalhar um dia inteiro offline se for preciso**.

## Contexto e referências
- Hoje o PIN é conferido no servidor e o segredo do aparelho gira a cada PIN. Nada disso funciona sem rede.
- Risco aceito no rascunho A: PIN de 6 dígitos com acesso físico ao aparelho é quebrável. Por isso o que se faz sem rede é limitado e revisto na sincronização.

## Critérios de aceite
### PIN sem rede
1. **Dado** um aparelho do tipo Caixa com rede, **quando** sincroniza, **então** baixa o verificador do PIN **só** dos operadores e supervisores da sua loja, derivado no servidor e protegido pela chave do aparelho; o PIN em si nunca desce.
2. **Dado** o aparelho sem rede, **quando** um operador da loja digita o PIN certo, **então** entra, e a sessão fica marcada como offline.
3. **[Segurança]** **Dado** cinco PINs errados sem rede, **quando** ocorre o quinto, **então** aquele usuário fica bloqueado **naquele aparelho** até a próxima sincronização, e o bloqueio é enviado ao servidor.
4. **[Segurança]** **Dado** um usuário desativado ou com o PIN trocado no servidor, **quando** o aparelho sincroniza, **então** o verificador antigo é apagado; até lá, o que ele fez sem rede chega marcado e entra em "Pendências do PDV" para revisão.
5. **Dado** um usuário que nunca entrou neste aparelho com rede, **quando** tenta entrar sem rede, **então** vê "A primeira entrada neste aparelho precisa de internet.".

### Operar sem rede
6. **Dado** o aparelho sem rede e dentro da janela, **quando** abro o caixa, **então** a sessão nasce `Open` com `opened_offline`; sangria, suprimento, contagem cega e fechamento funcionam e viram eventos.
7. **Dado** uma ação restrita sem rede, **quando** o supervisor digita o PIN, **então** a aprovação é conferida no aparelho, gravada como evento com a marca offline e, na sincronização, vira `supervisor_approvals` com `offline = true`; supervisor igual ao operador continua recusado.
8. **Dado** o fechamento feito sem rede, **quando** as vendas e a contagem sincronizam, **então** o servidor calcula o esperado e a quebra, e a sessão segue para `Closed` ou `PendingReview` (CX-02); o relatório de fechamento lista as vendas feitas sem rede.

### Limites
9. **Dado** o aparelho há mais tempo sem sincronizar do que a janela da loja, **quando** tento abrir um turno, **então** vejo "Este caixa está há N dias sem enviar as vendas. Conecte à internet para abrir o caixa."; a sessão já aberta pode ser fechada.
10. **Dado** o aparelho a 80% da janela, **quando** opero, **então** a faixa de estado fica em alerta: "Conecte à internet até amanhã às 14h.".
11. **Dado** o aparelho sem rede, **quando** abro o menu, **então** aparecem desabilitados, cada um com "Disponível com internet.": fiado, vale-troca, devolução, busca de cliente cadastrado, consulta de saldo, registro de aparelho e instalação de impressora nova.
12. **Dado** a rede voltando, **quando** o aparelho sincroniza, **então** a fila sobe sozinha, o segredo do aparelho gira e a faixa volta ao normal, sem ação do operador.

## Regras de negócio e técnicas
- A janela máxima é parâmetro da loja, com teto (PP-10).
- O verificador é derivado de forma que não permita reconstruir o PIN sem a chave do aparelho; o desenho exato fica no adendo à ADR 0026 e passa por revisão de segurança antes da implementação.
- A exceção "perfil sensível pode ter PIN de supervisor" é nomeada, como a `production.approve_quality`.
- Aprovação offline nunca vale para ação que exige rede.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova.
- **Endpoints:** a descida da OFF-01 ganha a coleção de verificadores; a subida da OFF-02 leva os eventos de sessão e de aprovação.
- **Tabelas:** `identity.device_pin_verifiers`; `opened_offline`, `closed_offline` em `pos.cash_sessions`; `offline` em `pos.supervisor_approvals`.
- **Auditoria:** entrada offline e bloqueio de PIN chegam ao `audit_log` na sincronização.

## Telas e estados
- **Entrada do `/pdv` sem rede:** lista dos operadores da loja e teclado de PIN, com a faixa "Sem conexão".
- **Itens desabilitados** com a explicação (CA 11).
- **Loja → Lojas e caixas:** parâmetro "Tempo máximo sem internet".
- **Ajuda:** `Ajuda/pdv-sem-conexao.md` atualizada com o que funciona e o que não funciona.

## Plano de testes
- **Vitest:** PIN sem rede (CA 2, 3 e 5); janela (CA 9 e 10); itens desabilitados (CA 11).
- **Integração (Testcontainers):** verificador só da loja (CA 1); revogação e revisão (CA 4); fechamento offline calculado no servidor (CA 8); aprovação offline marcada (CA 7).
- **Revisão de segurança** do desenho do verificador antes de codar.
- **E2E no fechamento do marco:** um turno inteiro sem rede, com sincronização no fim.
- **Campo:** um dia de operação offline no lojista piloto.

## Fora do escopo
- Comandas e KDS sem rede (PP-23).
- NFC-e sem rede (PP-01, NFCE-04).
- Cópia do registro de eventos em disco (IMP-03).

## Pontos em aberto
- **PP-10:** 72 h, 7 dias ou parâmetro por loja. O piso de mercado é 7 dias.
- PP-11: PIN de supervisor.

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
