# CAD-01b — Consulta de CNPJ e de CEP que preenche o cadastro do terceiro

| Campo | Valor |
|---|---|
| ID | CAD-01b |
| Título | Consulta de CNPJ e de CEP na BrasilAPI, com adaptadores `ICnpjLookup` e `ICepLookup`, cache, limites, cadastro manual quando falha e "Atualizar pela Receita" |
| Tamanho | M (quebra da CAD-01; ganhou o CEP na revisão pré-Fase 1) |
| Status | Done |
| Requisitos | R-TER-1 (consulta automática por CNPJ) · R-PRP-3 (CNPJ e CEP preenchem o cadastro) · R-PRP-7 (sugere, o usuário confirma) · PRD §6 Observabilidade (alerta de integração) |
| Dependências | CAD-01a; FND-09d (`CepInput`, `DocumentInput`); Fase 0 (FND-12 alerta de integração, FND-04 rate limit) |
| Bloqueia | — (CAD-06 e CAD-04b reaproveitam o `ICepLookup`) |
| Ordem de execução | 5 (Marco A — terceiros reais) |

## User story
Como **quem cadastra clientes e fornecedores**, quero **digitar só o CNPJ ou o CEP e ver razão social, nome fantasia e endereço preenchidos**, para **cadastrar mais rápido e sem erro de digitação**.

## Contexto e referências
- PRD R-TER-1, R-PRP-3.
- `docs/02-arquitetura.md` §1 e §6.3 (consulta de CNPJ em API pública com cache).
- Decidido em 18/10/2026 (tabela 1.1): **BrasilAPI** para CNPJ (`/api/cnpj/v1/{cnpj}`) e CEP (`/api/cep/v2/{cep}`), sem provedor reserva automático; cache de 30 dias; CNPJ com situação diferente de ativa gera só aviso.
- Revisão pré-Fase 1 §2.5 (CAD-01b): regex antes da URL, sem redirect, resposta até 64 KB, log do HttpClient desligado, limite global e circuit breaker (a BrasilAPI limita por IP e o IP da VPS é compartilhado), cache por tenant sem o QSA e expurgado por `TenantJob` (razão social de MEI contém nome e CPF).
- FND-08 (nunca buscar URL fornecida pelo tenant), FND-12 (alerta por integração), ADR 0014 (adaptador só com `HttpClient`, tempo limite, erros traduzidos).
- CAD-01a CA 4: a duplicidade do documento é conferida **antes** da consulta.

## Critérios de aceite
**CNPJ**
1. **Dado** o formulário "Novo terceiro" com um CNPJ válido e sem duplicidade (CAD-01a CA 4), **quando** saio do campo (ou clico "Consultar CNPJ"), **então** o sistema consulta a BrasilAPI e mostra razão social, nome fantasia, endereço com código IBGE, telefone, e-mail e situação cadastral num quadro "Dados da Receita", com o botão "Usar estes dados"; se o CNPJ já existe no tenant, a consulta não é feita.
2. **Dado** o quadro "Dados da Receita", **quando** clico "Usar estes dados", **então** os campos vazios do formulário e o bloco "Endereço de cobrança" são preenchidos e continuam editáveis; nada é gravado até "Salvar terceiro" (R-PRP-7).
3. **Dado** um CNPJ com situação diferente de "Ativa" (baixada, inapta, suspensa, nula), **quando** a consulta volta, **então** o quadro mostra o aviso âmbar "Situação na Receita: {situação}" e o cadastro não é bloqueado.
4. **Dado** um CNPJ consultado neste tenant nos últimos 30 dias, **quando** consulto de novo, **então** o resultado vem do cache, sem chamar o provedor, com "Consultado em dd/mm/aaaa".
5. **Dado** o provedor fora do ar, com erro, acima de 5 s ou com o circuit breaker aberto, **quando** consulto, **então** a tela diz "Não foi possível consultar o CNPJ agora. Preencha os dados ou tente de novo." e o cadastro manual segue; não há tentativa em outro provedor; a falha conta para o alerta `cnpj:brasilapi` (FND-12).
6. **Dado** um CNPJ não encontrado, **quando** consulto, **então** a tela diz "CNPJ não encontrado na base pública. Confira o número ou preencha os dados."
7. **Dado** um CNPJ inválido, **quando** tento consultar, **então** a API responde 400 "CNPJ inválido." e o provedor não é chamado.
8. **Dado** um terceiro PJ existente e `parties.edit`, **quando** clico "Atualizar pela Receita", **então** vejo a comparação campo a campo (atual × Receita), escolho quais aplicar e confirmo "Aplicar selecionados"; o resto não muda.
9. **Dado** o tipo PF, **quando** informo o CPF, **então** não há consulta e só a validação do dígito vale.

