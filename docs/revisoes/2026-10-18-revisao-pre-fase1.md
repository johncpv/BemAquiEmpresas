# Revisão pré-Fase 1: relatório consolidado

- **Data:** 18/10/2026.
- **Objeto:** as 15 stories da Fase 1 (CAD-01a a CAD-06, MIG-01a a MIG-01e), escritas pelo SM e validadas pelo PO no branch `docs/fase1-stories`.
- **Revisores:** quatro, todos somente leitura e conferindo as stories contra o código da Fase 0:
  - AIOX architect (Aria);
  - AIOX po + pm (Pax e Morgan);
  - design-squad, com `audit-design`;
  - cybersecurity, com `audit-app-security` (threat model).
- **Como usar:** a seção 1 lista o que depende do PO. As seções 2 e 3 entram nas stories e ADRs depois da decisão, sem nova consulta. Nada deste relatório foi aplicado ainda.

## 0. Vereditos

| Story | Arquitetura | Produto | Design | Segurança | Consolidado |
|---|---|---|---|---|---|
| CAD-06 Configurações da empresa | GO c/ ajustes | GO (6ª posição) | GO c/ ajustes | GO cond. | **GO com ajustes** |
| CAD-05a Plano de contas | GO c/ ajustes | GO (11ª posição) | GO c/ ajustes | GO | **GO com ajustes** |
| CAD-05b Contas e condições | GO c/ ajustes | GO | GO c/ ajustes | GO cond. | **GO com ajustes** |
| CAD-01a Terceiros | GO c/ ajustes | GO (1ª posição) | Ajustar antes | GO cond. | **Ajustar antes** |
| CAD-01b Consulta de CNPJ | GO | GO (+ CEP) | GO | GO cond. | **GO com ajustes** |
| CAD-03 Locais e centros | GO c/ ajustes | GO | GO c/ ajustes | GO | **GO com ajustes** |
| CAD-02a Itens | GO c/ ajustes | GO | GO c/ ajustes | GO | **GO com ajustes** |
| CAD-02b Conversões | GO | GO | GO | GO | **GO** |
| CAD-04a Cargos e custo-hora | GO c/ ajustes | GO | GO c/ ajustes | GO cond. | **GO com ajustes** |
| CAD-04b Funcionários | GO c/ ajustes | GO cond. (por último, quebrar) | Ajustar antes | **NO-GO** | **NO-GO até C1/A1 de segurança** |
| MIG-01a Infra de importação | **NO-GO** | GO (2ª posição) | Ajustar antes | **NO-GO** | **NO-GO até a ADR 0017** |
| MIG-01b Terceiros e financeiro | GO c/ ajustes | GO c/ ajustes | GO c/ ajustes | GO cond. | **GO com ajustes** |
| MIG-01c Itens e locais | GO c/ ajustes | GO | GO c/ ajustes | GO | **GO com ajustes** |
| MIG-01d Saldos de estoque | NO-GO na Fase 1 | Sai para a Fase 3 | — | GO | **Sai da Fase 1 (Fase 3, após EST-01)** |
| MIG-01e Títulos em aberto | NO-GO na Fase 1 | Sai para a Fase 2 | — | GO | **Sai da Fase 1 (Fase 2, após FIN-02)** |

Os NO-GOs não pedem trabalho novo. Eles somem com as decisões D3 e D6 e com os ajustes das seções 2.2 e 2.5.

## 1. Decisões do PO

Cada decisão traz uma recomendação, que pode ser aprovada como está. "Consenso" quer dizer que os revisores que trataram do assunto concordam.

