# FIS-06 — NFS-e da instalação e da manutenção (D)

| Campo | Valor |
|---|---|
| ID | FIS-06 |
| Título | NFS-e do serviço de instalação a partir da OS de instalação aceita (COM-06) ou do projeto, pelo `IFiscalEmitter` (padrão do município configurado no EmissorFiscal), com ISS e retenções vindos da regra de serviço, vínculo com a NF-e do equipamento na venda mista, XML e PDF guardados e enviados ao cliente, ISS no custo do projeto; condicionada à P-02 |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-FIS-7 (NFS-e para serviços de instalação/manutenção; D, P-02) · R-FIS-8 (IBS/CBS) · R-PRJ-3 · R-PRJ-5 · R-COM-1 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-01 (`IFiscalEmitter.IssueNfseAsync`), FIS-02a (configuração), FIS-02b (regra de serviço), COM-06 (OS de instalação e aceite técnico), FIS-03a (venda mista); **P-02** decidida; NFS-e de Caçapava configurada e credenciada no EmissorFiscal (lacuna L-g) |
| Bloqueia | — |
| Ordem de execução | 16 (Marco 5.6 "Desejáveis"; não bloqueia o go-live, Pontos em aberto 1) |

## User story
Como **financeiro**, quero **emitir a nota de serviço da instalação quando o cliente aceita a entrega técnica**, para **faturar o serviço no sistema, junto com a nota do equipamento, e ver o ISS na margem do projeto**.

## Contexto e referências
- PRD R-FIS-7 ("NFS-e para serviços de instalação/manutenção (CNAE principal da empresa) — D — ver pendência P-02"); `05-backlog` FIS-06 ("D, condicionado à pendência P-02").
- `06-pendencias-riscos` **P-02** ("CNAE principal é instalação/manutenção (4329-1/03) … define … a NFS-e da instalação (Caçapava)"; decisão de 16/10/2026: inclusão do CNAE de fabricação em andamento, fechar quando o cartão CNPJ estiver atualizado) e **P-15** (retenções opcionais no título a pagar — aqui é o serviço **prestado**, não tomado).
- EmissorFiscal (FIS-01): contrato `POST /api/v1/nfse` estável (ISS e retenções prontos, `nfeVinculada` para a venda mista, município da prestação); padrão de Caçapava decidido (CONAM/e-Transparência, ADR 005 de lá), autenticação por usuário e código de contribuinte da prefeitura, layout 4.00 com `Reg60_RTC` (IBS/CBS) obrigatório; **faltam**: configuração do município, credenciais, cancelamento, PDF, webhook de NFS-e e os campos IBS/CBS no contrato (lacuna L-g e L-b). Do contador: `CodSrv`, `SRV_CTN`, `SRV_NBS`, CST e `CClassTrib` do serviço.
- `04-fluxos-estados` §1 "OS de instalação" (`InProgress ─aceite técnico─► Completed`).

## Critérios de aceite
1. **Dado** `fiscal.create` e uma OS de instalação `Completed` (aceite técnico) ou o projeto com serviço contratado, **quando** clico "Emitir NFS-e", **então** nasce a NFS-e `Draft` com o tomador (cliente do projeto), o serviço pela regra de serviço ativa (FIS-02b com a operação "Prestação de serviço": item da LC 116, código do município, códigos nacionais, CNAE quando o padrão pedir), a discriminação sugerida ("Instalação de {equipamento}, projeto {número}, OS {número}"), o município da prestação (o da obra, `site_address`; ISS no local da prestação quando a regra disser), o valor do serviço pelo contrato, ISS e retenções calculados pela regra, e a NF-e do equipamento vinculada quando existir.
2. **Dado** o rascunho válido, **quando** clico "Emitir NFS-e" e confirmo no diálogo com o impacto, **então** a emissão vai pelo `IFiscalEmitter.IssueNfseAsync` com o `Idempotency-Key` estável; autorizada, a nota guarda número, código de verificação, XML (e o PDF, se o EmissorFiscal passar a oferecer, Pontos em aberto 3), publica `InvoiceAuthorized` com `type = NFSe` e o ISS (e IBS/CBS, quando houver) como custo do projeto (`CostType.Tax`), e o e-mail ao cliente sai pela fila; rejeitada, mostra o motivo e permite corrigir.
3. **Dado** o retorno assíncrono, **quando** o EmissorFiscal não tem webhook de NFS-e (lacuna L-g), **então** o `fiscal-pending-check` (FIS-01 CA 10) consulta as NFS-e em processamento.
4. **Dado** a necessidade de cancelar uma NFS-e, **quando** o EmissorFiscal ainda não suporta (lacuna L-g), **então** "Cancelar NFS-e" mostra "Cancele no portal da prefeitura e registre aqui." com "Registrar cancelamento feito na prefeitura" (`fiscal.approve`, motivo e número do protocolo), que lança o custo inverso e marca a nota `Cancelled` (origem "manual"), sem apagar nada.
5. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; emitir sem `fiscal.create`, cancelar sem `fiscal.approve`: 403; `RequireModule` e `perm:`; IDOR em `/outgoing-invoices/{id}`; OS e projeto validados pelos Contracts dos donos.
6. **[Segurança]** **Dado** as credenciais da prefeitura, **quando** são configuradas, **então** ficam no EmissorFiscal (não no BemAqui), conforme a ADR 0023; o BemAqui não recebe nem guarda a senha web da prefeitura.
7. **Dado** a NFS-e, **quando** existe com número, **então** nunca é apagada; XML guardado 5 anos; histórico com quem emitiu e quem cancelou.

