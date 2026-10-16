# 06 — Pendências e riscos

Regra: item pendente não é implementado até ter decisão registrada aqui (data + quem decidiu).

## Pendências (decisão do PO / cliente / contadora)

| ID | Pendência | Bloqueia | Com quem | Decisão |
|---|---|---|---|---|
| P-01 | **Regime tributário** da Konebras (o "ME" sugere Simples Nacional, mas precisa confirmar) | FIS-02, FIS-03, custo médio (créditos) | Contadora | |
| P-02 | **CNAE x fabricação:** o CNAE principal é instalação/manutenção (4329-1/03) e o secundário é comércio atacadista (4669-9/99). Não há CNAE industrial. Emitir NF-e de "produto fabricado" (com IPI, CFOP de produção própria) pode exigir incluir CNAE de fabricação (ex.: fabricação de máquinas de elevação) e inscrição estadual adequada. Também define se a instalação sai em NFS-e municipal (Caçapava) | FIS-03, FIS-06, regras fiscais | Contadora | |
| P-03 | **Reforma Tributária (IBS/CBS):** quais campos e quando passam a ser obrigatórios para o regime da empresa; o EmissorFiscal já suporta? | FIS-01, FIS-03 | Contadora + análise FIS-01 | |
| P-04 | Etapas reais dos roteiros (os atuais são exemplos) e horas padrão por etapa | Testes reais de PLN-01; cadastro em produção | Konebras | |
| P-05 | % de encargos para custo-hora | PRD-04 | Contadora | |
| P-06 | Layout de exportação para a contadora (e qual sistema contábil ela usa) | RH-03, FIN-14 | Contadora | |
| P-07 | Acesso ao banco/exports do Dolibarr para mapear migração | MIG-01 | PO | |
| P-08 | Conta PJ no Banco Inter com API habilitada (certificado e credenciais de integração) | BNK-01 | Konebras | |
| P-09 | Domínio do SaaS e nome do produto | FND-10 | PO | 16/10/2026 (John): **BemAqui Empresas**, tenant Konebras em `app.konebras.com.br`; Dolibarr vai para `adm.konebras.com.br` até o go-live — ADR 0004 |
| P-10 | **Contratos de manutenção recorrente** de elevadores (manutenção mensal é o CNAE principal): a Konebras presta esse serviço? Se sim, vira módulo próprio (contratos, visitas, chamados, faturamento recorrente) | Escopo pós-MVP | PO/Konebras | |
| P-11 | Políticas de garantia e assistência técnica pós-entrega | PRJ-7 | Konebras | |
| P-12 | Dispositivos do chão de fábrica (tablets, quantidade) e TV/monitor para o painel | PRD-03, PNL-01 | Konebras | |
| P-13 | Impressora de etiquetas para QR code (modelo/tamanho da etiqueta) | PAT-01 | Konebras | |
| P-14 | Política de precificação: markup sobre custo ou margem sobre preço | COM-01 | PO | |

## Riscos

| Risco | Impacto | Mitigação |
|---|---|---|
| Folha calculada completa (INSS, IRRF, férias, 13º, rescisão, mudanças de tabela) é complexa e com responsabilidade legal | Alto | Separada em fase própria (7); cadastro estruturado desde a Fase 1; 3 meses de conferência paralela com a contadora antes de substituir |
| Questões fiscais (P-01, P-02, P-03) atrasam o faturamento pelo sistema | Alto | Resolver com a contadora durante as Fases 0–2; FIS-01 (análise do EmissorFiscal) pode ser antecipado e rodar em paralelo |
| Escopo amplo para MVP desenvolvido por Claude Code com um PO | Médio | Fases com valor incremental; go-live ao fim da Fase 5; não iniciar fase seguinte com a anterior incompleta |
| Dados do Dolibarr inconsistentes | Médio | Importação com relatório de erros, reexecutável; saneamento antes da carga final |
| Adoção no chão de fábrica (operador não apontar) | Médio | PWA simples, poucos toques, painel TV mostrando o efeito do apontamento; supervisor pode apontar por eles |
| VPS única (ponto único de falha) | Médio | Backup externo diário testado; compose reproduzível; plano de restauração documentado em < 2 h |
| Leitura de NF por PDF com erro de extração | Baixo | Conferência manual obrigatória; XML continua sendo o caminho preferencial |
| Dependência da API do Banco Inter | Baixo | Abstração de provedor; baixa manual e OFX como alternativa |