| # | Decisão | Recomendação | Origem |
|---|---|---|---|
| D1 | **Ordem da fase em marcos**, para a homologação ter dados reais cedo | **Marco A, terceiros reais:** CAD-01a → MIG-01a → CAD-05b → MIG-01b → CAD-01b. **Marco B, catálogo real:** CAD-06 → CAD-03 → CAD-02a → CAD-02b → MIG-01c. **Marco C, base de custo:** CAD-05a → CAD-04a → CAD-04b. O PO vê os terceiros da Konebras depois de 4 stories, e não de 12. | Produto (consenso com arquitetura) |
| D2 | **MIG-01d e MIG-01e saem da Fase 1** | A MIG-01e vai para a Fase 2, logo depois da FIN-02 e antes da FIN-05: o fluxo de caixa nasce com os títulos reais. A MIG-01d vai para a Fase 3, depois da EST-01 (o estoque do Dolibarr está zerado). As duas rodam de novo na GOL-01. | Consenso dos 4 |
| D3 | **Leitura do Dolibarr por snapshot**, sem conexão ao banco em produção | Dump só das tabelas e colunas mapeadas, com usuário somente leitura. O dump é cifrado com `age`, restaurado num MariaDB temporário na rede interna do projeto e apagado depois da carga. Nenhum container do BemAqui entra na rede do Dolibarr. | Consenso dos 4 |
| D4 | **Tela da importação** | **Tela só de leitura** "Configurações → Importação do Dolibarr" (`settings.view`), com totais por entidade e pendências com link para o registro, mais o chip "Importado do Dolibarr" no detalhe. A carga continua só pela CLI. A arquitetura propôs "só CLI"; o design mostrou que, sem tela, o PO não tem como conferir as pendências. | Design × arquitetura |
| D5 | **Corte financeiro único** | Saldos bancários e títulos entram juntos, num "pacote financeiro de abertura" com uma só data de corte (posição no fim do dia D). O saldo do banco é o do **extrato** informado pelo PO, e a soma do Dolibarr fica só para conferência. Em homologação, as cargas da Fase 1 são ensaio; a carga que vale é a da GOL-01. | Arquitetura + produto |
| D6 | **CAD-04b quebrada e por último** | **04b-base** (identificação, cargo, salário, histórico, desligamento, vínculo com o usuário, LGPD) fica no fim da Fase 1. **04c** (dependentes, banco/Pix, adicionais, vale-transporte, afastamentos, documentos) vai para o início da Fase 3, onde estão os consumidores (PRD-03/04). | Produto (segurança concorda: reduz a superfície de dado pessoal) |
| D7 | **Componentes e moldes antes da primeira tela** | Nova story **FND-09d "Componentes de formulário"**, antes do Marco A, com:<br>• os 3 moldes: página em seções, detalhe com abas, painel lateral;<br>• os componentes: máscaras, número brasileiro, combobox, árvore, editor de linhas, aviso que não bloqueia, faixa de próximos passos, `ConfirmDialog` com ação alternativa.<br>O PO aprova os moldes **e** valida a FND-09a/09b, que ainda está pendente, antes da CAD-01a. | Design + produto |
| D8 | **Dados reais na homologação (LGPD)** | Antes da primeira carga real (MIG-01b), uma ADR curta de retenção e direitos do titular. A homologação com dados reais segue as regras de acesso de produção, e a allowlist da plataforma sai do modo aberto. | Segurança |
| D9 | **CPF na lista** | Na lista e na busca, CPF parcial (`***.456.789-**`). Completo só no detalhe, e a busca por CPF é sempre exata. O PO tinha sugerido "não mascarar"; a segurança recomenda mascarar, pelo princípio da minimização. | Produto × segurança |
| D10 | **Excluir × inativar em estoque e produção** | Padrão único em **todos** os cadastros: "Excluir" (exclusão lógica) quando o registro não tem uso e "Inativar" quando tem. Para isso, a ação `delete` entra no catálogo de `inventory` e de `production`. O PO tinha sugerido "só inativar" nesses dois módulos; a arquitetura prefere a regra uniforme. | Produto × arquitetura |
| D11 | **Padrões de produto da tabela 1.1** | Aprovar em bloco, ou indicar as exceções. | Produto |

