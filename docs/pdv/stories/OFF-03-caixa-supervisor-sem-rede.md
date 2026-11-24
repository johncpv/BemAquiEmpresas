# OFF-03 — Caixa e supervisor sem rede: abrir turno, PIN conferido no aparelho com chave de cifra própria, limites para aprovação offline, sangria, suprimento e fechamento offline, janela máxima com piso de horário do servidor e o que fica desabilitado

| Campo | Valor |
|---|---|
| ID | OFF-03 |
| Título | Caixa e supervisor sem rede: abrir turno, PIN conferido no aparelho com chave de cifra própria, limites para aprovação offline, sangria, suprimento e fechamento offline, janela máxima com piso de horário do servidor e o que fica desabilitado |
| Tamanho | G |
| Status | Pronta para revisão (24/11/2026, branch `feat/off-03`) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 5) · [Rascunho A](../adr/rascunho-A-offline.md) itens 9 e 11 e fase F2 · ADR 0026 §6 · PP-10 e PP-11 |
| Dependências | OFF-02, CX-02, PDV-04; PP-10 e PP-39 decididas |
| Bloqueia | A primeira versão pública do PDV |
| Paridade com o mercado | Requisitos mínimos 1, 6 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 13 (Fase 11) |

## User story
Como **dono de um trailer ou de uma loja com internet ruim**, quero **abrir, operar e fechar o caixa sem rede**, para **trabalhar um dia inteiro offline se for preciso**.

## Contexto e referências
- Hoje o PIN é conferido no servidor e o segredo do aparelho gira a cada PIN. Nada disso funciona sem rede.
- Risco aceito no rascunho A: PIN de 6 dígitos com acesso físico ao aparelho é quebrável. Por isso o que se faz sem rede é limitado e revisto na sincronização.
- **Re-estimada de M para G** pela revisão independente de 15/11/2026: o rascunho A estima de 5 a 8 semanas-pessoa para esta etapa do offline. O SPK-03 revê o número.
- A revisão apontou quatro furos no desenho original, tratados nos critérios 1, 13, 14 e 15: a chave ECDSA do aparelho só assina e não protege o verificador; o mesmo PIN vale online; não havia limite para aprovação offline; e a janela era medida pelo relógio do aparelho.

## Critérios de aceite
### PIN sem rede
1. **[Segurança]** **Dado** um aparelho do tipo Caixa com rede, **quando** sincroniza, **então** baixa o verificador do PIN **só** dos operadores e supervisores da sua loja, derivado no servidor com função lenta e sal por aparelho; no aparelho ele fica cifrado com uma **chave de cifra não extraível própria** (AES ou ECDH do WebCrypto), separada da chave ECDSA que assina os lotes. O PIN em si nunca desce.
2. **Dado** o aparelho sem rede, **quando** um operador da loja digita o PIN certo, **então** entra, e a sessão fica marcada como offline.
3. **[Segurança]** **Dado** cinco PINs errados sem rede, **quando** ocorre o quinto, **então** aquele usuário fica bloqueado **naquele aparelho** até a próxima sincronização, e o bloqueio é enviado ao servidor.
4. **[Segurança]** **Dado** um usuário desativado ou com o PIN trocado no servidor, **quando** o aparelho sincroniza, **então** o verificador antigo é apagado; até lá, o que ele fez sem rede chega marcado e entra em "Pendências do PDV" para revisão.
5. **Dado** um usuário criado ou incluído na loja depois da última sincronização deste aparelho, **quando** tenta entrar sem rede, **então** vê "Este caixa ainda não recebeu o seu cadastro. Conecte à internet uma vez." (o verificador desce para todos os operadores da loja a cada sincronização; quem não estava na lista naquele momento não entra).

### Operar sem rede
6. **Dado** o aparelho sem rede e dentro da janela, **quando** abro o caixa, **então** a sessão nasce `Open` com `opened_offline`; sangria, suprimento, contagem cega e fechamento funcionam e viram eventos.
7. **Dado** uma ação restrita sem rede, **quando** o supervisor digita o PIN, **então** a aprovação é conferida no aparelho, gravada como evento com a marca offline e, na sincronização, vira `supervisor_approvals` com `offline = true`; supervisor igual ao operador continua recusado.
8. **Dado** o fechamento feito sem rede, **quando** as vendas e a contagem sincronizam, **então** o servidor calcula o esperado e a quebra, e a sessão segue para `Closed` ou `PendingReview` (CX-02); o relatório de fechamento lista as vendas feitas sem rede.

### Limites
9. **Dado** o aparelho há mais tempo sem sincronizar do que a janela da loja, **quando** tento abrir um turno, **então** vejo "Este caixa está há N dias sem enviar as vendas. Conecte à internet para abrir o caixa."; a sessão já aberta pode ser fechada.
10. **Dado** o aparelho a 80% da janela, **quando** opero, **então** a faixa de estado fica em alerta: "Conecte à internet até amanhã às 14h.".
11. **Dado** o aparelho sem rede, **quando** abro o menu, **então** aparecem desabilitados, cada um com "Disponível com internet.": fiado, vale-troca, devolução, busca de cliente cadastrado, consulta de saldo, registro de aparelho e instalação de impressora nova.
12. **Dado** a rede voltando, **quando** o aparelho sincroniza, **então** a fila sobe sozinha, o segredo do aparelho gira e a faixa volta ao normal, sem ação do operador.

