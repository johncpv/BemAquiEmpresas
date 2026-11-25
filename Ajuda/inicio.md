---
titulo: Início
rota: /app
aba:
padrao: true
modulo:
---

## Para que serve
É a primeira tela depois de entrar, para todo mundo. No alto fica a **Minha pauta**: o que depende de você hoje, numa lista só, na ordem do que mais destrava o trabalho dos outros. Abaixo ficam os próximos passos para colocar a empresa para funcionar (só para quem administra os acessos) e os atalhos para os módulos a que você tem acesso.

## Como usar
- **Minha pauta:** cada cartão é um tipo de pendência, com a quantidade ("3 ordens de pagamento aguardam você"), o porquê numa linha (o total e o vencimento mais próximo) e até 5 itens. O botão de cada item já abre o documento na ação que resolve: "Aprovar", "Pagar", "Decidir qualidade", "Prorrogar validade", "Gerar solicitação", "Gerar OP". "Ver todos" abre a lista filtrada. No celular, cada cartão mostra os 3 primeiros.
- **O que entra na pauta:** ordens de pagamento que aguardam a sua decisão ou que estão aprovadas para pagar, aditivos de contrato aguardando aprovação, qualidade para decidir, pedidos de compra aguardando você, faltas de material sem solicitação de compra, títulos a receber vencidos, OS atrasadas, não conformidades sob a sua responsabilidade, cargos sem custo-hora, títulos a pagar que vencem em até 3 dias sem ordem de pagamento e os seus orçamentos que vencem em até 7 dias. Você só vê o que os seus grupos permitem decidir ou abrir.
- **Os três níveis:** primeiro **Destrava outra pessoa ou dinheiro** (aprovações, decisões e pagamentos que alguém espera, com a faixa âmbar), depois **Já passou do prazo** (vencidos e atrasados, com a faixa vermelha) e por fim **Vence em breve**. Dentro de cada nível vem antes o que vence primeiro e, no empate, o de maior valor.
- **Autoinspeção:** na qualidade, quem fez uma etapa da tentativa não decide. Se não houver outro aprovador disponível, a OS aparece para você com a marca "Autoinspeção", e a decisão fica registrada assim.
- **Atualização:** a pauta é recalculada a cada minuto e quando você volta para esta aba. A quantidade total aparece no título da aba do navegador, por exemplo "(4) Início".
- **Sua loja hoje (Comércio):** para quem vê as vendas, três cartões no alto. **Vendas de hoje** mostra o valor e a quantidade, com o mesmo dia da semana passada e a variação. **Caixas abertos** mostra quantos caixas estão abertos, de quais lojas e há quanto tempo; o chip vermelho "Cancelamentos acima de 3%" marca o caixa em que o valor das vendas canceladas no turno passou do limite da loja (padrão 3%, ajustável em Loja → Lojas). **Ticket médio** mostra o valor médio por venda hoje e nos últimos 7 dias. Toque no título do cartão para abrir a lista já filtrada: as vendas de hoje ou os caixas abertos. Quando algum caixa aberto está há mais de 10 minutos sem falar com o servidor, o cartão avisa "Pode haver vendas ainda não enviadas pelos caixas.", com o atalho para as Pendências do PDV. Os números são atualizados a cada minuto. Na fábrica, esses cartões não aparecem.
- **Próximos passos:** para quem administra os acessos: convidar a equipe (quem pode criar usuários), revisar os grupos de acesso (quem pode editar grupos) e exigir a verificação em duas etapas (quem pode editar as configurações). Cada cartão leva direto à tela certa. Sem nenhuma dessas permissões, a seção não aparece; a sua própria verificação em duas etapas fica no menu do usuário, em "Verificação em duas etapas".
- **Seus módulos:** atalhos para os módulos liberados para a empresa e habilitados nos seus grupos.
- **Menu lateral (computador e tablet):** à esquerda, as áreas do sistema com os módulos de cada uma logo abaixo. O módulo aberto fica marcado. **Recolher** (no pé do menu) deixa só os ícones das áreas, para sobrar espaço na tela: passe o mouse para ver o nome e clique no ícone para escolher o módulo. **Expandir o menu** volta a lista completa. A escolha fica lembrada neste navegador.
- **Dock (celular):** a barra na borda de baixo com as áreas. Toque na área e escolha o módulo na lista que sobe da borda.
- **Busca (Ctrl K):** digite o nome de uma tela ou módulo e tecle Enter. As telas usadas por último aparecem primeiro.
- **Tema:** Automático (segue o computador ou o celular), Claro ou Escuro. A escolha fica salva no seu perfil.

## Perguntas frequentes
**Por que um item sumiu da pauta?** Porque foi resolvido: a pauta não guarda nada, ela é montada na hora com o que está pendente. Aprovou, pagou, prorrogou ou gerou a solicitação, o item sai na próxima atualização.

**Por que o Início é a primeira tela?** Para ninguém esquecer aprovação, vencimento ou atraso: ao entrar, você já vê o que depende de você e resolve com um clique.

**Apareceu "Não foi possível carregar" num cartão.** Aquela parte da pauta demorou ou falhou; o resto continua certo. Toque em "Tentar de novo". Se continuar, abra a lista pelo módulo e avise o suporte.

**A pauta diz "Nada pendente para você hoje."** Nenhuma pendência depende de você agora. Use os atalhos dos seus módulos para seguir o trabalho.

**Não encontro um módulo no menu.** Ele pode não estar liberado na versão contratada ou não estar habilitado nos seus grupos. Fale com quem administra os acessos da empresa.

**Por que não vejo todos os módulos?** O sistema mostra só o que faz sentido para o tipo de negócio da empresa (Fábrica, Comércio, Prestação de serviços ou E-commerce). Módulo que não faz parte desse perfil não aparece no menu, na busca nem na ajuda. Módulo do perfil que não está na versão contratada aparece com cadeado e abre a tela "Funcionalidade não liberada nessa versão do sistema.". E módulo liberado que não está habilitado nos seus grupos também não aparece para você. Para mudar o perfil ou a versão, fale com o suporte do BemAqui; para os grupos, com quem administra os acessos da empresa.

**Por que a área de Estoque e Compras se chama "{{area.factory|Fábrica}}"?** O nome das áreas acompanha o tipo de negócio da empresa: numa fábrica, a área se chama "Fábrica"; num comércio, numa prestadora de serviços ou numa loja virtual, ela se chama "Retaguarda", que é onde se controla estoque, compras e cadastros. A área **Loja** reúne as telas de venda no balcão e só aparece quando a empresa usa esses módulos. Os nomes valem para o menu, a busca e a ajuda; nome de permissão e texto de nota fiscal não mudam.

**As vendas de hoje não batem com o que vendi.** As vendas feitas com o caixa sem internet só entram quando o caixa envia; se o cartão avisar que pode haver vendas não enviadas, confira as Pendências do PDV. Vendas canceladas não entram no total.

**O que é o traço amarelo no menu?** Ele marca onde você está: no menu lateral, a área e o módulo abertos; no dock do celular, a área.