### 1.1 Padrões de produto propostos (D11)

| Story | Ponto | Padrão proposto |
|---|---|---|
| CAD-06 | Dados da empresa | O perfil do banco do tenant é a fonte da verdade. O login mostra o nome fantasia. O CNPJ é só leitura no tenant e é alterado pelo super admin. |
| CAD-06 | % de encargos | Valor inicial de **80,00%**, com a Dica "confirme com a contadora". Faixa de 0 a 200%, com 2 casas. |
| CAD-06 | Precificação | Markup como modo padrão. O % padrão entra na COM-01. |
| CAD-06 | Garantia padrão | Produto acabado: 12 meses. Componente e Serviço: 3 meses. Demais tipos: sem garantia. A configuração fica no módulo de estoque (seção 2.3). |
| CAD-06 | Numeração | Prefixo de 1 a 6 caracteres (A–Z, 0–9), de 3 a 8 dígitos. Nenhum tipo de documento novo agora: o código do item é digitado e mantém a ref do Dolibarr; a matrícula é texto opcional. |
| CAD-06 | Logo | PNG, JPG ou WebP, até 1 MB e 1024 px. |
| CAD-05a | Plano de contas | A estrutura sugerida na story vira o seed v1, editável e sem esperar a contadora; o seed nunca sobrescreve. Máscara `9.9.99`. Os grupos da DRE são os sugeridos. Centros de custo: Administrativo, Comercial, Produção e Instalação. Inclui as contas "A classificar" de receita e despesa. |
| CAD-05b | Condições de pagamento | Seed com À vista, 30, 30/60 e 30/60/90 dias. Saldo inicial negativo aceito, com aviso. Tipos de conta: só Banco e Caixa. Vencimento em dia não útil não é ajustado (rever na FIN-01). |
| CAD-01a | Permissões de `parties` | Comercial, Financeiro e Almoxarifado com ver, criar e editar. Engenharia só com ver. Excluir fica com o Proprietário. Aplicadas aos tenants que já existem pelo *seed patch* (seção 2.3). |
| CAD-01a | Documento | PF ou PJ sem documento é aceito, com o chip "Sem documento". O documento passa a ser exigido só na emissão fiscal ou de boleto. Validação da IE só pelo formato. Regimes tributários conforme a lista sugerida. Fusão de duplicados só na migração. |
| CAD-01a | Situação | Campo novo Ativo/Inativo. Os terceiros encerrados no Dolibarr entram inativos, não excluídos. |
| CAD-01b | Provedores | **BrasilAPI** para CNPJ **e CEP**, sem provedor reserva automático (a falha só gera aviso). Cache de 30 dias. CNPJ com situação diferente de ativa: só aviso. |
| CAD-03 | Calendário | Um calendário padrão do tenant. As horas por dia saem dos turnos. Pontos facultativos vêm sugeridos e desmarcados. Turno que vira o dia fica fora do MVP. O local padrão é "ALM — Almoxarifado principal"; com depósito único no Dolibarr, a carga vincula esse depósito ao ALM. |
| CAD-02a | Itens | Lote e série para qualquer tipo, exceto Serviço. NCM validado só pelo formato e opcional para material de consumo. Item genérico por modelo, e a série identifica cada unidade. |
| CAD-02b | Conversões | Fator com 6 casas e resultado com 4, arredondado com `AwayFromZero`. Conversão só por item. Unidade do fornecedor em texto livre. |
| CAD-04a | Custo-hora | Salário de referência do cargo, com padrão na média da faixa. Horas mensais = horas semanais × 5, editável. Custo-hora do **cargo** visível com `hr.view`; custo por funcionário não é exposto (seção 2.5). Seed com a jornada "44 h seg–sex". Sindicato em tabela simples. |
| CAD-04b-base | Funcionários | Documentos: CTPS e título de eleitor. Lista de campos sensíveis conforme a story. Cargo e admissão não são sensíveis. Matrícula em texto opcional. Exportação e anonimização pela LGPD ficam para uma story da Fase 6. |
| MIG-01a | Importação | Lista do que não migra conforme a story. Reexecução não sobrescreve o que foi editado no BemAqui e mostra o conflito no relatório. Primeira carga na homologação. Usuário de sistema "Importação Dolibarr", com o operador registrado. |
| MIG-01b | Terceiros | Instaladores e transportadoras vêm de uma lista de ids do PO no de-para; sem ela, entram como Fornecedor. Prospects entram como Cliente, com a observação "Prospect no Dolibarr". |
| MIG-01c | Itens | Produto sem NCM entra **inativo**. O tipo vem do de-para produto a produto com o PO. Produto em várias categorias fica com a primeira. Sub-depósitos são achatados com o nome do pai. |

