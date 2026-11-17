# 08 — Como o cliente recebe os módulos e como testar o PDV

Guia de 17/11/2026. Explica quem decide o que cada empresa vê no sistema e como testar o PDV sem tocar na Konebras.

## 1. Existe um "super admin"?

Sim. Ele fica num ambiente separado, a **Plataforma** ([ADR 0012](../adr/0012-sessao-do-super-admin.md)):

- **Endereço:** `https://plataforma-homolog.bemaquiapp.com.br/plataforma`.
- **Acesso:** login próprio, com 2FA obrigatório.
- **Isolamento:** o login de uma empresa não vale na Plataforma, e o da Plataforma não vale dentro de uma empresa.

## 2. Quem decide o que a empresa vê

```
 PLATAFORMA (super admin)                                DENTRO DA EMPRESA (o cliente)
 ────────────────────────                                ─────────────────────────────
 ┌──────────────┐
 │ 1. PLANO     │  o que ele contratou
 │ ex.: Piloto  │  (lista de módulos)  ─┐
 └──────────────┘                       │
 ┌──────────────┐                       │     ┌──────────────────────────┐
 │ 2. PERFIL    │  o ramo do negócio    ├──►  │ MÓDULOS LIBERADOS        │
 │ Fábrica,     │  (PERF-01 / PERF-02)  │     │ para essa empresa        │
 │ Comércio...  │                      ─┤     └────────────┬─────────────┘
 └──────────────┘                       │                  │
 ┌──────────────┐                       │                  ▼
 │ 3. EXCEÇÃO   │  liberar ou bloquear  │     ┌──────────────────────────┐
 │ manual       │  um módulo avulso    ─┘     │ GRUPOS E PERMISSÕES      │
 └──────────────┘                             │ (o Proprietário define   │
                                              │  em Configurações)       │
                                              └────────────┬─────────────┘
                                                           ▼
                                              ┌──────────────────────────┐
                                              │ USUÁRIO vê o menu        │
                                              └──────────────────────────┘
```

**Regra:** um módulo fica **liberado** quando está no plano **e** em algum perfil da empresa, **ou** quando há uma exceção manual ([ADR 0046](../adr/0046-perfis-de-negocio-e-modulo-oculto.md)).

| Situação do módulo | O que o cliente vê |
|---|---|
| Está no plano **e** no ramo dele | Liberado, aparece normal |
| É do ramo dele, mas o plano não cobre | Aparece **com cadeado**: "Funcionalidade não liberada nessa versão do sistema." Serve de vitrine para vender mais |
| Não é do ramo dele (ex.: Produção numa padaria) | **Oculto**: não aparece no menu, na busca nem na ajuda. O endereço direto responde "não liberado" |
| Exceção manual na Plataforma | Vale a decisão manual, por cima do plano e do perfil |

Dentro da empresa, o Proprietário ainda escolhe, por grupo, quais módulos e ações cada pessoa usa (camadas 2 e 3 do controle de acesso, no `CLAUDE.md`).

## 3. O que a Plataforma tem e o que vem

| Tela da Plataforma | Situação em 17/11/2026 |
|---|---|
| Lista de empresas | Existe |
| Criar empresa (endereço, dono, plano) | Existe |
| Módulos da empresa, com exceção manual | Existe |
| Planos | Existe um só, o "Piloto", com os 16 módulos |
| Escolher o ramo ao criar a empresa; aba "Perfis" na empresa | PERF-02, em construção |

A Konebras tem o perfil **Fábrica**, que inclui todos os 16 módulos de hoje. Para ela nada mudou (PERF-01 em produção, conferido no banco e por teste de regressão).

## 4. Como testar o PDV (quando a PERF-02 estiver em produção)

1. Entrar na Plataforma.
2. Criar a empresa **"Loja Demo"** com o ramo **Comércio**, segmento **Loja de balcão**.
3. A Plataforma dá um endereço próprio para ela e manda o convite do Proprietário para o seu e-mail.
4. Entrar nesse endereço como dono. O sistema aparece como um lojista veria:
   - sem Produção nem Engenharia;
   - "Retaguarda" no lugar de "Fábrica" (PERF-03);
   - com as Formas de pagamento no Financeiro (PAG-01).
5. A Konebras continua no endereço dela, intocada.

Cada story nova do PDV (catálogo de produtos, depois o caixa) aparece na Loja Demo assim que entra em produção. Ela é o ambiente de teste do PDV até existir uma loja piloto.

## 5. Onde ver o resto

- Plano completo: [README](README.md), [backlog](05-backlog.md).
- Protótipo de tela: [prototipo/pdv-unificado.html](prototipo/pdv-unificado.html), publicado em https://claude.ai/artifact/LwN12sBM1ACHTPxheJgmcA.
- Resultado do teste do caixa sem internet: [spikes/SPK-03-resultado.md](spikes/SPK-03-resultado.md).