**CEP**
10. **Dado** qualquer endereço do terceiro (criação ou aba Endereços), **quando** completo os 8 dígitos do CEP, **então** o sistema consulta a BrasilAPI e preenche logradouro, bairro, cidade, UF e código IBGE nos campos vazios, que continuam editáveis; CEP não encontrado diz "CEP não encontrado. Preencha o endereço."; falha ou tempo limite (5 s) diz "Não foi possível consultar o CEP agora." e conta para o alerta `cep:brasilapi`; o resultado fica em cache por 30 dias.

**Segurança**
11. **[Segurança]** **Dado** os endpoints `GET /api/v1/parties/cnpj/{cnpj}` e `GET /api/v1/parties/cep/{cep}`, **quando** um usuário sem `parties.create` e sem `parties.edit` chama, **então** recebe 403 `forbidden`; com o módulo `parties` não liberado, 403 `module_not_released`.
12. **[Segurança]** **Dado** muitas consultas seguidas, **quando** um usuário passa de 30 por minuto (por usuário + tenant), **então** recebe 429 com "Muitas consultas seguidas. Aguarde um minuto."; além disso, um limite global por instância e um circuit breaker protegem o IP compartilhado da VPS.
13. **[Segurança]** **Dado** a chamada ao provedor, **quando** inspeciono a requisição (servidor HTTP falso), **então**: o CNPJ e o CEP passam por regex (`^[0-9A-Z]{12}[0-9]{2}$` e `^[0-9]{8}$`) antes de montar a URL; a URL base é fixa na configuração da plataforma; redirect é recusado; a resposta acima de 64 KB é descartada; a resposta é validada por esquema e tratada como texto; campos fora do esperado são descartados; o log do `HttpClient` está desligado.
14. **[Segurança]** **Dado** o cache de CNPJ, **quando** gravado, **então** fica no banco do tenant (`parties.cnpj_lookups`), sem o quadro societário (QSA), e um `TenantJob` diário apaga as linhas com mais de 30 dias; a razão social do cache é tratada como dado pessoal possível (MEI) e não vai para log.
15. **Dado** os adaptadores, **quando** troco de provedor por configuração, **então** nada muda na tela nem no cadastro (`ICnpjLookup`, `ICepLookup`).

## Regras de negócio e técnicas
- `ICnpjLookup.LookupAsync(cnpj)` em `Parties.Application`, devolvendo `Result<CnpjData>`; `ICepLookup.LookupAsync(cep)` no BuildingBlocks (para a CAD-06 e a CAD-04b reaproveitarem), devolvendo `Result<AddressData>`. Adaptadores só com `HttpClient`, tempo limite de 5 s, sem nova tentativa e sem reserva automática.
- Cache de CNPJ: `parties.cnpj_lookups` (`document`, `payload` jsonb sem QSA, `looked_up_at`), por tenant, expurgado aos 30 dias. Cache de CEP: em memória, 30 dias (dado público, sem dado pessoal).
- O mapeamento da resposta (CEP, UF, IBGE, logradouro) fica no adaptador; o domínio recebe sempre `CnpjData`/`AddressData`.
- Logs: o CNPJ pode aparecer; a resposta do provedor e a razão social não são logadas.
- Rate limit pela infraestrutura da FND-04 (política `RateLimit:LookupsPerMinute` = 30).
- CNPJ alfanumérico: se a BrasilAPI ainda não o aceitar, a consulta devolve "não encontrado" e o cadastro segue manual.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `parties.create` ou `parties.edit`.
- **Eventos:** nenhum.
- **Tabelas:** `parties.cnpj_lookups` (criada nesta story).