## 2. Correções sem decisão do PO

Estas correções são técnicas ou de consistência. Entram nas stories e ADRs depois da aprovação das decisões da seção 1.

### 2.1 ADRs novas
- **ADR 0016 "Decisões técnicas da Fase 1"**
  - **Rota anônima do logo:** `GET /api/v1/public/tenant-logo`, adendo à lista de rotas anônimas da ADR 0009.
    - O tenant vem do host, sem parâmetro de id, e só é servido o logo com scan `Clean`.
    - Cabeçalhos: `Content-Type` da allowlist, `nosniff`, CSP `default-src 'none'` e ETag.
    - Cache por host e rate limit. Sem data URI no `tenant-context`.
  - **Numeração:**
    - "Próximo número" passa a ser `INSERT … ON CONFLICT DO UPDATE SET last_value = GREATEST(…)`. Hoje, sem linha no ano, nada é gravado.
    - A troca do reinício anual fica bloqueada depois do primeiro número emitido, porque ela troca a chave da sequência e a reinicia.
    - Índice único em `number` em cada tabela de documento.
  - **Seed patch:** tabela `identity.seed_patches`, versionada e idempotente. Ela só **acrescenta** concessões aos grupos com `seed_key` e incrementa o `perm_version`. É aplicada pelo sistema, fora da delegação, como adendo às ADRs 0008 e 0010.
  - **Dados sensíveis por rota:** `/{id}` devolve o resumo e `/{id}/sensitive` exige `hr.view_sensitive`. A leitura é registrada como `SensitiveRead` por `IAuditWriter.RecordReadAsync` na mesma requisição, e só o Proprietário vê esses registros. Um teste de arquitetura impede propriedade sensível no DTO de resumo.
  - **Data de corte:** vale a posição no **fim** do dia D. Os movimentos de abertura entram em D 23:59:59 America/Sao_Paulo, gravados em UTC.
  - **`OpeningBalance` ("Saldo inicial"):** tipo próprio de movimento, e não `Adjustment`. Entra no 03 com a EST-01.
  - **Dono das condições de pagamento:** o Finance (`finance.payment_terms`, `IPaymentTerms` em `Finance.Contracts`). O Parties guarda só o id. O uso inverso é consultado por `IPaymentTermUsageProvider`. Corrigir o 03.
  - **`delete` em `inventory` e `production`:** conforme a D10.
  - **Expurgo controlado:** procedimento por migration para dado pessoal que tenha ido parar na auditoria imutável.
- **ADR 0017 "Importação do Dolibarr"**
  - Snapshot conforme a D3, com GRANT **por coluna** e usuário restrito por host.
  - SELECT só com colunas explícitas, de uma lista fixa de tabelas.
  - Módulo `Migration` com as 5 camadas, fora do `ModuleCatalog` e sem rota HTTP de escrita. O teste de arquitetura passa a contar 14 módulos.
  - Interfaces `I*Import` nos Contracts dos destinos, que só o `Migration` pode referenciar e que chamam os mesmos handlers da Application.
  - Idempotência por `external_refs` com o `xmin` gravado. `--dry-run` executa tudo numa transação desfeita no fim.
  - Soma do `llx_bank` em `DECIMAL(24,8)` com um único arredondamento.
  - Proteção contra carga no tenant errado: o `MAIN_INFO_SIREN` precisa ser igual ao CNPJ do tenant, com confirmação pela chave.
  - CSV do relatório protegido contra injeção de fórmula.
  - `DROP USER` e remoção do segredo na GOL-01.

