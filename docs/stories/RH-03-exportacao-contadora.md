# RH-03 — Exportação do RH para a contadora (layout padrão)

| Campo | Valor |
|---|---|
| ID | RH-03 |
| Título | Pacote mensal para a contadora em CSV no layout padrão do sistema (`v1`, documentado): movimentação do mês (admissões, desligamentos, reajustes, trocas de cargo, afastamentos) e, quando houver, a folha lançada fechada; os eventos do ponto entram no pacote com a RH-01c; gerado só por quem tem `hr.view_sensitive`, com dados minimizados, sem guardar o arquivo e com registro de cada exportação |
| Tamanho | M (D no PRD) |
| Status | Done (pré-condição externa: a contadora confere as colunas do layout `v1`) |
| Requisitos | R-RH-8 (exportação para contadora, D) · P-06 (layout padrão documentado; adicionais como plug-ins) · PRD §6 LGPD · R-SEC-5 · R-PRP-3 |
| Dependências | RH-02a (folha lançada e parâmetros do RH), RH-02c (folha fechada no pacote), CAD-04b e CAD-04c1 (movimentação e afastamentos) |
| Bloqueia | RH-01c (acrescenta o `ponto.csv` ao pacote) |
| Ordem de execução | 5 (Marco 6.2 "Folha lançada") |

## User story
Como **RH**, quero **gerar com um clique o pacote do mês para a contadora**, para **ela calcular a folha com as mudanças que já estão no sistema, sem eu redigitar nada**.

## Contexto e referências
- PRD R-RH-8 ("Exportação para contadora (layout a definir)", D); `06` P-06 (decidida em 16/10/2026: "um layout padrão do sistema (CSV/XLSX documentado); layouts adicionais entram depois, como plug-ins de exportação"); R-FIN-14 é a exportação financeira, separada.
- PRD R-PRP-3 ("nada digitado duas vezes").
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D15 (a Fase 6 começa pela folha; o ponto vem depois, e o `ponto.csv` entra com a RH-01c), tabela 3.3 pontos 28 a 30, §4.4 padrão 8.
- ADR 0016 e 0018 (dado sensível, `SensitiveRead`, minimização), MIG-01a (CSV sem injeção de fórmula); ADR 0042 (valores pré-configurados "a confirmar com a contadora").

