---
titulo: Tabelas da folha
rota: /app/rh/tabelas-folha
aba: tabelas-folha
padrao: true
modulo: hr
---

## Para que serve
Guardar as tabelas legais que a folha calculada usa (INSS, IRRF, salário-família, salário mínimo, FGTS, vale-transporte e encargos da empresa), cada uma com a data em que passa a valer. Quando o governo muda uma tabela, mesmo no meio do ano, você cadastra a versão nova e cada competência usa a tabela certa.

## Como usar
- Cada tipo tem uma seção com a versão **Vigente**, as futuras (chip **A partir de {data}**) e as antigas. As que o sistema trouxe no pacote do ano chegam com o chip **A confirmar com a contadora** e a marca "Pacote {ano}".
- **Nova versão** abre já preenchida com a vigente: mude só o que mudou, informe **Vale a partir de** (primeiro dia do mês), a **Fonte** (norma e data de publicação) e o **Arredondamento**, e clique em **Salvar tabela**.
- No INSS, informe o limite de cada faixa e a alíquota; a última faixa termina no teto do salário de contribuição. No IRRF, informe as faixas com a parcela a deduzir (a última fica sem limite, "acima de"), a dedução por dependente, o desconto simplificado e a **Redução adicional** da norma vigente.
- Enquanto está **A confirmar com a contadora**, a tabela pode ser editada e já vale para o **Simulador**. Depois de conferida com a contadora, outra pessoa com permissão de aprovar clica em **Marcar como conferido**, informa com quem e quando conferiu, e a tabela fica conferida.
- Tabela conferida não muda. Se algo estiver errado, use **Retificar**: nasce uma versão nova com a mesma vigência, que passa por nova conferência. Até lá, a folha continua usando a conferida.
- O **Simulador** mostra, para uma competência, base, dependentes e filhos, o INSS, o IRRF e os demais valores com a memória do cálculo (faixa, alíquota, parcela, redução e arredondamento).
- A faixa no topo mostra quantas tabelas estão a confirmar; **Só as a confirmar** filtra a lista.

## Perguntas frequentes
**Por que algumas tabelas não vieram pré-cadastradas?** O pacote do ano só traz as tabelas com fonte oficial registrada. As que faltam aparecem com a nota do pacote: cadastre pela **Nova versão**, com a fonte.

**Por que não consigo conferir a tabela que eu cadastrei?** Quem cadastra ou edita não confere: peça a outra pessoa com permissão de aprovar. Se você for a única pessoa que aprova na empresa, a conferência pede o dataset de referência da contadora daquela vigência (uma linha por caso, "base;dependentes;esperado"), e o simulador precisa bater em todos os casos.

**Retificar muda a folha já fechada?** Não. Folha fechada nunca é recalculada. As folhas calculadas em rascunho ganham "Recalcular"; as fechadas ficam como estão.

**O fechamento da folha avisa das mudanças?** Sim. Tabela criada, editada, conferida ou retificada, e parâmetro do RH alterado desde o último fechamento aparecem no diálogo de fechamento, com o antes e o depois, quem e quando; é preciso marcar a ciência para fechar.

**O que é o arredondamento?** É como o resultado de cada imposto vira centavos: metade para cima (padrão), metade para o par ou truncado.
