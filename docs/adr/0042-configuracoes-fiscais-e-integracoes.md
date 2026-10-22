# ADR 0042: Configurações fiscais e de integrações pré-configuradas

- Status: Aceito (21/10/2026). Requisito novo do PO (John) na revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): **tudo o que depende da contadora, do banco ou de um parceiro é configurável em Configurações**, já pré-configurado com os valores propostos, editável e marcado "A confirmar com a contadora" até alguém conferir. Aprovou também os valores das tabelas 3.1 a 3.5 (multa 2%, juros 1% a.m., régua −3, 0, +3, +10 dias e os demais).
- Contexto: as stories das Fases 5 a 8 tinham dezenas de pontos que só a contadora, o Inter ou o parceiro do WhatsApp respondem (regime, CRT, CFOP e CST por cenário, alíquotas, código de serviço da NFS-e, IBS/CBS, multa e juros, régua). Esperar cada resposta para codar travaria as fases; fixar os valores no código obrigaria a um deploy a cada ajuste da contadora. Ao mesmo tempo, emitir nota em produção com um valor que ninguém conferiu é risco fiscal real.
- Complementa as ADRs 0007 (segredos, adendo de 21/10/2026), 0008 (`settings.edit` reservada, 2FA de novo, adendo de 21/10/2026), 0016 (adendo M6: cada módulo é dono dos parâmetros, Configurações só monta a tela), 0027 (EmissorFiscal), 0032 (Inter) e 0041 (WhatsApp).

## Decisão

### 1. Valor sugerido, editável e conferido
- Todo parâmetro fiscal, de cobrança e de integração nasce **pré-configurado** pelo seed do módulo dono com o valor proposto e é **editável** na tela de Configurações.
- Cada valor não conferido mostra o chip **"A confirmar com a contadora"** (alerta) e a ação **"Marcar como conferido"**, que grava **quem e quando** (auditado). Mudar um valor conferido volta o chip para "A confirmar" (a conferência vale para o valor, não para o campo).
- Armazenamento: na tabela de parâmetros do módulo dono (`<schema>.parameters`, ADR 0016 M6) ou na tabela própria da configuração (`fiscal.fiscal_settings`, `finance.bank_integrations`, `shared.whatsapp_settings`), com `review_state` (`ToConfirm`/`Confirmed`), `reviewed_by` e `reviewed_at` por chave.
- Estados da tela: carregando; **primeira configuração** com os valores sugeridos visíveis e a explicação de cada um; erro com o motivo e a ação; contagem "{n} valores a confirmar" no topo; `<Dica>` nos termos que confundem.

### 2. Produção exige conferência
- A emissão em **produção** (GOL-01b) exige os **campos fiscais críticos conferidos**: regime tributário, CRT, série exclusiva, ambiente, CFOP e CST padrão de cada cenário usado, alíquotas de ICMS, PIS e COFINS, IPI pelo NCM, IBS/CBS (CST e `cClassTrib`) e o código de serviço da NFS-e. Sem isso, ligar o ambiente de produção responde 422 `fiscal_settings_not_confirmed`, com a lista dos campos.
- Em homologação e no tenant `demo`, a falta de conferência **não bloqueia** (só o chip).
- Ligar o Inter em produção exige o teste de conexão de produção feito (D−1, ADR 0032).

### 3. Telas
| Tela | Rota | Ajuda | Story |
|---|---|---|---|
| Configurações → Fiscal | `/app/configuracoes/fiscal` | `Ajuda/configuracoes-fiscal.md` | FIS-02a |
| Configurações → Fiscal → Certificado digital | `/app/configuracoes/fiscal/certificado` | `Ajuda/configuracoes-fiscal-certificado.md` | FIS-02a |
| Configurações → Integrações → Banco Inter | `/app/configuracoes/integracoes/inter` | `Ajuda/configuracoes-integracoes-inter.md` | BNK-01a |
| Configurações → Cobrança | `/app/configuracoes/cobranca` | `Ajuda/configuracoes-cobranca.md` | BNK-01b (tela), FIN-09 (seção "Régua de cobrança") |
| Configurações → Integrações → WhatsApp | `/app/configuracoes/integracoes/whatsapp` | `Ajuda/configuracoes-integracoes-whatsapp.md` | WPP-01 |

