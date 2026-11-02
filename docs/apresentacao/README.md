# Kit de apresentação, demonstração e manual — BemAqui Empresas

Material para apresentar o BemAqui Empresas à Konebras Elevadores, montar o tenant `demo` que sustenta a apresentação e entregar o manual de cada papel. Tudo aqui mostra **só o que está pronto na `main`** em 01/11/2026.

## O que tem no kit

| Arquivo | Para quê | Quem usa |
|---|---|---|
| [`roteiro-apresentacao.md`](roteiro-apresentacao.md) | Roteiro de 35–40 min (e versão de 15 min), ato por ato, com o que clicar e o que falar | Quem apresenta |
| [`grupos-e-permissoes.md`](grupos-e-permissoes.md) | Grupos recomendados para a Konebras, alçadas e as 3 camadas de acesso em linguagem de cliente. É o slide de "controle gerencial" | Quem apresenta e quem configura os acessos |
| [`operacao-demo.md`](operacao-demo.md) | Passo a passo para montar o tenant `demo`: cadastros, usuários e a história em andamento, com valores e o estado de cada item no dia | Quem prepara o ambiente |
| [`manual-usuario/`](manual-usuario/) | Um manual por papel: rotina do dia, da semana e do mês, com link para a ajuda de cada tela e os 5 erros mais comuns | A equipe da Konebras, depois da apresentação |

## Ordem de preparação

1. **Leia o roteiro inteiro** ([`roteiro-apresentacao.md`](roteiro-apresentacao.md)) para saber o que cada ato precisa encontrar pronto.
2. **Monte o `demo`** seguindo [`operacao-demo.md`](operacao-demo.md), na ordem das etapas. Conte com 2 a 3 dias de trabalho, espalhados em uma semana: a história em andamento precisa de apontamentos e datas reais.
3. **Configure os grupos** pela matriz de [`grupos-e-permissoes.md`](grupos-e-permissoes.md) e convide um usuário de cada grupo (etapa 2 da operação demo).
4. **Ensaie uma vez de ponta a ponta**, com o cronômetro, nas mesmas máquinas do dia (notebook, tablet, TV e celular com o autenticador). O ensaio consome os itens "ao vivo"; anote o que precisa ser refeito.
5. **Refaça os itens consumidos** (lista no fim de [`operacao-demo.md`](operacao-demo.md), "Itens consumidos a cada ensaio").
6. **No dia**, rode o checklist pré-apresentação (fim de [`operacao-demo.md`](operacao-demo.md)).
7. **Depois da apresentação**, entregue a pasta [`manual-usuario/`](manual-usuario/) a cada responsável. Os manuais apontam para a ajuda do próprio sistema (pasta `Ajuda/`), que é a mesma que abre no botão **Ajuda** de cada tela.

## Três usos, um material

- **Apresentação:** o roteiro conta a vida de um pedido de elevador, do orçamento à margem real. O fio é um só: *cada módulo alimenta custo, margem e caixa com dado real, e o dono controla quem faz o quê.*
- **Demonstração:** a operação demo deixa cada ato com o dado no estado certo, para nada depender de sorte na hora.
- **Manual:** os manuais por papel seguem a mesma divisão de grupos da apresentação. O cliente vê na apresentação exatamente o que cada pessoa dele vai usar.

## O que não apresentar como pronto

| Item | Situação | Como falar |
|---|---|---|
| Férias calculadas (FLH-03), 13º calculado (FLH-04) e rescisão calculada (FLH-05) | **Em finalização.** Hoje férias, 13º e rescisão entram como folha **lançada** (valores da contadora) | "O cálculo de férias, 13º e rescisão está em finalização. Até lá, esses valores vêm da contadora e o sistema confere, gera holerite e paga." |
| Portal do cliente (POR-01a/b/c, POR-02) | **Próxima fase, depois do go-live** | "Na próxima fase, o cliente acompanha o projeto, baixa documentos e boletos e aprova o orçamento online." |
| WhatsApp (WPP-01) | **Próxima fase, depois do go-live** | "Hoje os avisos saem por e-mail e pelo sino. O WhatsApp entra na próxima fase." |
| Ponto legal (REP-P, Portaria 671) | Fora do escopo atual. O ponto do sistema é **controle interno de jornada** | Diga sempre "controle interno de jornada". |
| NFS-e de Caçapava pelo sistema | **Processo provisório:** a nota é emitida no portal da prefeitura e registrada no BemAqui | "Enquanto a prefeitura não está no emissor, o sistema prepara tudo para copiar e colar e registra a nota." |

Ver também a seção "Pendências do produto que afetam a demonstração" em [`operacao-demo.md`](operacao-demo.md).
