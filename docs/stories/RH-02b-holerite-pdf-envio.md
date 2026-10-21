# RH-02b — Folha lançada: holerite em PDF e envio ao funcionário

| Campo | Valor |
|---|---|
| ID | RH-02b |
| Título | Holerite em PDF no layout padrão (empresa, funcionário, competência, proventos, descontos, bases de INSS, FGTS e IRRF, FGTS do mês e líquido) pelo `IPdfRenderer`; download individual e em lote; envio por e-mail pela fila, só depois da folha fechada, no formato de proteção decidido pelo PO; histórico de envio por holerite |
| Tamanho | M (quebra da RH-02) |
| Status | Draft |
| Requisitos | R-RH-5 (parte: gerar holerite PDF, enviar por e-mail) · R-COM-1 · R-COM-3 · PRD §6 LGPD · R-SEC-5 · R-PRP-2 |
| Dependências | RH-02a (folha, holerites, linhas), RH-02c (envio só com a folha fechada), COM-03 (`IPdfRenderer`, `StoreGeneratedAsync`, e-mail com anexo depois do `Clean`, ADR 0019 §7 e 0022), CAD-06 (perfil e logo da empresa), FND-08 (e-mail) |
| Bloqueia | FLH-02a (o holerite calculado usa o mesmo layout) |
| Ordem de execução | 6 (Marco 6.2 "Folha lançada", depois da RH-02c) |

## User story
Como **RH**, quero **gerar o holerite de cada funcionário em PDF e enviá-lo por e-mail com um clique**, e como **funcionário**, quero **receber o meu holerite sem que outra pessoa veja o meu salário**, para **acabar com o holerite impresso e manter o sigilo**.

## Contexto e referências
- PRD R-RH-5; backlog RH-02, CA "holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido)".
- ADR 0022 (QuestPDF atrás do `IPdfRenderer`, Archivo embutida, teste na imagem Alpine, limites, sem URL externa); ADR 0019 §7 (`StoreGeneratedAsync`, e-mail com anexo só depois do `Clean`, `attachment_ids` no `email_logs`); FND-08 (fila, 3 tentativas, templates Fluid).
- PRD §6 LGPD; `03-modelo-dados` → `shared.email_logs.to` é `[PersonalData]`; `hr.employees.email` (e-mail pessoal, sensível, CAD-04b).
- P-21 (licença Community do QuestPDF, decidida).

## Critérios de aceite
**PDF**
1. **Dado** `hr.view_sensitive` e um holerite com linhas, **quando** clico "Baixar holerite", **então** o PDF é gerado pelo `IPdfRenderer` com: cabeçalho da empresa (razão social, CNPJ e logo `Clean`, CAD-06), funcionário (nome, matrícula, cargo, CBO, admissão), competência e tipo da folha, tabela de proventos e descontos (código, descrição, referência, valor), totais de proventos e descontos, líquido em destaque, bases do INSS, do FGTS e do IRRF e o FGTS do mês; **não** contém CPF completo (proposta: só os 3 primeiros e 2 últimos dígitos), endereço, dados bancários nem dependentes; teste de extração de texto confere os valores e a ausência desses campos.
2. **Dado** a folha, **quando** clico "Baixar todos os holerites", **então** recebo um ZIP com um PDF por funcionário (nome do arquivo `holerite-{competencia}-{matricula ou id curto}.pdf`, sem CPF nem nome), gerado em segundo plano para mais de 20 holerites, com a mensagem "Gerando {n} holerites…" e o aviso no sino quando pronto.
3. **Dado** a folha `Draft`, **quando** baixo o holerite, **então** o PDF leva a marca d'água "RASCUNHO — folha não fechada"; com a folha fechada, o PDF é guardado como arquivo do sistema (`StoreGeneratedAsync`, dono `Payslip`, leitura e escrita com `hr.view_sensitive`, `OwnerExists`) e o download registra `SensitiveRead`.
4. **Dado** o limite da ADR 0022, **quando** um holerite passa de 300 linhas, **então** 422 antes de gerar; o teste de desempenho gera 50 holerites em menos de 30 s na imagem Alpine.

**Envio**
5. **Dado** a folha `Closed` (RH-02c) e `hr.edit` + `hr.view_sensitive`, **quando** clico "Enviar holerites", **então** o diálogo mostra quantos funcionários têm e-mail pessoal cadastrado, quantos não têm (com a lista e o atalho "Completar dados pessoais") e o formato de proteção vigente (Pontos em aberto 1); confirmar "Enviar {n} holerites" enfileira um e-mail por funcionário (template `payslip`, assunto "Holerite de {mês}/{ano} — {empresa}", sem valor no assunto nem no corpo), com o PDF anexado pelo id depois do `Clean` (ADR 0019 §7); a mensagem de sucesso diz "{n} holerites na fila de envio".
6. **[Segurança]** **Dado** o envio, **quando** o e-mail é montado, **então** o destinatário é só o e-mail pessoal do próprio funcionário (nunca cópia, nunca outro endereço digitado), o corpo não tem salário nem líquido, e o `email_logs` guarda o destinatário mascarado na auditoria; o comando exige `Idempotency-Key`, e reenviar a folha não duplica o que já saiu: só "Reenviar" por holerite, com confirmação.
7. **Dado** um holerite enviado, **quando** abro a folha → Holerites, **então** vejo por funcionário a situação do envio (Na fila, Enviado em {data}, Falhou: {motivo}, Sem e-mail) pelo `email_logs`, com "Reenviar" e "Baixar".
8. **Dado** a folha reaberta depois do envio (RH-02c), **quando** é fechada de novo com mudança no holerite, **então** o holerite ganha a marca "Alterado depois do envio" e o `NextSteps` oferece "Reenviar os holerites alterados".

