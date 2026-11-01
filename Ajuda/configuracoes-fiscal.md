---
titulo: Configurações — Fiscal
rota: /app/configuracoes/fiscal
aba: fiscal
padrao: true
modulo: fiscal
---

## Para que serve
Reúne os dados fiscais que vão em toda nota da empresa: regime tributário e CRT, CNAEs, a série exclusiva da NF-e, o ambiente, o CFOP de cada tipo de venda, os CST, as alíquotas, os tributos que entram no custo do projeto, a NFS-e da instalação, o IBS/CBS da Reforma Tributária, o transporte e o prazo de cancelamento.

Tudo já vem **preenchido com os valores propostos** (por exemplo, Lucro Presumido, série 2, CFOP 5101 dentro de SP e 6101 para outro estado, ICMS de 18%, PIS de 0,65% e COFINS de 3%). Cada valor mostra o selo **A confirmar com a contadora** até alguém conferir.

## Como usar
1. Clique em **Baixar para a contadora**: sai um PDF com todos os campos, o valor e a situação, com espaço para a resposta dela.
2. Ajuste o que a contadora pedir e clique em **Salvar configuração fiscal**. Mudar um valor que já estava conferido volta o selo para "A confirmar".
3. Para cada valor que a contadora confirmou, clique em **Marcar como conferido** (ou em **Marcar seção como conferida**, que lista os campos antes de confirmar). O selo passa a mostrar quem conferiu e quando. **Desfazer conferência** volta o selo.
4. O índice **Seções** mostra quantos campos faltam em cada seção e leva direto ao primeiro pendente.
5. Inscrição estadual, inscrição municipal, endereço e código IBGE vêm de **Configurações → Empresa** (use **Editar dados da empresa**). Sem a inscrição estadual, o código IBGE ou a série, a emissão fica bloqueada e a tela diz o que falta.

## Homologação e produção
- Em **homologação** (padrão), as notas não têm valor fiscal e os valores a confirmar não bloqueiam a emissão: só aparece o aviso.
- **Produção** só liga na virada para a produção da empresa e depois que todos os campos críticos da NF-e estão conferidos. Ao trocar, o diálogo **Ligar a emissão com valor fiscal** pede o código do aplicativo autenticador.
- Os campos da NFS-e (item da LC 116, código municipal, CTN, NBS, ISS e retenções) só bloqueiam a NFS-e.

## Entrada
- **Crédito de impostos na entrada**: quais tributos da NF-e de entrada são recuperáveis (ICMS, IPI, PIS, COFINS). O padrão é **Nenhum**: os tributos da nota ficam no custo do material, com o selo **A confirmar com a contadora**.
- Com um tributo marcado, o recebimento pela nota tira esse valor do custo de entrada e guarda na nota como crédito. Não é campo crítico: não bloqueia a emissão.

## Perguntas frequentes
**Por que a série 2?** A série 1 costuma ser a do emissor atual. A série do sistema precisa ser exclusiva; confirme com a contadora qual foi reservada. Trocar a série depois de emitir pede confirmação e mostra a última nota da série atual.

**E se eu mudar o regime?** O CRT muda junto. As regras fiscais do regime anterior deixam de valer para a nota e ganham o aviso **A revisar** em Fiscal → Regras fiscais; nenhuma nota já emitida muda.

**E se eu mudar uma alíquota ou um CST?** As regras fiscais montadas com aquele valor **não mudam sozinhas**: depois de salvar, a tela lista as regras afetadas e elas aparecem no filtro **A revisar** de Fiscal → Regras fiscais.

**O que é "Entra no custo do projeto"?** São os tributos que o sistema soma ao custo do projeto para mostrar a margem real (ICMS, PIS, COFINS, ISS e IBS/CBS; o IPI fica fora). Não muda a nota.

**Quem pode mudar?** Só quem edita Configurações (o Proprietário). Quem só vê o Fiscal consulta em modo leitura. O **Histórico de alterações** mostra o antes e o depois de cada campo, quem conferiu e quando.
