# ADR 0018: LGPD, retenção e direitos do titular

- Status: Aceito (18/10/2026). O PO (John) aprovou a decisão D8 da revisão pré-Fase 1 com estes padrões.
- Contexto: a Fase 1 traz os primeiros dados pessoais de terceiros (PF e contatos) e de funcionários, e a primeira carga real do Dolibarr. O PRD §6 pede "acesso restrito ao RH; exportação e anonimização sob pedido". A revisão de segurança pediu, antes da primeira carga real, uma regra de retenção e de atendimento ao titular, e o fim do modo aberto do painel da plataforma.
- Complementa as ADRs 0009 (`[PersonalData]`), 0016 (dados sensíveis por rota e expurgo controlado) e 0017 (importação).

## Decisão

### Retenção
| Dado | Prazo |
|---|---|
| Terceiro PF e contato | Enquanto o cadastro estiver ativo, mais o prazo legal dos documentos em que aparece (fiscal e contábil: 5 anos após o exercício). Depois disso, pode ser anonimizado a pedido. |
| Funcionário desligado | Pelo prazo trabalhista e previdenciário: 5 anos após o desligamento como padrão, e mais tempo para os documentos que a contadora indicar (ex.: os previdenciários). Os campos sem obrigação legal podem ser anonimizados antes, a pedido. |
| Staging da migração (`migration.import_items`) e arquivos do relatório | Expurgada depois do aceite da carga pelo PO e em até 30 dias depois da carga. Ficam só `import_runs` (sem conteúdo) e `external_refs`. |
| Snapshot cifrado do Dolibarr | Apagado logo depois da carga (ADR 0017). |
| Registro de leitura sensível (`SensitiveRead`) | Junto com a auditoria, que é imutável. Não contém o dado lido, só quem, quando, o quê e o IP. |

### Auditoria imutável × direito de eliminação
- O conflito é resolvido na origem: **nenhum dado pessoal é gravado em claro na auditoria.**
  - Campo `[PersonalData]` aparece só como mascarado no diff (ADR 0009).
  - Os testes de canário e de allowlist da ADR 0016 garantem que nada escapa.
- Assim, anonimizar o cadastro elimina o dado, e a auditoria continua íntegra.
- Se um dado pessoal chegar à auditoria por engano, a correção é o expurgo controlado da ADR 0016 (migration revisada, com registro em `audit.purge_log`).

### Atendimento ao titular
- O pedido do titular (confirmação, acesso, correção, anonimização, eliminação, portabilidade) é atendido pelo **Proprietário** do tenant, que é o controlador. O BemAqui é o operador.
- A story de exportação e anonimização entra na **Fase 6** (junto com o RH-01/RH-02), com:
  - exportação dos dados de um terceiro PF, contato ou funcionário em JSON e PDF;
  - anonimização que mantém os documentos com movimento, trocando o dado pessoal por um marcador;
  - registro do pedido e do atendimento na auditoria.
- Até lá, a correção é feita pela tela de edição e o pedido é registrado à mão pelo Proprietário.

### Homologação com dados reais
- Com dados reais, a homologação segue as mesmas regras de acesso da produção:
  - só usuários convidados, com 2FA conforme a ADR 0010;
  - nada de usuário de teste compartilhado nem senha padrão;
  - backup cifrado e restauração só pelo runbook (ADR 0015);
  - nenhum dump em claro fora da VPS.
- **O painel da plataforma sai do modo aberto antes da primeira carga real (MIG-01b):** o arquivo de allowlist do nginx (`bemaqui-empresas-homolog.allow`, FND-10b) passa a ter só os IPs do PO, e o `Platform:AllowedNetworks` é preenchido como segunda camada.

## Consequências
- A MIG-01b tem como pré-condição o painel da plataforma fora do modo aberto.
- A MIG-01a implementa o expurgo da staging (`--purge-staging`) e o prazo de 30 dias, com alerta quando vence.
- A CAD-04b e a CAD-01a seguem os testes de auditoria e log sem dado pessoal.
- O backlog da Fase 6 ganha a story de exportação e anonimização (LGPD-01).

## Adendo (18/10/2026, decisão do PO): painel da plataforma sem restrição por IP
- O PO decidiu manter o painel da plataforma **sem allowlist de IP**. A pré-condição "painel fora do modo aberto antes da primeira carga real" deixa de valer, e a MIG-01b não recusa a carga por isso.
- O painel continua protegido por senha com 2FA obrigatório, sessão própria, rate limit e auditoria de login (FND-13). O `Platform:AllowedNetworks` e o arquivo `.allow` do nginx continuam disponíveis para quando o PO quiser restringir.
- **Risco aceito:** a tela de login do super admin fica exposta à internet. O que compensa é o 2FA e o bloqueio por tentativas.
