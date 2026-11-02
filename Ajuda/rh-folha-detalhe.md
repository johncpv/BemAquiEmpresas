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
- Se alguma tabela da folha (RH → Tabelas da folha) ou parâmetro do RH mudou desde o último fechamento, o diálogo de fechamento lista cada mudança (o quê, antes e depois, quem e quando) e pede que você marque a ciência antes de fechar.


## Folha calculada
- Na folha criada no modo **Calculada**, clique em **Calcular**. O sistema monta, para cada funcionário, as linhas pelas rubricas com regra: **salário** (proporcional na admissão e no desligamento), **afastamento** (doença ou acidente: a empresa paga os primeiros dias, a previdência o resto), **insalubridade** ou **periculosidade** (sem acúmulo: vale o maior), **faltas** e **atrasos** do espelho fechado, **salário-família**, **vale-transporte**, **INSS**, **IRRF** e o **FGTS do mês**, com as tabelas da competência (RH → Tabelas da folha) e os parâmetros de Configurações → Parâmetros → RH. As guias do FGTS, do INSS (empregados e empresa) e do IRRF saem calculadas.
- Sem o ponto do mês fechado, aparece "Feche o ponto de {mês} antes de calcular." com o link. Sem uma tabela, "Falta a tabela {tipo} para {mês}."
- Acima de 20 funcionários, o cálculo roda em segundo plano: a tela mostra "Calculando os holerites…" (e, depois de 30 segundos, o aviso de demora) e você recebe o aviso no sino.
- Funcionário sem cargo, sem salário ou sem jornada (com falta ou atraso) fica **sem cálculo**, com o motivo; os outros seguem. Avisos como "documentos do salário-família pendentes" aparecem na folha sem travar.
- O chip **Calculada com tabelas a confirmar** aparece quando alguma tabela usada ainda está "A confirmar com a contadora".

## Horas extras e DSR
- Na folha calculada, as horas vêm só do **espelho de ponto fechado** (nada é recontado). Cada minuto extra do dia é classificado em **dia útil**, **domingo ou folga** (dia sem jornada) ou **feriado** (pelo calendário padrão), e sai na linha **Horas extras {percentual}%**, com as horas na referência: horas × valor da hora × (1 + percentual). Os percentuais vêm de Configurações → Parâmetros → RH (pré-configurados com 50% no dia útil e 100% em domingo, folga e feriado, "A confirmar com a contadora" pela convenção do sindicato).
- O valor da hora é o salário (com a insalubridade ou a periculosidade do mês, pelo parâmetro) dividido pelas horas mensais do cargo.
- O **adicional noturno** (22 h às 5 h, 20%, hora reduzida de 52 min 30 s) vem desligado; ligado nos parâmetros, as horas no horário noturno geram a linha **Adicional noturno**.
- O **DSR sobre variáveis** é o repouso sobre as extras, o adicional noturno e o banco pago: variáveis ÷ dias úteis do mês × (domingos + feriados), com os feriados do calendário padrão.
- A **falta injustificada** (sem abono aprovado) tira o repouso daquela semana: a linha **DSR da semana de {data}** desconta um dia de salário por semana (parâmetro "Perda do DSR por falta", ligado).
- Em **Ver memória** das linhas de horas, aparecem os dias de origem com as horas, o percentual, o parâmetro usado e o link **Ver os dias no espelho de ponto**. As horas também entram nas entradas do bloco **Memória de cálculo**.
- Sem os percentuais das horas extras, aparece "Cadastre os percentuais de horas extras em Configurações → Parâmetros → RH." com o link, e nada é calculado.
- Se um parâmetro das horas mudar depois do cálculo, a folha avisa "Parâmetro mudou depois do cálculo" com **Recalcular**, e a mudança aparece no diálogo de fechamento.

