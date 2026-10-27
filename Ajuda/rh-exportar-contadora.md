---
titulo: Exportar para a contadora
rota: /app/rh/exportar-contadora
aba:
padrao: true
modulo: hr
---

## Para que serve
Gerar com um clique o pacote do mês para a contadora calcular a folha com as mudanças que já estão no sistema: admissões, desligamentos, reajustes, trocas de cargo, afastamentos e, quando houver, a folha fechada. Nada é digitado de novo.

## Como usar
- Escolha a **competência**. O sistema sugere o mês anterior; dá para escolher o mês atual ou qualquer mês antes dele.
- Confira o bloco **O que vai entrar no pacote**: quantos eventos de movimentação, se a folha do mês está fechada e o que fica de fora.
- Se aparecer "A folha de {mês} ainda está em rascunho: ela não entra no pacote.", feche a folha pelo atalho **Abrir a folha** e volte para gerar.
- Clique em **Gerar pacote (CSV)**. O navegador baixa um arquivo ZIP (`contadora-aaaa-mm-v1.zip`) com os arquivos do layout e o `leia-me.txt`.
- Depois de gerar, use **Avisar a contadora**: abre o seu e-mail com o texto pronto. O arquivo não vai anexado; anexe o ZIP que você baixou.
- Gerou de novo a mesma competência (por exemplo, depois de reabrir a folha)? A tela e o `leia-me.txt` dizem "Substitui a exportação de {data}". Mande o novo pacote à contadora.

## Layout padrão v1
O **layout padrão** é o formato dos arquivos que o sistema gera. Ele está "A confirmar com a contadora": confira as colunas com ela e marque como conferido em Configurações → Parâmetros → RH ("Layout do pacote para a contadora"). Se ela pedir outra coluna ou outro formato, entra uma versão nova do layout (v2) ou um layout adicional.

Todos os arquivos: separador ponto e vírgula (`;`), UTF-8 com BOM (a planilha abre os acentos certos), primeira linha com o cabeçalho, datas `dd/mm/aaaa`, decimal com vírgula e duas casas, sem separador de milhar (`3500,00`), CPF com máscara (`123.456.789-09`). Célula que começaria com `=`, `+`, `-` ou `@` vem com um apóstrofo na frente, para a planilha não executar fórmula.

**movimentacao.csv**: uma linha por evento da competência. Cabeçalho: `evento;matricula;nome;cpf;nascimento;cargo;cbo;salario;jornada;data;tipo_afastamento;fim_previsto`.
- `evento`: `admissao`, `desligamento`, `reajuste`, `troca_cargo` ou `afastamento`.
- `matricula`, `nome` e `cpf`: em todos os eventos (a matrícula fica vazia se o funcionário não tiver).
- `nascimento` e `jornada` (nome da jornada): só na admissão.
- `cargo` e `cbo` (`9999-99`): na admissão (o cargo da admissão) e na troca de cargo (o cargo novo).
- `salario`: na admissão (o salário da admissão) e no reajuste (o salário novo).
- `data`: a admissão, o desligamento, a vigência do reajuste ou da troca, ou o início do afastamento.
- `tipo_afastamento` (Doença, Acidente de trabalho, Maternidade, Paternidade, Serviço militar ou Outros) e `fim_previsto` (último dia previsto): só no afastamento.

Entram o afastamento que cobre algum dia do mês (inclusive o que começou antes) e o reajuste e a troca de cargo com vigência no mês. Afastamento cancelado não entra. O **motivo** do afastamento e das mudanças nunca sai.

Exemplo de linha: `admissao;001;Ana Souza;123.456.789-09;10/05/1990;Montador;7155-05;3500,00;44 h — seg a sex 08:00–17:48;05/03/2026;;`

**folha.csv**: só quando a folha da competência está **fechada** ou **paga** (folha em rascunho não entra). Uma linha por linha de holerite e uma por guia, iguais às da folha. Cabeçalho: `registro;tipo_folha;matricula_ou_cpf;codigo_rubrica;descricao_rubrica;referencia;valor;tipo_guia;vencimento`.
- `registro`: `holerite` ou `guia`.
- `tipo_folha`: Mensal, Adiantamento, Férias, 13º 1ª parcela, 13º 2ª parcela ou Rescisão.
- `matricula_ou_cpf`: a matrícula do funcionário ou, sem ela, o CPF (vazia na guia).
- `codigo_rubrica`, `descricao_rubrica` e `referencia` ("30 dias", "10:00 h"): a linha do holerite (vazias na guia).
- `valor`: o valor da linha ou da guia.
- `tipo_guia` (FGTS, INSS, IRRF retido, Contribuição sindical ou Outros) e `vencimento`: só na guia.

Exemplos de linha: `holerite;Mensal;001;SAL;Salário;30 dias;3500,00;;` e `guia;Mensal;;;;;560,00;FGTS;20/04/2026`

**leia-me.txt**: a versão do layout (e se a contadora já conferiu), a competência, a data de geração, o que substitui e a lista do que entrou e do que não entrou.

**Versões do layout**
- **v1** (atual): `movimentacao.csv`, `folha.csv` e `leia-me.txt`. O ponto ainda não entra ("Ponto: não incluído; o ponto do sistema ainda não está em uso."); quando o ponto estiver em uso, o `ponto.csv` entra numa versão nova.

## Perguntas frequentes
**Quem pode gerar o pacote?** Só quem tem a permissão "Ver dados sensíveis" do RH. Sem ela, a opção nem aparece no menu.

**O que fica de fora?** Só vai o necessário para a folha: nada de endereço, telefone, e-mail, conta para pagamento, dependentes, localização do ponto ou documentos anexados. O tipo do afastamento só sai para quem também tem "Ver dados de saúde".

**O arquivo fica guardado no sistema?** Não. Ele é montado na hora e baixado; o sistema guarda só o registro de quem gerou, quando, a competência e a versão do layout (lista "Exportações anteriores").

**A planilha tirou o zero da frente da matrícula.** Abra o CSV pela importação de dados da planilha e marque a coluna como texto.