## Telas e estados
- No formulário do terceiro (CAD-01a): botão "Consultar CNPJ" ao lado do documento; quadro "Dados da Receita" com os dados, o aviso de situação e "Usar estes dados"; no detalhe, "Atualizar pela Receita" com a comparação; `CepInput` em todo endereço.
- **Estados:** consultando (indicador no campo, sem travar o formulário); encontrado; não encontrado; falha (CA 5 e 10); limite excedido.
- **Propositivo:** a consulta dispara sozinha ao sair de um CNPJ válido e sem duplicidade e ao completar o CEP; só os campos vazios são preenchidos.
- **Ajuda:** seções "Consulta de CNPJ" e "Consulta de CEP" e a pergunta "Por que a consulta não achou meu CNPJ?" em `Ajuda/cadastros-terceiro-novo.md` e `Ajuda/cadastros-terceiro.md` (abas `dados` e `enderecos`); `<Dica>` no botão "Consultar CNPJ".

## Plano de testes
- **Unidade:** mapeamento das respostas gravadas da BrasilAPI para `CnpjData` e `AddressData`; comparação campo a campo (CA 8); regex (CA 13).
- **Integração (Testcontainers + servidor HTTP falso):** consulta e preenchimento (CA 1, 2), sem consulta quando duplicado; situação não ativa (CA 3); cache e expurgo pelo `TenantJob` (CA 4, 14); fora do ar, tempo limite e circuit breaker contando para o alerta (CA 5); não encontrado (CA 6); inválido sem chamada (CA 7); CEP (CA 10); 403 e 429 (CA 11, 12); redirect recusado, resposta acima de 64 KB descartada e URL fixa (CA 13); troca de adaptador (CA 15).
- **E2E (Playwright):** digitar CNPJ, usar os dados, completar o CEP de um endereço de entrega e salvar, com o servidor falso.

## Fora do escopo
- Consulta de CPF.
- Validação da inscrição estadual pelo Sintegra.
- Provedor reserva automático.

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1): BrasilAPI para CNPJ e CEP, sem reserva automática; cache de 30 dias; situação não ativa só avisa. Tempo limite de 5 s e limite de 30 consultas por minuto adotados como padrão (sugestões da story, aprovadas em bloco na D11).
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (com servidor HTTP falso) e E2E passando.
- [ ] Migration criada (`parties.cnpj_lookups`).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Estados da consulta na tela.
- [ ] Ajuda atualizada (criação e abas `dados` e `enderecos`).
- [ ] Propositivo avaliado (consulta automática, só campos vazios).
- [ ] Alertas de integração `cnpj:brasilapi` e `cep:brasilapi` ligados.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ICnpjLookup`, `CnpjData` e adaptador BrasilAPI; `ICepLookup`, `AddressData` e adaptador BrasilAPI no BuildingBlocks.
- [ ] `HttpClient` com regex, sem redirect, limite de 64 KB, log desligado, limite global e circuit breaker.
- [ ] Cache no banco com `TenantJob` de expurgo; cache de CEP em memória; política de rate limit.
- [ ] Endpoints `GET /api/v1/parties/cnpj/{cnpj}` e `GET /api/v1/parties/cep/{cep}`.
- [ ] Quadro "Dados da Receita", comparação "Atualizar pela Receita" e preenchimento por CEP.

## Quality gates
- Agentes previstos: @dev, cybersecurity (integração externa e resposta não confiável), @qa.
- Gate: CA 13 (requisição mínima, sem redirect e com limite) e CA 14 (cache sem QSA e expurgado) obrigatórios.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** provedor BrasilAPI decidido (sem ADR própria; registrado na tabela 1.1 e aqui); consulta de CEP incluída; consulta só depois da checagem de duplicidade; CAs de segurança da §2.5 (regex, redirect, 64 KB, log, limite global, circuit breaker, cache sem QSA com expurgo); valores dos CAs 4, 5 e 12 fixados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (tabela 1.1: BrasilAPI, CEP, cache) e CAs de segurança da revisão pré-Fase 1; ordem 5 do Marco A | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 19/10/2026 | 1.0 | Implementada. Validação responde 422 (convenção da API). O CEP v2 da BrasilAPI não traz o código IBGE: ele vem da lista de municípios da UF (`/api/ibge/municipios/v1/{uf}`), em cache. O limite por usuário fica num filtro do endpoint, depois da autenticação. "Usar estes dados" também preenche e-mail e telefone do terceiro, que ganharam campos no formulário | Dev |