### 4. Valores pré-configurados
**Fiscal** (dono Fiscal; todos "A confirmar com a contadora"; as regras fiscais detalhadas da FIS-02b nascem do mesmo seed, também a confirmar):
- Regime: **Lucro Presumido** (P-01) e o CRT correspondente.
- **Série exclusiva** da NF-e do BemAqui (obrigatória em produção, diferente da série do emissor atual).
- Ambiente por tenant: homologação; produção só na GOL-01b (`catalog.tenants.fiscal_production_allowed`).
- CFOP padrão: venda de produção a contribuinte em SP **5101**; a não contribuinte em SP **5101**; outro estado, contribuinte **6101** e não contribuinte **6107** (DIFAL bloqueado); peça de revenda **5102**/**6102**.
- CST: ICMS **00**; PIS/COFINS **01** (cumulativo); IPI **50**.
- Alíquotas: ICMS interno SP **18%**; interestadual **12%**, **7%** ou **4%** conforme a origem e o destino; PIS **0,65%**; COFINS **3%**; IPI pelo NCM.
- Tributos que entram no custo do projeto: **ICMS, PIS, COFINS, ISS e IBS/CBS**; IPI fora (ADR 0025, adendo de 21/10/2026).
- NFS-e: item da LC 116 e código municipal de Caçapava, pré-preenchidos com o que a FIS-06 propõe ou, sem proposta, **7.02**, com `<Dica>` citando o **14.06** como alternativa; ISS e retenções da NFS-e.
- IBS/CBS: CST e `cClassTrib` a confirmar (P-03).
- Prazo de cancelamento: **24 h**. Grupo `transp` e volumes padrão.

**Certificado digital** (FIS-02a): envio do A1 (`.pfx` e senha) direto ao cofre do EmissorFiscal (ADR 0027 §3); o BemAqui mostra só validade, titular, CNPJ e thumbprint; CNPJ do certificado conferido com o da empresa; alerta de vencimento em 30, 15 e 7 dias ao Proprietário.

**Banco Inter** (BNK-01a, por conta): client id e secret, certificado mTLS (`.crt`/`.key` ou `.pfx`), conta, ambiente (sandbox; produção só na GOL-01b, teste de produção no D−1), webhook (URL com o token `?t=` mostrada uma vez, para o cadastro no Inter) e "Testar conexão"; `EnvironmentBanner`.

**Cobrança** (dono Finance): multa **2%** (`finance.late_fine_pct`), juros **1% a.m.** (`finance.late_interest_month_pct`), desconto padrão **0**, envio automático do boleto **marcado**, limite de **200 e-mails por dia**, comportamento do vencido e do dia não útil; seção "Régua de cobrança" (FIN-09): passos **−3, 0, +3 e +10 dias**, textos com placeholders de lista fixa e horário de envio. Multa, juros e desconto acima do limite da D8 exigem `finance.approve` no título.

**WhatsApp** (WPP-01): parceiro (BSP), credenciais, número remetente, modelos aprovados, janela de envio (dias úteis, das 8 h às 18 h) e canais ligados (régua, etapa).

### 5. Segredos só de escrita
- Certificado A1 e senha, client secret e certificado do Inter, credenciais do WhatsApp e chave do EmissorFiscal são **só de escrita**: nunca voltam numa resposta, a tela mostra "configurado em dd/mm/aaaa", o diff da auditoria registra só "alterado" e o canário confere log, auditoria e resposta (ADR 0007).
- O A1 nunca é guardado pelo BemAqui (ADR 0027 §3).

### 6. Quem edita
- Editar e marcar como conferido exigem **`settings.edit`**, reservada ao Proprietário (ADR 0008), conferida pelo endpoint do módulo dono; ver exige `settings.view`.
- **Salvar segredo** (certificado A1, credenciais e certificado do Inter, credenciais do WhatsApp, chave do EmissorFiscal) exige também **2FA de novo** (ADR 0008, adendo, D6).
- As 3 camadas valem em todas as telas: módulo dono não liberado mostra `ModuleNotReleased`; sem grupo, 403 `forbidden`.
- Auditoria com antes e depois em todo valor, menos nos segredos.

## Consequências
- **FIS-02a** entrega Configurações → Fiscal e o Certificado digital, o seed fiscal e a regra da seção 2 (re-estimada se crescer, sem story nova); **FIS-02b** usa o mesmo seed para as regras detalhadas; **BNK-01a** a tela do Inter; **BNK-01b** a tela de Cobrança; **FIN-09** a seção da régua; **WPP-01** a tela do WhatsApp; **GOL-01a** confere os campos críticos.
- Os pontos de contadora, Inter e parceiro das tabelas 3.2 a 3.5 viram **pré-condição externa** das stories (a conferência), sem bloquear o `Ready`.
- Cada tela tem estados, ajuda, 400 px e as 3 camadas; o teste de cobertura de ajuda inclui as cinco rotas.