### Limites offline
13. **[Segurança]** **Dado** os limites offline da loja (valor máximo por aprovação, valor acumulado e quantidade de aprovações por sessão), **quando** uma aprovação de supervisor sem rede passaria de algum deles, **então** o aparelho recusa com "Acima do limite sem internet. Conecte para aprovar."; **dado** aprovações offline dentro do limite, **quando** a sessão fecha, **então** elas são listadas no fechamento, e passar do limite de alerta da loja leva a sessão a `PendingReview` (CX-02 CA 6).
14. **[Segurança]** **Dado** a janela máxima sem sincronizar, **quando** o aparelho a calcula, **então** conta a partir do **último horário recebido do servidor**, guardado como piso: se o relógio do aparelho estiver antes desse horário, o aparelho usa o piso, registra a divergência e não abre turno novo até sincronizar. Atrasar o relógio não estende a janela.
15. **[Segurança]** **Dado** a decisão da PP-39, **quando** um supervisor aprova sem rede, **então** usa o segredo definido lá: o mesmo PIN ou um código de aprovação offline separado. Na opção com código separado, quebrar o verificador guardado no aparelho não dá uma credencial que valha com rede.

## Regras de negócio e técnicas
- A janela máxima é parâmetro da loja, com teto (PP-10).
- O desenho do verificador (função de derivação, sal, chave de cifra, onde fica o contador de tentativas) fica no adendo à ADR 0026 e passa por **revisão de segurança antes da implementação**. O que este plano afirma é só o requisito: quebrar exige o aparelho e tempo, e o que se ganha quebrando é limitado.
- A exceção "perfil sensível pode ter PIN de supervisor" é nomeada, como a `production.approve_quality`.
- Aprovação offline nunca vale para ação que exige rede.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova.
- **Endpoints:** a descida da OFF-01 ganha a coleção de verificadores; a subida da OFF-02 leva os eventos de sessão e de aprovação.
- **Tabelas:** `identity.device_pin_verifiers`; `opened_offline`, `closed_offline` em `pos.cash_sessions`; `offline` em `pos.supervisor_approvals`; limites offline em `pos.stores`.
- **Auditoria:** entrada offline e bloqueio de PIN chegam ao `audit_log` na sincronização.

## Telas e estados
- **Entrada do `/pdv` sem rede:** lista dos operadores da loja e teclado de PIN, com a faixa "Sem conexão".
- **Itens desabilitados** com a explicação (CA 11).
- **Loja → Lojas e caixas:** parâmetro "Tempo máximo sem internet".
- **Ajuda:** `Ajuda/pdv-sem-conexao.md` atualizada com o que funciona e o que não funciona.

## Plano de testes
- **Vitest:** PIN sem rede (CA 2, 3 e 5); janela e piso de horário, com o relógio atrasado de propósito (CA 9, 10 e 14); limites offline (CA 13); itens desabilitados (CA 11).
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
- PP-11: PIN de supervisor. **PP-39 (decidida em 15/11/2026):** operador com o mesmo PIN; supervisor com código de aprovação offline separado.
- Valores padrão dos limites offline (PP-22).

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
| 24/11/2026 | 1.0 | Implementada (branch `feat/off-03`), pronta para revisão. **Desenho** no adendo de 24/11/2026 à ADR 0026: verificador `HMAC-SHA256` sobre a chave PBKDF2 do hash guardado, por aparelho e com nonce, na descida do catálogo (`verifiers`), registrado em `identity.device_pin_verifiers` e cifrado no aparelho com uma chave AES-GCM não extraível própria; prova HMAC na entrada (`pos.auth`) e na aprovação (`pos.approval`), conferida na subida com o hash atual. **PP-39:** código sem internet do supervisor separado do PIN, em Meu perfil → Segurança. **PP-22 (limites sem rede), recomendação aceita (PO 24/11/2026):** R$ 50,00 por aprovação, 5 aprovações e R$ 150,00 por turno, conferência do fechamento acima de R$ 100,00 aprovados sem rede, cancelamento sem rede só da venda paga toda em dinheiro, troca de preço só com rede; tudo editável na loja (zero desliga). **Desvios:** `pos.supervisor_approvals` já tinha `online` (PDV-04): a aprovação sem rede grava `online = false` em vez de uma coluna `offline`, mais `cash_session_id` para os limites por turno; `pos.session_sales` ganhou `offline` e `sale_number` para a lista do fechamento (CA 8); a entrada sem rede vale como sessão do caixa por 12 h na conferência da subida. **Fora:** recusa no servidor da abertura feita depois da janela (só o aparelho bloqueia); E2E do turno sem rede (no fechamento do marco); revisão de segurança do desenho antes do deploy; campo de um dia offline no piloto. | Claude (dev) |