### 2.2 Ordem, escopo e testabilidade
- **CAs não testáveis:** os que remetem a "ponto em aberto" ou trazem "sugestão" no texto ganham o valor aprovado em 1.1. Casos:
  - CAD-06: CA 6, 10 e 13;
  - CAD-05a: CA 1;
  - CAD-01a: CA 11;
  - CAD-01b: CA 4, 5 e 11;
  - CAD-04a: CA 11;
  - CAD-04b: CA 4, 5 e 15;
  - MIG-01b: CA 6;
  - MIG-01c: CA 7.
- **Dependências que travavam a fase sem necessidade:**
  - o validador de CPF/CNPJ (`BrazilianDocuments`) passa da CAD-06 para a CAD-01a;
  - o CA 12 da CAD-01a (condição de pagamento padrão) passa para a CAD-05b;
  - a CAD-05a não bloqueia nenhuma outra story dentro da fase.
- **Seeds nos tenants existentes:** todo CA de seed ganha "e nos tenants existentes no próximo deploy". A busca por `seed_key` usa `IgnoreQueryFilters()`, para não recriar o registro excluído.
- **Contagens das cargas:** nos CAs, conferir "o total lido na extração", e não os números fixos de 16/10.
- **Histórico do terceiro (R-TER-3):** a aba "Histórico comercial e financeiro" passa a ser CA da COM-05 ou de uma story "Terceiro 360" na Fase 2.
- **Busca Ctrl K com dados:** CA na CAD-01a e na CAD-02a (ADR 0010, item 9).
- **NCM:** o invariante passa a ser "NCM obrigatório para item **ativo** que não é Serviço". Vale igual na tela e na importação.
- **Migrations:** uma por story, regerada no rebase e nunca editada à mão. Isso evita conflito no `ModelSnapshot` único.

### 2.3 Arquitetura
- **Configuração de garantia:** fica no Inventory, não no Identity. O enum `ItemType` nasce na CAD-02a. O `ITenantParameters` do Identity fica só com encargos e modo de precificação.
- **Testes de posse de tabela:** um teste confere, no `TenantDbContext.Model`, se cada assembly grava só no próprio schema. Outro varre o SQL cru dos módulos procurando referência a schema alheio.
- **`AuditableEntityRegistry`:** passa a usar nome qualificado e a lançar exceção em duplicata. Hoje, duas classes `Address` (HR e Parties) colidiriam, e o histórico de HR ficaria visível com `parties.view`.
- **Ajustes pequenos por story:**
  - CAD-03: `IWorkCenterCapacity` recebe o id do centro; `CalendarChanged` vira chamada síncrona, sem outbox.
  - CAD-01a: `IParties` resolve terceiro excluído pelo id; busca com `unaccent` e `pg_trgm`.
  - CAD-05b: sem `credentials_secret` agora, ele entra na BNK-01.
  - CAD-06: numeração gravada via `INumberFormatAdmin` (BuildingBlocks); perfil da empresa no schema `settings`.
- **`ModuleCatalog.Phase`:** documentar que o campo é só informativo, ou atualizar os valores. Hoje `hr` aparece como Fase 6 e precisa ser liberado no tenant para a CAD-04.

### 2.4 Design: padrões comuns a todos os cadastros (entram no README das stories)
1. **Lista:**
   - h1 com linha descritiva;
   - "Novo {entidade}" dentro de `<Can>`;
   - `DataTable` com busca e filtros na URL;
   - a primeira coluna leva ao detalhe;
   - vazio com ação para começar e vazio filtrado com "Limpar filtros".