**Permissões e auditoria**
9. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; baixar exige `hr.view_sensitive`, enviar `hr.edit` + `hr.view_sensitive`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/payroll-runs/{id}/payslips/{payslipId}/pdf`.
10. **[Segurança]** **Dado** geração, download e envio, **quando** consulto a auditoria, **então** vejo quem, quando e qual holerite, sem valor; o canário confirma que salário e líquido não aparecem em log, em `email_logs.model` nem em argumento de job (o job leva só ids, ADR 0019 §12).

## Regras de negócio e técnicas
- **Layout** `PayslipPdfDocument` no HR (ADR 0022), números em `pt-BR` com `tabular-nums`; um holerite por página.
- **Arquivo:** gerado na hora para rascunho (sem guardar); guardado como arquivo do sistema com a folha fechada; o PDF guardado é refeito se a folha for reaberta e fechada de novo.
- **E-mail:** fila `email` (FND-08), template do sistema em Fluid, sem dado do holerite no modelo além de nome, mês e empresa.
- **Lote:** `TenantJob` para ZIP e envio, argumentos só com ids.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view_sensitive` (baixar), `hr.edit` + `hr.view_sensitive` (enviar).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** dono de anexo `Payslip`; template de e-mail `payslip`; `hr.payslips` + `sent_attachment_id`, `changed_after_send`.

## Telas e estados
- Na folha (RH-02a): ações "Baixar holerite", "Baixar todos", "Enviar holerites", "Reenviar"; coluna de envio na aba Holerites.
- **Estados:** gerando (com o tempo limite de cerca de 30 s e depois o estado de demora); sem e-mail; falha de envio com o motivo; rascunho com a marca d'água.
- **Propositivo:** o diálogo de envio leva aos funcionários sem e-mail; depois do fechamento, o `NextSteps` da folha oferece "Enviar holerites".
- **400 px:** a situação do envio aparece no cartão do holerite.
- **Ajuda:** atualizar `Ajuda/rh-folha-detalhe.md` (aba `holerites`: "Holerite em PDF" e "Envio por e-mail"); `<Dica>` em "Proteção do holerite".

## Plano de testes
- **Unidade:** máscara do CPF e nome de arquivo (CA 1, 2).
- **Integração (Testcontainers):** PDF pelo texto extraído com e sem campos proibidos (CA 1); ZIP em segundo plano (CA 2); marca d'água e arquivo do sistema com `SensitiveRead` (CA 3); limite (CA 4); envio só com folha fechada, destinatário, corpo sem valor e `Idempotency-Key` (CA 5, 6); situação do envio (CA 7); alterado depois do envio (CA 8); três camadas (CA 9); canário (CA 10).
- **Imagem Alpine:** geração de 50 holerites e fontes só Archivo (ADR 0022).
- **E2E (Playwright):** fechar a folha de teste, enviar e ver a situação "Enviado".

## Fora do escopo
- Portal ou app do funcionário para ver os próprios holerites: fora do MVP (Pontos em aberto 2).
- Assinatura eletrônica do recebimento do holerite.

## Pontos em aberto
1. **Proteção do holerite no e-mail** (pré-condição, PO + jurídico): (a) PDF anexado sem senha, para o e-mail pessoal do funcionário (proposta mais simples); (b) PDF com senha (ex.: parte do CPF), que pede outra biblioteca além do QuestPDF e uma ADR; (c) e-mail só com o aviso e o download depois do login, que pede usuário para todo funcionário.
2. **Holerite no "Meu ponto"** (PO): mostrar o próprio holerite ao funcionário que tem usuário (autoatendimento), como alternativa ao e-mail.
3. Máscara do CPF no PDF (proposta: 3 primeiros e 2 últimos dígitos) ou CPF completo, conforme a contadora.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), imagem Alpine e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono `Payslip` com `OwnerExists`; template registrado.
- [ ] Estados de geração e envio; ajuda atualizada; teste de cobertura passando.
- [ ] Auditoria ligada, com `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PayslipPdfDocument`; jobs de ZIP e de envio.
- [ ] Endpoints `/payroll-runs/{id}/payslips/{payslipId}/pdf`, `/payroll-runs/{id}/pdfs`, `/payroll-runs/{id}/send`, `/payslips/{id}/resend`.

## Quality gates
- Agentes previstos: @dev, cybersecurity (sigilo do e-mail e do PDF), @qa.
- Gate: CA 1, 5, 6 e 10 obrigatórios.

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

- **Correções feitas na validação:** envio só com a folha fechada, por isso a ordem passou para depois da RH-02c; destinatário só o e-mail pessoal do funcionário, sem cópia (CA 6); assunto e corpo sem valor; PDF sem dado que não é do holerite (CA 1).
- **Pré-condições para `Ready`:** ponto em aberto 1; RH-02c concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
