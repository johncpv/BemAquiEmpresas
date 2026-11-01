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
- Com tudo lançado e conferido, use **Fechar folha**: os títulos a pagar dos salários e das guias aparecem na aba **Pagamentos**. Folha fechada fica só para consulta: "Folha fechada em {data}. Para mudar, reabra a folha."


## Usar horas do espelho
- Com o período de ponto do mês **fechado**, o holerite mostra ao lado os totais do espelho do funcionário: extras, faltas, atrasos e, com acordo, o banco de horas.
- **Usar horas do espelho** preenche só a **referência** das rubricas marcadas com **Origem no ponto** (no padrão: Horas extras 50% ← extras; Faltas ← faltas; Atrasos ← atrasos), como "2:00 h". O **valor** continua vindo da contadora: a linha que ainda não existia entra sem valor, para você informar. Confira e clique em **Salvar holerite**.
- Sem o período de ponto fechado, aparece "O ponto de {mês} ainda não foi fechado." Feche em RH → Ponto → Espelho.
- Se o ponto do mês for reaberto depois de a folha fechar, a folha mostra "O ponto deste mês foi reaberto depois do fechamento da folha."
- A marca **Origem no ponto** de cada rubrica fica em RH → Rubricas.
## Holerite em PDF
- **Baixar** (na linha do funcionário) gera o holerite em PDF no layout padrão: empresa, funcionário (com o CPF mascarado: só os 3 primeiros e os 2 últimos dígitos), cargo, CBO, admissão, competência, proventos, descontos, bases do INSS, do FGTS e do IRRF, FGTS do mês e o líquido. O PDF não traz CPF completo, endereço, dados bancários nem dependentes.
- Com a folha em lançamento, o PDF sai com a marca d'água "RASCUNHO — folha não fechada". Com a folha fechada, o PDF fica guardado e é refeito se a folha for reaberta e fechada de novo.
- **Baixar todos os holerites** gera um arquivo ZIP com um PDF por funcionário (nome `holerite-{competência}-{matrícula}.pdf`, sem nome nem CPF). Até 20 holerites, sai na hora; acima disso aparece "Gerando {n} holerites…" e você recebe um aviso no sino quando estiver pronto.
- Holerite com mais de 300 linhas não gera PDF. Cada download fica registrado no histórico de acessos.

## Aviso aos funcionários
- Com a folha fechada, **Avisar funcionários** mostra quantos recebem o aviso (têm usuário ligado e ativo), quem está **sem usuário** (com **Convidar usuário**, que abre o convite com o e-mail pessoal do cadastro) e quem está **desligado** (com **Baixar holerite**, para entregar pessoalmente).
- Confirme em **Avisar {n} funcionários**: cada um recebe um e-mail "Seu holerite de {mês}/{ano} está disponível", com o link para **Meus holerites**. O e-mail não leva anexo nem valor, e vai só para o e-mail do usuário ligado ao funcionário.
- Na lista, cada holerite mostra a situação do aviso: **Na fila**, **Avisado em {data}**, **Falhou: {motivo}**, **Sem usuário** ou **Desligado**, e **Visto em {data}** quando o funcionário abriu o holerite.
- Avisar a folha de novo não repete quem já foi avisado. Para mandar outra vez a um funcionário, use **Avisar de novo** na linha dele.
- Se a folha for reaberta e fechada de novo com mudança no holerite, ele ganha a marca **Alterado depois do aviso** e o próximo passo oferece **Avisar os funcionários dos holerites alterados**.
- O funcionário precisa de um usuário ligado ao cadastro (Funcionário → Usuário do sistema). Sem usuário, entregue o PDF.

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
