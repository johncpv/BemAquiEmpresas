# RH-03 — Exportação do RH para a contadora (layout padrão)

| Campo | Valor |
|---|---|
| ID | RH-03 |
| Título | Pacote mensal para a contadora em CSV e XLSX no layout padrão do sistema, documentado: movimentação do mês (admissões, desligamentos, reajustes, trocas de cargo, afastamentos), eventos do ponto fechado (extras, faltas, atrasos, abonos, banco de horas) e, quando houver, a folha lançada; gerado só por quem tem `hr.view_sensitive`, com dados minimizados e registro de cada exportação |
| Tamanho | M (D no PRD) |
| Status | Draft |
| Requisitos | R-RH-8 (exportação para contadora, D) · P-06 (layout padrão CSV/XLSX documentado; adicionais como plug-ins) · PRD §6 LGPD · R-SEC-5 · R-PRP-3 |
| Dependências | RH-01c (períodos fechados), CAD-04b/04c (movimentação e afastamentos), RH-02a/02c (folha lançada, opcional no pacote) |
| Bloqueia | — |
| Ordem de execução | 7 (Marco 6.2 "Folha lançada") |

## User story
Como **RH**, quero **gerar com um clique o pacote do mês para a contadora**, para **ela calcular a folha com as horas e as mudanças que já estão no sistema, sem eu redigitar nada**.

## Contexto e referências
- PRD R-RH-8 ("Exportação para contadora (layout a definir)", D); `06` P-06 (decidida em 16/10/2026: "um layout padrão do sistema (CSV/XLSX documentado); layouts adicionais entram depois, como plug-ins de exportação"); R-FIN-14 é a exportação financeira, separada.
- PRD R-PRP-3 ("nada digitado duas vezes").
- ADR 0016 e 0018 (dado sensível, `SensitiveRead`, minimização), MIG-01a (CSV sem injeção de fórmula).

## Critérios de aceite
1. **Dado** `hr.view_sensitive` e um período de ponto fechado (RH-01c), **quando** abro RH → Exportar para a contadora, escolho a competência e clico "Gerar pacote", **então** recebo um ZIP com os arquivos do layout padrão: `movimentacao.csv`, `ponto.csv` e, se houver folha da competência, `folha.csv`, e um `leia-me.txt` com a versão do layout; a mesma opção gera um XLSX com uma aba por arquivo.
2. **Dado** `movimentacao.csv`, **quando** gerado, **então** traz uma linha por evento da competência: admissão (matrícula, nome, CPF, nascimento, cargo, CBO, salário, jornada, data), desligamento (data), reajuste (salário novo, vigência), troca de cargo (cargo novo, vigência) e afastamento (tipo, início, fim previsto; o motivo nunca sai), com as colunas e formatos documentados na ajuda (datas `dd/mm/aaaa`, decimal com vírgula, separador `;`, UTF-8 com BOM).
3. **Dado** `ponto.csv`, **quando** gerado, **então** traz por funcionário os totais do período fechado: dias trabalhados, horas extras, faltas (dias e horas), atrasos, horas abonadas por tipo e o banco de horas (mês e acumulado), iguais aos do espelho fechado (teste com o dataset da RH-01c); período em aberto não é exportado (422 "Feche o período de ponto antes de exportar.").
4. **[Segurança]** **Dado** o pacote, **quando** gerado, **então** só leva o necessário para a folha (minimização): sem endereço, telefone, e-mail, dados bancários, dependentes, localização do ponto nem documentos anexados, a menos que a contadora precise e o PO decida (Pontos em aberto 1); toda célula que começa com `=`, `+`, `-` ou `@` sai como texto (MIG-01a).
5. **[Segurança]** **Dado** cada geração, **quando** acontece, **então** é registrada como `SensitiveRead` (entidade "Exportação contadora", competência, usuário, IP) e listada em "Exportações anteriores" (data, quem, competência, versão do layout); o arquivo não fica guardado no servidor (proposta) e a resposta tem `Cache-Control: no-store`.
6. **Dado** a mesma competência exportada de novo depois de uma reabertura do ponto, **quando** gero, **então** o `leia-me.txt` e a tela dizem "Substitui a exportação de {data}" e o `NextSteps` sugere "Avisar a contadora" (abre o e-mail do usuário com o texto pronto, sem anexo, proposta).
7. **Dado** o layout padrão, **quando** ele mudar, **então** a versão sobe (`v1`, `v2`), a ajuda mantém a descrição das versões e um teste golden trava o cabeçalho de cada arquivo da versão atual.
8. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem `hr.view_sensitive`, 403 `forbidden` e a opção some do menu. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Exportador** atrás de uma interface (`IAccountantExport`, proposta) com o layout padrão como primeira implementação; os layouts adicionais (P-06) entram depois como novas implementações, escolhidas por parâmetro do HR.
- **Fonte:** só dados fechados (período de ponto `Closed`) e a linha do tempo da competência; nada é recalculado na exportação.
- XLSX pela biblioteca já aprovada no projeto, se houver; senão, só CSV no MVP e a escolha da biblioteca vai para o ponto em aberto 3.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view_sensitive`.
- **Eventos:** nenhum.
- **Tabelas:** nenhuma nova (o registro fica na auditoria, `SensitiveRead`).

## Telas e estados
- **RH → Exportar para a contadora** (`/app/rh/exportar-contadora`): escolha da competência (só as fechadas habilitadas, com a dica "Feche o ponto do mês para exportar."), botões "Gerar pacote (CSV)" e "Gerar planilha (XLSX)", lista "Exportações anteriores".
- **Estados:** carregando; vazio ("Nenhum período fechado ainda." com o atalho "Ir para o espelho"); erro; gerando.
- **400 px:** a lista vira cartões.
- **Ajuda:** `Ajuda/rh-exportar-contadora.md` com o layout padrão completo (colunas, tipos, exemplos); `<Dica>` em "Layout padrão".

## Plano de testes
- **Unidade:** linhas de cada arquivo com o dataset fixo (CA 2, 3); proteção de célula (CA 4).
- **Integração (Testcontainers):** período em aberto recusado (CA 3); colunas proibidas ausentes (CA 4); `SensitiveRead`, `no-store` e lista de exportações (CA 5); nova exportação (CA 6); teste golden do cabeçalho (CA 7); três camadas (CA 8).
- **E2E (Playwright):** gerar o pacote da competência de teste e abrir o CSV.

## Fora do escopo
- Exportação financeira e fiscal (R-FIN-14): outra story.
- Envio automático do pacote à contadora por e-mail ou integração com o sistema dela: depois do MVP.
- eSocial: fora do escopo do produto (PRD §3.3).

## Pontos em aberto
1. **Colunas do layout padrão** (contadora): a proposta acima, e se a contadora precisa de dados bancários, dependentes de IR ou endereço na admissão (hoje fora, por minimização).
2. Guardar ou não o arquivo gerado (proposta: não guardar).
3. Biblioteca de XLSX (arquiteto): reaproveitar a que existir no projeto ou entregar só CSV no MVP.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), golden e E2E passando.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados; ajuda com o layout documentado; teste de cobertura passando.
- [ ] Auditoria ligada (`SensitiveRead`).
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IAccountantExport` e o layout `v1`; endpoint `/api/v1/hr/accountant-exports`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (minimização), @qa.
- Gate: CA 3, 4 e 5 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** layout padrão do sistema conforme a P-06 (decidida), sem inventar o layout da contadora; só período fechado; minimização e motivo de afastamento nunca exportado (CA 2, 4).
- **Pré-condições para `Ready`:** ponto em aberto 1.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
