---
titulo: Holerites da folha
rota: /app/rh/folha/$id
aba: holerites
padrao: true
modulo: hr
---

## Para que serve
Lançar, para cada funcionário, os proventos e descontos que a contadora calculou, à mão ou pelo arquivo CSV.

## Como usar
- Clique no nome do funcionário para abrir o holerite. Em cada linha, escolha a **rubrica**, informe a **referência** (até 20 caracteres, como "30 dias" ou "10:00 h") e o **valor**.
- Informe a **Base do INSS**, a **Base do FGTS**, a **Base do IRRF** e o **FGTS do mês**, como estão no holerite da contadora.
- Os totais (proventos, descontos e líquido) mudam enquanto você digita. A rubrica **informativa** (como o FGTS) aparece no holerite, mas não entra no líquido.
- **Copiar do mês anterior** traz as rubricas e os valores do holerite anterior do mesmo tipo como rascunho: confira e clique em **Salvar holerite**.
- **Importar lançamentos** recebe o arquivo da contadora no layout padrão do sistema (abaixo). O sistema mostra a prévia antes de gravar.

## Layout do arquivo (CSV v1)
- Uma linha por lançamento: `matricula_ou_cpf;codigo_rubrica;referencia;valor`. A primeira linha pode ser esse cabeçalho.
- Separador ponto e vírgula, decimal com vírgula (1.234,56), arquivo em UTF-8 ("CSV UTF-8" na planilha). Até 2 MB e 5 mil linhas.
- **matricula_ou_cpf:** a matrícula do funcionário ou o CPF (com ou sem pontos). **codigo_rubrica:** o código da rubrica em RH → Rubricas. **referencia:** opcional. **valor:** maior que zero.
- Exemplo: `123;SAL;30 dias;3.300,00` e `529.982.247-25;HE50;10:00 h;250,50`.
- A prévia mostra os funcionários encontrados, as linhas por rubrica, os totais e os erros por linha ("Linha 12: rubrica 'HE75' não existe."). Só sem erro o botão **Importar {n} lançamentos** fica habilitado.
- Importar **substitui** os lançamentos dos funcionários que estão no arquivo; os outros holerites ficam como estão. O arquivo não fica guardado: a auditoria registra só quantas linhas e quantos funcionários.

## Perguntas frequentes
**O líquido ficou negativo e não salva.** O líquido não pode ser negativo. Confira os descontos com a contadora.

**Apareceu "O holerite mudou desde que você abriu".** Outra pessoa salvou o mesmo holerite. Clique em **Recarregar** e lance de novo.

**Por que a célula "=1+1" virou erro?** O arquivo é lido só como texto: célula que começa com =, +, - ou @ não é número.