2. **Onde editar:**
   - cadastro simples, até cerca de 6 campos: painel lateral;
   - cadastro composto: rota de detalhe com abas;
   - criação: página única em seções, só com o obrigatório; as abas aparecem a partir da edição.
3. **Abas:**
   - ligadas à URL e a `useHelpTab`, com uma ajuda por aba;
   - "Histórico de alterações" é sempre a última.
   - No funcionário, a linha do tempo de RH chama "Linha do tempo".
4. **Campos:**
   - "obrigatório" no rótulo, com `aria-required`;
   - máscara na digitação e valor gravado normalizado;
   - números no padrão brasileiro, com `tabular-nums`;
   - aviso âmbar que não bloqueia;
   - foco no primeiro erro, inclusive em aba escondida, e contador de erros por aba.
5. **Padrões preenchidos:**
   - o campo que comanda vem primeiro (tipo do item, PF/PJ inferido pelos dígitos);
   - o derivado só muda enquanto o usuário não mexeu nele;
   - o primeiro registro vira o padrão.
6. **Nada digitado duas vezes:**
   - a consulta de CNPJ preenche dados e endereço, e o usuário confirma com "Usar estes dados";
   - criação rápida dentro do combobox;
   - "Duplicar";
   - documento já existente oferece "Adicionar papel" / "Abrir cadastro".
7. **Verbos:**
   - específicos: "Salvar conta contábil", "Salvar conta bancária", "Salvar centro de trabalho";
   - o diálogo repete o verbo, e o botão de sair dele diz "Voltar";
   - quando o servidor recusa, o diálogo oferece a ação alternativa ("Inativar", "Restaurar").
8. **Inativo:**
   - chip neutro, porque o vermelho é só para o crítico;
   - filtro "Inativos";
   - some dos seletores e continua visível em quem já o usa;
   - novo componente `Tag` para atributos, separado do chip de situação.
9. **Próximos passos:**
   - faixa fixa no detalhe depois de criar, com até 3 ações; o toast só confirma o verbo;
   - o que muda dado já existente (numeração, % de encargos que recalcula N cargos) pede confirmação mostrando o impacto.
10. **Estados:**
    - carregando, vazio e erro;
    - modo leitura sem `edit`;
    - "Dados protegidos" sem `view_sensitive`.
11. **Acessibilidade:**
    - papéis com `fieldset`/`legend`;
    - árvore como `treegrid`;
    - editor de linhas com "Remover {linha n}";
    - totais ao vivo em `aria-live`;
    - `Dica` com alvo de 24 px e `aria-label="Dica: {campo}"`.
12. **400 px:**
    - painel em tela cheia e editores empilhados;
    - abas com rolagem horizontal;
    - ações principais visíveis sem rolar;
    - um CA de 400 px e de teclado por tela com editor.
13. **Importado:** chip "Importado do Dolibarr" e a referência de origem no detalhe.

**Correções de story:**
- **CAD-01a:**
  - o bloco "Endereço de cobrança" entra no formulário de criação, porque hoje a consulta de CNPJ não tem onde preencher;
  - a duplicidade é checada **antes** da consulta à Receita.
- **CAD-04b:**
  - criação em página única;
  - salário, cargo, afastamento e desligamento viram ações com diálogo próprio ("Reajustar salário" / "Salário reajustado").
- **CAD-06:**
  - "Salvar numeração" confirma o que não tem volta;
  - a ordem das abas passa a ser Empresa, Numeração, Parâmetros, Segurança, IA.

### 2.5 Segurança: CAs a acrescentar
- **Todas:**
  - IDOR pai/filho: toda rota `/{paiId}/filhos/{id}` confere o vínculo;
  - id de outro módulo é validado pelo Contracts;
  - teste de arquitetura: todo endpoint de módulo tem `RequireModule` e uma policy `perm:`;
  - `Cache-Control: no-store` nas respostas com dado pessoal.