## Regras de negócio e técnicas
- Mesma tabela `fiscal.outgoing_invoices` com `type = NFSe` (03), campos do serviço em `service` (jsonb: item LC 116, código do município, códigos nacionais, município da prestação, ISS, retenções) e `linked_invoice_id` (a NF-e do equipamento).
- O ISS **prestado** e suas retenções vêm da regra; nada digitado à mão (como na FIS-03a).

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.create`, `fiscal.edit`, `fiscal.approve`.
- **Eventos:** `InvoiceAuthorized` e `InvoiceCancelled` com `type = NFSe`.
- **Tabelas:** `fiscal.outgoing_invoices` (`type = NFSe`, `service`, `linked_invoice_id`, `verification_code`).

## Telas e estados
- **Fiscal → Notas de saída** com o filtro "NFS-e"; botão "Emitir NFS-e" na OS de instalação aceita e no projeto; `NextSteps` da NF-e autorizada com instalação oferece "Emitir NFS-e da instalação".
- **Estados:** carregando; não disponível ("A NFS-e da sua cidade ainda não está configurada no Emissor Fiscal." para quem não pode configurar); rascunho; processando com estado de demora; rejeitada com o motivo.
- **400 px:** como a NF-e.
- **Ajuda:** `Ajuda/fiscal-nfse.md`; `<Dica>` em "Município da prestação", "Item da LC 116" e "Retenção do ISS".

## Plano de testes
- **Unidade:** montagem a partir da OS e da regra (CA 1); ISS e retenções.
- **Integração (Testcontainers + fake do EmissorFiscal):** emitir, autorizar pelo job, custo do ISS no projeto e e-mail (CA 2, 3); cancelamento registrado manual (CA 4); três camadas (CA 5); nunca apagar (CA 7).
- **Homologação da prefeitura (manual):** uma NFS-e no ambiente de homologação de Caçapava (que existe e não depende do A1, ADR 005 do EmissorFiscal), no tenant de teste.
- **E2E (Playwright):** aceite técnico → emitir NFS-e (fake) → ver no projeto.

## Fora do escopo
- NFS-e recorrente de contrato de manutenção (Fase 10). Serviço tomado (declaração de serviços tomados): fora do MVP.

## Pontos em aberto
1. **NFS-e no go-live** (PO): a instalação é o CNAE principal; sem a FIS-06, a nota de serviço continua fora do sistema depois do go-live. Proposta: não bloquear a GOL-01 (é D no PRD), mas decidir com a contadora quem emite a NFS-e até a FIS-06.
2. **P-02** (contadora): CNAE de fabricação e a separação NF-e × NFS-e na venda mista.
3. **PDF, cancelamento, webhook e IBS/CBS da NFS-e** (PO): extensões do EmissorFiscal (lacunas L-b e L-g), com prazo; o `Reg60_RTC` já é obrigatório em Caçapava.
4. **Códigos do serviço** (contadora): `CodSrv`, `SRV_CTN`, `SRV_NBS`, CST e `CClassTrib` da instalação e da manutenção.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake) e E2E passando; NFS-e de homologação registrada.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Efeito fiscal com confirmação.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IssueNfseAsync`/`GetNfseAsync` no adaptador; NFS-e na `OutgoingInvoice`; botões na OS e no projeto; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, contadora.
- Gate: CA 2, 4 e 6 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** cancelamento registrado manualmente enquanto o EmissorFiscal não cancela NFS-e (CA 4); credenciais da prefeitura só no EmissorFiscal (CA 6); a importância da NFS-e para a Konebras levada ao ponto 1.
- **Pré-condições para `Ready`:** P-02 decidida; pontos em aberto 1, 3 e 4; Caçapava configurada e credenciada no EmissorFiscal.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
