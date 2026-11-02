# Manual do RH

**Grupo:** RH (do sistema). **Ações:** `hr.view`, `hr.create`, `hr.edit`, `hr.view_sensitive` (salário, documentos), `hr.view_health` (atestados, afastamento por doença). Recomendado: `hr.approve` (ajustes de ponto, revisão da conta de salário, fechamento da folha). Com `hr.approve`, você precisa de 2FA.

O que você vê é dado pessoal protegido pela LGPD. Quem não tem as suas permissões vê "Dados protegidos".

## Rotina

### Todo dia

| Tarefa | Tela | Ajuda |
|---|---|---|
| Decidir os pedidos de ajuste de ponto e de abono | Pessoas → RH → Ponto → Ajustes `/app/rh/ponto/ajustes` | [rh-ponto-ajustes](../../../Ajuda/rh-ponto-ajustes.md) |
| Conferir as marcações do dia | Ponto → Marcações `/app/rh/ponto/marcacoes` | [rh-ponto-marcacoes](../../../Ajuda/rh-ponto-marcacoes.md) |
| Registrar afastamentos (com atestado) | Funcionário → aba Contrato ou Linha do tempo `/app/rh/funcionarios/$id` | [rh-funcionario-contrato](../../../Ajuda/rh-funcionario-contrato.md) · [rh-funcionario-linha-do-tempo](../../../Ajuda/rh-funcionario-linha-do-tempo.md) |

### Na admissão e no desligamento

| Tarefa | Tela | Ajuda |
|---|---|---|
| Cadastrar o funcionário (identificação e contrato) | RH → Funcionários → Novo `/app/rh/funcionarios/novo` | [rh-funcionario-novo](../../../Ajuda/rh-funcionario-novo.md) |
| Completar dados pessoais, dependentes, banco ou Pix, documentos | Abas do funcionário | [rh-funcionario-pessoais](../../../Ajuda/rh-funcionario-pessoais.md) · [rh-funcionario-dependentes](../../../Ajuda/rh-funcionario-dependentes.md) · [rh-funcionario-pagamento](../../../Ajuda/rh-funcionario-pagamento.md) · [rh-funcionario-documentos](../../../Ajuda/rh-funcionario-documentos.md) |
| Ligar o funcionário ao usuário do sistema (para apontar e marcar o ponto) | Funcionário → próximos passos | [rh-funcionario](../../../Ajuda/rh-funcionario.md) |
| Manter cargos (custo-hora), jornadas e sindicatos | RH → Cargos, Jornadas, Sindicatos | [rh-cargos](../../../Ajuda/rh-cargos.md) · [rh-jornadas](../../../Ajuda/rh-jornadas.md) · [rh-sindicatos](../../../Ajuda/rh-sindicatos.md) |

### Todo mês

| Ordem | Tarefa | Tela | Ajuda |
|---|---|---|---|
| 1 | Fechar o período de ponto (espelho) | Ponto → Espelho `/app/rh/ponto/espelho` | [rh-ponto-espelho](../../../Ajuda/rh-ponto-espelho.md) · [rh-ponto-espelho-funcionario](../../../Ajuda/rh-ponto-espelho-funcionario.md) |
| 2 | Gerar o pacote para a contadora | RH → Exportar para a contadora `/app/rh/exportar-contadora` | [rh-exportar-contadora](../../../Ajuda/rh-exportar-contadora.md) |
| 3 | Criar a folha do mês e lançar ou importar os valores da contadora | RH → Folha `/app/rh/folha` | [rh-folha](../../../Ajuda/rh-folha.md) · [rh-folha-detalhe](../../../Ajuda/rh-folha-detalhe.md) |
| 4 | Conferir (variação do líquido, novos, conta alterada) e informar os encargos | Folha → abas Conferência e Encargos | [rh-folha-detalhe-conferencia](../../../Ajuda/rh-folha-detalhe-conferencia.md) · [rh-folha-detalhe-encargos](../../../Ajuda/rh-folha-detalhe-encargos.md) |
| 5 | Fechar a folha e criar as ordens de pagamento de salários e guias | Folha → **Fechar folha** → aba Pagamentos | [rh-folha-detalhe-pagamentos](../../../Ajuda/rh-folha-detalhe-pagamentos.md) |
| 6 | Até a virada: comparar a folha calculada com a da contadora | RH → Conferência da folha `/app/rh/conferencia-folha` | [rh-conferencia-folha](../../../Ajuda/rh-conferencia-folha.md) |
| Quando muda a lei | Revisar as tabelas legais (INSS, IRRF, salário-família) e as rubricas | RH → Tabelas da folha, Rubricas | [rh-tabelas-folha](../../../Ajuda/rh-tabelas-folha.md) · [rh-rubricas](../../../Ajuda/rh-rubricas.md) |

## Férias, 13º e rescisão

O **cálculo** de férias, 13º e rescisão pelo sistema está **em finalização**. Até lá, crie a folha do tipo Férias, 13º (1ª ou 2ª parcela) ou Rescisão no modo **lançada**, com os valores da contadora: o sistema confere, gera o holerite, os títulos a pagar e a previsão no caixa como na folha mensal.

## Entre as telas

- **Ponto → folha → caixa:** o espelho fechado vai para o pacote da contadora e para a folha; a folha fechada vira títulos a pagar (um por funcionário e um por guia) e entra no fluxo de caixa. O Financeiro paga pelas ordens que você cria.
- **Quem fecha a folha não paga os salários dela.** É uma segregação de propósito.
- **Conta para pagamento trocada há pouco fica em quarentena:** o salário sai do pagamento automático até você revisar a conta (atalho **Revisar conta** no fechamento).
- **Custo-hora do cargo** (salário de referência × encargos ÷ horas) é o que entra no orçamento e no custo da OS. Mudou o salário de referência, o custo dos próximos apontamentos muda; os já fechados não.
- **Folha calculada x lançada:** até a virada, a oficial é a lançada (a da contadora). A calculada aparece como **Simulação**.

## Os 5 erros mais comuns

1. **Lançar a folha antes de fechar o ponto.** Horas extras e faltas ficam de fora ou erradas. Feche o espelho primeiro (a ordem do mês está acima).
2. **Aprovar ajuste de ponto sem ver o "antes e depois".** Abra **Ver** e confira as horas do dia antes de aprovar. Você não aprova ajuste seu: peça a outra pessoa.
3. **Colocar diagnóstico ou CID na justificativa.** A justificativa é vista por mais gente. O atestado vai anexado, e só quem tem **Ver dados de saúde** abre.
4. **Funcionário sem conta para pagamento.** A folha não fecha. Cadastre o banco ou o Pix na admissão.
5. **Esquecer de ligar o usuário ao funcionário.** O operador não aponta nem marca ponto ("Seu usuário não está ligado a um funcionário ativo").