- **Auditoria sem dado pessoal (CAD-01a, CAD-04a, CAD-04b):**
  - `document` sempre `[PersonalData]`;
  - histórico de RH com valores mascarados;
  - teste de arquitetura com allowlist: toda propriedade de entidade auditável de HR, e de PF/contato em Parties, é `[PersonalData]` ou está na lista de campos não sensíveis;
  - teste que lê o JSON bruto de `audit.audit_log` com valores canário;
  - histórico de entidade de HR exige `hr.view_sensitive`.
- **Log sem dado pessoal:**
  - `SensitivePropertyMask` ganha cpf, document, rg, pis, email, phone, salary, pix, bank e birth;
  - Serilog respeita `[PersonalData]`;
  - teste com canário no sink;
  - `Include Error Detail` continua desligado.
- **Anexos:**
  - `AttachmentOwnerRegistry` por tipo de dono: módulo, ação de leitura e de escrita, tipos aceitos, tamanho máximo e verificação de que o dono existe;
  - o logo aceita só imagem de até 1 MB;
  - documento de funcionário exige `hr.view_sensitive` e registra `SensitiveRead`.
- **CAD-01b:**
  - CNPJ validado por regex antes de montar a URL, sem redirect;
  - resposta limitada a 64 KB;
  - log do HttpClient desligado;
  - limite global e circuit breaker, porque a BrasilAPI limita por IP e o IP da VPS é compartilhado;
  - cache por tenant, sem o QSA e expurgado por `TenantJob`. A razão social de MEI contém nome e CPF.
- **CAD-04a e CAD-04b:**
  - comandos separados para dados gerais, dados pessoais e troca de salário, com `UnmappedMemberHandling.Disallow`: um PUT sem os campos sensíveis não pode anulá-los;
  - ordenar ou filtrar por campo sensível é ignorado sem a permissão;
  - a lista nunca devolve campo sensível;
  - não existe custo-hora por funcionário fora do sensível.
- **CAD-04c (afastamentos, LGPD art. 11):**
  - com `hr.view`, só a situação "Afastado";
  - tipo e motivo só com `hr.view_sensitive`;
  - motivo com limite de tamanho e a orientação "não informe CID";
  - eventos sem o tipo.
- **Contas bancárias (CAD-05b) e banco/Pix do funcionário (04c):**
  - toda troca grava histórico e notifica os Proprietários;
  - trocar a conta da empresa exige Proprietário ou `finance.approve`;
  - conta mascarada na lista para quem só tem `finance.view`.
- **Vínculo usuário↔funcionário:** exige `hr.edit` **e** `identity.edit`, é auditado e tem comando para desvincular.
- **MIG-01b:**
  - `note_private` sai do HTML do Dolibarr convertido para texto, sem caracteres de controle e bidi;
  - não importa `birthday`, IBAN, BIC nem titular;
  - os arquivos do relatório ficam com permissão 0600 e são apagados depois do aceite;
  - a staging tem prazo de expurgo.
- **Dependentes menores (04c):** CPF só quando o dependente for de IR.

## 3. Para depois da Fase 1
- **MIG-01e, na Fase 2:**
  - o histórico de pagos vai para a tabela `finance.imported_payment_history`, e não para `finance.receivables` com uma flag;
  - a FIN-01 define antes o invariante `paid_amount = Σ baixas`;
  - o pago parcial antes do corte entra como baixa "anterior ao corte".
- **MIG-01d, na Fase 3:** depois da EST-01, com o `OpeningBalance`. O `pmp` da staging é contrato com a MIG-01c, então não limpar a staging antes.
- **CAD-04c, no início da Fase 3:** conforme a D6, com os CAs de segurança de 2.5.
- **Primeiros passos:** sugestão de produto e design, sem decisão. É uma lista no Início com o progresso da implantação: empresa, contas, condições, terceiros, itens, cargos, funcionários.
