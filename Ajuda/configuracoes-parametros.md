---
titulo: Parâmetros
rota: /app/configuracoes/parametros
aba: parametros
padrao: true
modulo: settings
---

## Para que serve
Guardar os números que o sistema usa para calcular custo e preço: o percentual de encargos sobre a folha, o modo de precificação dos orçamentos, os limites do financeiro e os parâmetros da produção.

## Como usar
- **% de encargos estimados:** o sistema começa com 80,00%, um valor sugerido. Confirme com a contadora, ajuste e clique em **Salvar % de encargos**. Os cargos com custo-hora calculado são recalculados.
- **Orçamentos:** o **% padrão de markup ou margem** é o que a empresa costuma aplicar. O orçamento sugere o preço com ele. A **validade padrão do orçamento** (15 dias no começo) vem preenchida em todo orçamento novo. Clique em **Salvar % padrão**.
- **Modo de precificação:** escolha **Markup sobre o custo** (o preço é o custo mais um percentual) ou **Margem sobre o preço** (o percentual é a parte do preço que sobra) e clique em **Salvar modo de precificação**.
- **Limites do financeiro** (aparece para quem vê o Financeiro): desconto acima de um % do saldo ou de um valor, e cancelamento de título com saldo acima de um valor, só são confirmados por quem aprova em Caixa e pagamentos. Baixa com data mais antiga que os dias informados pede motivo e avisa o Proprietário. **Tolerância no pagamento** é quanto o pagamento da ordem pode passar do aprovado sem nova aprovação (padrão R$ 0,00). **Aviso de transferência acima de** avisa o Proprietário de toda transferência maior que o valor (padrão R$ 10.000,00). **Proprietário aprova a própria ordem**, quando há mais de um Proprietário, deixa quem pediu a ordem aprová-la; a troca avisa todos os Proprietários. Clique em **Salvar limites**.
- **Produção:** **Exibir custo-hora em cada apontamento** começa desligado. Desligado, as abas Apontamentos e Custos da OS mostram só o total de mão de obra por etapa e por OS; ligado, quem vê custo do projeto vê também o custo-hora do cargo e o custo de cada apontamento. **Apontamento aberto vira alerta depois de** (12 h no começo, de 1 a 72) é o limite da tela Apontamentos abertos. Clique em **Salvar parâmetros da produção**.
- Abra **Histórico de alterações** para ver o valor anterior, o novo, quem mudou e quando.

## Perguntas frequentes
**Como o % de encargos entra no custo?** Custo-hora do cargo = salário × (1 + % de encargos) ÷ horas do mês.

**Qual a diferença entre markup e margem?** Com custo de R$ 100 e 25%: no markup, o preço é R$ 125; na margem, o preço é R$ 133,33, e os R$ 33,33 são 25% do preço.

**Desligar o custo-hora por apontamento esconde o custo do projeto?** Não. O total de mão de obra da OS e do projeto continua para quem tem a permissão de ver custo; só o detalhe de cada apontamento some.

**Que valores o % de encargos aceita?** De 0 a 200, com até 2 casas decimais.

**Quais são os limites de desconto, cancelamento e data retroativa no começo?** Desconto acima de 5,00% do saldo ou de R$ 500,00; cancelamento de título com saldo acima de R$ 1.000,00; data de baixa mais de 7 dias no passado. Ajuste conforme a rotina da empresa.