## Critérios de aceite
1. **Dado** `hr.view_sensitive`, **quando** abro RH → Exportar para a contadora, escolho a competência (o mês atual ou um anterior) e clico "Gerar pacote", **então** recebo um ZIP com os arquivos do layout padrão `v1`: `movimentacao.csv`, `folha.csv` (só se houver folha da competência `Closed` ou `Paid`) e um `leia-me.txt` com a versão do layout, a competência, a data de geração e a lista do que entrou e do que não entrou ("Ponto: não incluído; o ponto do sistema ainda não está em uso." até a RH-01c).
2. **Dado** `movimentacao.csv`, **quando** gerado, **então** traz uma linha por evento da competência: admissão (matrícula, nome, CPF, nascimento, cargo, CBO, salário, jornada, data), desligamento (data), reajuste (salário novo, vigência), troca de cargo (cargo novo, vigência) e afastamento (tipo, início, fim previsto; o motivo nunca sai), com as colunas e formatos documentados na ajuda (datas `dd/mm/aaaa`, decimal com vírgula, separador `;`, UTF-8 com BOM).
3. **Dado** `folha.csv`, **quando** gerado, **então** traz uma linha por linha de holerite da folha fechada (matrícula, tipo da folha, código da rubrica, referência, valor) e uma por guia (tipo, valor, vencimento), iguais às da folha (teste com o dataset da RH-02a); folha em rascunho não entra.
4. **[Segurança]** **Dado** o pacote, **quando** gerado, **então** só leva o necessário para a folha (minimização): sem endereço, telefone, e-mail, dados bancários, dependentes, localização do ponto nem documentos anexados; toda célula que começa com `=`, `+`, `-` ou `@` sai como texto (MIG-01a).
5. **[Segurança]** **Dado** cada geração, **quando** acontece, **então** é registrada como `SensitiveRead` (entidade "Exportação contadora", competência, usuário, IP) e listada em "Exportações anteriores" (data, quem, competência, versão do layout); o arquivo **não fica guardado** no servidor e a resposta tem `Cache-Control: no-store`.
6. **Dado** a mesma competência exportada de novo (por exemplo, depois da reabertura da folha), **quando** gero, **então** o `leia-me.txt` e a tela dizem "Substitui a exportação de {data}" e o `NextSteps` sugere "Avisar a contadora" (abre o e-mail do usuário com o texto pronto, sem anexo).
7. **Dado** o layout padrão, **quando** ele mudar (inclusive quando a RH-01c acrescentar o `ponto.csv`), **então** a versão sobe (`v1`, `v2`), a ajuda mantém a descrição das versões e um teste golden trava o cabeçalho de cada arquivo da versão atual.
8. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo com `hr`, 403 `forbidden`; sem `hr.view_sensitive`, 403 `forbidden` e a opção some do menu. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Exportador** atrás de uma interface (`IAccountantExport`) com o layout padrão `v1` como primeira implementação; os layouts adicionais (P-06) entram depois como novas implementações, escolhidas por parâmetro do HR.
- **Fonte:** só dados fechados (folha `Closed`/`Paid`) e a linha do tempo da competência; nada é recalculado na exportação.
- **Formato:** só CSV no MVP; XLSX depois, com biblioteca de licença MIT.
- **Seções do pacote:** cada arquivo é uma seção registrada no exportador; a RH-01c registra a seção `ponto.csv` sem mudar esta story.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view_sensitive`.
- **Eventos:** nenhum.
- **Tabelas:** nenhuma nova (o registro fica na auditoria, `SensitiveRead`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 (bloco "Dados protegidos" padrão).
- **RH → Exportar para a contadora** (`/app/rh/exportar-contadora`): escolha da competência, botão "Gerar pacote (CSV)", o que vai entrar no pacote ("Movimentação: 3 eventos; Folha: fechada em {data}; Ponto: não incluído"), lista "Exportações anteriores".
- **Estados:** carregando; vazio ("Nenhuma exportação ainda. Escolha o mês e gere o pacote."); erro; gerando.
- **Propositivo:** competência sugerida = o mês anterior; aviso "A folha de {mês} ainda está em rascunho: ela não entra no pacote." com o atalho para a folha.
- **400 px:** a lista vira cartões; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/rh-exportar-contadora.md` com o layout padrão `v1` completo (colunas, tipos, exemplos); `<Dica>` em "Layout padrão".

## Plano de testes
- **Unidade:** linhas de cada arquivo com o dataset fixo (CA 2, 3); proteção de célula (CA 4).
- **Integração (Testcontainers):** pacote com e sem folha fechada e o `leia-me.txt` (CA 1); folha em rascunho fora (CA 3); colunas proibidas ausentes (CA 4); `SensitiveRead`, `no-store`, arquivo não guardado e lista de exportações (CA 5); nova exportação (CA 6); teste golden do cabeçalho (CA 7); três camadas (CA 8).
- **E2E (Playwright):** gerar o pacote da competência de teste e abrir o CSV.

## Fora do escopo
- Exportação financeira e fiscal (R-FIN-14): outra story.
- Eventos do ponto (`ponto.csv`): RH-01c.
- XLSX: depois do MVP, com biblioteca MIT.
- Envio automático do pacote à contadora por e-mail ou integração com o sistema dela: depois do MVP.
- eSocial: fora do escopo do produto (PRD §3.3).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 28–30):
1. **Colunas do layout padrão:** as do layout `v1` (CA 2, 3), sem dados bancários, dependentes nem endereço (minimização, CA 4); a contadora confere.
2. **Arquivo não guardado;** baixado só com `hr.view_sensitive` (CA 5, 8).
3. **Só CSV no MVP;** XLSX depois, com biblioteca MIT (regras).

Decisões da revisão aplicadas: D15 (folha antes do ponto: o pacote sai sem o ponto até a RH-01c, CA 1, 7); §4.4 padrão 8.

