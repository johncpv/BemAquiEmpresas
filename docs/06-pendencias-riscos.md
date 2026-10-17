# 06 — Pendências e riscos

Regra: item pendente não é implementado até ter decisão registrada aqui (data + quem decidiu).

## Pendências (decisão do PO / cliente / contadora)

| ID | Pendência | Bloqueia | Com quem | Resolver até | Decisão |
|---|---|---|---|---|---|
| P-01 | **Regime tributário** da Konebras. Pela consulta pública do CNPJ em 16/10/2026: ME, optou pelo Simples em 20/07/2022 e **hoje consta fora do Simples** (provável Lucro Presumido) | COM-01 (impostos estimados), EST-01 (crédito no custo médio), FIS-02, FIS-03 | Contadora | Antes da Fase 2 | |
| P-02 | **CNAE x fabricação:** o CNAE principal é instalação/manutenção (4329-1/03) e o secundário é comércio atacadista (4669-9/99). Não há CNAE industrial. Isso define a NF-e de produto fabricado (IPI, CFOP de produção própria) e a NFS-e da instalação (Caçapava) | FIS-03, FIS-06, regras fiscais | Contadora | Antes da Fase 5 | 16/10/2026 (John): **inclusão do CNAE de fabricação em andamento.** Fechar quando o cartão CNPJ estiver atualizado |
| P-03 | **Reforma Tributária (IBS/CBS):** campos e prazos | FIS-01, FIS-03 | Contadora + FIS-01 | Fase 5 | 16/10/2026 (John): **seguir a Nota Técnica vigente da SEFAZ/RTC** para a NF-e/NFS-e. A FIS-01 verifica se o EmissorFiscal já atende |
| P-04 | Etapas reais dos roteiros e horas padrão | PLN-01; cadastro em produção | Konebras | Antes da Fase 2 | 16/10/2026 (John): etapas confirmadas **Projeto → Corte → Montagem → Qualidade → Expedição**. As horas padrão são cadastradas no template (editáveis) |
| P-05 | % de encargos para custo-hora | PRD-04, COM-01 | PO | — | 16/10/2026 (John): **parâmetro editável na tela de Configurações** (CAD-06), com valor inicial sugerido e histórico de alteração |
| P-06 | Layout de exportação para a contadora | RH-03, FIN-14 | PO | — | 16/10/2026 (John): **um layout padrão do sistema** (CSV/XLSX documentado); layouts adicionais entram depois, como plug-ins de exportação |
| P-07 | Acesso ao banco do Dolibarr para mapear a migração | MIG-01 | PO | — | 16/10/2026: **resolvida.** Acesso via VPS. Volume: 118 terceiros, 34 produtos, 16 orçamentos, 4 pedidos, 26 faturas, 164 faturas de fornecedor, 166 lançamentos bancários, 4 OS, 4 BOM/OF, estoque zerado |
| P-08 | Conta PJ no Banco Inter com API habilitada | BNK-01 | Konebras | Pedir já | 16/10/2026 (John): **OK, será providenciada** |
| P-09 | Domínio do SaaS e nome do produto | FND-10 | PO | — | 16/10/2026 (John): **BemAqui Empresas**, tenant Konebras em `app.konebras.com.br` (mesma VPS, troca de conteúdo no go-live) — ADR 0004 |
| P-10 | Contratos de manutenção recorrente / OS de serviço / venda avulsa | Escopo | PO | — | 16/10/2026 (John): **entram no produto como Fase 10, planejada depois de todo o restante executado** |
| P-11 | Políticas de garantia e assistência técnica pós-entrega | PRJ-7 | PO/Konebras | Fase 2 | 16/10/2026 (John): **parâmetro na tela de Configurações** (meses padrão por tipo de item, sobrescrevível no item/produto; o campo `elev_garantia` do Dolibarr entra na migração) |
| P-12 | Dispositivos do chão de fábrica (tablets) e TV | PRD-03, PNL-01 | Konebras | Fase 3 | 16/10/2026 (John): OK, a Konebras providencia; PWA mira tablet Android de entrada |
| P-13 | Impressora de etiquetas para QR code | PAT-01 | Konebras | Fase 6 | 16/10/2026 (John): OK, a Konebras providencia; o modelo define o tamanho da etiqueta na PAT-01 |
| P-14 | Precificação: markup sobre custo ou margem sobre preço | COM-01 | PO | Antes da COM-01 | 16/10/2026 (John): **os dois modos, escolhidos em Configurações** (preço = custo × (1+markup) ou custo ÷ (1−margem)); a regra detalhada será aprofundada com o PO antes da COM-01 |
| P-15 | Retenções (ISS/INSS/IR) sobre serviço tomado de instalador terceirizado | COM-06, FIN-02 | Contadora | Fase 2 | 16/10/2026 (John): **opcional**. O título a pagar tem campos de retenção, preenchidos quando aplicável, sem cálculo automático no MVP |
| P-16 | Rateio de frete/IPI/ICMS-ST no custo de entrada do material | EST-01, CMP-04 | Contadora | Fase 3 | 16/10/2026 (John): **sim**, frete e IPI entram no custo de entrada (rateio proporcional ao valor dos itens) |
| P-17 | DRE gerencial por competência ou por caixa | FIN-08 | Contadora/PO | Fase 4 | 16/10/2026 (John): **competência, pela data da venda** (receita na data da venda/emissão, despesa na data do documento) |

## Riscos

| Risco | Impacto | Mitigação |
|---|---|---|
| Folha calculada completa (INSS, IRRF, férias, 13º, rescisão, mudanças de tabela) é complexa e com responsabilidade legal | Alto | Separada em fase própria (7); cadastro estruturado desde a Fase 1; 3 meses de conferência paralela com a contadora antes de substituir |
| Questões fiscais (P-01, P-02, P-03) atrasam o faturamento pelo sistema | Alto | Resolver com a contadora durante as Fases 0–2; FIS-01 (análise do EmissorFiscal) pode ser antecipado e rodar em paralelo |
| Escopo amplo desenvolvido por Claude Code com um PO | Médio | Fases com valor incremental; go-live único quando o PO considerar seguro; stories com CA testável (Definition of Ready) |
| VPS compartilhada com ~10 projetos (mesma rede Docker, sem alta disponibilidade) | Médio | **Risco aceito pelo PO em 16/10/2026.** Postgres em rede interna própria, containers não-root com limites, backup diário cifrado fora da VPS, monitor externo, VPS dedicada ao entrar o 2º tenant |
| Perda de até 24 h de dados (backup diário) | Médio | **RPO de 24 h aceito pelo PO em 16/10/2026.** Dump diário cifrado, cópia externa, restauração testada mensalmente |
| Dados do Dolibarr inconsistentes | Baixo | Volume pequeno (P-07); importação com relatório de erros, reexecutável |
| Adoção no chão de fábrica (operador não apontar) | Médio | PWA simples, poucos toques, painel TV mostrando o efeito do apontamento; supervisor pode apontar por eles |
| Aprovador único (proprietário aprova tudo) vira gargalo | Médio | Delegação temporária / aprovador substituto nas alçadas |
| Leitura de NF por PDF com erro de extração | Baixo | Conferência manual obrigatória; XML continua sendo o caminho preferencial |
| Dependência da API do Banco Inter | Baixo | Abstração de provedor; baixa manual e OFX como alternativa |
