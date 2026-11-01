---
titulo: Manutenção do bem
rota: /app/patrimonio/bens/$id
aba: manutencao
padrao: false
modulo: assets
---

## Para que serve
Cuidar da manutenção do bem: os planos preventivos (a cada N dias ou a cada N horas de uso), a leitura do horímetro e as OS de manutenção do bem, da mais recente à mais antiga.

## Como usar
- **Novo plano**: dê a descrição, a periodicidade (a cada N dias ou a cada N horas de uso), a antecedência do aviso (7 dias, se não mudar), o executor padrão, o checklist e a última execução (a data, ou o horímetro no plano por horas). Confirme **Salvar plano**. O plano mostra a próxima preventiva.
- **Registrar horímetro**: digite o valor do painel da máquina e a data. A leitura precisa ser maior que a anterior. No plano por horas, a próxima data é prevista pela média de uso das últimas leituras.
- **Nova OS de manutenção**: abre uma corretiva ou uma preventiva avulsa para este bem. **Abrir OS agora**, no cartão do plano, abre a preventiva do plano com o checklist dele.
- O chip **Vencida** (vermelho) mostra o plano que passou do vencimento; **A vencer** (âmbar), o que está na antecedência.

## Perguntas frequentes
**Quando a OS preventiva é aberta?** O sistema abre sozinho no dia do vencimento (no plano por horas, quando a leitura do horímetro chega ao previsto). Antes, na antecedência, avisa quem cuida do patrimônio.

**Por que a situação do bem mudou para Em manutenção?** Uma OS do bem está em andamento, ou foi aberta com a máquina parada. Ao concluir ou cancelar, o bem volta a Ativo, se nenhuma outra OS o segurar.

**De onde conta o próximo vencimento?** Da conclusão da última preventiva. Se a manutenção atrasou, o próximo vencimento conta do dia em que ela foi feita.

**O bem foi baixado. E os planos?** Ficam inativos e não abrem OS. Se o bem for reativado, edite o plano e marque **Plano ativo** para voltar.