**Pré-condição externa:** a contadora confere as colunas do layout `v1`; se ela pedir outra coluna ou outro layout, entra uma versão nova ou um plug-in (P-06); não bloqueia o `Ready`.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), golden e E2E passando.
- [x] Permissões só do `ModuleCatalog`.
- [x] Tela com estados; ajuda com o layout documentado; teste de cobertura passando.
- [x] Auditoria ligada (`SensitiveRead`).
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `IAccountantExport` com seções registráveis e o layout `v1`; endpoint `/api/v1/hr/accountant-exports`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (minimização), @qa.
- Gate: CA 1, 4 e 5 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: sem a RH-01c, que vem depois e acrescenta o ponto) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** layout padrão do sistema conforme a P-06 (decidida), sem inventar o layout da contadora; só dado fechado; minimização e motivo de afastamento nunca exportado.
- **Correções da revalidação (21/10/2026):** pontos 28 a 30 decididos (colunas do `v1` com conferência da contadora, arquivo não guardado, só CSV); com a folha antes do ponto, o pacote não exige período de ponto fechado e o `ponto.csv` passa para a RH-01c (CA 1, 3, 7); `folha.csv` descrito (CA 3); ordem 5 do Marco 6.2, bloqueando a RH-01c; CAD-04c → CAD-04c1.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência das colunas pela contadora.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D15, tabela 3.3 pontos 28–30, §4.4 padrão 8); nova ordem (Marco 6.2, ordem 5; `ponto.csv` na RH-01c) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 27/10/2026 | 1.0 | Implementada (@dev). Backend: `IAccountantExport` com o layout padrão `v1` (`StandardAccountantExport`) montado por seções registradas no DI (`IAccountantExportSection`: `movimentacao.csv`, `folha.csv` e a seção do ponto "não incluído", que a RH-01c troca pelo `ponto.csv`); regras puras em `AccountantLayoutV1` (cabeçalhos, formatos `dd/mm/aaaa`, decimal com vírgula, CPF com máscara, CBO `9999-99`, `leia-me.txt`); CSV pelo `SafeCsv` (`;`, UTF-8 com BOM, célula com `=`, `+`, `-` ou `@` com apóstrofo). Rotas `GET /api/v1/hr/accountant-exports` (tela: o que entra, folhas em rascunho, exportação anterior e histórico, sem valores) e `GET /api/v1/hr/accountant-exports/package?period=` (ZIP em memória, `attachment`, `private, no-store`, nunca gravado), as duas com `hr.view_sensitive` no grupo `hr` (`RequireModule`). Cada geração grava `SensitiveRead` (`AccountantExport`, a competência, o layout como referência, usuário e IP), lido por um novo `IAuditReader` (BuildingBlocks) para "Exportações anteriores" e "Substitui a exportação de {data}"; nada em log nem outbox. Layout escolhido pelo parâmetro do RH `hr.accountant_export_layout` (tipo `ExportLayout`, padrão `v1`, "A confirmar com a contadora", ADR 0042), editável em Configurações → Parâmetros → RH. Sem migration (nenhuma tabela; o parâmetro entra pelo seed). Front: RH → Exportar para a contadora (`/app/rh/exportar-contadora`, aba só com `hr.view_sensitive`, bloco "Dados protegidos" sem ela), competência sugerida = mês anterior (só o atual ou anteriores), "O que vai entrar no pacote", aviso da folha em rascunho com "Abrir a folha", "Gerar pacote (CSV)", `NextSteps` "Avisar a contadora" (`mailto:` com o texto pronto, sem anexo), "Exportações anteriores" em tabela e em cartões a 400 px; ajuda `Ajuda/rh-exportar-contadora.md` com o layout `v1` completo e `<Dica>` em "Layout padrão". Testes: `AccountantExportRulesTests` (golden dos cabeçalhos, linhas do dataset fixo, proteção de célula, leia-me) e `AccountantExportTests` (Testcontainers: pacote com e sem folha fechada, rascunho fora, minimização por canário, `SensitiveRead`, `no-store`, outbox intacto, nova exportação, três camadas, parâmetro); E2E `accountant-export.spec.ts` (desktop e 400 px). Decisões (recomendação aceita, PO 24/10): (1) afastamento entra quando cobre algum dia da competência, não só quando começa nela; (2) o tipo do afastamento só sai para quem tem também `hr.view_health` (regra da CAD-04c1b), senão a coluna vem vazia e o `leia-me.txt` explica; (3) `folha.csv` usa `matricula_ou_cpf` (o CPF quando não há matrícula), como o layout de importação da RH-02a, e traz também a descrição da rubrica; (4) o CPF vai em todas as linhas da movimentação (identificação), o nascimento só na admissão; (5) o pacote é gerado por `GET` autenticado (como a exportação do rastreio, EST-04), com o registro na auditoria. | @dev |