## Banco de horas na folha
- Quem tem acordo de banco de horas aparece na seção **Banco de horas** dos holerites, com o saldo do espelho fechado, o do período e o saldo depois da folha. As extras desses dias vão para o saldo, não para pagamento. Sem ninguém, a seção mostra "Sem banco de horas neste mês."
- O saldo **não vence sozinho**. Só sai por decisão do RH: **Pagar {n} h do banco** (do saldo positivo, pagas como hora extra de dia útil) ou **Descontar {n} h do banco** (do saldo negativo, pelo valor da hora), sempre com o motivo. O holerite é recalculado na hora; **Tirar** desfaz a decisão.
- Com o saldo acima de 40 h, a seção sugere **Pagar {n} h do banco**; você decide.
- A baixa do saldo no espelho acontece só no **fechamento** da folha (e é desfeita se a folha for reaberta). O saldo que sobra na rescisão é tratado na folha de rescisão.
- Pagar e descontar exigem as permissões de editar o RH e de ver dados sensíveis.

## Simulação
- Até a virada, a calculada é **simulação**: a faixa "Simulação: em conferência paralela com a folha da contadora" oferece **Comparar com a lançada**. O botão **Fechar folha** não aparece e o holerite sai só como prévia, com a marca "Simulação: não é o holerite oficial"; ele nunca vai para **Meus holerites** nem para o e-mail.
- Depois da virada, fecha como a lançada (2FA de novo, um título por funcionário e um por guia), e só com todas as tabelas usadas conferidas: senão, "Confirme com a contadora as tabelas {lista} antes de fechar."

## Memória de cálculo
- No holerite calculado, cada linha tem **Ver memória**: a base, a faixa, a alíquota, a parcela a deduzir, a redução do IRRF, o arredondamento, a tabela e a vigência. No IRRF, a memória mostra se valeu a dedução legal ou o **desconto simplificado** e quanto daria a outra opção.
- O bloco **Memória de cálculo** mostra a versão do motor, as tabelas usadas, as entradas do cadastro e do ponto, os parâmetros e os encargos da empresa. Só aparece o dado do próprio funcionário; o tipo do afastamento não aparece.
- Linha calculada não se edita. O ajuste é um **lançamento avulso**: em **Lançar avulso**, escolha uma rubrica sem regra (prêmio, adiantamento, pensão, consignado, vale-transporte fornecido), o valor e o **motivo** (obrigatório). O holerite volta recalculado; o avulso entra nas bases pelas marcas da rubrica e fica a cada **Recalcular**. A pensão judicial (rubrica com "Deduz da base do IRRF") entra na dedução legal do IRRF.
- Se o cadastro, um parâmetro ou uma tabela mudar depois do cálculo, a folha avisa "Cadastro ou tabela mudou depois do cálculo de {n} funcionários." com **Recalcular**.
- O **Histórico de alterações** da folha mostra cada cálculo: quem, quando, a versão do motor, as tabelas e os parâmetros e, com a permissão de dados sensíveis, o líquido antes e depois.

## Retificação
- Folha calculada **fechada nunca é recalculada**. Se algo mudou depois, a folha mostra **Ver diferença** (o que mudaria, por funcionário e rubrica, sem gravar nada) e **Gerar retificação**.
- A retificação é uma folha complementar **em rascunho**, ligada à fechada, só com as diferenças (o provento que diminuiu vira desconto). Ela segue o fechamento normal e, antes da virada, a conferência paralela. Uma retificação em rascunho por vez.

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

**Na folha calculada, por que não consigo editar a linha?** A linha calculada é refeita a cada cálculo. Para ajustar, lance um avulso com o motivo.

**A hora extra do feriado saiu na rubrica de 100%.** O feriado usa a rubrica de domingo ou folga (HE100) até alguém criar, em RH → Rubricas, uma rubrica com a regra "Horas extras em feriado". Se o percentual do feriado for outro, sai numa linha própria com o percentual.

**O sábado sem jornada conta como folga?** Sim: dia sem jornada no cargo é "domingo ou folga" e usa o percentual de domingo ou folga. Se a convenção pedir outro, ajuste o percentual nos parâmetros.

**Por que a célula "=1+1" virou erro?** O arquivo é lido só como texto: célula que começa com =, +, - ou @ não é número.

**Na folha de 13º calculada, onde vejo os avos?** No holerite, em "Ver memória" da linha do 13º (mês a mês: dias no vínculo, faltas, dias pela previdência e se o mês conta) e nas entradas do cálculo ("Avos", "1ª parcela paga", "Adiantamento pago com as férias"). No 13º não há lançamento avulso: o ajuste é feito no cadastro, no ponto e nas folhas do ano, e depois em "Recalcular".
